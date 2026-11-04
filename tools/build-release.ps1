<#
.SYNOPSIS
  Builds release files into artifacts/release and writes SHA256SUMS.txt.

.DESCRIPTION
  1. Runs the tests.
  2. Publishes rwat.exe (self-contained, single file, win-x64).
  3. Builds the bridge mod against the local game install (its assemblies are referenced, never copied) and zips it.
  4. Copies README.md, LICENSE and THIRD_PARTY_NOTICES.md, then hashes every release file.
  Publishing (GitHub release, Steam Workshop) is done by the maintainer by hand.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/build-release.ps1 -RimWorldDir "F:\SteamLibrary\steamapps\common\RimWorld"
#>
param(
    [Parameter(Mandatory = $true)][string]$RimWorldDir
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = $props.Project.PropertyGroup.Version
$out = Join-Path $root 'artifacts\release'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null

Write-Host "== tests"
dotnet test tests/AgentTranslation.Tests -c Release -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'tests failed' }

Write-Host "== rwat.exe"
$pub = Join-Path $root 'artifacts\publish-rwat'
if (Test-Path $pub) { Remove-Item $pub -Recurse -Force }
dotnet publish src/AgentTranslation.Server -c Release -o $pub -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'publish failed' }
Copy-Item (Join-Path $pub 'rwat.exe') $out

Write-Host "== bridge mod"
dotnet build src/AgentTranslation.Bridge -c Release -p:RimWorldDir="$RimWorldDir" -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'bridge build failed' }
$mod = Join-Path $root 'artifacts\bridge-mod\!AgentTranslation_Bridge'
$zip = Join-Path $out "AgentTranslation_Bridge-$version.zip"
Compress-Archive -Path $mod -DestinationPath $zip

foreach ($f in 'README.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md') { Copy-Item (Join-Path $root $f) $out }

Write-Host "== SHA256SUMS.txt"
$lines = Get-ChildItem $out -File | Where-Object Name -ne 'SHA256SUMS.txt' | Sort-Object Name | ForEach-Object {
    (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name
}
[IO.File]::WriteAllText((Join-Path $out 'SHA256SUMS.txt'), ($lines -join "`n") + "`n", (New-Object Text.UTF8Encoding $false))
Get-Content (Join-Path $out 'SHA256SUMS.txt')
Write-Host "Release files: $out"
