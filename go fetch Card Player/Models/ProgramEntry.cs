using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CardPlayer.Models;

public partial class ProgramEntry : ObservableObject
{
    [ObservableProperty] private string _typeDigit     = "";
    [ObservableProperty] private string _playerType     = "";
    [ObservableProperty] private string _programName   = "";
    [ObservableProperty] private string _programPath   = "";
    [ObservableProperty] private string _options       = "";
    [ObservableProperty] private string _sendKeys      = "";
    [ObservableProperty] private string _sendKeysDelay = "";

    public ProgramEntry() { }

    public ProgramEntry(string[] cols)
    {
        // Detect old format (6 cols: TypeDigit,PlayerType,ProgramPath,Options,SendKeys,SendKeysDelay)
        // vs new format (7 cols: TypeDigit,PlayerType,ProgramName,ProgramPath,Options,SendKeys,SendKeysDelay)
        // Heuristic: if col[2] looks like a path (contains \ or /) it's the old format
        bool isOldFormat = cols.Length == 6 ||
                           (cols.Length >= 3 && (cols[2].Contains('\\') || cols[2].Contains('/')));

        if (isOldFormat)
        {
            _typeDigit     = cols.Length > 0 ? cols[0].Trim() : "";
            _playerType     = cols.Length > 1 ? cols[1].Trim() : "";
            _programPath   = cols.Length > 2 ? cols[2].Trim() : "";
            _options       = cols.Length > 3 ? cols[3].Trim() : "";
            _sendKeys      = cols.Length > 4 ? cols[4].Replace("\r","").Replace("\n","") : "";
            _sendKeysDelay = cols.Length > 5 ? cols[5].Trim() : "";
            // Auto-populate name from path
            if (!string.IsNullOrWhiteSpace(_programPath))
                _programName = Path.GetFileNameWithoutExtension(_programPath);
        }
        else
        {
            _typeDigit     = cols.Length > 0 ? cols[0].Trim() : "";
            _playerType     = cols.Length > 1 ? cols[1].Trim() : "";
            _programName   = cols.Length > 2 ? cols[2].Trim() : "";
            _programPath   = cols.Length > 3 ? cols[3].Trim() : "";
            _options       = cols.Length > 4 ? cols[4].Trim() : "";
            _sendKeys      = cols.Length > 5 ? cols[5].Replace("\r","").Replace("\n","") : "";
            _sendKeysDelay = cols.Length > 6 ? cols[6].Trim() : "";
        }

        // Auto-populate name from path if still blank
        if (string.IsNullOrWhiteSpace(_programName) && !string.IsNullOrWhiteSpace(_programPath))
            _programName = Path.GetFileNameWithoutExtension(_programPath);
    }

    // When ProgramPath changes, auto-fill ProgramName if it's still empty
    partial void OnProgramPathChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(ProgramName) && !string.IsNullOrWhiteSpace(value))
            ProgramName = Path.GetFileNameWithoutExtension(value);
    }

    public string ToCsvLine() =>
        $"{CsvEsc(TypeDigit)},{CsvEsc(PlayerType)},{CsvEsc(ProgramName)},{CsvEsc(ProgramPath)},{CsvEsc(Options)},{CsvEsc(SendKeys)},{CsvEsc(SendKeysDelay)}";

    private static string CsvEsc(string s) =>
        s.Contains(',') || s.Contains('"') || s.Contains('\n')
            ? $"\"{s.Replace("\"", "\"\"")}\""
            : s;
}
