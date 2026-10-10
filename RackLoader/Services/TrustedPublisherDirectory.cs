using System.Security.Cryptography;
using System.Text.Json;
using BepInEx.Logging;

namespace RackLoader.Services;

public static class TrustedPublisherDirectory
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(6) };

    public static string ReadDefaultServiceUrl(string pluginDirectory)
    {
        var urlPath = Path.Combine(pluginDirectory, "worker-url.txt");
        return File.Exists(urlPath) ? File.ReadAllText(urlPath).Trim() : string.Empty;
    }

    public static IReadOnlyList<string> Load(
        string serviceUrl,
        string cachePath,
        IEnumerable<string> configuredKeyIds,
        ManualLogSource logger)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in configuredKeyIds)
        {
            var id = value.Trim();
            if (id.Length == 64 && id.All(Uri.IsHexDigit))
                ids.Add(id.ToLowerInvariant());
            else if (id.Length > 0)
                logger.LogWarning($"Ignoring invalid trusted publisher fingerprint '{id}'.");
        }

        var fullCachePath = Path.GetFullPath(cachePath);
        var loaded = false;
        if (!string.IsNullOrWhiteSpace(serviceUrl))
        {
            try
            {
                var baseUri = new Uri(serviceUrl.TrimEnd('/') + "/", UriKind.Absolute);
                if (baseUri.Scheme != Uri.UriSchemeHttps && !baseUri.IsLoopback)
                    throw new InvalidOperationException("The signing service URL must use HTTPS.");
                var endpoint = new Uri(baseUri, "api/trusted-keys");
                using var response = HttpClient.GetAsync(endpoint).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();
                using var responseStream = response.Content.ReadAsStream();
                using var document = JsonDocument.Parse(responseStream);
                var keys = document.RootElement.GetProperty("keys");
                var cacheEntries = new List<CachedTrustedKey>();
                foreach (var item in keys.EnumerateArray())
                {
                    var keyId = item.GetProperty("keyId").GetString() ?? string.Empty;
                    var publicKeyPem = item.GetProperty("publicKeyPem").GetString() ?? string.Empty;
                    using var rsa = RSA.Create();
                    rsa.ImportFromPem(publicKeyPem);
                    var actualKeyId = Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo())).ToLowerInvariant();
                    if (rsa.KeySize < 2048 || !string.Equals(actualKeyId, keyId, StringComparison.OrdinalIgnoreCase))
                        throw new CryptographicException("The trusted publisher directory contains an invalid key fingerprint.");
                    ids.Add(actualKeyId);
                    cacheEntries.Add(new CachedTrustedKey(actualKeyId, publicKeyPem));
                }

                Directory.CreateDirectory(Path.GetDirectoryName(fullCachePath)!);
                File.WriteAllText(fullCachePath, JsonSerializer.Serialize(cacheEntries));
                loaded = true;
                logger.LogInfo($"Loaded {cacheEntries.Count} trusted publisher key(s) from the signing service.");
            }
            catch (Exception exception)
            {
                logger.LogWarning($"Could not refresh trusted publishers from '{serviceUrl}': {exception.Message}");
            }
        }

        if (!loaded && File.Exists(fullCachePath))
        {
            try
            {
                var cacheEntries = JsonSerializer.Deserialize<List<CachedTrustedKey>>(File.ReadAllText(fullCachePath)) ?? new();
                foreach (var item in cacheEntries)
                {
                    using var rsa = RSA.Create();
                    rsa.ImportFromPem(item.PublicKeyPem);
                    var actualKeyId = Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo())).ToLowerInvariant();
                    if (string.Equals(actualKeyId, item.KeyId, StringComparison.OrdinalIgnoreCase))
                        ids.Add(actualKeyId);
                }
                logger.LogInfo($"Using {cacheEntries.Count} cached trusted publisher key(s).");
            }
            catch (Exception exception)
            {
                logger.LogWarning($"Could not read trusted publisher cache: {exception.Message}");
            }
        }

        return ids.ToArray();
    }

    private sealed record CachedTrustedKey(string KeyId, string PublicKeyPem);
}
