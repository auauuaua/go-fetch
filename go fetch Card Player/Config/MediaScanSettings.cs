using System.Collections.Generic;

namespace CardPlayer.Config;

public class MediaTypeScanSettings
{
    public string ScanPath        { get; set; } = "";
    public string ScanMode        { get; set; } = "folders";
    public int    Depth            { get; set; } = 0;
    public bool   AutoDisplayName  { get; set; } = true;
    public int    InverseDepth     { get; set; } = 0;
    public bool   AutoQrCode       { get; set; } = false;
    public bool   AutoUppercase    { get; set; } = false;
}

public class MediaScanSettingsConfig
{
    /// <summary>Keyed by TypeDigit.</summary>
    public Dictionary<string, MediaTypeScanSettings> Types { get; set; } = new();
}
