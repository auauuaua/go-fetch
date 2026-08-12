using System.Collections.Generic;

namespace CardPlayer.Config;

/// <summary>
/// One cell in a remote profile grid.
/// </summary>
public class RemoteCell
{
    public int Row { get; set; }
    public int Col { get; set; }
    public string Label { get; set; } = "";
    public string IrCode { get; set; } = "";
}

/// <summary>
/// A named remote control profile with a variable grid size.
/// </summary>
public class RemoteProfile
{
    public string Name { get; set; } = "New Remote";
    public int Rows { get; set; } = 7;
    public int Cols { get; set; } = 3;
    /// <summary>Milliseconds to ignore repeat IR codes after one is received. 0 = no debounce.</summary>
    public int Debounce { get; set; } = 0;
    public List<RemoteCell> Cells { get; set; } = new();
}

/// <summary>
/// Root object saved to RemoteProfiles.json
/// </summary>
public class RemoteProfilesConfig
{
    public string ActiveProfile { get; set; } = "";
    public List<RemoteProfile> Profiles { get; set; } = new();
}