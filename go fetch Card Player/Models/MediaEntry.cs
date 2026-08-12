using CommunityToolkit.Mvvm.ComponentModel;

namespace CardPlayer.Models;

/// <summary>
/// Cards.csv columns:
///   0 = Type_Digit
///   1 = Title
///   2 = QR_Code
///   3 = Path
///   4 = Art_Path
///   5 = Art_Fit
///   6 = Art_Back_Path
///   7 = State
///   8 = Text_Side
///   9 = Text_Font
///  10 = Text_Style
///  11 = Text_Size
///  12 = Text_Color
///  13 = Front_Bg_Color
///  14 = Back_Bg_Color
/// </summary>
public partial class MediaEntry : ObservableObject
{
    [ObservableProperty] private string _typeDigit = "";
    [ObservableProperty] private string _displayText = "";
    [ObservableProperty] private string _qrCode = "";
    [ObservableProperty] private string _path = "";
    [ObservableProperty] private string _artPath = "";
    [ObservableProperty] private string _artFit = "";
    [ObservableProperty] private string _artBackPath = "";
    [ObservableProperty] private string _state = "new";

    // Text layout columns (cols 7-12)
    [ObservableProperty] private string _textSide = "front";
    [ObservableProperty] private string _textFont = "Arial";
    [ObservableProperty] private string _textStyle = "Normal";
    [ObservableProperty] private int _textSize = 36;
    [ObservableProperty] private string _textColor = "#000000";

    // Background colors (cols 13-14)
    [ObservableProperty] private string _frontBgColor = "#FFFFFF";
    [ObservableProperty] private string _backBgColor = "#FFFFFF";

    // Computed by the tab VM — not stored in CSV
    [ObservableProperty] private string _computedStatus = "";

    // Notify ComputedStatus when relevant fields change
    partial void OnQrCodeChanged(string value) => OnPropertyChanged(nameof(ComputedStatus));
    // Strip surrounding quotes before storing (e.g. pasted as "C:\path\file.mkv")
    partial void OnPathChanged(string value)
    {
        var stripped = StripQuotes(value);
        if (stripped != value) { _path = stripped; OnPropertyChanged(nameof(Path)); }
        OnPropertyChanged(nameof(ComputedStatus));
    }
    partial void OnArtPathChanged(string value)
    {
        var stripped = StripQuotes(value);
        if (stripped != value) { _artPath = stripped; OnPropertyChanged(nameof(ArtPath)); }
        OnPropertyChanged(nameof(ComputedStatus));
    }
    partial void OnArtBackPathChanged(string value)
    {
        var stripped = StripQuotes(value);
        if (stripped != value) { _artBackPath = stripped; OnPropertyChanged(nameof(ArtBackPath)); }
    }

    /// <summary>Background color for the status cell.</summary>
    public string StatusColor => ComputedStatus switch
    {
        var s when s.StartsWith("Ready") => "#2D2D8A2D",
        var s when s.StartsWith("Not Ready") => "#2DCD3232",
        var s when s == "Generated" => "#2D2D6EA8",
        var s when s.StartsWith("Generated") => "#2D1E7A5E",
        var s when s == "Skip" => "#2D888888",
        _ => "Transparent"
    };

    /// <summary>Sets QrCode without firing PropertyChanged — used during save dedup to avoid revalidation cascade.</summary>
    public void SetQrCodeSilent(string value) => _qrCode = value;

    public MediaEntry() { }

    public MediaEntry(string[] cols)
    {
        _typeDigit = cols.Length > 0 ? cols[0].Trim() : "";
        _displayText = cols.Length > 1 ? cols[1].Trim() : "";
        _qrCode = cols.Length > 2 ? cols[2].Trim() : "";
        _path = cols.Length > 3 ? StripQuotes(cols[3]) : "";
        _artPath = cols.Length > 4 ? StripQuotes(cols[4]) : "";
        _artFit = cols.Length > 5 ? cols[5].Trim() : "";
        _artBackPath = cols.Length > 6 ? StripQuotes(cols[6]) : "";
        _state = cols.Length > 7 ? cols[7].Trim() : "new";
        _textSide = cols.Length > 8 && !string.IsNullOrWhiteSpace(cols[8]) ? cols[8].Trim() : "front";
        _textFont = cols.Length > 9 ? cols[9].Trim() : "Arial";
        _textStyle = cols.Length > 10 ? cols[10].Trim() : "Normal";
        _textSize = cols.Length > 11 && int.TryParse(cols[11].Trim(), out var ts) ? ts : 36;
        _textColor = cols.Length > 12 ? cols[12].Trim() : "#000000";
        _frontBgColor = cols.Length > 13 ? cols[13].Trim() : "#FFFFFF";
        _backBgColor = cols.Length > 14 ? cols[14].Trim() : "#FFFFFF";
    }

    /// <summary>Apply a layout profile's settings to this entry.</summary>
    public void ApplyLayoutProfile(Config.CardLayoutProfile p)
    {
        TextSide = p.TextSide;
        TextFont = p.TextFont;
        TextStyle = p.TextStyle;
        TextSize = p.TextSize;
        TextColor = p.TextColor;
        ArtFit = p.ArtFit;
        FrontBgColor = p.FrontBgColor;
        BackBgColor = p.BackBgColor;
    }

    private static string StripQuotes(string s)
    {
        s = s.Trim();
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
            s = s[1..^1].Trim();
        return s;
    }

    public string ToCsvLine() =>
        $"{CsvEsc(TypeDigit)},{CsvEsc(DisplayText)},{CsvEsc(QrCode)},{CsvEsc(Path)}," +
        $"{CsvEsc(ArtPath)},{CsvEsc(ArtFit)},{CsvEsc(ArtBackPath)},{CsvEsc(State)}," +
        $"{CsvEsc(TextSide)},{CsvEsc(TextFont)}," +
        $"{CsvEsc(TextStyle)},{TextSize},{CsvEsc(TextColor)}," +
        $"{CsvEsc(FrontBgColor)},{CsvEsc(BackBgColor)}";

    private static string CsvEsc(string? s)
    {
        if (s == null) return "";
        return s.Contains(',') || s.Contains('"') || s.Contains('\n')
            ? $"\"{s.Replace("\"", "\"\"")}\""
            : s;
    }
}

/// <summary>Valid values for the Art_Fit column.</summary>
public static class MediaEntryArtFitOptions
{
    public static readonly string[] Values = { "", "fill", "fit", "square fill", "square fit" };
}