using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using RackLoader.Abstractions;
using RackLoader.Contracts;
using RackLoader.Models;
using RackLoader.Rkm;

namespace RackLoader.Formats.Rkm;

public sealed class RkmFormatHandler : IModFormatHandler
{
    private readonly string _cacheRoot;
    private readonly string[] _trustedKeyIds;
    private readonly bool _allowUnsigned;
    private readonly ManualLogSource _logger;

    public RkmFormatHandler(string cacheRoot, IEnumerable<string> trustedKeyIds, bool allowUnsigned, ManualLogSource logger)
    {
        _cacheRoot = Path.GetFullPath(cacheRoot);
        _trustedKeyIds = trustedKeyIds?.ToArray() ?? throw new ArgumentNullException(nameof(trustedKeyIds));
        _allowUnsigned = allowUnsigned;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string Name => "Rack Mod (.rkm)";

    public bool CanHandle(IModCandidate candidate) => candidate is RkmFileCandidate;

    public IModPackageInspection Inspect(IModCandidate candidate)
    {
        if (candidate is not RkmFileCandidate fileCandidate)
            throw new ArgumentException("Expected an .rkm file candidate.", nameof(candidate));

        var verifiedPackage = RkmPackageVerifier.Verify(fileCandidate.FilePath, _trustedKeyIds, _allowUnsigned);
        return new RkmPackageInspection(verifiedPackage);
    }

    public ILoadedMod Load(
        IModPackageInspection package,
        IReadOnlyList<ILoadedMod> dependencies,
        ModLoadContext context)
    {
        if (package is not RkmPackageInspection rkmPackage)
            throw new ArgumentException("Expected an inspected .rkm package.", nameof(package));

        var extractedDirectory = RkmPackageVerifier.Extract(rkmPackage.Package, _cacheRoot, _trustedKeyIds, _allowUnsigned);
        var runtimeDependencies = dependencies.Select(dependency => dependency as LoadedRkmMod
            ?? throw new InvalidOperationException("An .rkm dependency was loaded by an incompatible handler.")).ToArray();
        var manifest = rkmPackage.Package.Manifest;
        var loadContext = new RkmModAssemblyLoadContext(
            $"RackMod:{manifest.Id}@{manifest.Version}",
            extractedDirectory,
            manifest.Modules,
            runtimeDependencies);

        try
        {
            var entryPath = RkmPaths.GetPathUnderRoot(extractedDirectory, manifest.EntryPoint.Module);
            var entryAssembly = loadContext.LoadFromAssemblyPath(entryPath);
            var entryType = entryAssembly.GetType(manifest.EntryPoint.Type, throwOnError: true, ignoreCase: false)!;
            if (!typeof(IRackMod).IsAssignableFrom(entryType) || entryType.IsAbstract || entryType.ContainsGenericParameters)
            {
                throw new InvalidOperationException(
                    $"Entry type '{manifest.EntryPoint.Type}' must be a concrete implementation of {nameof(IRackMod)}.");
            }

            if (Activator.CreateInstance(entryType) is not IRackMod mod)
                throw new InvalidOperationException($"Entry type '{manifest.EntryPoint.Type}' needs a public parameterless constructor.");

            var metadata = new ModMetadata(manifest.Id, manifest.Name, manifest.Version);
            var modContext = new RkmModContext(metadata, _logger, extractedDirectory, manifest.Assets);
            mod.Load(modContext);
            return new LoadedRkmMod(metadata, mod, loadContext, modContext);
        }
        catch
        {
            loadContext.Unload();
            throw;
        }
    }
}
