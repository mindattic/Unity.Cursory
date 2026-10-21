<#
.SYNOPSIS
    Deploy Cursory's dedicated server: a thin call to Unity.Multiplayer's `mp-host game` (UM-T58).

.DESCRIPTION
    Server only: the Windows client is deployed to this PC by deploy.bat (tools/deploy.ps1, UC-T27).
    Kept so the global /deploy router finds a deploy.ps1; everything it does is generic `mp-host game`, which finds
    Unity 6000.4.3f1 from ProjectSettings/ProjectVersion.txt, builds the server package from Main.unity (the first
    Build Settings scene) with the library's batch build, and runs the mp-host pipeline. Defaults (server name
    'cursory', image mindattic/unity-cursory) are in mp-host.json. Any other mp-host game option passes through, e.g.
    --public-address 203.0.113.5, --rendezvous-url ..., --upnp, --development. See docs/hosting.md.

.EXAMPLE
    ./deploy.ps1 -DryRun
    ./deploy.ps1 -Profile GenericVps --public-address 203.0.113.5
    ./deploy.ps1 -Profile RunPod --rendezvous-url http://203.0.113.5:8080
    ./deploy.ps1 -Profile Local
    ./deploy.ps1 -Profile Local -Down
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    # GenericVps (default), RunPod, Local, or any other mp-host profile.
    [Alias('Profile')]
    [string]$HostingProfile = 'GenericVps',

    # Narrate every step; nothing is built, pushed or started.
    [switch]$DryRun,

    # Reuse the package already in Builds/ServerPackage instead of running Unity.
    [switch]$SkipBuild,

    # Local only: stop the local server.
    [switch]$Down,

    # Further mp-host game options, passed through as given.
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$MpHostArgs
)

$ErrorActionPreference = 'Stop'

$verb = if ($Down) { 'down' } elseif ($HostingProfile -eq 'Local') { 'local' } else { 'deploy' }
$arguments = @('game', $verb, '--project', $PSScriptRoot)
if ($verb -eq 'deploy') { $arguments += @('--profile', $HostingProfile) }
if ($DryRun) { $arguments += '--dry-run' }
if ($SkipBuild -and -not $Down) { $arguments += '--skip-build' }
if ($MpHostArgs) { $arguments += $MpHostArgs }

# mp-host: $env:MP_HOST, else the installed tool (Unity.Multiplayer's tools/install-mp-host.ps1), else a dotnet run of
# the Unity.Multiplayer checkout beside this repo.
[string[]]$command = if ($env:MP_HOST) { @($env:MP_HOST) }
    elseif (Get-Command mp-host -ErrorAction SilentlyContinue) { @('mp-host') }
    else { @('dotnet', 'run', '--project', (Join-Path $PSScriptRoot '../Unity.Multiplayer/src/Hosting.Cli/Hosting.Cli.csproj'), '-c', 'Release', '--') }

$exe = $command[0]
$prefix = @($command | Select-Object -Skip 1)
& $exe @prefix @arguments
exit $LASTEXITCODE
