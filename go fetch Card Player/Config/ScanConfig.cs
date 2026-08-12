using System.Collections.Generic;

namespace CardPlayer.Config;

public class ScanTypeConfig
{
    public string ScanPath          { get; set; } = "";
    public string ScanMode          { get; set; } = "folders";
    public int    Depth             { get; set; } = 0;
    public int    InverseDepth      { get; set; } = 0;
    public string ArtPattern        { get; set; } = "first";
    public string ArtDirOverride    { get; set; } = "";
    public string DefaultArtFit     { get; set; } = "";   // kept for backward compat
    public string DefaultProfileName { get; set; } = "";  // name of CardLayoutProfile to apply on scan
    public string ExcludeFolders    { get; set; } = "";   // comma-separated folder names
    public string FileExtensions    { get; set; } = "";   // comma-separated extensions (files mode)
}

public class ScanConfig
{
    public Dictionary<string, ScanTypeConfig> ByTypeDigit { get; set; } = new();
}
