using System.IO;

namespace RackLoader.Contracts;

/// <summary>Host services available to a running mod.</summary>
public interface IRackModContext
{
    string ModId { get; }
    string Name { get; }
    string Version { get; }

    void LogInfo(string message);
    void LogWarning(string message);
    void LogError(string message);

    /// <summary>Opens a package asset by its path relative to the package's assets directory.</summary>
    Stream OpenAsset(string assetPath);
}
