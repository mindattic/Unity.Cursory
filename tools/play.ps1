<#
.SYNOPSIS
    Launches a playable Unity.Cursory for development, muted.
.DESCRIPTION
    Starts the deployed Unity.Cursory.exe (OutDir\Unity.Cursory.exe, building and publishing it with
    tools\run.ps1 first when it is missing or -Build is given) with MINDATTIC_CLI_MUTED=1 and -muted,
    so a CLI or agent launching the game never plays sound. The mute is Unity.Shared's session mute: it
    lasts for this run only and never changes the player's saved settings.json (see SessionAudio.cs and
    Unity.Shared's README "Root override for tests and automation" section). Pass -Sound to launch with
    audio. Any other arguments go to the player (for example -screen-fullscreen 0). This is for manual
    and dev testing only; tools\unity.ps1 runs the batch-mode build and test passes.
.PARAMETER Build
    Rebuild and publish the player with tools\run.ps1 before launching.
.PARAMETER Sound
    Launch with audio (no MINDATTIC_CLI_MUTED, no -muted).
.PARAMETER Wait
    Wait for the player to exit and return its exit code.
.PARAMETER OutDir
    Where the player is published. Defaults to tools\run.ps1's own default, C:\Apps\Unity.Cursory.
.EXAMPLE
    tools\play.ps1
    tools\play.ps1 -Build
    tools\play.ps1 -Sound
#>
param(
    [switch]$Build,
    [switch]$Sound,
    [switch]$Wait,
    [string]$OutDir = 'C:\Apps\Unity.Cursory',
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$PlayerArgs
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot   # tools\ -> repo root
$exe = Join-Path $OutDir 'Unity.Cursory.exe'
$log = Join-Path $repoRoot 'Logs/play.log'

if ($Build -or -not (Test-Path $exe)) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'run.ps1') -OutDir $OutDir
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
New-Item -ItemType Directory -Force (Split-Path $log) | Out-Null

$arguments = @('-logFile', "`"$log`"")
if (-not $Sound) { $arguments += '-muted' }
if ($PlayerArgs) { $arguments += $PlayerArgs }

# The child inherits this process's environment; set the mute only around the launch.
$previous = $env:MINDATTIC_CLI_MUTED
if ($Sound) { Remove-Item Env:MINDATTIC_CLI_MUTED -ErrorAction SilentlyContinue } else { $env:MINDATTIC_CLI_MUTED = '1' }
try {
    $process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $OutDir -PassThru
} finally {
    if ($null -eq $previous) { Remove-Item Env:MINDATTIC_CLI_MUTED -ErrorAction SilentlyContinue } else { $env:MINDATTIC_CLI_MUTED = $previous }
}

Write-Host ("Launched Unity.Cursory (pid {0}, {1}); log {2}" -f $process.Id, $(if ($Sound) { 'with sound' } else { 'muted' }), $log)
if ($Wait) {
    $process.WaitForExit()
    exit $process.ExitCode
}
exit 0
