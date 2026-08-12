using CommunityToolkit.Mvvm.ComponentModel;

namespace CardPlayer.Models;

public partial class IrCodeEntry : ObservableObject
{
    [ObservableProperty] private string _typeDigit   = "";
    [ObservableProperty] private string _irCode      = "";
    [ObservableProperty] private string _keySend     = "";
    [ObservableProperty] private string _remoteLabel = "";
    [ObservableProperty] private int    _gridRow     = 0;
    [ObservableProperty] private int    _gridCol     = 0;

    public IrCodeEntry() { }

    public IrCodeEntry(string[] cols)
    {
        _typeDigit   = cols.Length > 0 ? cols[0].Trim() : "";
        _irCode      = cols.Length > 1 ? cols[1].Trim() : "";
        _keySend     = cols.Length > 2 ? cols[2].Replace("\r","").Replace("\n","") : "";
        _remoteLabel = cols.Length > 3 ? cols[3].Replace("\r","").Replace("\n","").Trim() : "";
        _gridRow     = cols.Length > 4 && int.TryParse(cols[4].Trim(), out int r) ? r : 0;
        _gridCol     = cols.Length > 5 && int.TryParse(cols[5].Trim(), out int c) ? c : 0;
    }

    public string ToCsvLine() =>
        $"{CsvEsc(TypeDigit)},{CsvEsc(IrCode)},{CsvEsc(KeySend)},{CsvEsc(RemoteLabel)},{GridRow},{GridCol}";

    private static string CsvEsc(string s) =>
        s.Contains(',') || s.Contains('"') || s.Contains('\n')
            ? $"\"{s.Replace("\"", "\"\"")}\""
            : s;
}
