using System;
using System.Diagnostics;
using System.IO;
using ExileCore2;

namespace CraftUntilHighlighted;

/// <summary>
/// The folder dumps are written to, and the button that opens it. The folder is created if it is not there, so the
/// button works before the first dump.
/// </summary>
internal static class DumpFolder
{
    /// <summary>The plugin's config directory, set in Initialise. Settings nodes are not handed the plugin.</summary>
    public static string ConfigDirectory { get; set; } = "";

    public static string Path => System.IO.Path.Combine(ConfigDirectory, "dumps");

    public static void Open()
    {
        try
        {
            Directory.CreateDirectory(Path);
            Process.Start(new ProcessStartInfo { FileName = Path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"[CraftUntilHighlighted] Could not open the dump folder: {ex.Message}", 5f);
        }
    }
}
