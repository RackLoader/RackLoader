using RackLoader.Models;
using System.Collections.Generic;

namespace RackLoader.Abstractions;

/// <summary>
/// Bridges one supported mod representation to RackLoader's format-neutral lifecycle.
/// </summary>
public interface IModFormatHandler
{
    string Name { get; }

    bool CanHandle(IModCandidate candidate);

    IModPackageInspection Inspect(IModCandidate candidate);

    ILoadedMod Load(
        IModPackageInspection package,
        IReadOnlyList<ILoadedMod> dependencies,
        ModLoadContext context);
}
