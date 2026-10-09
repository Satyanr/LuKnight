# Isolated source-gate fixtures: no real Git HEAD, SDK publish, or installer execution.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'Build-Release.ps1'
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('LuKnight-release-source-' + [Guid]::NewGuid().ToString('N'))
$fixture = [IO.Path]::GetFullPath($fixture)
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
if (-not $fixture.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe fixture path.' }
$global:releaseFixtureHead = 'a' * 40
$global:releaseFixtureDirty = ''
$global:releaseFixtureMoveHead = $false
$global:releaseFixturePublishCalled = $false
$global:releaseFixtureCompilerCalled = $false
function git {
    if ($args -contains 'status') { if ($global:releaseFixtureDirty) { $global:releaseFixtureDirty } }
    elseif ($args -contains 'rev-parse') { $global:releaseFixtureHead }
    else { throw 'Unexpected fixture Git command.' }
    $global:LASTEXITCODE = 0
}
function dotnet {
    $global:releaseFixturePublishCalled = $true
    if ($args[0] -ne 'publish') { throw 'Unexpected fixture SDK command.' }
    $output = $args[[Array]::IndexOf($args, '-o') + 1]
    [IO.File]::WriteAllText((Join-Path $output 'coreclr.dll'), 'fixture runtime')
    [IO.File]::WriteAllText((Join-Path $output 'LuKnight.exe'), 'fixture application')
    if ($global:releaseFixtureMoveHead) { $global:releaseFixtureHead = 'b' * 40 }
    $global:LASTEXITCODE = 0
}
function Invoke-FixtureCompiler {
    $global:releaseFixtureCompilerCalled = $true
    throw 'Source gate reached installer compilation unexpectedly.'
}
function Assert-Rejected {
    param([string]$ExpectedMessage, [string]$ExpectedCommit = ('a' * 40))
    $global:releaseFixturePublishCalled = $false
    $global:releaseFixtureCompilerCalled = $false
    try {
        & $fixtureBuild -Version '1.0.0' -ExpectedCommit $ExpectedCommit -Iscc 'Invoke-FixtureCompiler'
        throw 'Invalid release source was accepted.'
    } catch {
        if ($_.Exception.Message -ne $ExpectedMessage) { throw }
    }
    if ($global:releaseFixtureCompilerCalled) { throw 'Rejected source reached installer compilation.' }
}
try {
    New-Item -ItemType Directory -Path (Join-Path $fixture 'tools') -Force | Out-Null
    $fixtureBuild = Join-Path $fixture 'tools\Build-Release.ps1'
    Copy-Item -LiteralPath $source -Destination $fixtureBuild
    foreach ($entry in @(' M LuKnight.csproj', '?? untracked.cs')) {
        $global:releaseFixtureDirty = $entry
        Assert-Rejected 'Release packaging requires a clean Git working tree.'
        if ($global:releaseFixturePublishCalled -or (Test-Path -LiteralPath (Join-Path $fixture 'artifacts'))) {
            throw 'Dirty source touched publish staging.'
        }
    }
    $global:releaseFixtureDirty = ''
    Assert-Rejected 'Current HEAD does not match the expected release commit.' ('b' * 40)
    if ($global:releaseFixturePublishCalled) { throw 'Wrong source commit reached publish.' }
    $global:releaseFixtureMoveHead = $true
    Assert-Rejected 'Git HEAD changed during release publish.'
    if (-not $global:releaseFixturePublishCalled) { throw 'Moving-HEAD fixture did not reach publish.' }
    Write-Output 'PASS: 4 isolated release source-gate fixtures.'
} finally {
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force }
}
