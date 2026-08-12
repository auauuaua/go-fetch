using System;
using System.IO;

namespace CardPlayer.Services;

/// <summary>
/// Central location for all persistent data paths.
/// Everything lives under %LOCALAPPDATA%\go fetch\go fetch Card Player.
/// </summary>
public static class AppPaths
{
    public static readonly string DataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "go fetch", "go fetch Card Player");

    public static string Combine(string fileName) =>
        Path.Combine(DataDir, fileName);

    /// <summary>Ensures the data directory exists. Call once at startup.</summary>
    public static void EnsureDataDir() =>
        Directory.CreateDirectory(DataDir);
}