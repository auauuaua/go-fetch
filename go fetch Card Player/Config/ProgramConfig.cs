using System.Collections.Generic;

namespace CardPlayer.Config;

public class ShiftKey
{
    public int Row { get; set; }
    public int Col { get; set; }
}

public class ProgramFunction
{
    public string Name { get; set; } = "";
    public string KeySend { get; set; } = "";
}

public class CellMapping
{
    public int Row { get; set; }
    public int Col { get; set; }
    public string FunctionName { get; set; } = "";
}

/// <summary>Cell mappings for one specific remote profile.</summary>
public class PerProfileMappings
{
    public List<CellMapping> Mappings { get; set; } = new();
    public List<CellMapping> ShiftMappings { get; set; } = new();
    public List<ShiftKey> ShiftKeys { get; set; } = new();
    /// <summary>Milliseconds to ignore repeat IR codes after one is received for this media type on this remote. 0 = off.</summary>
    public int Debounce { get; set; } = 0;
}

public class ProgramConfig
{
    public string TypeDigit { get; set; } = "";
    public string PlayerType { get; set; } = "";
    public string ProgramName { get; set; } = "";
    public string ProgramPath { get; set; } = "";
    public string Options { get; set; } = "";
    public bool NoTrailingSpace { get; set; } = false;
    public string SendKeys { get; set; } = "";
    public string SendKeysDelay { get; set; } = "";
    public string DispatchMethod { get; set; } = "sendkeys";
    public int TcpPort { get; set; } = 9999;
    public List<ProgramFunction> Functions { get; set; } = new();

    /// <summary>Per-remote-profile cell mappings, keyed by profile name.</summary>
    public Dictionary<string, PerProfileMappings> ProfileMappings { get; set; } = new();

    public string ShiftEntryFunction { get; set; } = "";
    public string ShiftExitFunction { get; set; } = "";
    public string ShiftEndMethod { get; set; } = "nextkey";
    public int ShiftTimerMs { get; set; } = 3000;
    public bool ResetTimerOnKeyPress { get; set; } = false;

    /// <summary>Returns the per-profile mappings for the given name, creating an empty entry if needed.</summary>
    public PerProfileMappings GetOrCreateProfileMappings(string profileName)
    {
        if (!ProfileMappings.TryGetValue(profileName, out var pm))
        {
            pm = new PerProfileMappings();
            ProfileMappings[profileName] = pm;
        }
        return pm;
    }

    /// <summary>Renames a profile key in ProfileMappings.</summary>
    public void RenameProfileMappings(string oldName, string newName)
    {
        if (oldName == newName) return;
        if (ProfileMappings.TryGetValue(oldName, out var pm))
        {
            ProfileMappings.Remove(oldName);
            ProfileMappings[newName] = pm;
        }
    }
}

public class ProgramsConfig
{
    public List<ProgramConfig> Players { get; set; } = new();
}