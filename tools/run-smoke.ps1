<#
.SYNOPSIS
  UC-T14's nightly end-to-end test, locally: builds the Linux player, builds the images, runs the compose stack
  (rendezvous + dedicated server on level 2 + two bot clients) and waits for both bots to see level 2 solved.

.DESCRIPTION
  The same steps as .github/workflows/nightly.yml. Unity builds smoke/build/Cursory.x86_64 through
  Unity.Cursory.EditorTools.BotBuild.BuildFromCommandLine; the rendezvous image is built from Unity.Multiplayer's
  server/Rendezvous/Dockerfile (found beside this repo through Packages/manifest.json, or UNITY_MULTIPLAYER_ROOT).
  Exits 0 when both bots log "[BotDriver:X] OK:", 1 otherwise. Logs land in smoke/logs/ (git-ignored).

.PARAMETER Unity
  The 6000.4.3f1 editor. Defaults to UNITY_EDITOR, then Unity Hub's install of ProjectSettings/ProjectVersion.txt.

.PARAMETER SkipBuild
  Reuse smoke/build/ instead of building the player again.

.PARAMETER Keep
  Leave the stack running afterwards (docker compose -f smoke/docker-compose.yml down -v stops it).

.PARAMETER TimeoutSeconds
  How long to wait for both bots.
#>
param(
    [string]$Unity = $env:UNITY_EDITOR,
    [switch]$SkipBuild,
    [switch]$Keep,
    [int]$TimeoutSeconds = 300
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$smoke = Join-Path $repo 'smoke'
$build = Join-Path $smoke 'build'
$logs = Join-Path $smoke 'logs'
New-Item -ItemType Directory -Force $logs | Out-Null

function Invoke-Native([scriptblock]$command) {
    # docker reports progress on stderr; judge native tools by exit code only.
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $command } finally { $ErrorActionPreference = $saved }
}

function Get-MultiplayerRoot {
    if ($env:UNITY_MULTIPLAYER_ROOT) { return (Resolve-Path $env:UNITY_MULTIPLAYER_ROOT).Path }
    $manifest = Get-Content (Join-Path $repo 'Packages/manifest.json') -Raw | ConvertFrom-Json
    $ref = $manifest.dependencies.'com.mindattic.unity.multiplayer'
    $package = Join-Path (Join-Path $repo 'Packages') $ref.Substring(5)
    (Resolve-Path (Join-Path $package '../..')).Path
}

function Get-UnityEditor {
    if ($Unity) { return $Unity }
    $version = (Select-String -Path (Join-Path $repo 'ProjectSettings/ProjectVersion.txt') -Pattern '^m_EditorVersion:\s*(\S+)').Matches[0].Groups[1].Value
    $exe = "$env:ProgramFiles\Unity\Hub\Editor\$version\Editor\Unity.exe"
    if (Test-Path $exe) { return $exe }
    throw "Unity $version was not found; pass -Unity or set UNITY_EDITOR."
}

# --- 1. the Linux player every role runs ------------------------------------------------------------------------
if (-not $SkipBuild) {
    Remove-Item $build -Recurse -Force -ErrorAction SilentlyContinue
    $a = @('-batchmode', '-nographics', '-quit', '-projectPath', $repo, '-executeMethod', 'Unity.Cursory.EditorTools.BotBuild.BuildFromCommandLine',
        '-botOutput', $build, '-logFile', (Join-Path $logs 'build.log'))
    $p = Start-Process -FilePath (Get-UnityEditor) -ArgumentList $a -Wait -PassThru -NoNewWindow
    if ($p.ExitCode -ne 0 -or -not (Test-Path (Join-Path $build 'Cursory.x86_64'))) {
        Write-Host "FAIL: the Linux player did not build (Unity exit $($p.ExitCode)); see $(Join-Path $logs 'build.log')" -ForegroundColor Red
        exit 1
    }
}

# --- 2. images ---------------------------------------------------------------------------------------------------
# Every image is Linux. Docker Desktop in Windows-containers mode still runs its Linux engine on its own pipe, so
# point this process at it (as deploy.ps1 does) rather than asking for an engine switch.
if (-not $env:DOCKER_HOST) {
    $os = Invoke-Native { docker info --format '{{.OSType}}' 2>$null }
    if ($os -eq 'windows') {
        $linux = 'npipe:////./pipe/dockerDesktopLinuxEngine'
        if ((Invoke-Native { docker -H $linux info --format '{{.OSType}}' 2>$null }) -eq 'linux') { $env:DOCKER_HOST = $linux }
        else { Write-Warning 'Docker is in Windows-containers mode and no Linux engine answered; switch Docker Desktop to Linux containers.' }
    }
}

$mp = Get-MultiplayerRoot
Invoke-Native { docker build -q -f (Join-Path $mp 'server/Rendezvous/Dockerfile') -t cursory-rendezvous:ci $mp }
if ($LASTEXITCODE -ne 0) { Write-Host 'FAIL: rendezvous image' -ForegroundColor Red; exit 1 }
Invoke-Native { docker build -q -f (Join-Path $smoke 'Bot.Dockerfile') -t cursory-smoke:ci $smoke }
if ($LASTEXITCODE -ne 0) { Write-Host 'FAIL: game image' -ForegroundColor Red; exit 1 }

# --- 3. run and wait ---------------------------------------------------------------------------------------------
$compose = Join-Path $smoke 'docker-compose.yml'
Invoke-Native { docker compose -f $compose down -v 2>$null | Out-Null }
Invoke-Native { docker compose -f $compose up -d }
if ($LASTEXITCODE -ne 0) { Write-Host 'FAIL: docker compose up' -ForegroundColor Red; exit 1 }

$result = 1
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while ((Get-Date) -lt $deadline) {
    $text = (Invoke-Native { docker compose -f $compose logs --no-color botA botB 2>$null }) -join "`n"
    if ($text -match '\[BotDriver:A\] OK:' -and $text -match '\[BotDriver:B\] OK:') {
        ($text -split "`n") | Where-Object { $_ -match '\[BotDriver:.\] OK:' } | ForEach-Object { Write-Host $_ }
        Write-Host 'PASS: both bots saw level 2 solved.' -ForegroundColor Green
        $result = 0
        break
    }
    if ($text -match 'FAIL:') {
        ($text -split "`n") | Where-Object { $_ -match 'FAIL:' } | ForEach-Object { Write-Host $_ -ForegroundColor Red }
        break
    }
    Start-Sleep -Seconds 3
}
if ($result -ne 0 -and (Get-Date) -ge $deadline) { Write-Host "FAIL: timed out after $TimeoutSeconds s" -ForegroundColor Red }

Invoke-Native { docker compose -f $compose logs --no-color } | Set-Content -Path (Join-Path $logs 'compose.log') -Encoding utf8
if (-not $Keep) { Invoke-Native { docker compose -f $compose down -v 2>$null | Out-Null } }
exit $result
