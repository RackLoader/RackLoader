using System.Collections.Generic;
using System.Linq;
using RackLoader.Abstractions;
using RackLoader.Models;
using RackLoader.Rkm;

namespace RackLoader.Formats.Rkm;

public sealed class RkmPackageInspection : IModPackageInspection
{
    public RkmPackageInspection(RkmVerifiedPackage package)
    {
        Package = package;
        Metadata = new ModMetadata(package.Manifest.Id, package.Manifest.Name, package.Manifest.Version);
        Dependencies = package.Manifest.Dependencies
            .Select(dependency => new ModDependency(dependency.Id, dependency.Version))
            .ToArray();
    }

    public RkmVerifiedPackage Package { get; }
    public ModMetadata Metadata { get; }
    public IReadOnlyList<ModDependency> Dependencies { get; }
}
