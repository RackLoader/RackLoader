using System.Collections.Generic;
using RackLoader.Models;

namespace RackLoader.Abstractions;

/// <summary>Validated, format-neutral metadata retained between inspection and activation.</summary>
public interface IModPackageInspection
{
    ModMetadata Metadata { get; }
    IReadOnlyList<ModDependency> Dependencies { get; }
}
