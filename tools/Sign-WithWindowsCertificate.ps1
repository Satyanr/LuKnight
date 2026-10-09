param(
    [Parameter(Mandatory)]
    [string]$Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'


$thumbprint =
    [Environment]::GetEnvironmentVariable(
        'LUKNIGHT_SIGNING_THUMBPRINT')


$timestampUrl =
    [Environment]::GetEnvironmentVariable(
        'LUKNIGHT_TIMESTAMP_URL')


$signTool =
    [Environment]::GetEnvironmentVariable(
        'LUKNIGHT_SIGNTOOL')


if (-not $thumbprint)
{
    throw 'LUKNIGHT_SIGNING_THUMBPRINT is not configured.'
}


$thumbprint =
    $thumbprint `
        -replace '\s', ''


if ($thumbprint -notmatch
    '^[0-9a-fA-F]{40}$')
{
    throw 'Signing certificate thumbprint is invalid.'
}


if (-not $timestampUrl)
{
    throw 'LUKNIGHT_TIMESTAMP_URL is not configured.'
}


$timestampUri =
    $null


if (-not [Uri]::TryCreate(
        $timestampUrl,
        [UriKind]::Absolute,
        [ref]$timestampUri))
{
    throw 'Timestamp URL is invalid.'
}


if ($timestampUri.Scheme -notin @('http', 'https'))
{
    throw 'Timestamp URL must use HTTP or HTTPS.'
}


if (-not $signTool)
{
    $command =
        Get-Command `
            signtool.exe `
            -ErrorAction SilentlyContinue


    if ($command)
    {
        $signTool =
            $command.Source
    }
}


if (-not $signTool -or
    -not (
        Test-Path `
            -LiteralPath $signTool `
            -PathType Leaf))
{
    throw (
        'SignTool.exe was not found. ' +
        'Set LUKNIGHT_SIGNTOOL.'
    )
}


$target =
    [IO.Path]::GetFullPath(
        $Path)


if (-not (
    Test-Path `
        -LiteralPath $target `
        -PathType Leaf))
{
    throw 'Signing target does not exist.'
}


& $signTool `
    sign `
    /fd SHA256 `
    /sha1 $thumbprint `
    /tr $timestampUrl `
    /td SHA256 `
    $target


if ($LASTEXITCODE -ne 0)
{
    throw 'SignTool failed.'
}


& $signTool `
    verify `
    /pa `
    /v `
    $target


if ($LASTEXITCODE -ne 0)
{
    throw 'SignTool verification failed.'
}


$signature =
    Get-AuthenticodeSignature `
        -FilePath $target


if ($signature.Status -ne 'Valid')
{
    throw 'Authenticode signature is not valid.'
}


if ($null -eq
    $signature.TimeStamperCertificate)
{
    throw 'Authenticode timestamp is missing.'
}


Write-Host (
    'Signed and verified: ' +
    [IO.Path]::GetFileName($target)
)
