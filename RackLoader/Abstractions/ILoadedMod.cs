using RackLoader.Models;

namespace RackLoader.Abstractions;

/// <summary>
/// Runtime handle retained by RackLoader after a format handler activates a mod.
/// </summary>
public interface ILoadedMod
{
    ModMetadata Metadata { get; }
}
