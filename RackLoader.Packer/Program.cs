using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net.Http.Headers;
using RackLoader.Rkm;

namespace RackLoader.Packer;

internal static class Program
{
    private const string PasswordEnvironmentVariable = "RACKLOADER_RKM_KEY_PASSWORD";
    private const string DeveloperKeyEnvironmentVariable = "RACKLOADER_DEVELOPER_KEY";
    private const string SigningUrlEnvironmentVariable = "RACKLOADER_SIGNING_URL";

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
                return PrintUsage();

            return args[0].ToLowerInvariant() switch
            {
                "keygen" => GenerateKeyPair(ParseArguments(args, 1)),
                "pack" => Pack(ParseArguments(args, 1)),
                "help" or "--help" or "-h" => PrintUsage(),
                _ => throw new ArgumentException($"Unknown command '{args[0]}'.")
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"RackLoader.Packer: {exception.Message}");
            return 1;
        }
    }

    private static int GenerateKeyPair(Arguments arguments)
    {
        var privatePath = Path.GetFullPath(arguments.Required("--private-key"));
        var publicPath = Path.GetFullPath(arguments.Required("--public-key"));
        if (string.Equals(privatePath, publicPath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Private and public keys must use different file paths.");
        if (File.Exists(privatePath) || File.Exists(publicPath))
            throw new IOException("A key file already exists. Choose new paths to avoid replacing a signing identity.");

        var password = Environment.GetEnvironmentVariable(PasswordEnvironmentVariable);
        if (string.IsNullOrEmpty(password))
            password = ReadNewPassword();
        if (password.Length < 12)
            throw new ArgumentException("Use a signing-key password with at least 12 characters.");

        using var rsa = RSA.Create();
        rsa.KeySize = 3072;
        var encryption = new PbeParameters(
            PbeEncryptionAlgorithm.Aes256Cbc,
            HashAlgorithmName.SHA256,
            iterationCount: 200_000);
        var encryptedPrivateKey = rsa.ExportEncryptedPkcs8PrivateKey(password.AsSpan(), encryption);
        var privatePem = RkmPem.EncodeEncryptedPrivateKeyInfo(encryptedPrivateKey);
        var publicPem = RkmPem.EncodeSubjectPublicKeyInfo(rsa.ExportSubjectPublicKeyInfo());

        Directory.CreateDirectory(Path.GetDirectoryName(privatePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(publicPath)!);
        File.WriteAllText(privatePath, privatePem, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.WriteAllText(publicPath, publicPem, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var keyId = Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo())).ToLowerInvariant();
        Console.WriteLine($"Private key: {privatePath}");
        Console.WriteLine($"Public key:  {publicPath}");
        Console.WriteLine($"Publisher key ID: {keyId}");
        Console.WriteLine("Keep the private key and its password out of source control.");
        return 0;
    }

    private static int Pack(Arguments arguments)
    {
        var dependencies = arguments.Many("--dependency").Select(ParseDependency).ToArray();
        var developerKey = Environment.GetEnvironmentVariable(DeveloperKeyEnvironmentVariable);
        var unsigned = string.Equals(arguments.Optional("--unsigned"), "true", StringComparison.OrdinalIgnoreCase);
        if (!unsigned && !string.IsNullOrWhiteSpace(developerKey) &&
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PasswordEnvironmentVariable)))
            throw new ArgumentException("Set either a developer key or a local signing-key password, not both.");

        var signingUrl = arguments.Optional("--signing-url") ?? Environment.GetEnvironmentVariable(SigningUrlEnvironmentVariable) ?? FindLocalWorkerUrl();
        Func<byte[], RkmSigningMaterial>? signer = null;
        string? privateKeyPath = arguments.Optional("--private-key");
        if (!unsigned && !string.IsNullOrWhiteSpace(developerKey))
        {
            if (string.IsNullOrWhiteSpace(signingUrl))
                throw new InvalidOperationException($"Set --signing-url or {SigningUrlEnvironmentVariable} to the deployed RackLoader Worker URL.");
            signer = manifestBytes => SignRemotely(signingUrl, developerKey, manifestBytes);
            privateKeyPath = null;
        }

        var options = new RkmPackageBuildOptions
        {
            Id = arguments.Required("--id"),
            Name = arguments.Required("--name"),
            Version = arguments.Required("--version"),
            EntryModuleFileName = arguments.Required("--entry-module"),
            EntryType = arguments.Required("--entry-type"),
            PrivateKeyPath = privateKeyPath,
            OutputPath = arguments.Required("--output"),
            Unsigned = unsigned,
            Signer = signer,
            AssetDirectory = arguments.Optional("--assets"),
            ModulePaths = arguments.Many("--module"),
            Dependencies = dependencies
        };

        var keyId = RkmPackageWriter.Build(options);
        Console.WriteLine($"Created: {Path.GetFullPath(options.OutputPath)}");
        Console.WriteLine(unsigned ? "Package is unsigned." : $"Signed by: {keyId}");
        return 0;
    }

    private static RkmSigningMaterial SignRemotely(string workerUrl, string developerKey, byte[] manifestBytes)
    {
        if (!Uri.TryCreate(workerUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttps && !baseUri.IsLoopback))
            throw new ArgumentException("The signing service URL must be a valid HTTPS URL.");

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", developerKey);
        var endpoint = new Uri(new Uri(baseUri.ToString().TrimEnd('/') + "/"), "api/sign");
        var body = JsonSerializer.Serialize(new { manifestBase64 = Convert.ToBase64String(manifestBytes) });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = client.PostAsync(endpoint, content).GetAwaiter().GetResult();
        var responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            string detail;
            try { detail = JsonDocument.Parse(responseText).RootElement.GetProperty("error").GetString() ?? response.ReasonPhrase ?? "request failed"; }
            catch { detail = response.ReasonPhrase ?? "request failed"; }
            throw new InvalidOperationException($"RackLoader signing service rejected the request ({(int)response.StatusCode}): {detail}");
        }

        var signature = JsonSerializer.Deserialize<RemoteSignatureResponse>(responseText, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidDataException("The signing service returned an empty response.");
        return new RkmSigningMaterial(signature.PublicKeyPem, new RkmSignature
        {
            Algorithm = signature.Algorithm,
            PublicKeyId = signature.PublicKeyId,
            Signature = signature.Signature
        });
    }

    private static string? FindLocalWorkerUrl()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; directory is not null && depth < 8; depth++, directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "RackLoader.Cloudflare", ".deployment", "worker-url.txt");
            if (File.Exists(candidate))
                return File.ReadAllText(candidate).Trim();
        }
        return null;
    }

    private static RkmDependency ParseDependency(string value)
    {
        var separator = value.IndexOf('|');
        if (separator <= 0 || separator == value.Length - 1)
            throw new ArgumentException($"Dependency '{value}' must be written as ID|EXACT_VERSION.");
        return new RkmDependency
        {
            Id = value.Substring(0, separator),
            Version = value.Substring(separator + 1)
        };
    }

    private static string ReadNewPassword()
    {
        var first = ReadSecret("Signing key password (12+ characters): ");
        var second = ReadSecret("Repeat password: ");
        if (!string.Equals(first, second, StringComparison.Ordinal))
            throw new ArgumentException("The passwords do not match.");
        return first;
    }

    private static string ReadSecret(string prompt)
    {
        if (Console.IsInputRedirected)
            throw new InvalidOperationException(
                $"Set {PasswordEnvironmentVariable} or run keygen in an interactive terminal.");

        Console.Write(prompt);
        var value = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return value.ToString();
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (value.Length > 0)
                    value.Length--;
                continue;
            }
            if (!char.IsControl(key.KeyChar))
                value.Append(key.KeyChar);
        }
    }

    private static Arguments ParseArguments(string[] args, int startIndex)
    {
        var values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        for (var index = startIndex; index < args.Length; index += 2)
        {
            var name = args[index];
            if (!name.StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
                throw new ArgumentException($"Expected an option and value near '{name}'.");
            if (!values.TryGetValue(name, out var list))
                values[name] = list = new List<string>();
            list.Add(args[index + 1]);
        }
        return new Arguments(values);
    }

    private static int PrintUsage()
    {
        Console.WriteLine("RackLoader.Packer keygen --private-key <file.pem> --public-key <file.pem>");
        Console.WriteLine("RackLoader.Packer pack --id <id> --name <name> --version <exact-version> --entry-module <dll> --entry-type <full-type> [--private-key <file.pem>] --output <file.rkm> --assets <directory> --module <dll> [--dependency <id|version>] [--unsigned true]");
        Console.WriteLine($"Set {DeveloperKeyEnvironmentVariable} to sign through the Cloudflare Worker; set {SigningUrlEnvironmentVariable} if the URL is not in the local deployment file.");
        Console.WriteLine($"Set {PasswordEnvironmentVariable} to unlock the encrypted private key when packing.");
        return 0;
    }

    private sealed class RemoteSignatureResponse
    {
        public string Algorithm { get; set; } = string.Empty;
        public string PublicKeyId { get; set; } = string.Empty;
        public string PublicKeyPem { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
    }

    private sealed class Arguments
    {
        private readonly IReadOnlyDictionary<string, List<string>> _values;

        public Arguments(IReadOnlyDictionary<string, List<string>> values) => _values = values;

        public string Required(string name)
        {
            var value = Optional(name);
            return string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException($"Required option '{name}' is missing.")
                : value;
        }

        public string? Optional(string name)
        {
            if (!_values.TryGetValue(name, out var values))
                return null;
            if (values.Count != 1)
                throw new ArgumentException($"Option '{name}' can only be specified once.");
            return values[0];
        }

        public IReadOnlyList<string> Many(string name) =>
            _values.TryGetValue(name, out var values) ? values : Array.Empty<string>();
    }
}
