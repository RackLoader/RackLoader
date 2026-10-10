param(
    [switch]$Push,
    [string]$Version
)

$ErrorActionPreference = "Stop"
$projectPath = Join-Path $PSScriptRoot "RackLoader.ModSdk.csproj"
$outputPath = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\artifacts\nuget"))

if ($Push -and [string]::IsNullOrWhiteSpace($env:NUGET_API_KEY)) {
    throw "Set NUGET_API_KEY before publishing."
}

New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
$packArguments = @("pack", $projectPath, "--configuration", "Release", "--output", $outputPath)
if (-not [string]::IsNullOrWhiteSpace($Version)) {
    $packArguments += "-p:Version=$Version"
}

& dotnet @packArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet pack failed with exit code $LASTEXITCODE."
}

$package = Get-ChildItem -LiteralPath $outputPath -Filter "RackLoader.ModSdk.*.nupkg" |
    Where-Object { $_.Name -notlike "*.snupkg" } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (-not $package) {
    throw "The .nupkg file was not created in $outputPath."
}

Write-Host "NuGet package: $($package.FullName)"
if ($Push) {
    & dotnet nuget push $package.FullName --api-key $env:NUGET_API_KEY --source "https://api.nuget.org/v3/index.json" --skip-duplicate
    if ($LASTEXITCODE -ne 0) {
        throw "NuGet publish failed with exit code $LASTEXITCODE."
    }
}
