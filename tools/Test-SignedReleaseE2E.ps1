param(
    [string]$Version = '1.0.0',
    [string]$Iscc = 'ISCC.exe',
    [string]$SignTool = '',
    [string]$TimestampUrl = 'http://timestamp.digicert.com'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo =
    [IO.Path]::GetFullPath(
        (Join-Path $PSScriptRoot '..'))

$signer =
    Join-Path `
        $repo `
        'tools\Sign-WithWindowsCertificate.ps1'

$summaryDirectory =
    Join-Path `
        $repo `
        'artifacts\signing-e2e'

$summaryPath =
    Join-Path `
        $summaryDirectory `
        'signing-e2e-summary.json'


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
            'Signing E2E requires a clean Git working tree.'
        )
    }
}


function Get-Head
{
    $value =
        & git `
            -C $repo `
            rev-parse HEAD

    if ($LASTEXITCODE -ne 0 -or
        -not $value)
    {
        throw 'Unable to resolve Git HEAD.'
    }

    return ([string]$value).Trim()
}


if (-not (
    Test-Path `
        -LiteralPath $signer `
        -PathType Leaf))
{
    throw 'Signing provider script is missing.'
}


if ($Version -notmatch
    '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$')
{
    throw 'Invalid stable version.'
}


$timestampUri =
    $null


if (-not [Uri]::TryCreate(
        $TimestampUrl,
        [UriKind]::Absolute,
        [ref]$timestampUri))
{
    throw 'Invalid timestamp URL.'
}


if ($timestampUri.Scheme -notin
    @(
        'http',
        'https'
    ))
{
    throw (
        'Timestamp URL must use HTTP or HTTPS.'
    )
}


if (-not $SignTool)
{
    $command =
        Get-Command `
            signtool.exe `
            -ErrorAction SilentlyContinue

    if ($command)
    {
        $SignTool =
            $command.Source
    }
}


if (-not $SignTool)
{
    $kits =
        Join-Path `
            ${env:ProgramFiles(x86)} `
            'Windows Kits\10\bin'

    if (Test-Path -LiteralPath $kits)
    {
        $SignTool =
            Get-ChildItem `
                -LiteralPath $kits `
                -Filter signtool.exe `
                -Recurse `
                -File `
                -ErrorAction SilentlyContinue |
            Where-Object {
                $_.FullName -match
                    '\\x64\\signtool\.exe$'
            } |
            Sort-Object `
                FullName `
                -Descending |
            Select-Object `
                -First 1 `
                -ExpandProperty FullName
    }
}


if (-not $SignTool -or
    -not (
        Test-Path `
            -LiteralPath $SignTool `
            -PathType Leaf))
{
    throw 'SignTool.exe was not found.'
}


if (-not (
    Test-Path `
        -LiteralPath $Iscc `
        -PathType Leaf))
{
    throw 'ISCC.exe was not found.'
}


Assert-CleanWorkingTree

$commit =
    Get-Head


$oldThumbprint =
    $env:LUKNIGHT_SIGNING_THUMBPRINT

$oldTimestamp =
    $env:LUKNIGHT_TIMESTAMP_URL

$oldSignTool =
    $env:LUKNIGHT_SIGNTOOL


$certificate =
    $null

$tempCertificate =
    Join-Path `
        ([IO.Path]::GetTempPath()) `
        (
            'LuKnight-signing-e2e-' +
            [Guid]::NewGuid().ToString('N') +
            '.cer'
        )


$thumbprint =
    $null

$summary = $null
if (Test-Path -LiteralPath $summaryPath)
{
    Remove-Item -LiteralPath $summaryPath -Force
}

try
{
    Write-Host (
        'Creating temporary non-exportable ' +
        'development code-signing certificate.'
    )


    $certificate =
        New-SelfSignedCertificate `
            -Type CodeSigningCert `
            -Subject 'CN=Lu-Knight Signing E2E' `
            -FriendlyName 'Lu-Knight Signing E2E' `
            -CertStoreLocation 'Cert:\CurrentUser\My' `
            -HashAlgorithm SHA256 `
            -KeyAlgorithm RSA `
            -KeyLength 3072 `
            -KeyExportPolicy NonExportable `
            -NotAfter (Get-Date).AddDays(1)


    $thumbprint =
        $certificate.Thumbprint


    Export-Certificate `
        -Cert $certificate `
        -FilePath $tempCertificate `
        -Force |
        Out-Null


    Import-Certificate `
        -FilePath $tempCertificate `
        -CertStoreLocation `
            'Cert:\CurrentUser\Root' |
        Out-Null


    Import-Certificate `
        -FilePath $tempCertificate `
        -CertStoreLocation `
            'Cert:\CurrentUser\TrustedPublisher' |
        Out-Null


    $env:LUKNIGHT_SIGNING_THUMBPRINT =
        $thumbprint

    $env:LUKNIGHT_TIMESTAMP_URL =
        $TimestampUrl

    $env:LUKNIGHT_SIGNTOOL =
        $SignTool


    Write-Host (
        'Running signed release acceptance.'
    )


    & (
        Join-Path `
            $repo `
            'tools\Build-Release.ps1'
    ) `
        -Version $Version `
        -ExpectedCommit $commit `
        -Iscc $Iscc `
        -SignerScript $signer `
        -RequireSigned


    if (-not $?)
    {
        throw 'Signed release build failed.'
    }


    $application =
        Join-Path `
            $repo `
            'artifacts\publish\LuKnight.exe'

    $installer =
        Join-Path `
            $repo `
            'artifacts\release\LuKnightSetup.exe'


    $applicationSignature =
        Get-AuthenticodeSignature `
            -FilePath $application

    $installerSignature =
        Get-AuthenticodeSignature `
            -FilePath $installer


    if ($applicationSignature.Status -ne
        'Valid')
    {
        throw (
            'Signed application did not verify.'
        )
    }


    if ($null -eq
        $applicationSignature.TimeStamperCertificate)
    {
        throw (
            'Signed application timestamp is missing.'
        )
    }


    if ($installerSignature.Status -ne
        'Valid')
    {
        throw (
            'Signed installer did not verify.'
        )
    }

    if ($applicationSignature.SignerCertificate.Thumbprint -ne $thumbprint -or
        $installerSignature.SignerCertificate.Thumbprint -ne $thumbprint)
    {
        throw 'Release artifacts were not signed by the temporary development certificate.'
    }

    if ($null -eq
        $installerSignature.TimeStamperCertificate)
    {
        throw (
            'Signed installer timestamp is missing.'
        )
    }


    $provenance =
        Get-Content `
            -LiteralPath (
                Join-Path `
                    $repo `
                    'artifacts\release\release-provenance.json'
            ) `
            -Raw |
        ConvertFrom-Json


    if ($provenance.sourceCommit -ne
        $commit)
    {
        throw (
            'Signed release provenance commit mismatch.'
        )
    }


    if ($provenance.application.authenticodeStatus -ne
            'Valid' -or
        $provenance.application.timestamped -ne
            $true)
    {
        throw (
            'Application signing provenance is invalid.'
        )
    }


    if ($provenance.installer.authenticodeStatus -ne
            'Valid' -or
        $provenance.installer.timestamped -ne
            $true)
    {
        throw (
            'Installer signing provenance is invalid.'
        )
    }


    Assert-CleanWorkingTree


    if ((Get-Head) -ne
        $commit)
    {
        throw (
            'Git HEAD changed during signing acceptance.'
        )
    }


    New-Item `
        -ItemType Directory `
        -Path $summaryDirectory `
        -Force |
        Out-Null


    $summary =
        [ordered]@{
            schemaVersion =
                1

            generatedAtUtc =
                [DateTimeOffset]::UtcNow.ToString('O')

            commit =
                $commit

            version =
                $Version

            developmentCertificate =
                $true

            applicationSignature =
                'Valid'

            applicationTimestamped =
                $true

            installerSignature =
                'Valid'

            installerTimestamped =
                $true

            provenanceVerified =
                $true

            passed =
                $true
        }


}
finally
{
    $cleanupErrors = @()
    #
    # Never leave the development trust anchor installed.
    #

    if ($thumbprint)
    {
        foreach ($store in
            @(
                'Cert:\CurrentUser\TrustedPublisher',
                'Cert:\CurrentUser\Root',
                'Cert:\CurrentUser\My'
            ))
        {
            $certificatePath =
                Join-Path `
                    $store `
                    $thumbprint

            try
            {
                if (Test-Path -LiteralPath $certificatePath)
                {
                    if ($store -eq 'Cert:\CurrentUser\My')
                    {
                        Remove-Item -LiteralPath $certificatePath -DeleteKey -Force
                    }
                    else
                    {
                        Remove-Item -LiteralPath $certificatePath -Force
                    }
                }
                if (Test-Path -LiteralPath $certificatePath)
                {
                    throw 'Temporary development certificate remains installed.'
                }
            }
            catch
            {
                $cleanupErrors += $_.Exception.Message
            }
        }
    }


    try
    {
        if (Test-Path -LiteralPath $tempCertificate)
        {
            Remove-Item -LiteralPath $tempCertificate -Force
        }
    }
    catch
    {
        $cleanupErrors += $_.Exception.Message
    }


    if ($null -eq $oldThumbprint)
    {
        Remove-Item `
            Env:LUKNIGHT_SIGNING_THUMBPRINT `
            -ErrorAction SilentlyContinue
    }
    else
    {
        $env:LUKNIGHT_SIGNING_THUMBPRINT =
            $oldThumbprint
    }


    if ($null -eq $oldTimestamp)
    {
        Remove-Item `
            Env:LUKNIGHT_TIMESTAMP_URL `
            -ErrorAction SilentlyContinue
    }
    else
    {
        $env:LUKNIGHT_TIMESTAMP_URL =
            $oldTimestamp
    }


    if ($null -eq $oldSignTool)
    {
        Remove-Item `
            Env:LUKNIGHT_SIGNTOOL `
            -ErrorAction SilentlyContinue
    }
    else
    {
        $env:LUKNIGHT_SIGNTOOL =
            $oldSignTool
    }

    if ($cleanupErrors.Count -gt 0)
    {
        throw ('Signing E2E cleanup failed: ' + ($cleanupErrors -join '; '))
    }
}

$summary.cleanupSucceeded = $true
[IO.File]::WriteAllText(
    $summaryPath,
    ($summary | ConvertTo-Json -Depth 4))
Write-Host ''
Write-Host 'PASS: signed release E2E acceptance.'
Write-Host "Evidence: $summaryPath"
