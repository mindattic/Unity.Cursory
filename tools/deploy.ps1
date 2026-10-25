#Requires -Version 5.1
<#
.SYNOPSIS
    The Windows CLIENT deploy (UC-T27): pulls latest, rebuilds the Windows Release player from scratch only if the
    player's source changed, publishes a standalone copy to C:\Apps\Unity.Cursory\, refreshes the Desktop shortcut
    and (with -Launch) runs it. Same pipeline as Unity.Eve, Unity.SpaceGod and Unity.GridGame.

.DESCRIPTION
    Not the dedicated server: the root deploy.ps1 hosts that through Unity.Multiplayer's `mp-host game` (UM-T58).
    deploy.bat (beside this repo's root deploy.ps1) is this script's entry point and what the Desktop shortcut runs.

    1. git pull --ff-only, unless tracked files have local changes or the pull fails (then it deploys the commit
       already checked out; untracked files such as .prose/ never block it).
    2. Skips the rebuild when C:\Apps\Unity.Cursory\.deploy-commit names a commit whose Assets, Packages and
       ProjectSettings match HEAD (a docs- or tickets-only commit doesn't rebuild the game).
    3. Stops any running Unity.Cursory.exe.
    4. Removes Build\WindowsRelease - the build OUTPUT only, never Library\ (Unity's import / compile cache).
    5. Runs tools\unity.ps1 -Method Unity.Cursory.EditorTools.CursoryBuild.BuildWindowsRelease (Editor must be
       closed) -> Build\WindowsRelease\Unity.Cursory.exe.
    6. Replaces C:\Apps\Unity.Cursory\ with it, writes .deploy-commit, and points the Desktop shortcut
       Unity.Cursory.lnk at deploy.bat (not the exe), so a double-click always pulls and rebuilds when needed.

.PARAMETER Launch
    After publishing (or skipping), run the deployed Unity.Cursory.exe.

.PARAMETER DryRun
    Don't pull, stop, build, publish or touch the shortcut: only report whether a deploy would rebuild.

.PARAMETER OutDir
    Where to publish. Defaults to C:\Apps\Unity.Cursory.

.EXAMPLE
    deploy.bat
    powershell -ExecutionPolicy Bypass -File tools\deploy.ps1 -DryRun
    powershell -ExecutionPolicy Bypass -File tools\deploy.ps1 -Launch
#>
param(
    [switch]$Launch,
    [switch]$DryRun,
    [string]$OutDir = 'C:\Apps\Unity.Cursory'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoDir   = Split-Path $PSScriptRoot   # tools\ -> repo root
$out       = $OutDir
$exeName   = 'Unity.Cursory.exe'
$procName  = [IO.Path]::GetFileNameWithoutExtension($exeName)
$buildDir  = Join-Path $repoDir 'Build\WindowsRelease'
$deployBat = Join-Path $repoDir 'deploy.bat'
$marker    = Join-Path $out '.deploy-commit'
$shortcut  = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Unity.Cursory.lnk'   # not $HOME\Desktop: OneDrive may move it

# ── Pull latest ────────────────────────────────────────────────────────────
# The ticket tools keep docs/USER_STORIES.md modified, so local changes skip the pull rather than throw: throwing
# would leave the game unopenable from the shortcut (U.SPC-T368).
if ($DryRun) {
    Write-Host '  Dry run: not pulling.' -ForegroundColor DarkGray
} else {
    $dirty = git -C $repoDir status --porcelain | Where-Object { $_ -notmatch '^\?\? ' }
    if ($dirty) {
        Write-Host "  $repoDir has uncommitted changes - not pulling; deploying the current commit." -ForegroundColor DarkYellow
    } else {
        git -C $repoDir pull --ff-only
        if ($LASTEXITCODE -ne 0) { Write-Host "  git pull --ff-only failed in $repoDir - deploying the current commit." -ForegroundColor DarkYellow }
    }
}
$head = (git -C $repoDir rev-parse HEAD).Trim()

function Set-Shortcut([string]$exe) {
    $link = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcut)
    $link.TargetPath       = $deployBat
    $link.WorkingDirectory = $repoDir
    $link.IconLocation     = "$exe,0"
    $link.Description      = 'Pull latest, build Release if changed, and launch Unity.Cursory'
    $link.Save()
    Write-Host "    Shortcut -> $shortcut" -ForegroundColor Gray
}

# ── Skip the rebuild if nothing the player is built from changed ─────────────
$deployed = $null
$upToDate = $false
if ((Test-Path (Join-Path $out $exeName)) -and (Test-Path $marker)) {
    $deployed = (Get-Content -LiteralPath $marker -Raw).Trim()
    # A marker naming an unknown commit makes git write to stderr, which Stop would turn into a throw: it means rebuild.
    $ErrorActionPreference = 'Continue'
    git -C $repoDir diff --quiet $deployed $head -- Assets Packages ProjectSettings 2>$null
    $upToDate = $LASTEXITCODE -eq 0
    $ErrorActionPreference = 'Stop'
}
if ($DryRun) {
    if ($upToDate) { Write-Host "  Dry run: deployed $deployed matches $head (no player source changed) - would skip the rebuild." -ForegroundColor Green }
    elseif ($deployed) { Write-Host "  Dry run: deployed $deployed differs from $head in Assets/Packages/ProjectSettings - would rebuild." -ForegroundColor Yellow }
    else { Write-Host "  Dry run: nothing deployed at $out (or no .deploy-commit) - would build $head." -ForegroundColor Yellow }
    exit 0
}
if ($upToDate) {
    if ($deployed -ne $head) { Set-Content -LiteralPath $marker -Value $head -NoNewline }
    Write-Host "  Already deployed at $head (no player source changed) - skipping rebuild." -ForegroundColor Green
    if (-not (Test-Path $shortcut)) { Set-Shortcut (Join-Path $out $exeName) }
    if ($Launch) { Start-Process -FilePath (Join-Path $out $exeName) -WorkingDirectory $out }
    exit 0
}

# ── Stop running instance ──────────────────────────────────────────────────
Write-Host ''
Write-Host '  Stopping running instance...' -ForegroundColor Yellow
$procs = Get-Process $procName -ErrorAction SilentlyContinue
if ($procs) {
    $procs | Stop-Process -Force
    Write-Host "    Stopped ($(@($procs).Count) process(es))" -ForegroundColor DarkYellow
    Start-Sleep -Milliseconds 800
} else {
    Write-Host '    Nothing running.' -ForegroundColor DarkGray
}

# ── Clear build output ───────────────────────────────────────────────────────
Write-Host ''
Write-Host '  Clearing build output (Build\WindowsRelease)...' -ForegroundColor Cyan
if (Test-Path $buildDir) { Remove-Item -Recurse -Force $buildDir }

# ── Build ────────────────────────────────────────────────────────────────────
Write-Host ''
Write-Host '  Building Windows Release player (Unity batch mode)...' -ForegroundColor Cyan
# The URP and ProjectSettings assets are committed in the form Unity 6000.4.3f1 writes them (UC-T30), so a build
# leaves the repo clean and the next deploy can still pull.
& (Join-Path $PSScriptRoot 'unity.ps1') -Method Unity.Cursory.EditorTools.CursoryBuild.BuildWindowsRelease -Log deploy-build -Graphics
$buildExit = $LASTEXITCODE
if ($buildExit -ne 0) {
    Write-Host "Build failed (exit $buildExit). Log: $repoDir\Logs\deploy-build.log" -ForegroundColor Red
    exit $buildExit
}

$builtExe = Join-Path $buildDir $exeName
if (-not (Test-Path $builtExe)) {
    Write-Host "Build completed but $exeName not found at $builtExe." -ForegroundColor Red
    exit 1
}

# ── Publish ─────────────────────────────────────────────────────────────────
Write-Host ''
Write-Host "  Publishing $builtExe" -ForegroundColor Cyan
Write-Host "    -> $out\$exeName"

if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force $out | Out-Null
Copy-Item -Recurse -Force (Join-Path $buildDir '*') $out

$exe = Join-Path $out $exeName
if (-not (Test-Path $exe)) {
    Write-Host "Publish completed but $exeName not found at $exe." -ForegroundColor Red
    exit 1
}

Set-Content -LiteralPath $marker -Value $head -NoNewline

# ── Desktop shortcut ─────────────────────────────────────────────────────────
Set-Shortcut $exe

# ── Summary ────────────────────────────────────────────────────────────────
Write-Host ''
Write-Host '  Published successfully.' -ForegroundColor Green
Write-Host "    Exe : $exe ($([math]::Round(((Get-Item $exe).Length / 1MB), 1)) MB)" -ForegroundColor Gray
Write-Host ''

if ($Launch) {
    Write-Host '  Launching...' -ForegroundColor Cyan
    Start-Process -FilePath $exe -WorkingDirectory $out
}

# An explicit success code: a caller checking $LASTEXITCODE would otherwise read the build step's value or nothing.
exit 0
