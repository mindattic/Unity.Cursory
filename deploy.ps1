<#
.SYNOPSIS
    Build Cursory's dedicated server package and deploy it with Unity.Multiplayer's mp-host (UC-T13).

.DESCRIPTION
    1. Builds the server package in Unity batch mode through Unity.Cursory.EditorTools.ServerBuild.BuildFromCommandLine,
       which opens the bootable scene and hands off to the library's ServerPackageBuild (UM-T23): a headless Linux
       x86_64 Dedicated Server build plus server.json, start.sh, Dockerfile and manifest in
       Builds/ServerPackage/<GenericVps|RunPod>.
    2. Hands the package to mp-host (UM-T25), which does everything else:
         GenericVps (default)  mp-host deploy --profile GenericVps: image -> GHCR -> SSH box -> compose with rendezvous
         RunPod                mp-host deploy --profile RunPod: WebSocket transport on a RunPod-mapped TCP port
         Local                 mp-host local up: server + rendezvous on this machine with Docker Desktop

    Credentials (GHCR token, SSH target, RunPod key) live in MindAttic.Vault, stored once with
    `mp-host credentials set-ghcr|set-ssh|set-runpod ...`; nothing credential-shaped is read from or written to this
    repo. The rendezvous shared secret is -RendezvousSecret or $env:MINDATTIC_RENDEZVOUS_SECRET (Local makes a
    throwaway one when neither is given). See docs/hosting.md.

.EXAMPLE
    ./deploy.ps1 -DryRun
    ./deploy.ps1 -Profile GenericVps -Name prod-box -PublicAddress 203.0.113.5
    ./deploy.ps1 -Profile RunPod -Name cursory-ws -RendezvousUrl http://203.0.113.5:8080
    ./deploy.ps1 -Profile Local
    ./deploy.ps1 -Profile Local -Down
#>
[CmdletBinding()]
param(
    [Alias('Profile')]
    [ValidateSet('GenericVps', 'RunPod', 'Local')]
    [string]$HostingProfile = 'GenericVps',

    # Print the Unity command and pass --dry-run to mp-host: nothing is built, pushed or started.
    [switch]$DryRun,

    # Reuse the package already in Builds/ServerPackage/<profile> instead of running Unity.
    [switch]$SkipBuild,

    # Local only: stop the local stack (mp-host local down).
    [switch]$Down,

    # The server's name in the vault (GenericVps: the `mp-host credentials set-ssh --name` target).
    [string]$Name,

    # GHCR repository (ghcr.io/<Image>:<Tag>) for GenericVps and RunPod.
    [string]$Image = 'mindattic/unity-cursory',
    [string]$Tag = 'latest',

    [string]$RendezvousUrl,
    [string]$RendezvousSecret = $env:MINDATTIC_RENDEZVOUS_SECRET,

    # Local only: the rendezvous image; default builds Unity.Multiplayer's server/Rendezvous/Dockerfile locally.
    [string]$RendezvousImage,

    # The address players and rendezvous see (GenericVps: the box's public IP; Local: this PC's LAN IP).
    [string]$PublicAddress,

    # Local only: let mp-host try a UPnP port mapping on the router (off by default).
    [switch]$Upnp,

    # Unity editor executable. Default: $env:UNITY_EDITOR, else Unity Hub's install of ProjectVersion.txt's version.
    [string]$Unity = $env:UNITY_EDITOR,

    # Path to an mp-host executable. Default: $env:MP_HOST, else mp-host on PATH, else `dotnet run` from Unity.Multiplayer.
    [string]$MpHost = $env:MP_HOST,

    [switch]$Development
)

$ErrorActionPreference = 'Stop'
$repo = $PSScriptRoot
$packageProfile = if ($HostingProfile -eq 'RunPod') { 'RunPod' } else { 'GenericVps' }
$packageDir = Join-Path $repo "Builds/ServerPackage/$packageProfile"
if (-not $Name) { $Name = if ($HostingProfile -eq 'Local') { 'cursory-local' } else { 'cursory' } }

function Write-Step([string]$text) { Write-Host "==> $text" -ForegroundColor Cyan }

function Format-Args([string[]]$arguments) {
    # Echo a command line with the rendezvous secret masked.
    $out = @(); $mask = $false
    foreach ($a in $arguments) {
        if ($mask) { $out += '***'; $mask = $false; continue }
        if ($a -eq '--rendezvous-secret') { $mask = $true }
        $out += $(if ($a -match '\s') { "`"$a`"" } else { $a })
    }
    $out -join ' '
}

function Get-MultiplayerRoot {
    if ($env:UNITY_MULTIPLAYER_ROOT) { return (Resolve-Path $env:UNITY_MULTIPLAYER_ROOT).Path }
    $manifest = Get-Content (Join-Path $repo 'Packages/manifest.json') -Raw | ConvertFrom-Json
    $ref = $manifest.dependencies.'com.mindattic.unity.multiplayer'
    if ($ref -notlike 'file:*') { throw "Packages/manifest.json does not reference com.mindattic.unity.multiplayer by file: path; set UNITY_MULTIPLAYER_ROOT." }
    # file:../../Unity.Multiplayer/unity/com.mindattic.unity.multiplayer -> the repo two levels above the package.
    $package = Join-Path (Join-Path $repo 'Packages') $ref.Substring(5)
    if (-not (Test-Path $package)) { throw "The multiplayer package '$package' is missing; check out Unity.Multiplayer beside this repo or set UNITY_MULTIPLAYER_ROOT." }
    (Resolve-Path (Join-Path $package '../..')).Path
}

function Get-UnityEditor {
    if ($Unity) { return $Unity }
    $version = (Select-String -Path (Join-Path $repo 'ProjectSettings/ProjectVersion.txt') -Pattern '^m_EditorVersion:\s*(\S+)').Matches[0].Groups[1].Value
    $candidates = @(
        "$env:ProgramFiles\Unity\Hub\Editor\$version\Editor\Unity.exe",
        "/Applications/Unity/Hub/Editor/$version/Unity.app/Contents/MacOS/Unity",
        "$HOME/Unity/Hub/Editor/$version/Editor/Unity"
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    throw "Unity $version was not found (looked in Unity Hub's default folders). Pass -Unity <editor exe> or set UNITY_EDITOR."
}

function Invoke-Native([scriptblock]$command) {
    # Native tools (docker build, dotnet run) report progress on stderr; with a redirected stream Windows
    # PowerShell would turn that into a terminating error under 'Stop', so judge them by exit code only.
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $command } finally { $ErrorActionPreference = $saved }
}

function Use-LinuxDockerEngine {
    # The server image is Linux. Docker Desktop in Windows-containers mode still runs its Linux engine on its own
    # pipe, so point this process (only) at it rather than asking the user to switch engines.
    if ($env:DOCKER_HOST -or -not (Get-Command docker -ErrorAction SilentlyContinue)) { return }
    $os = & docker info --format '{{.OSType}}' 2>$null
    if ($os -ne 'windows') { return }
    $linux = 'npipe:////./pipe/dockerDesktopLinuxEngine'
    $probe = & docker -H $linux info --format '{{.OSType}}' 2>$null
    if ($probe -eq 'linux') {
        $env:DOCKER_HOST = $linux
        Write-Host "Docker is in Windows-containers mode; using Docker Desktop's Linux engine ($linux) for this run." -ForegroundColor DarkGray
    }
    else { Write-Warning 'Docker is in Windows-containers mode and no Linux engine answered; switch Docker Desktop to Linux containers.' }
}

$script:mpHostCommand = $null
function Invoke-MpHost([string[]]$arguments) {
    if (-not $script:mpHostCommand) {
        if ($MpHost) { $script:mpHostCommand = @($MpHost) }
        elseif (Get-Command mp-host -ErrorAction SilentlyContinue) { $script:mpHostCommand = @('mp-host') }
        else {
            $project = Join-Path (Get-MultiplayerRoot) 'src/Hosting.Cli/Hosting.Cli.csproj'
            $script:mpHostCommand = @('dotnet', 'run', '--project', $project, '-c', 'Release', '--')
        }
    }
    $exe = $script:mpHostCommand[0]
    $prefix = @($script:mpHostCommand | Select-Object -Skip 1)
    Write-Host "mp-host $(Format-Args $arguments)" -ForegroundColor DarkGray
    Invoke-Native { & $exe @prefix @arguments }
    if ($LASTEXITCODE -ne 0) { throw "mp-host exited $LASTEXITCODE." }
}

# --- Local down: nothing to build -------------------------------------------------------------------------------
if ($Down) {
    if ($HostingProfile -ne 'Local') { throw "-Down is for -Profile Local; tear a hosted server down with 'mp-host destroy --profile $HostingProfile --name $Name --server-id <id> --yes'." }
    $downArgs = @('local', 'down', '--name', $Name)
    if ($DryRun) { $downArgs += '--dry-run' } else { Use-LinuxDockerEngine }
    Invoke-MpHost $downArgs
    return
}

# --- 1. Build the server package (Unity batch mode, the library's ServerPackageBuild) ---------------------------
$unityExe = $null
if (-not $SkipBuild) {
    $unityExe = Get-UnityEditor
    $logFile = Join-Path $repo "Builds/ServerPackage/build-$packageProfile.log"
    $unityArgs = @('-batchmode', '-quit', '-nographics', '-projectPath', $repo,
        '-executeMethod', 'Unity.Cursory.EditorTools.ServerBuild.BuildFromCommandLine',
        '-serverProfile', $packageProfile, '-serverOutput', $packageDir, '-logFile', $logFile)
    if ($Development) { $unityArgs += '-serverDevelopment' }

    Write-Step "Build the $packageProfile server package -> $packageDir"
    if ($DryRun) {
        Write-Host "[dry-run] `"$unityExe`" $(Format-Args $unityArgs)"
    }
    else {
        New-Item -ItemType Directory -Force (Split-Path $logFile) | Out-Null
        Write-Host "`"$unityExe`" $(Format-Args $unityArgs)" -ForegroundColor DarkGray
        $quoted = $unityArgs | ForEach-Object { if ($_ -match '\s') { "`"$_`"" } else { $_ } }
        $process = Start-Process -FilePath $unityExe -ArgumentList $quoted -NoNewWindow -Wait -PassThru
        if ($process.ExitCode -ne 0) {
            if (Test-Path $logFile) { Get-Content $logFile -Tail 40 | Write-Host }
            throw "Unity's server package build failed (exit $($process.ExitCode)); full log: $logFile"
        }
    }
}

$haveManifest = Test-Path (Join-Path $packageDir 'server-package.json')
if (-not $DryRun -and -not $haveManifest) { throw "No server package at '$packageDir' (server-package.json missing). Run without -SkipBuild." }

# The package argument mp-host gets: the built folder, or (a dry run with nothing built) the Unity command it narrates.
$packageArgs = if ($haveManifest) { @('--package', $packageDir) }
    else { @('--unity', $(if ($unityExe) { $unityExe } else { 'Unity' }), '--unity-project', $repo) }

# --- 2. Hand off to mp-host ------------------------------------------------------------------------------------
if (-not $DryRun) { Use-LinuxDockerEngine }
switch ($HostingProfile) {
    'Local' {
        if (-not $RendezvousSecret) { $RendezvousSecret = [guid]::NewGuid().ToString('N') + [guid]::NewGuid().ToString('N') }
        if (-not $RendezvousImage) {
            $RendezvousImage = 'mindattic-rendezvous:local'
            $root = Get-MultiplayerRoot
            $dockerArgs = @('build', '-f', (Join-Path $root 'server/Rendezvous/Dockerfile'), '-t', $RendezvousImage, $root)
            Write-Step "Build the rendezvous image from Unity.Multiplayer ($RendezvousImage)"
            if ($DryRun) { Write-Host "[dry-run] docker $(Format-Args $dockerArgs)" }
            else {
                Invoke-Native { & docker @dockerArgs }
                if ($LASTEXITCODE -ne 0) { throw "docker build of the rendezvous image failed (exit $LASTEXITCODE). Is Docker Desktop running?" }
            }
        }

        # Inside the compose network the game reaches rendezvous by its service name; players use <LAN IP>:8080.
        $localArgs = @('local', 'up', '--name', $Name) + $packageArgs + @(
            '--rendezvous-url', $(if ($RendezvousUrl) { $RendezvousUrl } else { 'http://rendezvous:8080' }),
            '--rendezvous-secret', $RendezvousSecret, '--rendezvous-image', $RendezvousImage)
        if ($PublicAddress) { $localArgs += @('--public-address', $PublicAddress) }
        if (-not $Upnp) { $localArgs += '--no-upnp' }
        if ($DryRun) { $localArgs += '--dry-run' }
        Write-Step 'Run the server and rendezvous on this machine (mp-host local up)'
        Invoke-MpHost $localArgs
        if (-not $DryRun) { Write-Host "Stop it with: ./deploy.ps1 -Profile Local -Down$(if ($Name -ne 'cursory-local') { " -Name $Name" })" }
    }

    default {
        if (-not $PublicAddress) {
            if ($HostingProfile -eq 'RunPod') { $PublicAddress = $Name }   # cosmetic on RunPod; the pod advertises RUNPOD_PUBLIC_IP
            elseif ($DryRun) { $PublicAddress = '203.0.113.5'; Write-Warning "No -PublicAddress; the dry run uses the placeholder $PublicAddress." }
            else { throw '-PublicAddress (the box''s public IP or host name) is required for GenericVps.' }
        }
        if (-not $RendezvousUrl) {
            if ($HostingProfile -eq 'GenericVps') { $RendezvousUrl = "http://${PublicAddress}:8080" }   # rendezvous runs beside the game on the box
            elseif ($DryRun) { $RendezvousUrl = 'http://rendezvous.example.com:8080'; Write-Warning "No -RendezvousUrl; the dry run uses the placeholder $RendezvousUrl." }
            else { throw '-RendezvousUrl is required for RunPod: the pod runs no rendezvous of its own (host one with -Profile GenericVps).' }
        }
        if (-not $RendezvousSecret) {
            if ($DryRun) { $RendezvousSecret = 'dry-run-placeholder-secret' }
            else { throw 'No rendezvous secret: pass -RendezvousSecret or set MINDATTIC_RENDEZVOUS_SECRET (the same value on every server that registers with that rendezvous).' }
        }

        $deployArgs = @('deploy', '--profile', $HostingProfile, '--name', $Name) + $packageArgs + @(
            '--image', $Image, '--tag', $Tag,
            '--rendezvous-url', $RendezvousUrl, '--rendezvous-secret', $RendezvousSecret,
            '--public-address', $PublicAddress)
        if ($DryRun) { $deployArgs += '--dry-run' }
        Write-Step "Deploy to $HostingProfile (mp-host deploy)"
        Invoke-MpHost $deployArgs
    }
}
