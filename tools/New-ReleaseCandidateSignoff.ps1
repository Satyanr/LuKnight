param(
    [string]$ExpectedVersion = '1.0.0'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo =
    [IO.Path]::GetFullPath(
        (Join-Path $PSScriptRoot '..'))

$outputDirectory =
    Join-Path `
        $repo `
        'artifacts\release-candidate'

$jsonPath =
    Join-Path `
        $outputDirectory `
        'rc-readiness.json'

$markdownPath =
    Join-Path `
        $outputDirectory `
        'rc-readiness.md'


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
            'Release Candidate sign-off requires a clean Git working tree.'
        )
    }
}


function Read-Evidence
{
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$Name
    )

    if (-not (
        Test-Path `
            -LiteralPath $Path `
            -PathType Leaf))
    {
        throw "$Name evidence is missing."
    }

    try
    {
        return (
            Get-Content `
                -LiteralPath $Path `
                -Raw |
            ConvertFrom-Json
        )
    }
    catch
    {
        throw "$Name evidence is invalid JSON."
    }
}


function Assert-EvidenceCommit
{
    param(
        [Parameter(Mandatory)]
        [object]$Evidence,

        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [string]$Commit
    )

    if ([string]::IsNullOrWhiteSpace(
        [string]$Evidence.commit))
    {
        throw "$Name evidence has no commit identity."
    }

    if (-not [string]::Equals(
        [string]$Evidence.commit,
        $Commit,
        [StringComparison]::OrdinalIgnoreCase))
    {
        throw (
            "$Name evidence belongs to another commit."
        )
    }
}

function Assert-True
{
    param([object]$Value, [string]$Name)
    if ($Value -isnot [bool] -or $Value -ne $true)
    {
        throw "$Name must be JSON true."
    }
}

function Assert-Integer
{
    param([object]$Value, [long]$Expected, [string]$Name)
    if (($Value -isnot [int] -and $Value -isnot [long]) -or
        $Value -ne $Expected)
    {
        throw "$Name is invalid."
    }
}


Assert-CleanWorkingTree

$commit =
    Get-Head

foreach ($oldOutput in @($jsonPath, $markdownPath))
{
    if (Test-Path -LiteralPath $oldOutput)
    {
        Remove-Item -LiteralPath $oldOutput -Force
    }
}
if ($ExpectedVersion -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$')
{
    throw 'Invalid stable expected version.'
}

#
# ------------------------------------------------------------
# Version
# ------------------------------------------------------------
#

$projectPath =
    Join-Path `
        $repo `
        'LuKnight.csproj'


[xml]$project =
    Get-Content `
        -LiteralPath $projectPath `
        -Raw


$projectVersion =
    [string](
        $project.Project.PropertyGroup |
        Where-Object Version |
        Select-Object `
            -First 1
    ).Version


if ($projectVersion -ne
    $ExpectedVersion)
{
    throw (
        "Project version is $projectVersion, " +
        "expected $ExpectedVersion."
    )
}


#
# ------------------------------------------------------------
# Regression evidence
# ------------------------------------------------------------
#

$regressionDirectory =
    Join-Path `
        $repo `
        'artifacts\regression'


$regression =
    Read-Evidence `
        -Path (
            Join-Path `
                $regressionDirectory `
                'regression-summary.json'
        ) `
        -Name 'Regression'


$regressionSignoff =
    Read-Evidence `
        -Path (
            Join-Path `
                $regressionDirectory `
                'regression-signoff.json'
        ) `
        -Name 'Regression sign-off'


Assert-EvidenceCommit `
    -Evidence $regression `
    -Name 'Regression' `
    -Commit $commit


Assert-EvidenceCommit `
    -Evidence $regressionSignoff `
    -Name 'Regression sign-off' `
    -Commit $commit


Assert-Integer $regression.schemaVersion 1 'Regression schema'
Assert-Integer $regressionSignoff.schemaVersion 1 'Regression sign-off schema'
Assert-Integer $regression.failed 0 'Regression failures'
Assert-True $regression.selected.nativeFixtures 'Native Fixtures selection'
Assert-True $regressionSignoff.automatedReady 'Regression automated readiness'
Assert-True $regressionSignoff.manualPhysicalComplete 'Physical acceptance'
Assert-True $regressionSignoff.phase12EComplete 'Phase 12E completion'

# Recheck complete runner coverage rather than trusting aggregate PASS labels.
$runnerSource = [IO.File]::ReadAllText((Join-Path $repo 'tools\Run-Regression.ps1'))
$groups = @(
    @('core', 'coreCases', $true, 'core'),
    @('native-fixture', 'nativeFixtureCases', $true, 'nativeFixture'),
    @('ambient-desktop', 'ambientDesktopCases', $regression.selected.ambientDesktop, 'ambientDesktop'),
    @('interactive-desktop', 'interactiveDesktopCases', $regression.selected.interactiveDesktop, 'interactiveDesktop'),
    @('external-ai', 'externalAiCases', $regression.selected.externalAi, 'externalAi')
)
$expectedTotal = 0
foreach ($group in $groups)
{
    if ($group[2] -isnot [bool]) { throw 'Regression tier selection must be boolean.' }
    $rows = @($regression.results | Where-Object tier -eq $group[0])
    if (-not $group[2])
    {
        if ($rows.Count -ne 0 -or $regressionSignoff.automated.($group[3]) -ne 'NotRun')
        {
            throw 'Unselected regression tier contains inconsistent evidence.'
        }
        continue
    }
    $block = [regex]::Match($runnerSource,
        '\$' + [regex]::Escape($group[1]) + '\s*=\s*@\((.*?)\)',
        [Text.RegularExpressions.RegexOptions]::Singleline)
    $expected = @([regex]::Matches($block.Groups[1].Value, "'(?<flag>--[a-z0-9-]+)'") |
        ForEach-Object { $_.Groups['flag'].Value })
    $actual = @($rows | ForEach-Object runner)
    if ($expected.Count -eq 0 -or $actual.Count -ne $expected.Count -or
        @($actual | Select-Object -Unique).Count -ne $expected.Count -or
        @(Compare-Object $expected $actual).Count -ne 0 -or
        $regressionSignoff.automated.($group[3]) -ne 'Pass')
    {
        throw 'Regression runner coverage is incomplete or inconsistent.'
    }
    foreach ($row in $rows)
    {
        Assert-True $row.passed 'Regression runner PASS'
        Assert-Integer $row.exitCode 0 'Regression runner exit code'
    }
    $expectedTotal += $expected.Count
}
Assert-Integer $regression.total $expectedTotal 'Regression total'
Assert-Integer $regression.passed $expectedTotal 'Regression passed total'
if (@($regression.results).Count -ne $expectedTotal)
{
    throw 'Regression contains unexpected runner evidence.'
}

if ($regression.schemaVersion -ne 1)
{
    throw 'Unsupported regression evidence schema.'
}


if ($regression.failed -ne 0)
{
    throw 'Regression contains failures.'
}


if ($regression.selected.nativeFixtures -ne
    $true)
{
    throw (
        'Final RC regression requires Native Fixtures.'
    )
}


if ($regressionSignoff.automated.core -ne
        'Pass' -or
    $regressionSignoff.automated.nativeFixture -ne
        'Pass')
{
    throw (
        'Core or Native Fixture regression is not PASS.'
    )
}


if ($regressionSignoff.physical.microphone -ne
        'Pass' -or
    $regressionSignoff.physical.speaker -ne
        'Pass' -or
    $regressionSignoff.physical.multiMonitor -ne
        'Pass' -or
    $regressionSignoff.physical.trayShell -ne
        'Pass')
{
    throw (
        'Physical Windows acceptance is incomplete.'
    )
}


if ($regressionSignoff.phase12EComplete -ne
    $true)
{
    throw (
        'Phase 12E regression sign-off is incomplete.'
    )
}


#
# ------------------------------------------------------------
# Installer E2E
# ------------------------------------------------------------
#

$installerEvidence =
    Read-Evidence `
        -Path (
            Join-Path `
                $repo `
                'artifacts\installer-e2e\installer-e2e-summary.json'
        ) `
        -Name 'Installer E2E'


Assert-EvidenceCommit `
    -Evidence $installerEvidence `
    -Name 'Installer E2E' `
    -Commit $commit


Assert-Integer $installerEvidence.schemaVersion 1 'Installer E2E schema'
Assert-Integer $installerEvidence.total 8 'Installer E2E total'
Assert-Integer $installerEvidence.passed 8 'Installer E2E passed'
Assert-Integer $installerEvidence.failed 0 'Installer E2E failed'
Assert-True $installerEvidence.isolatedProfile 'Installer isolation'
Assert-True $installerEvidence.cleanupSucceeded 'Installer cleanup'
if ($installerEvidence.baseVersion -ne $ExpectedVersion)
{
    throw 'Installer E2E version mismatch.'
}

if ($installerEvidence.schemaVersion -ne 1)
{
    throw 'Unsupported installer E2E schema.'
}


if ($installerEvidence.isolatedProfile -ne
        $true -or
    $installerEvidence.total -ne 8 -or
    $installerEvidence.passed -ne 8 -or
    $installerEvidence.failed -ne 0 -or
    $installerEvidence.cleanupSucceeded -ne
        $true)
{
    throw (
        'Installer E2E acceptance is incomplete.'
    )
}


$expectedInstallerCases =
    @(
        'fresh-install',
        'seed-profile',
        'upgrade-targetpid',
        'upgrade-preserves-data',
        'uninstall-keep-data',
        'reinstall-preserves-data',
        'uninstall-remove-data',
        'clean-reinstall'
    )


$actualInstallerCases =
    @(
        $installerEvidence.cases
    )


if ($actualInstallerCases.Count -ne
        $expectedInstallerCases.Count -or
    @(
        Compare-Object `
            -ReferenceObject $expectedInstallerCases `
            -DifferenceObject $actualInstallerCases
    ).Count -ne 0)
{
    throw (
        'Installer E2E case coverage is incomplete.'
    )
}


#
# ------------------------------------------------------------
# Signing E2E
# ------------------------------------------------------------
#

$signingEvidence =
    Read-Evidence `
        -Path (
            Join-Path `
                $repo `
                'artifacts\signing-e2e\signing-e2e-summary.json'
        ) `
        -Name 'Signing E2E'


Assert-EvidenceCommit `
    -Evidence $signingEvidence `
    -Name 'Signing E2E' `
    -Commit $commit


Assert-Integer $signingEvidence.schemaVersion 1 'Signing E2E schema'
foreach ($field in @('developmentCertificate', 'applicationTimestamped',
        'installerTimestamped', 'provenanceVerified', 'cleanupSucceeded', 'passed'))
{
    Assert-True $signingEvidence.$field "Signing E2E $field"
}
if ($signingEvidence.version -ne $ExpectedVersion)
{
    throw 'Signing E2E version mismatch.'
}

if ($signingEvidence.schemaVersion -ne 1)
{
    throw 'Unsupported signing E2E schema.'
}


if ($signingEvidence.developmentCertificate -ne
        $true -or
    $signingEvidence.applicationSignature -ne
        'Valid' -or
    $signingEvidence.applicationTimestamped -ne
        $true -or
    $signingEvidence.installerSignature -ne
        'Valid' -or
    $signingEvidence.installerTimestamped -ne
        $true -or
    $signingEvidence.provenanceVerified -ne
        $true -or
    $signingEvidence.cleanupSucceeded -ne
        $true -or
    $signingEvidence.passed -ne
        $true)
{
    throw (
        'Signed release development acceptance is incomplete.'
    )
}


#
# ------------------------------------------------------------
# Revalidate source identity
# ------------------------------------------------------------
#

Assert-CleanWorkingTree


if ((Get-Head) -ne
    $commit)
{
    throw (
        'Git HEAD changed while producing RC sign-off.'
    )
}


#
# ------------------------------------------------------------
# Write sanitized RC evidence
# ------------------------------------------------------------
#

New-Item `
    -ItemType Directory `
    -Path $outputDirectory `
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
            $projectVersion

        regression =
            [ordered]@{
                core =
                    'Pass'

                nativeFixtures =
                    'Pass'

                physicalWindows =
                    'Pass'
            }

        installerE2E =
            'Pass'

        signedReleaseDryRun =
            'Pass'

        developmentSigningOnly =
            $true

        publicCertificate =
            'Pending'

        internalRcEvidenceReady =
            $true

        publicReleaseReady =
            $false
    }


[IO.File]::WriteAllText(
    $jsonPath,
    (
        $summary |
        ConvertTo-Json `
            -Depth 6
    ))


$markdown =
    @(
        '# Lu-Knight Release Candidate Readiness',
        '',
        "- Commit: ``$commit``",
        "- Version: ``$projectVersion``",
        '',
        '## Evidence',
        '',
        '| Gate | Status |',
        '| --- | --- |',
        '| Core regression | PASS |',
        '| Native fixture regression | PASS |',
        '| Physical Windows acceptance | PASS |',
        '| Installer / updater E2E | PASS |',
        '| Signed release dry-run | PASS |',
        '| Real public certificate | PENDING |',
        '',
        '## Result',
        '',
        '**Internal RC evidence ready: YES**',
        '',
        '**Public release ready: NO**',
        '',
        '> Public release remains blocked until a real publicly trusted ' +
        'code-signing certificate is used for the final release artifacts.'
    )


[IO.File]::WriteAllLines(
    $markdownPath,
    $markdown)


Write-Host ''
Write-Host (
    'PASS: internal Release Candidate evidence is complete.'
)

Write-Host (
    'PUBLIC RELEASE: BLOCKED pending real code-signing certificate.'
)

Write-Host (
    "Evidence: $jsonPath"
)
