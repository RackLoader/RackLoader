using System;
using System.IO;
using System.Linq;

namespace RackLoader.Rkm;

public static class RkmPaths
{
    public static string ValidatePackagePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || path.StartsWith("/", StringComparison.Ordinal))
            throw new InvalidDataException($"Invalid package path '{path}'. Use a relative path with forward slashes.");

        var segments = path.Split('/');
        if (segments.Any(segment =>
                string.IsNullOrWhiteSpace(segment) ||
                segment is "." or ".." ||
                segment.EndsWith(' ') ||
                segment.EndsWith('.') ||
                segment.Contains(':') ||
                segment.Any(char.IsControl) ||
                segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                IsReservedWindowsName(segment)))
        {
            throw new InvalidDataException($"Invalid package path '{path}'.");
        }

        return path;
    }

    public static string GetPathUnderRoot(string rootDirectory, string packagePath)
    {
        ValidatePackagePath(packagePath);

        var root = Path.GetFullPath(rootDirectory);
        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(root, packagePath.Replace('/', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (!fullPath.StartsWith(rootPrefix, comparison))
            throw new InvalidDataException($"Package path '{packagePath}' escapes its destination directory.");

        return fullPath;
    }

    public static string NormalizeAssetPath(string assetPath)
    {
        var normalized = assetPath.Replace('\\', '/');
        if (normalized.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Substring("assets/".Length);

        ValidatePackagePath("assets/" + normalized);
        return normalized;
    }

    private static bool IsReservedWindowsName(string segment)
    {
        var name = segment.Split('.')[0];
        if (name.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("NUL", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return name.Length == 4 &&
               (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
               name[3] is >= '1' and <= '9';
    }
}
