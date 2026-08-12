using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CardPlayer.Config;

namespace CardPlayer.Services;

public static class JsonConfigService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented          = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // ── Players.json ────────────────────────────────────────────────────

    public static ProgramsConfig LoadPrograms(string path)
    {
        if (!File.Exists(path)) return new ProgramsConfig();
        try
        {
            return JsonSerializer.Deserialize<ProgramsConfig>(File.ReadAllText(path), Options)
                   ?? new ProgramsConfig();
        }
        catch { return new ProgramsConfig(); }
    }

    public static void SavePrograms(string path, ProgramsConfig config)
        => File.WriteAllText(path, JsonSerializer.Serialize(config, Options));

    // ── RemoteProfiles.json ────────────────────────────────────────────────

    public static RemoteProfilesConfig LoadRemoteProfiles(string path)
    {
        if (!File.Exists(path)) return new RemoteProfilesConfig();
        try
        {
            return JsonSerializer.Deserialize<RemoteProfilesConfig>(File.ReadAllText(path), Options)
                   ?? new RemoteProfilesConfig();
        }
        catch { return new RemoteProfilesConfig(); }
    }

    public static void SaveRemoteProfiles(string path, RemoteProfilesConfig config)
        => File.WriteAllText(path, JsonSerializer.Serialize(config, Options));
}
