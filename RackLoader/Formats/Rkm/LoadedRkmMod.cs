using System.Reflection;
using RackLoader.Abstractions;
using RackLoader.Models;
using RackLoader.Contracts;

namespace RackLoader.Formats.Rkm;

public sealed class LoadedRkmMod : ILoadedMod
{
    internal LoadedRkmMod(
        ModMetadata metadata,
        IRackMod instance,
        RkmModAssemblyLoadContext loadContext,
        RkmModContext modContext)
    {
        Metadata = metadata;
        Instance = instance;
        LoadContext = loadContext;
        ModContext = modContext;
    }

    public ModMetadata Metadata { get; }
    public IRackMod Instance { get; }
    internal RkmModAssemblyLoadContext LoadContext { get; }
    internal RkmModContext ModContext { get; }

    internal Assembly? TryLoadOwnAssembly(AssemblyName name) => LoadContext.TryLoadOwnAssembly(name);
}
