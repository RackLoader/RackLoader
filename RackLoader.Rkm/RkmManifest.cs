namespace RackLoader.Rkm;

public sealed class RkmManifest
{
    public string Format { get; set; } = "RackMod";
    public int FormatVersion { get; set; } = 1;
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public RkmEntryPoint EntryPoint { get; set; } = new();
    public List<RkmPayloadFile> Modules { get; set; } = new();
    public List<RkmPayloadFile> Assets { get; set; } = new();
    public List<RkmDependency> Dependencies { get; set; } = new();
}

public sealed class RkmEntryPoint
{
    public string Module { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}

public sealed class RkmPayloadFile
{
    public string Path { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
}

public sealed class RkmDependency
{
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
}

public sealed class RkmSignature
{
    public string Algorithm { get; set; } = string.Empty;
    public string PublicKeyId { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
}
