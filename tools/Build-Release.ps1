param([string]$Version = '1.0.0', [string]$Iscc = 'ISCC.exe', [switch]$NoRestore)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') { throw 'Use a stable semantic version (for example 1.0.1).' }
$publish = Join-Path $repo 'artifacts\publish'
$release = Join-Path $repo 'artifacts\release'
# Always start from a clean, verified staging path; never package a source directory.
foreach ($target in @($publish, $release)) {
    $resolved = [IO.Path]::GetFullPath($target)
    if (-not $resolved.StartsWith($repo + '\artifacts\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe staging path.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
    New-Item -ItemType Directory -Path $resolved -Force | Out-Null
}
$arguments = @('publish', (Join-Path $repo 'LuKnight.csproj'), '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', $publish,
    "-p:Version=$Version", "-p:AssemblyVersion=$Version.0", "-p:FileVersion=$Version.0", "-p:InformationalVersion=$Version", '-p:DebugType=None', '-p:DebugSymbols=false')
if ($NoRestore) { $arguments += '--no-restore' }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
$files = Get-ChildItem -LiteralPath $publish -Recurse -File
foreach ($file in $files) {
    $relative = $file.FullName.Substring($publish.Length + 1)
    if ($file.Name -match '(?i)(^\.env|settings\.json|credential|secret|\.pfx$|\.p12$|\.pdb$|\.tmp$|\.bak$)' -or $relative -match '(?i)^(tests|tools|Reference|output)[\\/]') { throw "Unexpected release content: $relative" }
    if ($file.Extension -in '.json','.xml','.config','.txt') {
        if ([IO.File]::ReadAllText($file.FullName) -match 'AIza[0-9A-Za-z_-]{30,}|-----BEGIN.*PRIVATE KEY-----') { throw "Possible secret in release: $relative" }
    }
}
if (-not (Test-Path (Join-Path $publish 'coreclr.dll'))) { throw 'Release is not self-contained.' }
& $Iscc '/Q' "/DAppVersion=$Version" "/DPublishDir=$publish" (Join-Path $repo 'installer\LuKnight.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
$installer = Join-Path $release 'LuKnightSetup.exe'
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
$manifest = [ordered]@{ version = $Version; url = "https://github.com/Satyanr/LuKnight/releases/download/v$Version/LuKnightSetup.exe"; sha256 = $hash; size = (Get-Item -LiteralPath $installer).Length }
[IO.File]::WriteAllText((Join-Path $release 'update.json'), ($manifest | ConvertTo-Json))
[IO.File]::WriteAllText((Join-Path $release 'checksum.sha256'), "$hash  LuKnightSetup.exe`n")
Write-Output "Release ready: $release"
