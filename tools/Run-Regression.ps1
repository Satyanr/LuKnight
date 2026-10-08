param(
    [switch]$IncludeNativeFixtures,
    [switch]$IncludeAmbientDesktop,
    [switch]$IncludeInteractiveDesktop,
    [switch]$IncludeExternalAi,
    [switch]$NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = [IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..'))

$testProject = Join-Path `
    $repo `
    'tests\LuKnight.RenderChecks\LuKnight.RenderChecks.csproj'

$reportDirectory = Join-Path `
    $repo `
    'artifacts\regression'

New-Item `
    -ItemType Directory `
    -Path $reportDirectory `
    -Force |
    Out-Null


#
# ------------------------------------------------------------
# Tier 1
#
# Deterministic / local regression.
#
# These must not intentionally interact with arbitrary
# user desktop applications or make real AI requests.
# ------------------------------------------------------------
#

$coreCases = @(
    '--regression-matrix',
    '--native-fixture-audit',
    '--regression-signoff',

    '--architecture',
    '--security',
    '--security-privacy',
    '--privacy',

    '--startup-isolation',
    '--recovery',
    '--shutdown',
    '--audit',
    '--long-run',

    '--performance',
    '--performance-acceptance',

    '--assistant',
    '--desktop-commands',
    '--window-targeting',

    '--uia',
    '--uia-resolver',
    '--uia-text',
    '--screen-assist',
    '--ui-risk',

    '--strong-confirm',
    '--permissions',

    '--planner',
    '--planner-exec',
    '--skills',

    '--scheduler',
    '--companion',

    '--speech',
    '--voice-routing',
    '--voice-ux',
    '--tts',

    '--mouse-input',

    '--behavior-settings',
    '--product',
    '--startup',
    '--settings',
    '--physics',
    '--tray',
    '--attention',
    '--desktop'
)


#
# ------------------------------------------------------------
# Tier 2
#
# Native fixture tests.
#
# They may create dedicated Lu-Knight test windows and
# exercise real UIA/mouse/keyboard/tray Windows APIs.
#
# They must not intentionally target unrelated user windows.
# ------------------------------------------------------------
#

$nativeFixtureCases = @(
    '--uia-action-live',
    '--mouse-action-live',
    '--mouse-fallback-live',
    '--uia-text-live',
    '--keyboard-fallback-live',
    '--screen-assist-live',

    '--permissions-live',
    '--planner-live',
    '--user-skill-live',
    '--workflow-live',

    '--scheduler-live',
    '--companion-live'
)


#
# ------------------------------------------------------------
# Tier 3
#
# Ambient desktop diagnostics.
#
# These can inspect and PRINT metadata about the user's
# currently visible desktop windows.
#
# Never persist their raw stdout/stderr.
# ------------------------------------------------------------
#

$ambientDesktopCases = @(
    '--window-targeting-live',
    '--uia-live'
)


#
# ------------------------------------------------------------
# Tier 4
#
# Interactive real desktop smoke test.
#
# May open installed applications and Explorer.
# ------------------------------------------------------------
#

$interactiveDesktopCases = @(
    '--desktop-commands-live'
)


#
# ------------------------------------------------------------
# Tier 5
#
# External provider test.
#
# May use configured Gemini credentials/network/quota.
# ------------------------------------------------------------
#

$externalAiCases = @(
    '--assistant-live'
)


$script:results =
    [System.Collections.Generic.List[object]]::new()


function Invoke-DotNetChecked
{
    param(
        [string[]]$Arguments,
        [string]$Failure
    )


    & dotnet @Arguments


    if ($LASTEXITCODE -ne 0)
    {
        throw $Failure
    }
}


function Invoke-RegressionCase
{
    param(
        [string]$Tier,
        [string]$Flag
    )


    Write-Host ''
    Write-Host (
        '============================================================'
    )

    Write-Host "[$Tier] $Flag"

    Write-Host (
        '============================================================'
    )


    $timer =
        [Diagnostics.Stopwatch]::StartNew()


    & dotnet run `
        --project $testProject `
        -c Release `
        --no-build `
        -- $Flag


    $exitCode =
        $LASTEXITCODE


    $timer.Stop()


    $passed =
        $exitCode -eq 0


    $script:results.Add(
        [pscustomobject]@{
            tier =
                $Tier

            runner =
                $Flag

            passed =
                $passed

            exitCode =
                $exitCode

            durationMs =
                [math]::Round(
                    $timer.Elapsed.TotalMilliseconds)
        })


    if ($passed)
    {
        Write-Host "PASS: $Flag"
    }
    else
    {
        Write-Host "FAIL: $Flag"
    }
}


if (-not $NoBuild)
{
    Invoke-DotNetChecked `
        -Arguments @(
            'clean',
            $testProject,
            '-c',
            'Release'
        ) `
        -Failure 'Regression clean failed.'


    Invoke-DotNetChecked `
        -Arguments @(
            'restore',
            $testProject
        ) `
        -Failure 'Regression restore failed.'


    Invoke-DotNetChecked `
        -Arguments @(
            'build',
            $testProject,
            '-c',
            'Release',
            '--no-restore'
        ) `
        -Failure 'Regression build failed.'
}


foreach ($flag in $coreCases)
{
    Invoke-RegressionCase `
        -Tier 'core' `
        -Flag $flag
}


if ($IncludeNativeFixtures)
{
    foreach ($flag in $nativeFixtureCases)
    {
        Invoke-RegressionCase `
            -Tier 'native-fixture' `
            -Flag $flag
    }
}


if ($IncludeAmbientDesktop)
{
    Write-Warning (
        'Ambient desktop checks may print current window/process ' +
        'metadata to this console. Close sensitive windows first.'
    )


    foreach ($flag in $ambientDesktopCases)
    {
        Invoke-RegressionCase `
            -Tier 'ambient-desktop' `
            -Flag $flag
    }
}


if ($IncludeInteractiveDesktop)
{
    Write-Warning (
        'Interactive desktop checks may open real installed ' +
        'applications and Explorer windows.'
    )


    foreach ($flag in $interactiveDesktopCases)
    {
        Invoke-RegressionCase `
            -Tier 'interactive-desktop' `
            -Flag $flag
    }
}


if ($IncludeExternalAi)
{
    Write-Warning (
        'External AI checks may use Gemini credentials, network ' +
        'access and provider quota.'
    )


    foreach ($flag in $externalAiCases)
    {
        Invoke-RegressionCase `
            -Tier 'external-ai' `
            -Flag $flag
    }
}


#
# ------------------------------------------------------------
# Sanitized evidence only.
#
# IMPORTANT:
#
# Never persist child stdout/stderr here.
#
# Some live diagnostics intentionally display local desktop
# metadata for the developer's immediate inspection.
#
# The persisted report contains only:
# runner, tier, exit status, duration, commit and tool version.
# ------------------------------------------------------------
#

$commit =
    'unknown'


try
{
    $commitResult =
        & git `
            -C $repo `
            rev-parse HEAD `
            2>$null


    if ($LASTEXITCODE -eq 0 -and
        $commitResult)
    {
        $commit = ([string]$commitResult).Trim()
    }
}
catch
{
}


$dotnetVersion = (& dotnet --version).Trim()


$passedCount =
    @(
        $results |
        Where-Object passed
    ).Count


$failedCount =
    $results.Count -
    $passedCount


$summary =
    [ordered]@{
        schemaVersion =
            1

        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')

        commit =
            $commit

        dotnetVersion =
            $dotnetVersion

        selected =
            [ordered]@{
                nativeFixtures =
                    [bool]$IncludeNativeFixtures

                ambientDesktop =
                    [bool]$IncludeAmbientDesktop

                interactiveDesktop =
                    [bool]$IncludeInteractiveDesktop

                externalAi =
                    [bool]$IncludeExternalAi
            }

        total =
            $results.Count

        passed =
            $passedCount

        failed =
            $failedCount

        results =
            $results
    }


$jsonPath =
    Join-Path `
        $reportDirectory `
        'regression-summary.json'


[IO.File]::WriteAllText(
    $jsonPath,
    (
        $summary |
        ConvertTo-Json `
            -Depth 6
    ))


$markdown =
    [System.Collections.Generic.List[string]]::new()


$markdown.Add(
    '# Lu-Knight Regression Summary')

$markdown.Add('')

$markdown.Add(
    "- Commit: ``$commit``")

$markdown.Add(
    "- Generated UTC: ``$($summary.generatedAtUtc)``")

$markdown.Add(
    "- Passed: $passedCount")

$markdown.Add(
    "- Failed: $failedCount")

$markdown.Add('')

$markdown.Add(
    '| Tier | Runner | Result | Duration (ms) |')

$markdown.Add(
    '| --- | --- | --- | ---: |')


foreach ($result in $results)
{
    $status =
        if ($result.passed)
        {
            'PASS'
        }
        else
        {
            'FAIL'
        }


    $markdown.Add(
        "| $($result.tier) | " +
        "``$($result.runner)`` | " +
        "$status | " +
        "$($result.durationMs) |")
}


$markdown.Add('')

$markdown.Add(
    '> Raw child-process output is intentionally not persisted. ' +
    'Live desktop diagnostics can contain private local metadata.')


$markdownPath =
    Join-Path `
        $reportDirectory `
        'regression-summary.md'


[IO.File]::WriteAllLines(
    $markdownPath,
    $markdown)


Write-Host ''
Write-Host (
    '============================================================'
)

Write-Host (
    "Regression summary: $passedCount passed / " +
    "$failedCount failed"
)

Write-Host "Report: $jsonPath"

Write-Host (
    '============================================================'
)


if ($failedCount -gt 0)
{
    exit 1
}


exit 0
