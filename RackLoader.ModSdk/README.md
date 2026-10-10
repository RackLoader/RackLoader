# RackLoader.ModSdk

NuGet build package that adds `.rkm` packaging to a Data Center mod project. It contains the `IRackMod` compile-time contract, the MSBuild target, and the packer binaries. It does not install the RackLoader runtime into the game.

## Use from a mod project

Add one package reference and the mod metadata to the project's `.csproj`:

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
    <PackageReference Include="RackLoader.ModSdk" Version="1.0.0" />
  </ItemGroup>
</Project>
```

The package adds the `RackLoader.Contracts` API and references to BepInEx, Harmony, Unity, and Data Center assemblies. By default it looks for the game at `D:\SteamLibrary\steamapps\common\Data Center`; set `<RackModGameDir>` to override it.

Implement the configured public entry type:

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

`dotnet build` builds the DLL and then creates `bin/<Configuration>/net6.0/ExampleWeather-1.0.0.rkm`. The `Assets` directory next to the project is included automatically. Read assets with `context.OpenAsset("textures/icon.png")`.

## Remote signing

Set the publisher token as a CI/local environment secret. Do not commit it to the project file:

```powershell
$env:RACKLOADER_DEVELOPER_KEY = "rkd_..."
dotnet build .\ExampleWeather.csproj -c Release
Remove-Item Env:RACKLOADER_DEVELOPER_KEY
```

The default signing endpoint is the RackLoader Publisher Worker. Override it with the `RackModSigningServiceUrl` MSBuild property or `RACKLOADER_SIGNING_URL` environment variable. Worker-issued publisher tokens are shown only once when the publisher activates their invitation.

## Optional package inputs

```xml
<ItemGroup>
  <RackModModule Include="$(MSBuildProjectDirectory)\Modules\WeatherAssets.dll" />
  <RackModDependency Include="example.shared" Version="2.0.0" />
</ItemGroup>
```

`RackModDependency` writes an exact ID/version requirement into the manifest. Add a normal `ProjectReference` or assembly `Reference` too if the mod source code uses that dependency's types; the dependency's `.rkm` must also be present in the game's `BepInEx/plugins/RackLoader/Mods` folder.

The resulting archive is not copied into the game automatically. Copy it into:

```text
<Data Center>\BepInEx\plugins\RackLoader\Mods\
```

## Build and publish this NuGet package

Build the `.nupkg` without publishing:

```powershell
.\RackLoader.ModSdk\publish.ps1
```

The package is written to `artifacts\nuget`. To publish it to nuget.org, first set a nuget.org API key as `NUGET_API_KEY`, then explicitly run:

```powershell
$env:NUGET_API_KEY = "<nuget.org API key>"
.\RackLoader.ModSdk\publish.ps1 -Push
Remove-Item Env:NUGET_API_KEY
```

Increment the package version before publishing a new release:

```powershell
.\RackLoader.ModSdk\publish.ps1 -Version 1.0.1
```

nuget.org package versions are immutable. Publishing requires a registered NuGet.org account and an API key with push access. The API key is not included in this repository or package.
