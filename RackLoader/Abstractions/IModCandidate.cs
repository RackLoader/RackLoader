namespace RackLoader.Abstractions;

/// <summary>
/// Opaque candidate supplied by a mod source. A format handler can inspect its concrete type.
/// </summary>
public interface IModCandidate
{
    string DisplayName { get; }
}
