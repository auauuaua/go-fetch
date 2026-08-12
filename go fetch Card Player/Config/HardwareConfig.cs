namespace CardPlayer.Config;

public class HardwareConfig
{
    public string Vid                      { get; set; } = "2E8A";
    public string Pid                      { get; set; } = "000A";
    public bool   DebugLogging             { get; set; } = false;
    public bool   PassthroughEnabled       { get; set; } = false;
    public string PassthroughPlayerType     { get; set; } = "";
}
