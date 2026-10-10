using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using RackLoader.Abstractions;
using RackLoader.Models;

namespace RackLoader.Services;

/// <summary>
/// Inspects all packages first, resolves exact dependency versions, then activates packages
/// in dependency order. Format handlers own validation and runtime activation.
/// </summary>
public sealed class ModManager
{
    private readonly ManualLogSource _logger;
    private readonly IModSource[] _sources;
    private readonly IModFormatHandler[] _formatHandlers;
    private readonly ModLoadContext _loadContext;
    private readonly List<ILoadedMod> _loadedMods = new();
    private readonly HashSet<string> _loadedPackageKeys = new(StringComparer.Ordinal);

    public ModManager(
        ManualLogSource logger,
        IEnumerable<IModSource> sources,
        IEnumerable<IModFormatHandler> formatHandlers,
        string cacheDirectory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _sources = sources?.ToArray() ?? throw new ArgumentNullException(nameof(sources));
        _formatHandlers = formatHandlers?.ToArray() ?? throw new ArgumentNullException(nameof(formatHandlers));
        _loadContext = new ModLoadContext(logger, cacheDirectory);
    }

    public IReadOnlyList<ILoadedMod> LoadedMods => _loadedMods;

    public IReadOnlyList<ModLoadResult> LoadAvailableMods()
    {
        var results = new List<ModLoadResult>();
        var candidates = new List<PendingPackage>();

        _logger.LogInfo(
            $"Inspecting .rkm packages from {_sources.Length} source(s) with {_formatHandlers.Length} format handler(s).");

        foreach (var source in _sources)
        {
            try
            {
                foreach (var candidate in source.Discover())
                {
                    if (candidate is null)
                    {
                        results.Add(CreateResult(source.Name, "<null candidate>", ModLoadStatus.Failed,
                            "The source returned a null candidate."));
                        continue;
                    }

                    InspectCandidate(source, candidate, candidates, results);
                }
            }
            catch (Exception exception)
            {
                results.Add(CreateResult(source.Name, source.Name, ModLoadStatus.Failed,
                    $"Discovery failed: {exception.Message}", exception));
            }
        }

        var uniquePackages = ResolveDuplicatePackages(candidates, results);
        var packagesByKey = uniquePackages.ToDictionary(package => package.Key, StringComparer.Ordinal);
        var states = new Dictionary<string, VisitState>(StringComparer.Ordinal);
        var loadedByKey = _loadedMods.ToDictionary(
            mod => CreateKey(mod.Metadata.Id, mod.Metadata.Version),
            StringComparer.Ordinal);
        foreach (var loadedKey in _loadedPackageKeys)
            states[loadedKey] = VisitState.Loaded;
        var dependencyStack = new List<PendingPackage>();

        foreach (var package in uniquePackages)
            LoadPackage(package, packagesByKey, states, loadedByKey, dependencyStack, results);

        var loadedThisRun = results.Count(result => result.Status == ModLoadStatus.Loaded);
        _logger.LogInfo($"Mod loading finished: {loadedThisRun} loaded, {results.Count - loadedThisRun} skipped or failed.");
        return results;
    }

    private void InspectCandidate(
        IModSource source,
        IModCandidate candidate,
        ICollection<PendingPackage> candidates,
        ICollection<ModLoadResult> results)
    {
        IModFormatHandler[] matchingHandlers;
        try
        {
            matchingHandlers = _formatHandlers
                .Where(handler => handler.CanHandle(candidate))
                .Take(2)
                .ToArray();
        }
        catch (Exception exception)
        {
            results.Add(CreateResult(source.Name, candidate.DisplayName, ModLoadStatus.Failed,
                exception.Message, exception));
            return;
        }

        if (matchingHandlers.Length == 0)
        {
            results.Add(CreateResult(source.Name, candidate.DisplayName, ModLoadStatus.UnsupportedFormat,
                "No registered handler accepts this candidate."));
            return;
        }

        if (matchingHandlers.Length > 1)
        {
            var names = string.Join(", ", matchingHandlers.Select(handler => handler.Name));
            results.Add(CreateResult(source.Name, candidate.DisplayName, ModLoadStatus.AmbiguousFormat,
                $"More than one format handler matched: {names}."));
            return;
        }

        var formatHandler = matchingHandlers[0];
        try
        {
            var inspection = formatHandler.Inspect(candidate);
            if (inspection is null || inspection.Metadata is null ||
                string.IsNullOrWhiteSpace(inspection.Metadata.Id) ||
                string.IsNullOrWhiteSpace(inspection.Metadata.Name) ||
                string.IsNullOrWhiteSpace(inspection.Metadata.Version) ||
                inspection.Dependencies is null)
            {
                throw new InvalidOperationException("The format handler returned incomplete package metadata.");
            }

            candidates.Add(new PendingPackage(source.Name, candidate.DisplayName, formatHandler, inspection));
        }
        catch (Exception exception)
        {
            results.Add(CreateResult(source.Name, candidate.DisplayName, ModLoadStatus.Failed,
                exception.Message, exception));
        }
    }

    private List<PendingPackage> ResolveDuplicatePackages(
        List<PendingPackage> candidates,
        ICollection<ModLoadResult> results)
    {
        var unique = new List<PendingPackage>();
        foreach (var group in candidates.GroupBy(package => package.Key, StringComparer.Ordinal))
        {
            var packages = group.ToArray();
            if (packages.Length > 1)
            {
                foreach (var package in packages)
                {
                    results.Add(CreateResult(package.SourceName, package.CandidateName,
                        ModLoadStatus.DuplicatePackage,
                        $"More than one package has ID '{package.Inspection.Metadata.Id}' and version '{package.Inspection.Metadata.Version}'."));
                }
                continue;
            }

            var candidate = packages[0];
            if (_loadedPackageKeys.Contains(candidate.Key))
            {
                results.Add(CreateResult(candidate.SourceName, candidate.CandidateName, ModLoadStatus.AlreadyLoaded,
                    $"'{candidate.Inspection.Metadata.Id}' version '{candidate.Inspection.Metadata.Version}' is already loaded."));
                continue;
            }

            unique.Add(candidate);
        }

        return unique;
    }

    private bool LoadPackage(
        PendingPackage package,
        IReadOnlyDictionary<string, PendingPackage> packagesByKey,
        IDictionary<string, VisitState> states,
        IDictionary<string, ILoadedMod> loadedByKey,
        IList<PendingPackage> dependencyStack,
        ICollection<ModLoadResult> results)
    {
        if (states.TryGetValue(package.Key, out var state))
        {
            if (state == VisitState.Loaded)
                return true;
            if (state == VisitState.Failed)
                return false;
            if (state == VisitState.Loading)
            {
                var cycleStart = dependencyStack.ToList().FindIndex(item => item.Key == package.Key);
                var cycle = dependencyStack.Skip(Math.Max(0, cycleStart));
                foreach (var cyclePackage in cycle)
                {
                    states[cyclePackage.Key] = VisitState.Failed;
                    results.Add(CreateResult(cyclePackage.SourceName, cyclePackage.CandidateName,
                        ModLoadStatus.DependencyCycle, "A dependency cycle was detected."));
                }
                states[package.Key] = VisitState.Failed;
                return false;
            }
        }

        states[package.Key] = VisitState.Loading;
        dependencyStack.Add(package);
        try
        {
            var loadedDependencies = new List<ILoadedMod>();
            foreach (var dependency in package.Inspection.Dependencies)
            {
                var dependencyKey = CreateKey(dependency.Id, dependency.Version);
                if (loadedByKey.TryGetValue(dependencyKey, out var existingDependency))
                {
                    loadedDependencies.Add(existingDependency);
                    continue;
                }

                if (!packagesByKey.TryGetValue(dependencyKey, out var dependencyPackage))
                {
                    states[package.Key] = VisitState.Failed;
                    results.Add(CreateResult(package.SourceName, package.CandidateName,
                        ModLoadStatus.MissingDependency,
                        $"Required mod '{dependency.Id}' version '{dependency.Version}' was not found."));
                    return false;
                }

                if (!LoadPackage(dependencyPackage, packagesByKey, states, loadedByKey, dependencyStack, results))
                {
                    if (states[package.Key] != VisitState.Failed)
                    {
                        states[package.Key] = VisitState.Failed;
                        results.Add(CreateResult(package.SourceName, package.CandidateName,
                            ModLoadStatus.DependencyFailed,
                            $"Required mod '{dependency.Id}' version '{dependency.Version}' did not load."));
                    }
                    return false;
                }

                loadedDependencies.Add(loadedByKey[dependencyKey]);
            }

            var loadedMod = package.FormatHandler.Load(package.Inspection, loadedDependencies, _loadContext);
            if (loadedMod is null || loadedMod.Metadata is null)
                throw new InvalidOperationException("The format handler returned no loaded mod.");
            if (!string.Equals(loadedMod.Metadata.Id, package.Inspection.Metadata.Id, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(loadedMod.Metadata.Version, package.Inspection.Metadata.Version, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The loaded mod identity does not match its inspected package.");
            }

            _loadedMods.Add(loadedMod);
            _loadedPackageKeys.Add(package.Key);
            loadedByKey.Add(package.Key, loadedMod);
            states[package.Key] = VisitState.Loaded;
            results.Add(CreateResult(package.SourceName, package.CandidateName, ModLoadStatus.Loaded,
                $"Loaded '{loadedMod.Metadata.Name}' ({loadedMod.Metadata.Id} {loadedMod.Metadata.Version}).",
                metadata: loadedMod.Metadata));
            return true;
        }
        catch (Exception exception)
        {
            if (states[package.Key] != VisitState.Failed)
            {
                states[package.Key] = VisitState.Failed;
                results.Add(CreateResult(package.SourceName, package.CandidateName, ModLoadStatus.Failed,
                    exception.Message, exception, package.Inspection.Metadata));
            }
            return false;
        }
        finally
        {
            if (dependencyStack.Count > 0 && ReferenceEquals(dependencyStack[^1], package))
                dependencyStack.RemoveAt(dependencyStack.Count - 1);
        }
    }

    private ModLoadResult CreateResult(
        string sourceName,
        string candidateName,
        ModLoadStatus status,
        string message,
        Exception? exception = null,
        ModMetadata? metadata = null)
    {
        if (status == ModLoadStatus.Loaded)
            _logger.LogInfo(message);
        else if (status is ModLoadStatus.Failed or ModLoadStatus.DependencyCycle)
            _logger.LogError($"{candidateName} ({sourceName}): {message}{(exception is null ? string.Empty : Environment.NewLine + exception)}");
        else
            _logger.LogWarning($"{candidateName} ({sourceName}): {message}");

        return new ModLoadResult(sourceName, candidateName, status, metadata, message);
    }

    private static string CreateKey(string id, string version) => id.ToUpperInvariant() + "\0" + version;

    private enum VisitState
    {
        Loading,
        Loaded,
        Failed
    }

    private sealed record PendingPackage(
        string SourceName,
        string CandidateName,
        IModFormatHandler FormatHandler,
        IModPackageInspection Inspection)
    {
        public string Key => CreateKey(Inspection.Metadata.Id, Inspection.Metadata.Version);
    }
}
