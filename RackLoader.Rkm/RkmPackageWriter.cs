using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace RackLoader.Rkm;

public sealed class RkmPackageBuildOptions
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string EntryModuleFileName { get; init; } = string.Empty;
    public string EntryType { get; init; } = string.Empty;
    public string? PrivateKeyPath { get; init; }
    public string OutputPath { get; init; } = string.Empty;
    public bool Unsigned { get; init; }
    public Func<byte[], RkmSigningMaterial>? Signer { get; init; }
    public string? AssetDirectory { get; init; }
    public IReadOnlyList<string> ModulePaths { get; init; } = Array.Empty<string>();
    public IReadOnlyList<RkmDependency> Dependencies { get; init; } = Array.Empty<RkmDependency>();
}

public sealed record RkmSigningMaterial(string PublicKeyPem, RkmSignature Signature);

public static class RkmPackageWriter
{
    private const string PublicKeyEntryName = "public-key.pem";
    private const string SignatureEntryName = "signature.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static string Build(RkmPackageBuildOptions options)
    {
        ValidateOptions(options);
        var payloads = CollectPayloads(options);
        var outputPath = Path.GetFullPath(options.OutputPath);
        var privateKeyPath = string.IsNullOrWhiteSpace(options.PrivateKeyPath)
            ? null
            : Path.GetFullPath(options.PrivateKeyPath);
        if ((privateKeyPath is not null && string.Equals(outputPath, privateKeyPath, StringComparison.OrdinalIgnoreCase)) ||
            payloads.Any(payload => string.Equals(outputPath, payload.SourcePath, StringComparison.OrdinalIgnoreCase)) ||
            (privateKeyPath is not null && payloads.Any(payload => string.Equals(privateKeyPath, payload.SourcePath, StringComparison.OrdinalIgnoreCase))))
        {
            throw new InvalidDataException("The package output path must be different from the signing key and all input files.");
        }

        if (!string.IsNullOrWhiteSpace(options.AssetDirectory) && Directory.Exists(options.AssetDirectory))
        {
            var assetRoot = Path.GetFullPath(options.AssetDirectory);
            if ((privateKeyPath is not null && IsWithinDirectory(assetRoot, privateKeyPath)) || IsWithinDirectory(assetRoot, outputPath))
                throw new InvalidDataException("Keep the signing key and generated package outside the asset directory.");
        }
        var outputDirectory = Path.GetDirectoryName(outputPath)!;
        Directory.CreateDirectory(outputDirectory);

        using var rsa = options.Signer is null && !options.Unsigned ? RSA.Create() : null;
        if (rsa is not null)
        {
            var keyPassword = Environment.GetEnvironmentVariable("RACKLOADER_RKM_KEY_PASSWORD");
            if (string.IsNullOrEmpty(keyPassword))
                throw new CryptographicException("Set RACKLOADER_RKM_KEY_PASSWORD to unlock the encrypted signing key.");
            rsa.ImportFromEncryptedPem(File.ReadAllText(options.PrivateKeyPath!).AsSpan(), keyPassword.AsSpan());
            if (rsa.KeySize < 2048)
                throw new CryptographicException("The signing key must be at least 2048 bits.");
        }
        var temporaryPath = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var publisherKeyId = "unsigned";

        try
        {
            var manifest = new RkmManifest
            {
                Id = options.Id,
                Name = options.Name,
                Version = options.Version,
                EntryPoint = new RkmEntryPoint
                {
                    Module = "modules/" + options.EntryModuleFileName,
                    Type = options.EntryType
                },
                Dependencies = options.Dependencies
                    .OrderBy(dependency => dependency.Id, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(dependency => dependency.Version, StringComparer.Ordinal)
                    .Select(dependency => new RkmDependency { Id = dependency.Id, Version = dependency.Version })
                    .ToList()
            };

            byte[] manifestBytes;
            using (var fileStream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var payload in payloads.OrderBy(item => item.PackagePath, StringComparer.Ordinal))
                {
                    using var source = new FileStream(payload.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var entry = archive.CreateEntry(payload.PackagePath, CompressionLevel.Optimal);
                    using var destination = entry.Open();
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var buffer = new byte[81920];
                    int bytesRead;

                    while ((bytesRead = source.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        hash.AppendData(buffer, 0, bytesRead);
                        destination.Write(buffer, 0, bytesRead);
                    }

                    var payloadFile = new RkmPayloadFile
                    {
                        Path = payload.PackagePath,
                        Sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()
                    };

                    if (payload.Kind == PayloadKind.Module)
                        manifest.Modules.Add(payloadFile);
                    else
                        manifest.Assets.Add(payloadFile);
                }

                RkmManifestValidation.Validate(manifest);
                manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
                RkmSigningMaterial? signingMaterial = null;
                if (!options.Unsigned)
                {
                    if (options.Signer is not null)
                    {
                        signingMaterial = options.Signer(manifestBytes);
                    }
                    else
                    {
                        var publicKeyBytes = rsa!.ExportSubjectPublicKeyInfo();
                        var keyId = Convert.ToHexString(SHA256.HashData(publicKeyBytes)).ToLowerInvariant();
                        signingMaterial = new RkmSigningMaterial(
                            RkmPem.EncodeSubjectPublicKeyInfo(publicKeyBytes),
                            new RkmSignature
                            {
                                Algorithm = "RSA-PSS-SHA256",
                                PublicKeyId = keyId,
                                Signature = Convert.ToBase64String(rsa.SignData(manifestBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
                            });
                    }

                    ValidateSigningMaterial(manifestBytes, signingMaterial);
                    publisherKeyId = signingMaterial.Signature.PublicKeyId;
                }

                WriteEntry(archive, "manifest.json", manifestBytes);
                if (signingMaterial is not null)
                {
                    WriteEntry(archive, PublicKeyEntryName, System.Text.Encoding.UTF8.GetBytes(signingMaterial.PublicKeyPem));
                    WriteEntry(archive, SignatureEntryName, JsonSerializer.SerializeToUtf8Bytes(signingMaterial.Signature, JsonOptions));
                }
            }

            File.Move(temporaryPath, outputPath, overwrite: true);
            return publisherKeyId;
        }
        catch
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
            throw;
        }
    }

    private static void ValidateOptions(RkmPackageBuildOptions options)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.Id) || options.Id.Length > 128)
            throw new InvalidDataException("A mod ID between 1 and 128 characters is required.");
        if (string.IsNullOrWhiteSpace(options.Name))
            throw new InvalidDataException("A mod name is required.");
        if (string.IsNullOrWhiteSpace(options.Version))
            throw new InvalidDataException("A mod version is required.");
        if (string.IsNullOrWhiteSpace(options.EntryType))
            throw new InvalidDataException("The fully qualified IRackMod entry type is required.");
        if (!options.Unsigned && options.Signer is null &&
            (string.IsNullOrWhiteSpace(options.PrivateKeyPath) || !File.Exists(options.PrivateKeyPath)))
            throw new FileNotFoundException("The RSA private key file was not found.", options.PrivateKeyPath);
        if (options.Unsigned && options.Signer is not null)
            throw new InvalidDataException("An unsigned package cannot have a signing service configured.");
        if (string.IsNullOrWhiteSpace(options.OutputPath))
            throw new InvalidDataException("An output .rkm path is required.");
        if (!options.OutputPath.EndsWith(".rkm", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The package output file must use the .rkm extension.");
        if (options.ModulePaths.Count == 0)
            throw new InvalidDataException("At least one module assembly is required.");

        var entryFileName = Path.GetFileName(options.EntryModuleFileName);
        if (string.IsNullOrWhiteSpace(entryFileName) ||
            !options.ModulePaths.Any(path => Path.GetFileName(path).Equals(entryFileName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("The entry module must be one of the supplied module assemblies.");
        }
    }

    private static void ValidateSigningMaterial(byte[] manifestBytes, RkmSigningMaterial material)
    {
        if (material is null || string.IsNullOrWhiteSpace(material.PublicKeyPem) || material.Signature is null)
            throw new CryptographicException("The signing service returned incomplete signature metadata.");
        if (!string.Equals(material.Signature.Algorithm, "RSA-PSS-SHA256", StringComparison.Ordinal))
            throw new CryptographicException("The signing service returned an unsupported signature algorithm.");

        using var rsa = RSA.Create();
        rsa.ImportFromPem(material.PublicKeyPem);
        var publicKey = rsa.ExportSubjectPublicKeyInfo();
        var keyId = Convert.ToHexString(SHA256.HashData(publicKey)).ToLowerInvariant();
        if (rsa.KeySize < 2048 || !string.Equals(keyId, material.Signature.PublicKeyId, StringComparison.OrdinalIgnoreCase))
            throw new CryptographicException("The signing service returned a mismatched public-key ID.");
        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(material.Signature.Signature);
        }
        catch (FormatException exception)
        {
            throw new CryptographicException("The signing service returned an invalid Base64 signature.", exception);
        }
        if (!rsa.VerifyData(manifestBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
            throw new CryptographicException("The signing service returned a signature that does not match the manifest.");
    }

    private static List<PayloadInput> CollectPayloads(RkmPackageBuildOptions options)
    {
        var payloads = new List<PayloadInput>();
        foreach (var modulePath in options.ModulePaths)
        {
            if (!File.Exists(modulePath))
                throw new FileNotFoundException("A module assembly was not found.", modulePath);
            if (!Path.GetExtension(modulePath).Equals(".dll", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Module '{modulePath}' must be a managed .dll assembly.");
            _ = AssemblyName.GetAssemblyName(modulePath);

            var packagePath = RkmPaths.ValidatePackagePath("modules/" + Path.GetFileName(modulePath));
            payloads.Add(new PayloadInput(Path.GetFullPath(modulePath), packagePath, PayloadKind.Module));
        }

        if (!string.IsNullOrWhiteSpace(options.AssetDirectory) && Directory.Exists(options.AssetDirectory))
        {
            var assetRoot = Path.GetFullPath(options.AssetDirectory);
            if ((File.GetAttributes(assetRoot) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("The asset directory cannot be a symbolic link or junction.");

            foreach (var assetPath in EnumerateAssetFiles(assetRoot))
            {
                var relativePath = Path.GetRelativePath(assetRoot, assetPath).Replace('\\', '/');
                var packagePath = RkmPaths.ValidatePackagePath("assets/" + relativePath);
                payloads.Add(new PayloadInput(Path.GetFullPath(assetPath), packagePath, PayloadKind.Asset));
            }
        }

        var duplicate = payloads
            .GroupBy(item => item.PackagePath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidDataException($"More than one input maps to package path '{duplicate.Key}'.");

        return payloads;
    }

    private static IEnumerable<string> EnumerateAssetFiles(string assetRoot)
    {
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(assetRoot);

        while (pendingDirectories.Count > 0)
        {
            var directory = pendingDirectories.Pop();
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException($"Asset links are not supported: '{file}'.");
                yield return file;
            }

            foreach (var childDirectory in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
            {
                if ((File.GetAttributes(childDirectory) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException($"Asset links are not supported: '{childDirectory}'.");
                pendingDirectories.Push(childDirectory);
            }
        }
    }

    private static void WriteEntry(ZipArchive archive, string name, byte[] contents)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(contents, 0, contents.Length);
    }

    private static bool IsWithinDirectory(string directory, string path)
    {
        var relative = Path.GetRelativePath(directory, path);
        return !Path.IsPathRooted(relative) &&
               relative != ".." &&
               !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private sealed record PayloadInput(string SourcePath, string PackagePath, PayloadKind Kind);

    private enum PayloadKind
    {
        Module,
        Asset
    }
}
