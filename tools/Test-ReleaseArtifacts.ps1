param(
    [Parameter(Mandatory)]
    [string]$Version,

    [Parameter(Mandatory)]
    [string]$Commit,

    [string]$ReleaseDirectory = '',

    [switch]$RequireSigned
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'


$repo =
    [IO.Path]::GetFullPath(
        (Join-Path $PSScriptRoot '..'))


if (-not $ReleaseDirectory)
{
    $ReleaseDirectory =
        Join-Path `
            $repo `
            'artifacts\release'
}


if ($Version -notmatch
    '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$')
{
    throw 'Invalid release version.'
}


if ($Commit -notmatch
    '^[0-9a-fA-F]{40}$')
{
    throw 'Invalid release commit.'
}


$installer =
    Join-Path `
        $ReleaseDirectory `
        'LuKnightSetup.exe'


$manifestPath =
    Join-Path `
        $ReleaseDirectory `
        'update.json'


$checksumPath =
    Join-Path `
        $ReleaseDirectory `
        'checksum.sha256'


$provenancePath =
    Join-Path `
        $ReleaseDirectory `
        'release-provenance.json'


foreach ($path in
         @(
             $installer,
             $manifestPath,
             $checksumPath,
             $provenancePath
         ))
{
    if (-not (
        Test-Path `
            -LiteralPath $path `
            -PathType Leaf))
    {
        throw (
            "Required release artifact is missing: " +
            [IO.Path]::GetFileName($path)
        )
    }
}

$manifest =
    Get-Content `
        -LiteralPath $manifestPath `
        -Raw |
    ConvertFrom-Json


if ($manifest.version -ne
    $Version)
{
    throw 'update.json version mismatch.'
}


$expectedUrl =
    "https://github.com/Satyanr/LuKnight/" +
    "releases/download/v$Version/LuKnightSetup.exe"


if ($manifest.url -cne
    $expectedUrl)
{
    throw 'update.json contains an unexpected installer URL.'
}

$installerInfo =
    Get-Item `
        -LiteralPath $installer


$installerHash =
    (
        Get-FileHash `
            -LiteralPath $installer `
            -Algorithm SHA256
    ).Hash.ToLowerInvariant()


if ($manifest.sha256 -ne
    $installerHash)
{
    throw 'Installer SHA-256 does not match update.json.'
}


if ([long]$manifest.size -ne
    $installerInfo.Length)
{
    throw 'Installer size does not match update.json.'
}


$checksum =
    (
        Get-Content `
            -LiteralPath $checksumPath `
            -Raw
    ).Trim()


if ($checksum -ne
    "$installerHash  LuKnightSetup.exe")
{
    throw 'checksum.sha256 does not match installer.'
}

$provenance =
    Get-Content `
        -LiteralPath $provenancePath `
        -Raw |
    ConvertFrom-Json


if ([int]$provenance.schemaVersion -ne 1)
{
    throw 'Unsupported release provenance schema.'
}


if ($provenance.repository -ne
    'Satyanr/LuKnight')
{
    throw 'Release provenance repository mismatch.'
}


if ($provenance.version -ne
    $Version)
{
    throw 'Release provenance version mismatch.'
}


if (-not [string]::Equals(
        [string]$provenance.sourceCommit,
        $Commit,
        [StringComparison]::OrdinalIgnoreCase))
{
    throw 'Release provenance commit mismatch.'
}


if ($provenance.runtime -ne
        'win-x64' -or
    $provenance.selfContained -isnot [bool] -or
    $provenance.selfContained -ne $true)
{
    throw 'Unexpected release runtime configuration.'
}


if ($provenance.installer.name -ne
        'LuKnightSetup.exe' -or
    $provenance.installer.sha256 -ne
        $installerHash -or
    [long]$provenance.installer.size -ne
        $installerInfo.Length)
{
    throw 'Release provenance installer identity mismatch.'
}

$manifestHash =
    (
        Get-FileHash `
            -LiteralPath $manifestPath `
            -Algorithm SHA256
    ).Hash.ToLowerInvariant()


if ($provenance.updateManifest.name -cne 'update.json' -or
    $provenance.updateManifest.sha256 -ne
    $manifestHash)
{
    throw 'Release provenance update manifest hash mismatch.'
}

$signature =
    Get-AuthenticodeSignature `
        -FilePath $installer


if ($RequireSigned -and
    $signature.Status -ne
        'Valid')
{
    throw (
        'Release requires a valid Authenticode signature.'
    )
}


if ([string]$provenance.installer.authenticodeStatus -ne
    [string]$signature.Status)
{
    throw (
        'Release provenance Authenticode status mismatch.'
    )
}

Write-Host (
    "PASS: release artifacts verified " +
    "for v$Version @ $Commit"
)
