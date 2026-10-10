using BepInEx;
using BepInEx.Unity.IL2CPP;
using RackLoader.Abstractions;
using RackLoader.Formats.Rkm;
using RackLoader.Services;

namespace RackLoader;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInProcess("Data Center.exe")]
public sealed class RackLoaderPlugin : BasePlugin
{
    public const string PluginGuid = "dev.rackloader.core";
    public const string PluginName = "RackLoader";
    public const string PluginVersion = "0.1.0";

    private ModManager? _modManager;

    public override void Load()
    {
        Log.LogInfo($"{PluginName} {PluginVersion} initialized for Data Center.");

        var trustedPublisherKeyIds = Config.Bind(
            "Security",
            "TrustedPublisherKeyIds",
            string.Empty,
            "Additional SHA-256 publisher fingerprints, separated by commas or semicolons.");
        var allowUnsignedMods = Config.Bind(
            "Security",
            "AllowUnsignedMods",
            false,
            "Allow loading unsigned .rkm packages. Unsigned packages are not authenticated.");

        var pluginDirectory = Path.Combine(Paths.BepInExRootPath, "plugins", PluginName);
        var modDirectory = Path.Combine(pluginDirectory, "Mods");
        var cacheDirectory = Path.Combine(Paths.BepInExRootPath, "cache", PluginName);
        var signingServiceUrl = Config.Bind(
            "Security",
            "SigningServiceUrl",
            TrustedPublisherDirectory.ReadDefaultServiceUrl(pluginDirectory),
            "RackLoader Cloudflare Worker URL. The worker-url.txt file installed beside RackLoader sets the default.");
        var configuredKeyIds = trustedPublisherKeyIds.Value.Split(
            new[] { ',', ';', '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var trustedKeys = TrustedPublisherDirectory.Load(
            signingServiceUrl.Value,
            Path.Combine(Paths.ConfigPath, PluginName, "trusted-publishers.json"),
            configuredKeyIds,
            Log);

        var modManager = new ModManager(
            Log,
            new IModSource[] { new RkmDirectorySource(modDirectory) },
            new IModFormatHandler[] { new RkmFormatHandler(cacheDirectory, trustedKeys, allowUnsignedMods.Value, Log) },
            cacheDirectory);

        _modManager = modManager;
        modManager.LoadAvailableMods();
    }
}
