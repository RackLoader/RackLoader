using System.Collections.Generic;

namespace RackLoader.Abstractions;

/// <summary>
/// Provides candidates without prescribing whether they come from files, folders, or another source.
/// </summary>
public interface IModSource
{
    string Name { get; }

    IEnumerable<IModCandidate> Discover();
}
