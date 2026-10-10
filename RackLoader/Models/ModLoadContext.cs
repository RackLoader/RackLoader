using BepInEx.Logging;

namespace RackLoader.Models;

/// <summary>
/// Host services exposed to a format handler while it activates a mod.
/// </summary>
public sealed class ModLoadContext
{
    public ModLoadContext(ManualLogSource logger, string cacheDirectory)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        CacheDirectory = string.IsNullOrWhiteSpace(cacheDirectory)
            ? throw new ArgumentException("A cache directory is required.", nameof(cacheDirectory))
            : cacheDirectory;
    }

    public ManualLogSource Logger { get; }
    public string CacheDirectory { get; }
}
