# Exercise acceptance decisions with synthetic evidence in an isolated Git repo.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('LuKnight-signoff-' + [guid]::NewGuid().ToString('N'))
$reportDirectory = Join-Path $testRoot 'artifacts\regression'
$testTools = Join-Path $testRoot 'tools'
$checks = 0

function Assert-Case {
    param([string]$Name, [object]$Evidence, [int]$ExpectedExit,
        [string]$Status, [string]$Tier = 'nativeFixture', [string[]]$PhysicalArguments = @())
    $summaryPath = Join-Path $reportDirectory 'regression-summary.json'
    if ($null -eq $Evidence) {
        Remove-Item -LiteralPath $summaryPath -ErrorAction SilentlyContinue
    } else {
        [IO.File]::WriteAllText($summaryPath, ($Evidence | ConvertTo-Json -Depth 8))
    }
    $signoffPath = Join-Path $reportDirectory 'regression-signoff.json'
    Remove-Item -LiteralPath $signoffPath -ErrorAction SilentlyContinue
    $previousErrorPreference = $ErrorActionPreference
    try {
        # Windows PowerShell promotes child stderr to errors. Rejections are
        # intentional here; assert the process exit instead of aborting early.
        $ErrorActionPreference = 'Continue'
        & powershell -NoProfile -File (Join-Path $testTools 'New-RegressionSignoff.ps1') @PhysicalArguments *> $null
        $caseExit = $LASTEXITCODE
    } finally { $ErrorActionPreference = $previousErrorPreference }
    if ($caseExit -ne $ExpectedExit) { throw "Unexpected exit for $Name." }
    if ($Status -eq 'Rejected') {
        if (Test-Path -LiteralPath $signoffPath) { throw "Rejected evidence produced sign-off: $Name." }
    } else {
        $signoff = Get-Content -LiteralPath $signoffPath -Raw | ConvertFrom-Json
        if ($signoff.automated.$Tier -ne $Status) { throw "Wrong tier status for $Name." }
        $physicalPass = $PhysicalArguments.Count -gt 0
        if ($signoff.manualPhysicalComplete -ne $physicalPass -or
            $signoff.phase12EComplete -ne ($physicalPass -and $ExpectedExit -eq 0)) {
            throw "Wrong physical completion for $Name."
        }
        if (-not $physicalPass -and $signoff.physical.microphone -ne 'NotRun') {
            throw "Physical defaults changed for $Name."
        }
    }
    $script:checks++
}

try {
    New-Item -ItemType Directory -Path $testTools, $reportDirectory -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repo 'tools\New-RegressionSignoff.ps1') -Destination $testTools
    Copy-Item -LiteralPath (Join-Path $repo 'tools\Run-Regression.ps1') -Destination $testTools
    [IO.File]::WriteAllText((Join-Path $testRoot '.gitignore'), "artifacts/`n")
    & git -C $testRoot init --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Unable to initialize synthetic evidence repo.' }
    & git -C $testRoot add -- .gitignore tools
    if ($LASTEXITCODE -ne 0) { throw 'Unable to stage synthetic evidence repo.' }
    & git -C $testRoot -c user.name=SignoffChecks -c user.email=signoff@example.invalid commit --quiet -m 'Synthetic evidence fixture'
    if ($LASTEXITCODE -ne 0) { throw 'Unable to commit synthetic evidence repo.' }
    $commit = (& git -C $testRoot rev-parse HEAD).Trim()
    $runner = Get-Content -LiteralPath (Join-Path $testTools 'Run-Regression.ps1') -Raw
    $groups = [ordered]@{
        'core' = 'coreCases'
        'native-fixture' = 'nativeFixtureCases'
        'ambient-desktop' = 'ambientDesktopCases'
        'interactive-desktop' = 'interactiveDesktopCases'
        'external-ai' = 'externalAiCases'
    }
    $allRows = @(foreach ($tier in $groups.Keys) {
        $block = [regex]::Match($runner, '\$' + $groups[$tier] + '\s*=\s*@\((.*?)\)', 'Singleline')
        foreach ($flag in [regex]::Matches($block.Groups[1].Value, "'(?<flag>--[a-z0-9-]+)'")) {
            [pscustomobject]@{ tier = $tier; runner = $flag.Groups['flag'].Value; passed = $true; exitCode = 0 }
        }
    })
    $baseline = [pscustomobject]@{
        schemaVersion = 1
        commit = $commit
        selected = [pscustomobject]@{ nativeFixtures = $true; ambientDesktop = $false; interactiveDesktop = $false; externalAi = $false }
        failed = 0
        results = @($allRows | Where-Object { $_.tier -in 'core', 'native-fixture' })
    }
    function New-Evidence { $baseline | ConvertTo-Json -Depth 8 | ConvertFrom-Json }

    Assert-Case 'complete automated evidence' (New-Evidence) 0 'Pass'
    $dirtyProbe = Join-Path $testRoot 'dirty-probe.txt'
    [IO.File]::WriteAllText($dirtyProbe, 'dirty')
    Assert-Case 'untracked dirty working tree' (New-Evidence) 1 'Rejected'
    $previousErrorPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & powershell -NoProfile -NonInteractive -File (Join-Path $testTools 'Run-Regression.ps1') -NoBuild *> $null
        $runnerExit = $LASTEXITCODE
    } finally { $ErrorActionPreference = $previousErrorPreference }
    if ($runnerExit -ne 1 -or (Test-Path -LiteralPath (Join-Path $reportDirectory 'regression-summary.md'))) {
        throw 'Dirty release gate did not reject before producing evidence.'
    }
    $checks++
    Remove-Item -LiteralPath $dirtyProbe -Force
    $trackedProbe = Join-Path $testRoot '.gitignore'
    [IO.File]::AppendAllText($trackedProbe, "# dirty tracked file`n")
    Assert-Case 'tracked dirty working tree' (New-Evidence) 1 'Rejected'
    & git -C $testRoot restore -- .gitignore
    if ($LASTEXITCODE -ne 0) { throw 'Unable to reset synthetic tracked probe.' }
    Assert-Case 'explicit physical attestations' (New-Evidence) 0 'Pass' -PhysicalArguments @(
        '-Microphone', 'Pass', '-Speaker', 'Pass', '-MultiMonitor', 'Pass', '-TrayShell', 'Pass')
    $evidence = New-Evidence; $evidence.commit = '0000000000000000000000000000000000000000'
    Assert-Case 'stale commit' $evidence 1 'Rejected'
    $evidence = New-Evidence; $evidence.commit = 'unknown'
    Assert-Case 'unknown commit' $evidence 1 'Rejected'
    Assert-Case 'missing summary' $null 1 'Rejected'
    $evidence = New-Evidence; $evidence.selected.nativeFixtures = $false
    Assert-Case 'native not selected' $evidence 1 'NotRun'
    $evidence = New-Evidence; $evidence.results = @($evidence.results | Where-Object { $_.tier -eq 'core' })
    Assert-Case 'native empty' $evidence 1 'MissingEvidence'
    $evidence = New-Evidence; $evidence.results = @($evidence.results | Where-Object { $_.runner -ne '--companion-live' })
    Assert-Case 'native partial' $evidence 1 'MissingEvidence'
    $evidence = New-Evidence; $evidence.results += $evidence.results[-1]
    Assert-Case 'native duplicate' $evidence 1 'MissingEvidence'
    $evidence = New-Evidence; $evidence.results[-1].passed = $false; $evidence.failed = 1
    Assert-Case 'native fail' $evidence 1 'Fail'
    $evidence = New-Evidence; $evidence.results[-1].exitCode = 1
    Assert-Case 'nonzero exit despite pass flag' $evidence 1 'Fail'
    $evidence = New-Evidence; $evidence.results[-1].passed = 'false'
    Assert-Case 'nonboolean pass flag' $evidence 1 'MissingEvidence'
    foreach ($selection in 'ambientDesktop', 'interactiveDesktop', 'externalAi') {
        $tierName = switch ($selection) {
            'ambientDesktop' { 'ambient-desktop' }
            'interactiveDesktop' { 'interactive-desktop' }
            'externalAi' { 'external-ai' }
        }
        $evidence = New-Evidence; $evidence.selected.$selection = $true
        Assert-Case "$selection missing" $evidence 1 'MissingEvidence' -Tier $selection
        $evidence.results += @($allRows | Where-Object { $_.tier -eq $tierName })
        Assert-Case "$selection pass" $evidence 0 'Pass' -Tier $selection
        $evidence.results[-1].passed = $false; $evidence.failed = 1
        Assert-Case "$selection fail" $evidence 1 'Fail' -Tier $selection
    }
    Write-Host "PASS: $checks synthetic regression sign-off behavior checks."
} finally {
    $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
    $resolvedTempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolvedTestRoot.StartsWith($resolvedTempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolvedTestRoot) -notlike 'LuKnight-signoff-*') {
        throw 'Synthetic fixture cleanup path is outside the intended temporary directory.'
    }
    if (Test-Path -LiteralPath $resolvedTestRoot) {
        Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
    }
}
