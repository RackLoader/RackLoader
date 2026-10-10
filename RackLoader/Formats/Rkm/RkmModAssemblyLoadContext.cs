using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using RackLoader.Rkm;

namespace RackLoader.Formats.Rkm;

internal sealed class RkmModAssemblyLoadContext : AssemblyLoadContext
{
    private readonly Dictionary<string, ModuleFile> _modules;
    private readonly LoadedRkmMod[] _dependencies;
    private readonly Dictionary<string, Assembly> _loadedModules = new(StringComparer.OrdinalIgnoreCase);

    public RkmModAssemblyLoadContext(
        string name,
        string packageDirectory,
        IEnumerable<RkmPayloadFile> moduleFiles,
        IEnumerable<LoadedRkmMod> dependencies)
        : base(name, isCollectible: true)
    {
        _dependencies = dependencies.ToArray();
        _modules = new Dictionary<string, ModuleFile>(StringComparer.OrdinalIgnoreCase);

        foreach (var module in moduleFiles)
        {
            var path = RkmPaths.GetPathUnderRoot(packageDirectory, module.Path);
            var assemblyName = AssemblyName.GetAssemblyName(path);
            if (string.IsNullOrWhiteSpace(assemblyName.Name))
                throw new BadImageFormatException($"Module '{module.Path}' has no assembly name.");
            if (!_modules.TryAdd(assemblyName.Name, new ModuleFile(path, assemblyName)))
                throw new FileLoadException($"Multiple module files define assembly '{assemblyName.Name}'.");
        }
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName.Name))
            return null;

        if (IsHostAssembly(assemblyName.Name))
            return FindDefaultAssembly(assemblyName);

        var ownAssembly = TryLoadOwnAssembly(assemblyName);
        if (ownAssembly is not null)
            return ownAssembly;

        var dependencyAssemblies = _dependencies
            .Select(dependency => dependency.TryLoadOwnAssembly(assemblyName))
            .Where(assembly => assembly is not null)
            .Cast<Assembly>()
            .Distinct()
            .ToArray();

        if (dependencyAssemblies.Length > 1)
        {
            throw new FileLoadException(
                $"Assembly '{assemblyName.Name}' is provided by more than one direct dependency. " +
                "Use a single dependency that supplies this assembly.");
        }

        return dependencyAssemblies.FirstOrDefault() ?? FindDefaultAssembly(assemblyName);
    }

    internal Assembly? TryLoadOwnAssembly(AssemblyName requestedName)
    {
        if (string.IsNullOrWhiteSpace(requestedName.Name) || !_modules.TryGetValue(requestedName.Name, out var module))
            return null;

        if (requestedName.Version is not null && module.AssemblyName.Version is not null &&
            requestedName.Version != module.AssemblyName.Version)
        {
            return null;
        }

        if (_loadedModules.TryGetValue(requestedName.Name, out var loaded))
            return loaded;

        loaded = LoadFromAssemblyPath(module.Path);
        _loadedModules.Add(requestedName.Name, loaded);
        return loaded;
    }

    private static Assembly? FindDefaultAssembly(AssemblyName requestedName)
    {
        return AssemblyLoadContext.Default.Assemblies.FirstOrDefault(assembly =>
        {
            var loadedName = assembly.GetName();
            return string.Equals(loadedName.Name, requestedName.Name, StringComparison.OrdinalIgnoreCase) &&
                   (requestedName.Version is null || loadedName.Version == requestedName.Version);
        });
    }

    private static bool IsHostAssembly(string name)
    {
        return name.Equals("RackLoader.Contracts", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("System", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("netstandard", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("mscorlib", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Microsoft.CSharp", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("BepInEx", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Unity", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Il2CppInterop", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Il2Cpp", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("MonoMod", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("0Harmony", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("HarmonyX", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ModuleFile(string Path, AssemblyName AssemblyName);
}
