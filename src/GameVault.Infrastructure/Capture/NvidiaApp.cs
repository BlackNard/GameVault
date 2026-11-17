using GameVault.Core;
namespace GameVault.Infrastructure.Capture;

/// <summary>The few machine facts NVIDIA App detection needs, so detection can be tested without NVIDIA installed.</summary>
public interface INvidiaEnvironment
{
    string ProgramFiles { get; }
    string ProgramFilesX86 { get; }
    string VideosFolder { get; }
    bool DirectoryExists(string path);
    bool IsProcessRunning(string processName);
    /// <summary>The capture folder NVIDIA App has configured (HKCU\Software\NVIDIA Corporation\Global\ShadowPlay\NVSPCAPS, DefaultPathW), or null.</summary>
    string? RegisteredCaptureFolder();
}

/// <summary>
/// Read-only detection of NVIDIA App (and the older GeForce Experience overlay). GameVault never starts it, scripts it
/// or edits its settings: NVIDIA publishes no API for that. It only finds where NVIDIA saves its captures so they can be copied.
/// </summary>
public static class NvidiaApp
{
    private static readonly string[] InstallFolders = ["NVIDIA App", "NVIDIA GeForce Experience"];
    private static readonly string[] ProcessNames = ["NVIDIA Overlay", "NVIDIA app", "NVIDIA GeForce Experience"];

    public static NvidiaAppInfo Detect(INvidiaEnvironment environment)
    {
        var installed = false;
        foreach (var programFiles in new[] { environment.ProgramFiles, environment.ProgramFilesX86 })
        {
            if (string.IsNullOrWhiteSpace(programFiles)) continue;
            foreach (var folder in InstallFolders)
                installed |= environment.DirectoryExists(Path.Combine(programFiles, "NVIDIA Corporation", folder));
        }
        if (!installed) return NvidiaAppInfo.NotFound;
        var running = ProcessNames.Any(environment.IsProcessRunning);
        var registered = environment.RegisteredCaptureFolder();
        var captureFolder = !string.IsNullOrWhiteSpace(registered) && Path.IsPathFullyQualified(registered)
            ? Path.GetFullPath(registered)
            : string.IsNullOrWhiteSpace(environment.VideosFolder) ? null : Path.Combine(environment.VideosFolder, "NVIDIA");
        return new NvidiaAppInfo(true, running, captureFolder);
    }

    public static bool UsesNvidia(CaptureSource source, NvidiaAppInfo info) => source switch
    {
        CaptureSource.NvidiaApp => true,
        CaptureSource.GameVault => false,
        _ => info.Installed
    };

    /// <summary>The default shortcuts NVIDIA documents; they are customizable in NVIDIA App, so they are only a hint.</summary>
    public const string HotkeyHint = "NVIDIA App's default shortcuts are Alt+F9 (record) and Alt+F1 (screenshot); Alt+Z opens its overlay. Check NVIDIA App if you changed them.";
}
