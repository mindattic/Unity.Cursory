# Hosting Cursory's dedicated server

Cursory runs a headless dedicated server that owns the simulation (BIBLE UC-LAW-1). Everything about building
and hosting it comes from Unity.Multiplayer; this page only covers what's specific to Cursory. For the
general picture (profiles, ports, TLS, updates) read the library's
[hosting guide](https://mindattic.github.io/Unity.Multiplayer/articles/hosting.html), plus
[GenericVps](https://mindattic.github.io/Unity.Multiplayer/articles/hosting/genericvps.html),
[RunPod](https://mindattic.github.io/Unity.Multiplayer/articles/hosting/runpod.html) and
[Local](https://mindattic.github.io/Unity.Multiplayer/articles/hosting/local.html).

## One command

Cursory has no build or deploy code of its own (UM-T58). Unity.Multiplayer's `mp-host game` builds and deploys any
game on the library; `deploy.ps1` is only a thin call to it, kept so the global `/deploy` router finds it.

```powershell
./deploy.ps1 -DryRun                     # see every step without building or touching a server
./deploy.ps1 --public-address <vps ip>   # GenericVps (the default): your own UDP-capable Linux VPS
./deploy.ps1 -Profile RunPod --rendezvous-url http://<vps>:8080   # a RunPod pod over WebSocket
./deploy.ps1 -Profile Local              # server + rendezvous on this PC with Docker Desktop
./deploy.ps1 -Profile Local -Down        # stop the local stack

# the same, without the script (mp-host installed with Unity.Multiplayer's tools/install-mp-host.ps1):
mp-host game local                       # from this folder; 'mp-host game down' stops it
mp-host game deploy --dry-run
```

`deploy.ps1` takes `-Profile` (any mp-host profile), `-DryRun`, `-SkipBuild` and `-Down`, and passes every other
`mp-host game` option through as given (`--public-address`, `--rendezvous-url`, `--upnp`, `--development`, ...).
It runs `$env:MP_HOST`, else the installed `mp-host`, else a `dotnet run` of `../Unity.Multiplayer/src/Hosting.Cli`.

What `mp-host game` does for Cursory:

1. **Finds Unity 6000.4.3f1** from `ProjectSettings/ProjectVersion.txt` in Unity Hub's folders (`--unity <exe>` or
   `$env:UNITY_EDITOR` override it). It needs **Linux Dedicated Server Build Support**.
2. **Builds the server package** with the library's own batch entry point,
   `Unity.Multiplayer.Editor.ServerPackageBuild.BuildFromCommandLine -serverScene Assets/Unity.Cursory/Scenes/Main.unity`
   (the first Build Settings scene), the same code as **MindAttic > Multiplayer > Build Server Package**. The output is
   a headless Linux x86_64 Dedicated Server build plus `server.json` (from Main.unity's `NetBootstrap`), `start.sh`, a
   `Dockerfile` and `server-package.json` in `Builds/ServerPackage/<GenericVps|RunPod>` (git-ignored), with the
   Unity log beside it.
3. **Runs the mp-host pipeline**: `mp-host deploy --profile GenericVps|RunPod` builds the Docker image, pushes it to
   GHCR and boots it next to rendezvous; `mp-host local up` renders a compose file for this machine.

`mp-host.json` holds Cursory's only defaults: server name `cursory` (`cursory-local` for Local) and image
`mindattic/unity-cursory`.
## The server scene

`Scenes/Main.unity` is the one scene that both clients and the server boot, so that NGO scene sync matches. Its
`NetworkManager` object holds the library's `NetworkManager`, Unity Transport and `NetBootstrap`, plus
Cursory's `LevelServer`. A headless build serves by itself: `NetBootstrap` switches to the Server topology when
it runs headless. When a Host or Server session starts, `LevelServer` loads the `LevelLibrary`, builds level 1's
`PhysicsWorld` and binds a `ServerSimulation` to the network tick. On every peer, before the session
starts, it also registers the UC-T7 network prefabs and sets the `CursorAvatar` as the player prefab. Bodies
replicate through `NgoBodySync`, and `GeometryBroadcast` sends the walls and labels after each level load. To recreate that wiring, run
**Unity.Cursory > Set Up Server Scene**. A second run changes nothing.

The defaults are the scene's `NetBootstrap` values: port 7777/UDP, 30 Hz. RunPod forces the WebSocket transport
because RunPod pods have no UDP. Rendezvous URL, secret and public address reach the container as
`MINDATTIC_*` environment variables from `mp-host`, so you never bake them into the build.

## Credentials

Credentials never live in this repo. Store them once in MindAttic.Vault through `mp-host`:

```powershell
mp-host credentials set-ghcr --username <github-user> --token <PAT with write:packages>   # GenericVps, RunPod
mp-host credentials set-ssh --name cursory --host <vps ip> --user deploy --key <private key> # GenericVps
mp-host credentials set-runpod --key <RunPod API key>                                       # RunPod
```

`cursory` is the vault name of the SSH target; with it stored, GenericVps's public address defaults to its host.
`-DryRun` reads these credentials too, so the printed login and SSH lines are accurate. The rendezvous shared secret
comes from the vault (`mp-host credentials set-rendezvous`, generated and stored on the first real run) or
`--rendezvous-secret`; every server that registers with one rendezvous uses the same value. mp-host masks it in
everything it prints.

## Per profile

| Profile | Package | Rendezvous | Notes |
|---|---|---|---|
| `GenericVps` (default) | GenericVps (UDP 7777) | beside the game on the box: `http://<public address>:8080` | `--public-address` is the box's IP (default: the stored SSH host). Updates are an in-place `docker compose up -d` |
| `RunPod` | RunPod (WebSocket, TCP 7777) | elsewhere, so `--rendezvous-url` is required (e.g. a GenericVps box) | The pod advertises `RUNPOD_PUBLIC_IP`/`RUNPOD_TCP_PORT_7777`. An update replaces the pod and drops its players |
| `Local` | GenericVps | `docker build` of Unity.Multiplayer's `server/Rendezvous/Dockerfile` as `mindattic-rendezvous:local` (the checkout `Packages/manifest.json` points at), unless you pass `--rendezvous-image` | The game registers at `http://rendezvous:8080` inside the compose network. Players join by LAN IP, or resolve codes at `<LAN IP>:8080`. UPnP is off unless you pass `--upnp` |

Use `-SkipBuild` to reuse the package that's already built, `--image`/`--tag` to choose another GHCR repository,
and `--development` for a development player. `mp-host game status|logs|update|destroy --yes` act on the server
the last `game deploy` from this machine made.
## End-to-end test: bots {#end-to-end-test-bots}

Two headless bot clients prove the whole path every night: a dedicated server serves level 2 (Mass 2.0, which one
cursor can't move), registers with the rendezvous service, and two bots join it by code and pull the block around
the wall onto the pad together (UC-T14).

- **Bot mode.** Any player build launched with `-bot A` or `-bot B` becomes a `BotDriver`
  (`Runtime/Bots/BotDriver.cs`). It switches the scene's `NetBootstrap` off before its Start (a headless player
  would otherwise serve), joins with `-joincode <code>` or the code in `-joincodefile <path>`, and resolves it at
  `-rendezvous <url>`. Both bots follow the same Core `BotPlan`: grab the block's goal-facing edge a quarter of its
  height either side of the centre (A above, B below) and pull toward the same waypoints, with no messages between
  them. A bot logs `[BotDriver:A] OK: ...` when UC-T9's synced `LevelStateSync.LevelSolved` says the level is won,
  or `FAIL: ...` (and quits with exit code 1) on a failure or after `-bottimeout` seconds (default 180).
- **Server side.** `-level 2` picks the first level (`LevelServer.LevelArg`) and `-joincodefile <path>` writes the
  published join code to a file (`JoinCodeFile`) for the bots to read.
- **In process.** `BotEndToEndTests` (PlayMode) runs the same thing without Docker: `NetBootstrap` + `LevelServer`
  as the server, an in-process rendezvous service, and two `BotDriver`s joining by code. About 20 seconds.
- **Under Docker.** `smoke/docker-compose.yml` runs the rendezvous image (Unity.Multiplayer's
  `server/Rendezvous/Dockerfile`), the server and both bots from one Linux player build
  (`Unity.Cursory.EditorTools.BotBuild.BuildFromCommandLine`, into `smoke/build/`), as Unity.Multiplayer's own
  nightly smoke does. `powershell -File tools/run-smoke.ps1` builds and runs it locally; `-SkipBuild` reuses the
  build and `-Keep` leaves the stack up. The image sets `SDL_VIDEODRIVER=dummy`: without it a plain Linux player run
  headless crashes on its first frame (SIGSEGV in `sdl::IsX11VideoDriver`, polling the mouse).
- **CI.** `.github/workflows/nightly.yml` runs nightly and on `workflow_dispatch`: dotnet Core tests, the PlayMode
  suite and the player build through GameCI, then the compose stack. It needs the `UNITY_LICENSE` secret (as
  Unity.Multiplayer's CI does), and `MINDATTIC_REPOS_TOKEN` if Unity.Multiplayer or MindAttic.Capture is private.
  The player is a plain Linux player, not the Dedicated Server subtarget, because GameCI's editor image has no
  Linux Dedicated Server build support.
