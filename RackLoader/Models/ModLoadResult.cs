namespace RackLoader.Models;

public enum ModLoadStatus
{
    Loaded,
    UnsupportedFormat,
    AmbiguousFormat,
    DuplicatePackage,
    AlreadyLoaded,
    MissingDependency,
    DependencyFailed,
    DependencyCycle,
    Failed
}

public sealed record ModLoadResult(
    string SourceName,
    string CandidateName,
    ModLoadStatus Status,
    ModMetadata? Metadata = null,
    string? Message = null);
