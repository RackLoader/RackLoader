**[🇷🇺 Русская версия](README.ru.md)**

# RackLoader

A BepInEx-based mod loader and mod development toolkit for **Data Center**. RackLoader targets BepInEx 6 IL2CPP and .NET 6.

## Features

- Loads mods packaged as `.rkm` archives.
- Verifies package signatures, module hashes, and publisher trust.
- Resolves mod dependencies by exact version and loads them in isolated contexts.
- Includes a NuGet SDK that builds and signs `.rkm` files from an MSBuild project.
- Supports mod assets such as textures and models.

## Requirements

- Data Center installed on Windows.
- BepInEx 6 IL2CPP for the game.
- .NET 6 SDK to build RackLoader or a mod.

## Build and install RackLoader

From the repository root, run:

```powershell
dotnet build .\RackLoader.sln -p:DeployToGame=true
```

This builds RackLoader and deploys it to the configured Data Center installation.

## Create a mod with the NuGet SDK

Add the package and mod metadata to the mod project's `.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <AssemblyName>ExampleWeather</AssemblyName>
    <RackModId>example.weather</RackModId>
    <RackModName>Example Weather</RackModName>
    <RackModVersion>1.0.0</RackModVersion>
    <RackModEntryType>Example.WeatherMod</RackModEntryType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="RackLoader.ModSdk" Version="1.0.1" />
  </ItemGroup>
</Project>
```

The SDK uses `D:\SteamLibrary\steamapps\common\Data Center` as the default game directory. Set `<RackModGameDir>` in the project to use a different installation.

Implement the mod entry point with `IRackMod`:

```csharp
using RackLoader.Contracts;

namespace Example;

public sealed class WeatherMod : IRackMod
{
    public void Load(IRackModContext context)
    {
        context.LogInfo($"Loaded {context.Name} {context.Version}");
    }
}
```

Build the project in Release mode:

```powershell
dotnet build .\ExampleWeather.csproj -c Release
```

The SDK invokes the packer after the build and creates `bin\Release\net6.0\ExampleWeather-1.0.0.rkm`. Copy the package to `Data Center\BepInEx\plugins\RackLoader\Mods`. Installing the NuGet SDK does not install the loader into the game.

## Sign a mod

Get a publisher token from the publisher site and pass it through the `RACKLOADER_DEVELOPER_KEY` environment variable. Keep the token out of the project file and source control:

```powershell
$env:RACKLOADER_DEVELOPER_KEY = "rkd_..."
dotnet build .\ExampleWeather.csproj -c Release
Remove-Item Env:RACKLOADER_DEVELOPER_KEY
```

The packer sends the package to the configured publisher Worker for signing. The default service URL is included in the SDK. Override it with the `RackModSigningServiceUrl` MSBuild property or the `RACKLOADER_SIGNING_URL` environment variable.

The loader retrieves trusted publisher keys from the RackLoader Publisher Worker. Its URL can be configured in `BepInEx/plugins/RackLoader/worker-url.txt` or with `[Security] SigningServiceUrl`. The last successfully retrieved publisher list is cached for offline startup.

## Include assets and dependencies

Put assets in an `Assets` directory next to the mod project file. Open an asset through the mod context:

```csharp
using var icon = context.OpenAsset("textures/icon.png");
```

Declare modules and dependencies in the project:

```xml
<ItemGroup>
  <RackModModule Include="$(MSBuildProjectDirectory)\Modules\WeatherAssets.dll" />
  <RackModDependency Include="example.shared" Version="2.0.0" />
  <ProjectReference Include="..\Shared\Shared.csproj" />
</ItemGroup>
```

`RackModDependency` records the dependency ID and exact version in the manifest; it does not add a compile-time reference. Add a `ProjectReference` or `Reference` if the mod source uses dependency types. The dependency's `.rkm` package must also be present in the `Mods` directory.

## Local signing and unsigned packages

To create a local signing key pair:

```powershell
dotnet run --project .\RackLoader.Packer\RackLoader.Packer.csproj -- keygen --private-key D:\RackLoaderKeys\publisher.private.pem --public-key D:\RackLoaderKeys\publisher.public.pem
```

Set `<RackModPrivateKey>D:\RackLoaderKeys\publisher.private.pem</RackModPrivateKey>` in the mod project and provide the key password through `RACKLOADER_RKM_KEY_PASSWORD` before building. Add the key fingerprint to `TrustedPublisherKeyIds` in the RackLoader configuration.

To build an unsigned archive, set `<RackModUnsigned>true</RackModUnsigned>`. Loading unsigned mods requires `AllowUnsignedMods=true` in the loader configuration; this option is disabled by default.

## Pack and publish the NuGet SDK

The SDK project and package README are in `RackLoader.ModSdk`. Create a NuGet package with:

```powershell
.\RackLoader.ModSdk\publish.ps1
```

The package is written to `artifacts\nuget`. To publish it, set `NUGET_API_KEY` and run the script with `-Push`:

```powershell
$env:NUGET_API_KEY = "..."
.\RackLoader.ModSdk\publish.ps1 -Push
Remove-Item Env:NUGET_API_KEY
```

## Publisher service

The Cloudflare Worker, activation site, and deployment instructions are in [RackLoader.Cloudflare](RackLoader.Cloudflare/README.md).

> Package signatures verify publisher identity and file integrity. They do not sandbox mod code.
