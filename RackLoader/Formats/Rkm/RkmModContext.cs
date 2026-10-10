using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Logging;
using RackLoader.Contracts;
using RackLoader.Models;
using RackLoader.Rkm;

namespace RackLoader.Formats.Rkm;

internal sealed class RkmModContext : IRackModContext
{
    private readonly ManualLogSource _logger;
    private readonly string _packageDirectory;
    private readonly HashSet<string> _assetPaths;

    public RkmModContext(
        ModMetadata metadata,
        ManualLogSource logger,
        string packageDirectory,
        IEnumerable<RkmPayloadFile> assets)
    {
        ModId = metadata.Id;
        Name = metadata.Name;
        Version = metadata.Version;
        _logger = logger;
        _packageDirectory = packageDirectory;
        _assetPaths = assets.Select(asset => asset.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public string ModId { get; }
    public string Name { get; }
    public string Version { get; }

    public void LogInfo(string message) => _logger.LogInfo($"[{ModId}] {message}");
    public void LogWarning(string message) => _logger.LogWarning($"[{ModId}] {message}");
    public void LogError(string message) => _logger.LogError($"[{ModId}] {message}");

    public Stream OpenAsset(string assetPath)
    {
        var normalized = RkmPaths.NormalizeAssetPath(assetPath);
        var packagePath = _assetPaths.FirstOrDefault(path =>
            string.Equals(path, "assets/" + normalized, StringComparison.OrdinalIgnoreCase));
        if (packagePath is null)
            throw new FileNotFoundException($"Asset '{assetPath}' is not listed in the mod package.");

        var fullPath = RkmPaths.GetPathUnderRoot(_packageDirectory, packagePath);
        return new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }
}
