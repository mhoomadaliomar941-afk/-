# Builds the mod, runs the unit tests and assembles a ready-to-install package in dist\.
# Requires the .NET 8 SDK (https://dotnet.microsoft.com/download). Run from PowerShell:
#   powershell -ExecutionPolicy Bypass -File packaging\build-release.ps1
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

[xml]$props = Get-Content "Directory.Build.props"
$version = $props.Project.PropertyGroup.Version
$out = "dist\OrganizationEmpire-v$version"
Write-Host "== Organization Empire $version"

dotnet test tests\OrganizationEmpire.Core.Tests\OrganizationEmpire.Core.Tests.csproj -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "Unit tests failed" }

dotnet build src\OrganizationEmpire\OrganizationEmpire.csproj -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
if (Test-Path "$out.zip") { Remove-Item "$out.zip" -Force }
New-Item -ItemType Directory -Force -Path "$out\scripts\OrganizationEmpire" | Out-Null
Copy-Item "src\OrganizationEmpire\bin\Release\net48\OrganizationEmpire.dll" "$out\scripts\"

dotnet run --project tools\OrganizationEmpire.PackTool -c Release -- "$out\scripts\OrganizationEmpire"
if ($LASTEXITCODE -ne 0) { throw "PackTool failed" }

Copy-Item README.md "$out\README.md"
Copy-Item docs "$out\docs" -Recurse
if (Test-Path LICENSE) { Copy-Item LICENSE "$out\LICENSE" }

Compress-Archive -Path $out -DestinationPath "$out.zip"
Write-Host "== Done: $out.zip"

# Optional: install directly into your game folder, e.g.
#   Copy-Item "$out\scripts\*" "C:\Program Files\Rockstar Games\Grand Theft Auto V\scripts\" -Recurse -Force
