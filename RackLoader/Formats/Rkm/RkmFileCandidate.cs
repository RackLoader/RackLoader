using RackLoader.Abstractions;

namespace RackLoader.Formats.Rkm;

public sealed record RkmFileCandidate(string FilePath) : IModCandidate
{
    public string DisplayName => Path.GetFileName(FilePath);
}
