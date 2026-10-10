using System.Collections.Generic;
using System.IO;
using System.Linq;
using RackLoader.Abstractions;

namespace RackLoader.Formats.Rkm;

public sealed class RkmDirectorySource : IModSource
{
    public RkmDirectorySource(string directory)
    {
        Directory = Path.GetFullPath(directory);
    }

    public string Name => "RackLoader .rkm directory";
    public string Directory { get; }

    public IEnumerable<IModCandidate> Discover()
    {
        if (!System.IO.Directory.Exists(Directory))
            yield break;

        foreach (var path in System.IO.Directory
                     .EnumerateFiles(Directory, "*", SearchOption.TopDirectoryOnly)
                     .Where(path => Path.GetExtension(path).Equals(".rkm", System.StringComparison.OrdinalIgnoreCase))
                     .OrderBy(Path.GetFileName, System.StringComparer.OrdinalIgnoreCase))
        {
            yield return new RkmFileCandidate(path);
        }
    }
}
