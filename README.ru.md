**[🇬🇧 English version](README.md)**

# RackLoader

Загрузчик модов для Data Center на BepInEx 6 IL2CPP и .NET 6.

## Формат `.rkm` версии 1

`.rkm` — ZIP-архив с `manifest.json`, открытым ключом и подписью, DLL-модулями и ассетами. Манифест содержит SHA-256 хеши файлов и точные версии зависимостей. Он подписывается RSA-PSS/SHA-256.

Загрузчик проверяет подпись издателя через реестр RackLoader Publisher. URL Worker берётся из `BepInEx/plugins/RackLoader/worker-url.txt` или настройки `[Security] SigningServiceUrl`. Последний успешный список доверенных ключей кэшируется для офлайн-запуска.

Зависимости указываются как ID и точная версия. Загрузчик разрешает граф зависимостей и загружает требуемые версии раньше зависимых модов.

## Создание мода через NuGet

Пакет `RackLoader.ModSdk` подключает API `IRackMod`, ссылки на установленные BepInEx и Data Center сборки и запускает упаковщик после `dotnet build`. Добавь NuGet-пакет и поля мода в `.csproj`:

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

SDK по умолчанию ищет игру здесь: `D:\SteamLibrary\steamapps\common\Data Center`. При необходимости переопредели `<RackModGameDir>`.

Реализуй публичную точку входа:

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

### Подпись и сборка

Полученный на сайте издателя токен передавай через переменную окружения, не сохраняя его в `.csproj`:

```powershell
$env:RACKLOADER_DEVELOPER_KEY = "rkd_..."
dotnet build .\ExampleWeather.csproj -c Release
Remove-Item Env:RACKLOADER_DEVELOPER_KEY
```

После сборки появится `bin\Release\net6.0\ExampleWeather-1.0.0.rkm`. Упаковщик подписывает пакет через Worker; адрес сервиса уже задан в SDK. Для своего Worker можно переопределить `RackModSigningServiceUrl` или `RACKLOADER_SIGNING_URL`.

Скопируй `.rkm` в `Data Center\BepInEx\plugins\RackLoader\Mods`. Сам NuGet SDK не устанавливает загрузчик в игру.

### Ассеты и зависимости

Ассеты берутся из каталога `Assets` рядом с `.csproj` и открываются через контекст мода:

```csharp
using var icon = context.OpenAsset("textures/icon.png");
```

Дополнительные DLL и зависимости можно указать так:

```xml
<ItemGroup>
  <RackModModule Include="$(MSBuildProjectDirectory)\Modules\WeatherAssets.dll" />
  <RackModDependency Include="example.shared" Version="2.0.0" />
  <ProjectReference Include="..\Shared\Shared.csproj" />
</ItemGroup>
```

`RackModDependency` записывает в манифест точные ID и версию, но не компилирует ссылку. Если используешь типы зависимости в исходниках, добавь обычный `ProjectReference` или `Reference`. Её `.rkm` также должна находиться в папке `Mods`.

### Локальная подпись и неподписанные пакеты

Для локальной подписи создай зашифрованный ключ:

```powershell
dotnet run --project .\RackLoader.Packer\RackLoader.Packer.csproj -- keygen --private-key D:\RackLoaderKeys\publisher.private.pem --public-key D:\RackLoaderKeys\publisher.public.pem
```

Укажи `<RackModPrivateKey>D:\RackLoaderKeys\publisher.private.pem</RackModPrivateKey>` в `.csproj` и перед сборкой задай пароль ключа в `RACKLOADER_RKM_KEY_PASSWORD`. Fingerprint такого ключа нужно добавить в `TrustedPublisherKeyIds` конфигурации RackLoader.

Неподписанный архив можно собрать с `<RackModUnsigned>true</RackModUnsigned>`. Его загрузка отдельно разрешается только параметром `AllowUnsignedMods=true`; по умолчанию он выключен.

## Сборка и публикация NuGet SDK

Проект пакета, README и скрипт публикации находятся в `RackLoader.ModSdk`. Команда `.\RackLoader.ModSdk\publish.ps1` соберёт `.nupkg` в `artifacts\nuget`. После настройки NuGet.org API key пакет можно опубликовать явным запуском `.\RackLoader.ModSdk\publish.ps1 -Push`.

## Установка загрузчика в игру

```powershell
dotnet build .\RackLoader.sln -p:DeployToGame=true
```

Код подписи подтверждает издателя и целостность файлов, но не делает доверенный мод безопасным для запуска.
