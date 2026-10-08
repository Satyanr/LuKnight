param(
    [ValidateSet('NotRun', 'Pass', 'Fail')]
    [string]$Microphone = 'NotRun',
    [ValidateSet('NotRun', 'Pass', 'Fail')]
    [string]$Speaker = 'NotRun',
    [ValidateSet('NotRun', 'Pass', 'Fail')]
    [string]$MultiMonitor = 'NotRun',
    [ValidateSet('NotRun', 'Pass', 'Fail')]
    [string]$TrayShell = 'NotRun'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$regressionDirectory = Join-Path $repo 'artifacts\regression'
$summaryPath = Join-Path $regressionDirectory 'regression-summary.json'
if (-not (Test-Path -LiteralPath $summaryPath)) {
    throw 'Regression summary is missing. Run tools/Run-Regression.ps1 first.'
}
$summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json

$commitResult = & git -C $repo rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or -not $commitResult) {
    throw 'Unable to resolve current Git commit.'
}
$currentCommit = ([string]$commitResult).Trim()
if ([string]::IsNullOrWhiteSpace([string]$summary.commit) -or $summary.commit -eq 'unknown') {
    throw 'Regression evidence has no Git commit identity.'
}
if (-not [string]::Equals([string]$summary.commit, $currentCommit,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Regression evidence belongs to another commit. Run the regression suite again for current HEAD.'
}
if ($summary.schemaVersion -ne 1) {
    throw 'Unsupported regression summary schema.'
}
foreach ($selection in 'nativeFixtures', 'ambientDesktop', 'interactiveDesktop', 'externalAi') {
    if ($summary.selected.$selection -isnot [bool]) {
        throw 'Regression tier selections must be JSON booleans.'
    }
}

# Read the declared runner matrix without executing the regression script.
$runnerSource = [IO.File]::ReadAllText((Join-Path $repo 'tools\Run-Regression.ps1'))
function Get-ExpectedRunners {
    param([string]$Group)
    $block = [regex]::Match($runnerSource,
        '\$' + [regex]::Escape($Group) + '\s*=\s*@\((.*?)\)',
        [Text.RegularExpressions.RegexOptions]::Singleline)
    if (-not $block.Success) { throw 'Regression matrix group is missing.' }
    $flags = @([regex]::Matches($block.Groups[1].Value, "'(?<flag>--[a-z0-9-]+)'") |
        ForEach-Object { $_.Groups['flag'].Value })
    if ($flags.Count -eq 0) { throw 'Regression matrix group is empty.' }
    return $flags
}

function Get-TierStatus {
    param([string]$Tier, [bool]$Selected, [string]$Group)
    if (-not $Selected) { return 'NotRun' }
    $rows = @($summary.results | Where-Object { $_.tier -eq $Tier })
    if ($rows.Count -eq 0) { return 'MissingEvidence' }

    # Do not coerce strings such as "false" to true or trust a pass flag
    # when the child actually exited with an error.
    foreach ($row in $rows) {
        if ($row.passed -isnot [bool] -or
            ($row.exitCode -isnot [int] -and $row.exitCode -isnot [long])) {
            return 'MissingEvidence'
        }
        if (-not $row.passed -or $row.exitCode -ne 0) { return 'Fail' }
    }
    $expected = @(Get-ExpectedRunners -Group $Group)
    $actual = @($rows | ForEach-Object { $_.runner })
    if ($actual.Count -ne $expected.Count -or
        @($actual | Select-Object -Unique).Count -ne $expected.Count -or
        @(Compare-Object -ReferenceObject $expected -DifferenceObject $actual).Count -ne 0) {
        return 'MissingEvidence'
    }
    return 'Pass'
}

$core = Get-TierStatus -Tier 'core' -Selected $true -Group 'coreCases'
$nativeFixtures = Get-TierStatus -Tier 'native-fixture' -Selected $summary.selected.nativeFixtures -Group 'nativeFixtureCases'
$ambientDesktop = Get-TierStatus -Tier 'ambient-desktop' -Selected $summary.selected.ambientDesktop -Group 'ambientDesktopCases'
$interactiveDesktop = Get-TierStatus -Tier 'interactive-desktop' -Selected $summary.selected.interactiveDesktop -Group 'interactiveDesktopCases'
$externalAi = Get-TierStatus -Tier 'external-ai' -Selected $summary.selected.externalAi -Group 'externalAiCases'

# Selected optional tiers must have complete passing evidence as well.
$selectedOptionalFailure = $ambientDesktop -in 'Fail', 'MissingEvidence' -or
    $interactiveDesktop -in 'Fail', 'MissingEvidence' -or
    $externalAi -in 'Fail', 'MissingEvidence'
$automatedReady = $core -eq 'Pass' -and $nativeFixtures -eq 'Pass' -and
    -not $selectedOptionalFailure -and $summary.failed -eq 0
$manualPhysicalComplete = $Microphone -eq 'Pass' -and $Speaker -eq 'Pass' -and
    $MultiMonitor -eq 'Pass' -and $TrayShell -eq 'Pass'
$phase12EComplete = $automatedReady -and $manualPhysicalComplete

$signoff = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    commit = $currentCommit
    automated = [ordered]@{
        core = $core
        nativeFixture = $nativeFixtures
        ambientDesktop = $ambientDesktop
        interactiveDesktop = $interactiveDesktop
        externalAi = $externalAi
    }
    physical = [ordered]@{
        microphone = $Microphone
        speaker = $Speaker
        multiMonitor = $MultiMonitor
        trayShell = $TrayShell
    }
    deferred = [ordered]@{
        installer = 'Phase12G'
        updaterEndToEnd = 'Phase12G'
    }
    automatedReady = $automatedReady
    manualPhysicalComplete = $manualPhysicalComplete
    phase12EComplete = $phase12EComplete
}

$jsonPath = Join-Path $regressionDirectory 'regression-signoff.json'
[IO.File]::WriteAllText($jsonPath, ($signoff | ConvertTo-Json -Depth 6))
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('# Lu-Knight Phase 12E Regression Sign-off')
$lines.Add('')
$lines.Add("- Commit: ``$currentCommit``")
$lines.Add("- Phase 12E complete: **$phase12EComplete**")
$lines.Add('')
$lines.Add('## Automated evidence')
$lines.Add('')
$lines.Add('| Tier | Status |')
$lines.Add('| --- | --- |')
$lines.Add("| Core | $core |")
$lines.Add("| Native Fixture | $nativeFixtures |")
$lines.Add("| Ambient Desktop | $ambientDesktop |")
$lines.Add("| Interactive Desktop | $interactiveDesktop |")
$lines.Add("| External AI | $externalAi |")
$lines.Add('')
$lines.Add('## Physical/manual evidence')
$lines.Add('')
$lines.Add('| Area | Status |')
$lines.Add('| --- | --- |')
$lines.Add("| Microphone | $Microphone |")
$lines.Add("| Speaker / TTS | $Speaker |")
$lines.Add("| Multi-monitor | $MultiMonitor |")
$lines.Add("| Windows tray shell | $TrayShell |")
$lines.Add('')
$lines.Add('## Deferred')
$lines.Add('')
$lines.Add('- Installer end-to-end: Phase 12G')
$lines.Add('- Updater end-to-end: Phase 12G')
$lines.Add('')
$lines.Add('> NotRun and MissingEvidence are never equivalent to PASS.')
$markdownPath = Join-Path $regressionDirectory 'regression-signoff.md'
[IO.File]::WriteAllLines($markdownPath, $lines)

Write-Host "Core: $core"
Write-Host "Native fixtures: $nativeFixtures"
Write-Host "Microphone: $Microphone"
Write-Host "Speaker: $Speaker"
Write-Host "Multi-monitor: $MultiMonitor"
Write-Host "Tray shell: $TrayShell"
Write-Host "Phase 12E complete: $phase12EComplete"
Write-Host "Sign-off: $jsonPath"
if (-not $automatedReady) { exit 1 }
exit 0
