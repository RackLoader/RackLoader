using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RackLoader.Rkm;

public sealed record RkmVerifiedPackage(
    string ArchivePath,
    RkmManifest Manifest,
    string PublicKeyId,
    string ManifestHash,
    bool IsSigned);

public static class RkmPackageVerifier
{
    private const string ManifestEntryName = "manifest.json";
    private const string PublicKeyEntryName = "public-key.pem";
    private const string SignatureEntryName = "signature.json";
    private const long MaxFileBytes = 512L * 1024 * 1024;
    private const long MaxPackagePayloadBytes = 2L * 1024 * 1024 * 1024;
    private const int MaxArchiveEntries = 10000;
    private const int MaxMetadataBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false
    };

    public static RkmVerifiedPackage Verify(
        string archivePath,
        IEnumerable<string> trustedPublicKeyIds,
        bool allowUnsigned = false)
    {
        var trusted = NormalizeTrustedIds(trustedPublicKeyIds);
        return VerifyCore(archivePath, trusted, null, allowUnsigned);
    }

    public static string Extract(
        RkmVerifiedPackage expectedPackage,
        string cacheRoot,
        IEnumerable<string> trustedPublicKeyIds,
        bool allowUnsigned = false)
    {
        if (expectedPackage is null)
            throw new ArgumentNullException(nameof(expectedPackage));

        var trusted = NormalizeTrustedIds(trustedPublicKeyIds);
        var cacheKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            expectedPackage.IsSigned + ":" + expectedPackage.PublicKeyId + ":" + expectedPackage.ManifestHash))).ToLowerInvariant();
        var fullCacheRoot = Path.GetFullPath(cacheRoot);
        Directory.CreateDirectory(fullCacheRoot);
        var finalDirectory = Path.Combine(fullCacheRoot, cacheKey);

        if (Directory.Exists(finalDirectory) && IsCacheValid(finalDirectory, expectedPackage.Manifest))
        {
            var verified = VerifyCore(expectedPackage.ArchivePath, trusted, null, allowUnsigned);
            EnsureSamePackage(expectedPackage, verified);
            return finalDirectory;
        }

        if (Directory.Exists(finalDirectory))
            Directory.Delete(finalDirectory, recursive: true);
        else if (File.Exists(finalDirectory))
            File.Delete(finalDirectory);

        var temporaryDirectory = Path.Combine(fullCacheRoot, cacheKey + ".tmp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var verified = VerifyCore(expectedPackage.ArchivePath, trusted, (payload, source) =>
            {
                var destinationPath = RkmPaths.GetPathUnderRoot(temporaryDirectory, payload.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                return new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            }, allowUnsigned);
            EnsureSamePackage(expectedPackage, verified);

            Directory.Move(temporaryDirectory, finalDirectory);
            return finalDirectory;
        }
        catch
        {
            if (Directory.Exists(temporaryDirectory))
                Directory.Delete(temporaryDirectory, recursive: true);
            throw;
        }
    }

    private static RkmVerifiedPackage VerifyCore(
        string archivePath,
        HashSet<string> trustedIds,
        Func<RkmPayloadFile, Stream, Stream?>? createPayloadDestination,
        bool allowUnsigned)
    {
        var fullArchivePath = Path.GetFullPath(archivePath);
        using var fileStream = new FileStream(fullArchivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read, leaveOpen: false);

        if (archive.Entries.Count > MaxArchiveEntries)
            throw new InvalidDataException("The package contains too many archive entries.");

        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            RkmPaths.ValidatePackagePath(entry.FullName);
            if (IsSymbolicLink(entry))
                throw new InvalidDataException($"Symbolic links are not allowed in packages ('{entry.FullName}').");
            if (!entries.TryAdd(entry.FullName, entry))
                throw new InvalidDataException($"The archive contains duplicate path '{entry.FullName}'.");
        }

        var manifestBytes = ReadMetadataEntry(GetRequiredEntry(entries, ManifestEntryName), MaxMetadataBytes);
        var hasPublicKey = entries.TryGetValue(PublicKeyEntryName, out var publicKeyEntry);
        var hasSignature = entries.TryGetValue(SignatureEntryName, out var signatureEntry);
        if (hasPublicKey != hasSignature)
            throw new InvalidDataException("A package must contain both public-key.pem and signature.json, or neither.");
        var isSigned = hasPublicKey;
        if (!isSigned && !allowUnsigned)
            throw new UnauthorizedAccessException("This package is unsigned. Enable AllowUnsignedMods in RackLoader's configuration to load it.");

        var manifest = JsonSerializer.Deserialize<RkmManifest>(manifestBytes, JsonOptions)
            ?? throw new InvalidDataException("The package manifest is invalid.");
        RkmManifestValidation.Validate(manifest);

        var keyId = "unsigned";
        if (isSigned)
        {
            var publicKeyBytes = ReadMetadataEntry(publicKeyEntry!, MaxMetadataBytes);
            var signatureBytes = ReadMetadataEntry(signatureEntry!, MaxMetadataBytes);
            var signature = JsonSerializer.Deserialize<RkmSignature>(signatureBytes, JsonOptions)
                ?? throw new InvalidDataException("The package signature metadata is invalid.");
            if (!string.Equals(signature.Algorithm, "RSA-PSS-SHA256", StringComparison.Ordinal))
                throw new InvalidDataException($"Unsupported package signature algorithm '{signature.Algorithm}'.");

            using var rsa = RSA.Create();
            rsa.ImportFromPem(Encoding.UTF8.GetString(publicKeyBytes));
            if (rsa.KeySize < 2048)
                throw new CryptographicException("The package signing key is smaller than 2048 bits.");

            keyId = Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo())).ToLowerInvariant();
            if (!string.Equals(signature.PublicKeyId, keyId, StringComparison.OrdinalIgnoreCase))
                throw new CryptographicException("The package public-key ID does not match the embedded key.");
            if (trustedIds.Count == 0 || !trustedIds.Contains(keyId))
                throw new UnauthorizedAccessException($"Publisher key {keyId} is not trusted. Refresh the trusted publisher list or add it to RackLoader's trusted key IDs.");

            byte[] signatureValue;
            try
            {
                signatureValue = Convert.FromBase64String(signature.Signature);
            }
            catch (FormatException exception)
            {
                throw new InvalidDataException("The package signature is not valid Base64.", exception);
            }

            if (!rsa.VerifyData(manifestBytes, signatureValue, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
                throw new CryptographicException("The package manifest signature is invalid.");
        }

        var expectedEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ManifestEntryName };
        if (isSigned)
        {
            expectedEntries.Add(PublicKeyEntryName);
            expectedEntries.Add(SignatureEntryName);
        }
        foreach (var file in manifest.Modules.Concat(manifest.Assets))
            expectedEntries.Add(file.Path);

        if (entries.Keys.Any(name => !expectedEntries.Contains(name)) || expectedEntries.Any(name => !entries.ContainsKey(name)))
            throw new InvalidDataException("The archive entries do not match the signed manifest.");

        long totalBytes = 0;
        foreach (var payload in manifest.Modules.Concat(manifest.Assets))
        {
            var entry = entries[payload.Path];
            if (entry.Length < 0 || entry.Length > MaxFileBytes)
                throw new InvalidDataException($"Payload '{payload.Path}' exceeds the size limit.");
            totalBytes = checked(totalBytes + entry.Length);
            if (totalBytes > MaxPackagePayloadBytes)
                throw new InvalidDataException("The package payload exceeds the total size limit.");

            using var input = entry.Open();
            using var output = createPayloadDestination?.Invoke(payload, input);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long actualLength = 0;
            int bytesRead;
            while ((bytesRead = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                actualLength = checked(actualLength + bytesRead);
                if (actualLength > MaxFileBytes)
                    throw new InvalidDataException($"Payload '{payload.Path}' exceeds the size limit.");
                hash.AppendData(buffer, 0, bytesRead);
                output?.Write(buffer, 0, bytesRead);
            }

            if (actualLength != entry.Length)
                throw new InvalidDataException($"Payload '{payload.Path}' has an unexpected uncompressed size.");

            var actualHash = hash.GetHashAndReset();
            var expectedHash = Convert.FromHexString(payload.Sha256);
            if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
                throw new CryptographicException($"Payload hash mismatch for '{payload.Path}'.");
        }

        var manifestHash = Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant();
        return new RkmVerifiedPackage(fullArchivePath, manifest, keyId, manifestHash, isSigned);
    }

    private static HashSet<string> NormalizeTrustedIds(IEnumerable<string> trustedPublicKeyIds)
    {
        if (trustedPublicKeyIds is null)
            throw new ArgumentNullException(nameof(trustedPublicKeyIds));

        return trustedPublicKeyIds
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static ZipArchiveEntry GetRequiredEntry(
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        string name)
    {
        if (!entries.TryGetValue(name, out var entry))
            throw new InvalidDataException($"Required package entry '{name}' is missing.");
        if (entry.Length < 0 || entry.Length > MaxMetadataBytes)
            throw new InvalidDataException($"Package metadata entry '{name}' exceeds the size limit.");
        return entry;
    }

    private static byte[] ReadMetadataEntry(ZipArchiveEntry entry, int maximumBytes)
    {
        using var input = entry.Open();
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int bytesRead;
        while ((bytesRead = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (output.Length + bytesRead > maximumBytes)
                throw new InvalidDataException($"Package metadata entry '{entry.FullName}' exceeds the size limit.");
            output.Write(buffer, 0, bytesRead);
        }
        return output.ToArray();
    }

    private static bool IsSymbolicLink(ZipArchiveEntry entry)
    {
        const int UnixFileTypeMask = 0xF000;
        const int UnixSymbolicLink = 0xA000;
        return (((entry.ExternalAttributes >> 16) & UnixFileTypeMask) == UnixSymbolicLink);
    }

    private static bool IsCacheValid(string cacheDirectory, RkmManifest manifest)
    {
        foreach (var payload in manifest.Modules.Concat(manifest.Assets))
        {
            var path = RkmPaths.GetPathUnderRoot(cacheDirectory, payload.Path);
            if (!File.Exists(path))
                return false;
            using var stream = File.OpenRead(path);
            using var sha256 = SHA256.Create();
            var actual = sha256.ComputeHash(stream);
            if (!CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(payload.Sha256)))
                return false;
        }
        return true;
    }

    private static void EnsureSamePackage(RkmVerifiedPackage expected, RkmVerifiedPackage actual)
    {
        if (expected.IsSigned != actual.IsSigned ||
            !string.Equals(expected.PublicKeyId, actual.PublicKeyId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(expected.ManifestHash, actual.ManifestHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new CryptographicException("The package changed after it was inspected.");
        }
    }
}
