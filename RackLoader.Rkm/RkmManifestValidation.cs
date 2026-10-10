using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace RackLoader.Rkm;

public static class RkmManifestValidation
{
    private static readonly Regex ModIdPattern = new("^[A-Za-z0-9._-]{1,128}$", RegexOptions.CultureInvariant);
    private static readonly Regex Sha256Pattern = new("^[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant);

    public static void Validate(RkmManifest manifest)
    {
        if (manifest is null)
            throw new InvalidDataException("The package manifest is empty.");
        if (!string.Equals(manifest.Format, "RackMod", StringComparison.Ordinal) || manifest.FormatVersion != 1)
            throw new InvalidDataException("Unsupported Rack Mod package format or version.");
        ValidateId(manifest.Id, "mod");
        if (string.IsNullOrWhiteSpace(manifest.Name) || manifest.Name.Length > 256)
            throw new InvalidDataException("The mod name is missing or too long.");
        if (string.IsNullOrWhiteSpace(manifest.Version) || manifest.Version.Length > 128)
            throw new InvalidDataException("The mod version is missing or too long.");
        if (manifest.EntryPoint is null || string.IsNullOrWhiteSpace(manifest.EntryPoint.Type))
            throw new InvalidDataException("The entry point is missing.");
        if (manifest.Modules is null || manifest.Modules.Count == 0 || manifest.Assets is null || manifest.Dependencies is null)
            throw new InvalidDataException("The manifest must contain module, asset, and dependency lists.");

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ValidateFiles(manifest.Modules, "modules/", paths);
        ValidateFiles(manifest.Assets, "assets/", paths);

        if (!manifest.Modules.Any(module => string.Equals(module.Path, manifest.EntryPoint.Module, StringComparison.Ordinal)))
            throw new InvalidDataException("The entry point module is not listed in the module hashes.");
        RkmPaths.ValidatePackagePath(manifest.EntryPoint.Module);

        var dependencyIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dependency in manifest.Dependencies)
        {
            if (dependency is null)
                throw new InvalidDataException("The manifest contains an empty dependency entry.");
            ValidateId(dependency.Id, "dependency");
            if (string.IsNullOrWhiteSpace(dependency.Version) || dependency.Version.Length > 128)
                throw new InvalidDataException($"Dependency '{dependency.Id}' has no exact version.");
            if (string.Equals(dependency.Id, manifest.Id, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(dependency.Version, manifest.Version, StringComparison.Ordinal))
            {
                throw new InvalidDataException("A mod cannot depend on the same ID and version.");
            }
            if (!dependencyIds.Add(dependency.Id.ToUpperInvariant() + "\0" + dependency.Version))
                throw new InvalidDataException($"Dependency '{dependency.Id}' version '{dependency.Version}' is listed more than once.");
        }
    }

    private static void ValidateFiles(IEnumerable<RkmPayloadFile> files, string prefix, ISet<string> paths)
    {
        foreach (var file in files)
        {
            if (file is null)
                throw new InvalidDataException("The manifest contains an empty file entry.");
            RkmPaths.ValidatePackagePath(file.Path);
            if (!file.Path.StartsWith(prefix, StringComparison.Ordinal))
                throw new InvalidDataException($"Payload path '{file.Path}' must be under '{prefix}'.");
            if (!Sha256Pattern.IsMatch(file.Sha256))
                throw new InvalidDataException($"Payload '{file.Path}' has an invalid SHA-256 hash.");
            if (!paths.Add(file.Path))
                throw new InvalidDataException($"Payload path '{file.Path}' is listed more than once.");
        }
    }

    private static void ValidateId(string id, string kind)
    {
        if (string.IsNullOrWhiteSpace(id) || !ModIdPattern.IsMatch(id))
            throw new InvalidDataException($"The {kind} ID must use only letters, digits, '.', '_' or '-'.");
    }
}
