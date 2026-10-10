namespace RackLoader.Models;

/// <summary>An exact mod identity required by another mod.</summary>
public sealed record ModDependency(string Id, string Version);
