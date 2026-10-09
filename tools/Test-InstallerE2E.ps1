param(
    [string]$Iscc = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
    [string]$BaseVersion = '1.0.0',
    [string]$UpgradeVersion = '1.0.1'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$stableVersion = '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$'
if ($BaseVersion -notmatch $stableVersion -or $UpgradeVersion -notmatch $stableVersion) { throw 'E2E versions must be stable semantic versions.' }
if ([Version]$UpgradeVersion -le [Version]$BaseVersion) { throw 'UpgradeVersion must be newer than BaseVersion.' }
function Get-SourceCommit {
    $value = & git -C $repo rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $value -notmatch '^[0-9a-fA-F]{40}$') { throw 'Unable to resolve source commit.' }
    return ([string]$value).Trim()
}
function Assert-CleanSource {
    $dirty = & git -C $repo status --porcelain --untracked-files=all
    if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect Git working tree.' }
    if ($dirty) { throw 'Installer E2E requires a clean Git working tree.' }
}
Assert-CleanSource
$commit = Get-SourceCommit
$token = [Guid]::NewGuid().ToString('N')
$artifacts = Join-Path $repo 'artifacts\installer-e2e'
$work = Join-Path $artifacts ('work-' + $token)
$publishBase = Join-Path $work 'publish-base'
$publishUpgrade = Join-Path $work 'publish-upgrade'
$installerOutput = Join-Path $work 'installers'
$localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
$installRoot = Join-Path $localAppData 'Programs\LuKnight-E2E'
$installDir = Join-Path $installRoot $token
$dataRoot = Join-Path $localAppData 'LuKnight-E2E'
$dataDir = Join-Path $dataRoot $token
function Assert-ChildPath {
    param([string]$Path, [string]$Root)
    $full = [IO.Path]::GetFullPath($Path)
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    if (-not $full.StartsWith($rootFull, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe installer E2E path.' }
}
function Assert-IsolatedPaths {
    Assert-ChildPath $installDir $installRoot
    Assert-ChildPath $dataDir $dataRoot
    Assert-ChildPath $work $artifacts
    if ([IO.Path]::GetFileName($installDir) -cne $token -or [IO.Path]::GetFileName($dataDir) -cne $token -or $token -notmatch '^[a-f0-9]{32}$') { throw 'Unsafe installer E2E identity.' }
}
Assert-IsolatedPaths
if ((Test-Path -LiteralPath $installDir) -or (Test-Path -LiteralPath $dataDir)) { throw 'E2E profile already exists.' }
# Native Windows argument quoting for PowerShell 5.1, which lacks ArgumentList.
function ConvertTo-NativeArgument {
    param([string]$Value)
    $quoted = [regex]::Replace($Value, '(\\*)"', '$1$1\"')
    $quoted = [regex]::Replace($quoted, '(\\+)$', '$1$1')
    return '"' + $quoted + '"'
}
function New-ProcessStart {
    param([string]$FilePath, [string[]]$Arguments = @(), [hashtable]$Environment = @{})
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $FilePath
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    if ($start.PSObject.Properties.Name -contains 'ArgumentList') {
        foreach ($argument in $Arguments) { [void]$start.ArgumentList.Add($argument) }
    } else { $start.Arguments = ($Arguments | ForEach-Object { ConvertTo-NativeArgument $_ }) -join ' ' }
    foreach ($name in $Environment.Keys) { $start.EnvironmentVariables[$name] = [string]$Environment[$name] }
    return $start
}
function Invoke-ProcessChecked {
    param([string]$FilePath, [string[]]$Arguments = @(), [hashtable]$Environment = @{})
    $process = [Diagnostics.Process]::Start((New-ProcessStart $FilePath $Arguments $Environment))
    if ($null -eq $process) { throw 'Process could not be started.' }
    try {
        if (-not $process.WaitForExit(300000)) { $process.Kill(); throw 'E2E process timed out.' }
        if ($process.ExitCode -ne 0) { throw ('Process exited with code ' + $process.ExitCode + '.') }
    } finally { $process.Dispose() }
}

# Inno Setup launchers can exit before their temporary child uninstaller.
# Wait for descendants as well before probing or starting another installer.
function Invoke-InstallerChecked {
    param([string]$FilePath, [string[]]$Arguments)
    $nativeArguments = ($Arguments | ForEach-Object { ConvertTo-NativeArgument $_ }) -join ' '
    $process = Start-Process -FilePath $FilePath -ArgumentList $nativeArguments -WindowStyle Hidden -Wait -PassThru
    if ($null -eq $process) { throw 'Installer process could not be started.' }
    try {
        if ($process.ExitCode -ne 0) { throw ('Installer exited with code ' + $process.ExitCode + '.') }
    } finally { $process.Dispose() }
}
function Invoke-Probe {
    param([string]$Executable, [string]$Mode)
    Invoke-ProcessChecked $Executable @("--installer-e2e-probe=$Mode") @{ LUKNIGHT_E2E_TOKEN = $token }
}
function Publish-Version {
    param([string]$Version, [string]$Output)
    $arguments = @('publish', (Join-Path $repo 'LuKnight.csproj'), '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', $Output,
        "-p:Version=$Version", "-p:AssemblyVersion=$Version.0", "-p:FileVersion=$Version.0", "-p:InformationalVersion=$Version", '-p:DebugType=None', '-p:DebugSymbols=false')
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $Version." }
    Assert-CleanSource
    if ((Get-SourceCommit) -ne $commit) { throw 'Git HEAD changed during installer E2E.' }
}
function Compile-E2EInstaller {
    param([string]$Version, [string]$PublishDirectory, [string]$OutputName)
    $defines = @('/Q', "/DAppVersion=$Version", "/DPublishDir=$PublishDirectory",
        "/DAppIdValue=LuKnight-E2E-$token", "/DAppNameValue=Lu-Knight E2E $($token.Substring(0,8))",
        "/DDefaultDirNameValue={localappdata}\Programs\LuKnight-E2E\$token",
        "/DDefaultGroupNameValue=Lu-Knight E2E $($token.Substring(0,8))", "/DRunValueNameValue=LuKnight-E2E-$token",
        "/DPreferenceKeyValue=Software\LuKnight-E2E\$token", "/DAppMutexValue=Local\LuKnight-E2E-$token",
        "/DDataDirectoryValue={localappdata}\LuKnight-E2E\$token", "/DCredentialTargetValue=LuKnight-E2E/$token/GeminiApiKey",
        "/DOutputDirValue=$installerOutput", "/DOutputBaseFilenameValue=$OutputName", '/DAllowSilentRemovePreferences=1', (Join-Path $repo 'installer\LuKnight.iss'))
    & $Iscc @defines
    if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed for $Version." }
    return (Join-Path $installerOutput "$OutputName.exe")
}
function Assert-InstalledVersion {
    param([string]$Version)
    $exe = Join-Path $installDir 'LuKnight.exe'
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Installed application is missing.' }
    if ((Get-Item -LiteralPath $exe).VersionInfo.FileVersion -ne "$Version.0") { throw 'Installed application version mismatch.' }
}
$installArguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', "/DIR=$installDir")
$uninstallArguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
$cases = [Collections.Generic.List[string]]::new()
$failure = $false
$cleanupFailed = $false
try {
    New-Item -ItemType Directory -Path $publishBase, $publishUpgrade, $installerOutput -Force | Out-Null
    Write-Host 'Building two isolated E2E packages.'
    Publish-Version $BaseVersion $publishBase
    Publish-Version $UpgradeVersion $publishUpgrade
    $installerBase = Compile-E2EInstaller $BaseVersion $publishBase "LuKnightSetup-$BaseVersion"
    $installerUpgrade = Compile-E2EInstaller $UpgradeVersion $publishUpgrade "LuKnightSetup-$UpgradeVersion"
    Assert-CleanSource
    if ((Get-SourceCommit) -ne $commit) { throw 'Git HEAD changed during installer E2E.' }
    Write-Host 'Running isolated install, update, and uninstall scenarios.'
    Invoke-InstallerChecked $installerBase $installArguments
    Assert-InstalledVersion $BaseVersion
    $cases.Add('fresh-install')
    $installedExe = Join-Path $installDir 'LuKnight.exe'
    Invoke-Probe $installedExe 'seed'
    Invoke-Probe $installedExe 'verify-installed'
    $cases.Add('seed-profile')
    $hold = [Diagnostics.Process]::Start((New-ProcessStart $installedExe @('--installer-e2e-probe=hold') @{ LUKNIGHT_E2E_TOKEN = $token }))
    if ($null -eq $hold) { throw 'Unable to start E2E hold process.' }
    try {
        Start-Sleep -Milliseconds 250
        if ($hold.HasExited) { throw 'E2E hold process exited before update handoff.' }
        $upgradeArguments = $installArguments + @('/UPDATE', "/TARGETPID=$($hold.Id)", '/NORUNAPP')
        Invoke-InstallerChecked $installerUpgrade $upgradeArguments
        if (-not $hold.WaitForExit(10000) -or $hold.ExitCode -ne 0) { throw 'E2E hold process failed.' }
    } finally {
        if (-not $hold.HasExited -and -not $hold.WaitForExit(10000)) { $hold.Kill() }
        $hold.Dispose()
    }
    Assert-InstalledVersion $UpgradeVersion
    $cases.Add('upgrade-targetpid')
    Invoke-Probe $installedExe 'verify-installed'
    $cases.Add('upgrade-preserves-data')
    $uninstaller = Join-Path $installDir 'unins000.exe'
    Invoke-InstallerChecked $uninstaller $uninstallArguments
    if (Test-Path -LiteralPath $installedExe) { throw 'Keep-data uninstall left application binary installed.' }
    $probeExe = Join-Path $publishUpgrade 'LuKnight.exe'
    Invoke-Probe $probeExe 'verify-preserved'
    $cases.Add('uninstall-keep-data')
    Invoke-InstallerChecked $installerUpgrade $installArguments
    Assert-InstalledVersion $UpgradeVersion
    Invoke-Probe $installedExe 'verify-preserved'
    $cases.Add('reinstall-preserves-data')
    Invoke-InstallerChecked $uninstaller ($uninstallArguments + @('/REMOVEPREFERENCES'))
    Invoke-Probe $probeExe 'verify-clean'
    $cases.Add('uninstall-remove-data')
    Invoke-InstallerChecked $installerUpgrade $installArguments
    Assert-InstalledVersion $UpgradeVersion
    Invoke-Probe $installedExe 'verify-clean'
    $cases.Add('clean-reinstall')
    Invoke-InstallerChecked $uninstaller ($uninstallArguments + @('/REMOVEPREFERENCES'))
    Invoke-Probe $probeExe 'cleanup'
} catch {
    $failure = $true
    Write-Warning ('Installer E2E stopped after ' + $cases.Count + ' completed cases: ' + $_.Exception.Message)
} finally {
    Assert-IsolatedPaths
    $uninstaller = Join-Path $installDir 'unins000.exe'
    if (Test-Path -LiteralPath $uninstaller -PathType Leaf) {
        try { Invoke-InstallerChecked $uninstaller ($uninstallArguments + @('/REMOVEPREFERENCES')) }
        catch { $cleanupFailed = $true }
    }
    $probe = Join-Path $publishUpgrade 'LuKnight.exe'
    if (-not (Test-Path -LiteralPath $probe -PathType Leaf)) { $probe = Join-Path $publishBase 'LuKnight.exe' }
    if (Test-Path -LiteralPath $probe -PathType Leaf) {
        try { Invoke-Probe $probe 'cleanup' }
        catch { $cleanupFailed = $true }
    }
    if ((Test-Path -LiteralPath (Join-Path $installDir 'LuKnight.exe')) -or (Test-Path -LiteralPath $dataDir)) { $cleanupFailed = $true }
    if (-not $cleanupFailed -and (Test-Path -LiteralPath $work)) {
        Assert-ChildPath $work $artifacts
        Remove-Item -LiteralPath $work -Recurse -Force
    }
}
Assert-CleanSource
if ((Get-SourceCommit) -ne $commit) { throw 'Git HEAD changed during installer E2E.' }
$passed = $cases.Count
$failed = if ($failure -or $cleanupFailed) { 1 } else { 0 }
$summary = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    commit = $commit
    baseVersion = $BaseVersion
    upgradeVersion = $UpgradeVersion
    isolatedProfile = $true
    cases = @($cases.ToArray())
    total = 8
    passed = $passed
    failed = $failed
    cleanupSucceeded = (-not $cleanupFailed)
}
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $artifacts 'installer-e2e-summary.json'), ($summary | ConvertTo-Json -Depth 5))
if ($failed -ne 0 -or $passed -ne 8) { throw 'Installer E2E acceptance failed; inspect sanitized summary and console diagnostics.' }
Write-Host 'PASS: installer E2E fresh install, upgrade, persistence and uninstall sweep.'
