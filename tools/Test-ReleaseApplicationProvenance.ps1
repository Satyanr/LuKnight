# Application provenance fixtures use only temporary binary copies; no installer runs.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$verify = Join-Path $PSScriptRoot 'Test-ReleaseArtifacts.ps1'
$binary = Join-Path $repo 'bin\Release\net10.0-windows\LuKnight.exe'
$versionInfo = (Get-Item -LiteralPath $binary).VersionInfo.FileVersion
if ($versionInfo -notmatch '^((0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*))\.0$') {
    throw 'Fixture requires a built application with a stable FileVersion.'
}
$version = $Matches[1]
$commit = 'a' * 40
$fixture = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) ('LuKnight-app-provenance-' + [Guid]::NewGuid().ToString('N'))))
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
if (-not $fixture.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe fixture path.' }
$publish = Join-Path $fixture 'publish'
$release = Join-Path $fixture 'release'
$count = 0
function Write-Json {
    param([string]$Path, $Value)
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 6))
}
function Write-FixtureMetadata {
    param([string]$FixtureVersion = $version)
    $installer = Join-Path $release 'LuKnightSetup.exe'
    $app = Join-Path $publish 'LuKnight.exe'
    $appSignature = Get-AuthenticodeSignature -FilePath $app
    $installerSignature = Get-AuthenticodeSignature -FilePath $installer
    $hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
    $manifest = [ordered]@{ version=$FixtureVersion; url="https://github.com/Satyanr/LuKnight/releases/download/v$FixtureVersion/LuKnightSetup.exe"; sha256=$hash; size=(Get-Item -LiteralPath $installer).Length }
    $manifestPath = Join-Path $release 'update.json'
    Write-Json $manifestPath $manifest
    [IO.File]::WriteAllText((Join-Path $release 'checksum.sha256'), "$hash  LuKnightSetup.exe")
    $provenance = [ordered]@{
        schemaVersion=1; repository='Satyanr/LuKnight'; version=$FixtureVersion; sourceCommit=$commit
        runtime='win-x64'; selfContained=$true
        application=@{
            name='LuKnight.exe'; fileVersion="$FixtureVersion.0"; size=(Get-Item -LiteralPath $app).Length
            sha256=(Get-FileHash -LiteralPath $app -Algorithm SHA256).Hash.ToLowerInvariant()
            authenticodeStatus=[string]$appSignature.Status
            timestamped=($null -ne $appSignature.TimeStamperCertificate)
        }
        installer=@{
            name='LuKnightSetup.exe'; size=(Get-Item -LiteralPath $installer).Length; sha256=$hash
            authenticodeStatus=[string]$installerSignature.Status
            timestamped=($null -ne $installerSignature.TimeStamperCertificate)
        }
        updateManifest=@{name='update.json'; sha256=(Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()}
    }
    Write-Json (Join-Path $release 'release-provenance.json') $provenance
}
function Assert-Rejected {
    param([string]$Expected, [string]$FixtureVersion = $version, [string]$FixturePublish = $publish)
    try {
        & $verify -Version $FixtureVersion -Commit $commit -PublishDirectory $FixturePublish -ReleaseDirectory $release
        throw 'Invalid application provenance was accepted.'
    } catch {
        if ($_.Exception.Message -ne $Expected) { throw }
    }
}
try {
    New-Item -ItemType Directory -Path $publish, $release | Out-Null
    Copy-Item -LiteralPath $binary -Destination (Join-Path $publish 'LuKnight.exe')
    Copy-Item -LiteralPath $binary -Destination (Join-Path $release 'LuKnightSetup.exe')
    Write-FixtureMetadata
    & $verify -Version $version -Commit $commit -PublishDirectory $publish -ReleaseDirectory $release
    $count++
    try {
        & $verify -Version $version -Commit $commit -PublishDirectory $publish -ReleaseDirectory $release -RequireSigned
        throw 'Unsigned release was accepted by RequireSigned.'
    } catch {
        if ($_.Exception.Message -ne 'Release requires a valid Authenticode signature for LuKnight.exe.') { throw }
    }
    $count++
    foreach ($case in @(
        @{section='application';field='authenticodeStatus';error='Release provenance application Authenticode status mismatch.'},
        @{section='application';field='timestamped';error='Release provenance application timestamp status mismatch.'},
        @{section='installer';field='authenticodeStatus';error='Release provenance installer Authenticode status mismatch.'},
        @{section='installer';field='timestamped';error='Release provenance installer timestamp status mismatch.'}
    )) {
        Write-FixtureMetadata
        $path = Join-Path $release 'release-provenance.json'
        $provenance = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        $section = $provenance.($case.section)
        if ($case.field -eq 'timestamped') { $section.timestamped = -not $section.timestamped }
        else { $section.authenticodeStatus = 'FixtureMismatch' }
        Write-Json $path $provenance
        Assert-Rejected $case.error
        $count++
    }
    Write-FixtureMetadata
    Assert-Rejected 'Publish directory is missing.' -FixturePublish (Join-Path $fixture 'missing')
    $count++
    foreach ($case in @(
        @{field='name';value='luknight.exe';error='Release provenance application identity mismatch.'},
        @{field='size';value=1;error='Release provenance application identity mismatch.'},
        @{field='sha256';value=('0' * 64);error='Release provenance application identity mismatch.'},
        @{field='fileVersion';value='9.8.7.0';error='Release provenance application version mismatch.'}
    )) {
        Write-FixtureMetadata
        $path = Join-Path $release 'release-provenance.json'
        $provenance = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        $provenance.application.($case.field) = $case.value
        Write-Json $path $provenance
        Assert-Rejected $case.error
        $count++
    }
    Write-FixtureMetadata '9.8.7'
    Assert-Rejected 'Published application file version mismatch.' -FixtureVersion '9.8.7'
    $count++
    Write-FixtureMetadata
    $app = Join-Path $publish 'LuKnight.exe'
    $bytes = [IO.File]::ReadAllBytes($app)
    $bytes[$bytes.Length - 1] = $bytes[$bytes.Length - 1] -bxor 1
    [IO.File]::WriteAllBytes($app, $bytes)
    Assert-Rejected 'Release provenance application identity mismatch.'
    $count++
    Remove-Item -LiteralPath $app
    Assert-Rejected 'Published LuKnight.exe is missing.'
    $count++
    Write-Output "PASS: $count application provenance fixtures."
} finally {
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force }
}
