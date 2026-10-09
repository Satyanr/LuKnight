param(
    [string]$Version = '1.0.0',
    [string]$Iscc = 'ISCC.exe',
    [switch]$NoRestore,
    [string]$ExpectedCommit = '',
    [string]$SignerScript = '',
    [switch]$RequireSigned
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'


$repo =
    [IO.Path]::GetFullPath(
        (Join-Path $PSScriptRoot '..'))


$resolvedSigner = $null
if ($SignerScript)
{
    $resolvedSigner = [IO.Path]::GetFullPath($SignerScript)
    if (-not (Test-Path -LiteralPath $resolvedSigner -PathType Leaf))
    {
        throw 'Signing provider script was not found.'
    }
}

if ($RequireSigned -and -not $resolvedSigner)
{
    throw 'RequireSigned requires a signing provider script.'
}

function Invoke-ReleaseSigner
{
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$Name
    )

    if (-not $resolvedSigner)
    {
        return
    }

    # Reset native exit status so a provider cannot hide a failed native signer
    # behind a successful final PowerShell logging command.
    $global:LASTEXITCODE = 0
    & $resolvedSigner -Path $Path
    if (-not $? -or $LASTEXITCODE -ne 0)
    {
        throw "Signing provider failed for $Name."
    }

    $signature = Get-AuthenticodeSignature -FilePath $Path
    if ($signature.Status -ne 'Valid')
    {
        throw "Signing provider did not produce a valid Authenticode signature for $Name."
    }

    if ($null -eq $signature.TimeStamperCertificate)
    {
        throw "Signing provider did not timestamp $Name."
    }
}


if ($Version -notmatch
    '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$')
{
    throw (
        'Use a stable semantic version ' +
        '(for example 1.0.1).'
    )
}

function Get-SourceCommit
{
    $value =
        & git `
            -C $repo `
            rev-parse HEAD


    if ($LASTEXITCODE -ne 0 -or
        -not $value)
    {
        throw 'Unable to resolve source Git commit.'
    }


    return ([string]$value).Trim()
}


function Assert-CleanWorkingTree
{
    $dirty =
        & git `
            -C $repo `
            status `
            --porcelain `
            --untracked-files=all


    if ($LASTEXITCODE -ne 0)
    {
        throw 'Unable to inspect Git working tree.'
    }


    if ($dirty)
    {
        throw (
            'Release packaging requires a clean Git working tree.'
        )
    }
}

Assert-CleanWorkingTree


$sourceCommit =
    Get-SourceCommit


if ($ExpectedCommit)
{
    if ($ExpectedCommit -notmatch
        '^[0-9a-fA-F]{40}$')
    {
        throw 'ExpectedCommit must be a full 40-character Git SHA.'
    }


    if (-not [string]::Equals(
            $sourceCommit,
            $ExpectedCommit,
            [StringComparison]::OrdinalIgnoreCase))
    {
        throw (
            'Current HEAD does not match the expected release commit.'
        )
    }
}

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
Assert-CleanWorkingTree


if ((Get-SourceCommit) -ne
    $sourceCommit)
{
    throw (
        'Git HEAD changed during release publish.'
    )
}

if (-not (Test-Path (Join-Path $publish 'coreclr.dll'))) { throw 'Release is not self-contained.' }
$app =
    Join-Path `
        $publish `
        'LuKnight.exe'


if (-not (Test-Path -LiteralPath $app))
{
    throw 'Published LuKnight.exe is missing.'
}


$appInfo =
    Get-Item `
        -LiteralPath $app


$expectedFileVersion =
    "$Version.0"


$appFileVersion =
    [string]$appInfo.VersionInfo.FileVersion


if ($appFileVersion -ne
    $expectedFileVersion)
{
    throw (
        'Published application file version mismatch.'
    )
}

Invoke-ReleaseSigner -Path $app -Name 'LuKnight.exe'

& $Iscc '/Q' "/DAppVersion=$Version" "/DPublishDir=$publish" (Join-Path $repo 'installer\LuKnight.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
$installer = Join-Path $release 'LuKnightSetup.exe'
Invoke-ReleaseSigner -Path $installer -Name 'LuKnightSetup.exe'

# Signing changes the binary; refresh metadata and hash only final artifacts.
$appInfo.Refresh()
$appHash = (Get-FileHash -LiteralPath $app -Algorithm SHA256).Hash.ToLowerInvariant()
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
$manifest = [ordered]@{ version = $Version; url = "https://github.com/Satyanr/LuKnight/releases/download/v$Version/LuKnightSetup.exe"; sha256 = $hash; size = (Get-Item -LiteralPath $installer).Length }
[IO.File]::WriteAllText((Join-Path $release 'update.json'), ($manifest | ConvertTo-Json))
[IO.File]::WriteAllText((Join-Path $release 'checksum.sha256'), "$hash  LuKnightSetup.exe`n")


$appSignature =
    Get-AuthenticodeSignature `
        -FilePath $app


$appSignatureStatus =
    [string]$appSignature.Status


$appTimestamped =
    $null -ne $appSignature.TimeStamperCertificate


$manifestPath =
    Join-Path `
        $release `
        'update.json'


$manifestHash =
    (
        Get-FileHash `
            -LiteralPath $manifestPath `
            -Algorithm SHA256
    ).Hash.ToLowerInvariant()

$installerSignature =
    Get-AuthenticodeSignature `
        -FilePath $installer


$installerSignatureStatus =
    [string]$installerSignature.Status


$installerTimestamped =
    $null -ne $installerSignature.TimeStamperCertificate

Assert-CleanWorkingTree


if ((Get-SourceCommit) -ne
    $sourceCommit)
{
    throw (
        'Git HEAD changed during release packaging.'
    )
}

$provenance =
    [ordered]@{
        schemaVersion =
            1

        repository =
            'Satyanr/LuKnight'

        version =
            $Version

        sourceCommit =
            $sourceCommit

        generatedAtUtc =
            [DateTimeOffset]::UtcNow.ToString('O')

        runtime =
            'win-x64'

        selfContained =
            $true

        dotnetVersion =
            (& dotnet --version).Trim()

        application =
            [ordered]@{
                name =
                    'LuKnight.exe'

                fileVersion =
                    $appFileVersion

                size =
                    $appInfo.Length

                sha256 =
                    $appHash

                authenticodeStatus =
                    $appSignatureStatus

                timestamped =
                    $appTimestamped
            }

        installer =
            [ordered]@{
                name =
                    'LuKnightSetup.exe'

                size =
                    (
                        Get-Item `
                            -LiteralPath $installer
                    ).Length

                sha256 =
                    $hash

                authenticodeStatus =
                    $installerSignatureStatus

                timestamped =
                    $installerTimestamped
            }

        updateManifest =
            [ordered]@{
                name =
                    'update.json'

                sha256 =
                    $manifestHash
            }
    }


$provenancePath =
    Join-Path `
        $release `
        'release-provenance.json'


[IO.File]::WriteAllText(
    $provenancePath,
    (
        $provenance |
        ConvertTo-Json `
            -Depth 6
    ))

Assert-CleanWorkingTree


if ((Get-SourceCommit) -ne
    $sourceCommit)
{
    throw (
        'Git HEAD changed during release packaging.'
    )
}


$verifyArguments = @(
    '-NoProfile',
    '-NonInteractive',
    '-ExecutionPolicy',
    'Bypass',
    '-File',
    (Join-Path $repo 'tools\Test-ReleaseArtifacts.ps1'),
    '-Version', $Version,
    '-Commit', $sourceCommit,
    '-PublishDirectory', $publish,
    '-ReleaseDirectory', $release
)

if ($RequireSigned)
{
    $verifyArguments += '-RequireSigned'
}

& powershell @verifyArguments


if ($LASTEXITCODE -ne 0)
{
    throw 'Release artifact verification failed.'
}

Write-Output "Release ready: $release"
