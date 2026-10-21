# Hosting Cursory's dedicated server

Cursory runs a headless dedicated server that owns the simulation (BIBLE UC-LAW-1). Everything about building
and hosting it comes from Unity.Multiplayer; this page only covers what's specific to Cursory. For the
general picture (profiles, ports, TLS, updates) read the library's
[hosting guide](https://mindattic.github.io/Unity.Multiplayer/articles/hosting.html), plus
[GenericVps](https://mindattic.github.io/Unity.Multiplayer/articles/hosting/genericvps.html),
[RunPod](https://mindattic.github.io/Unity.Multiplayer/articles/hosting/runpod.html) and
[Local](https://mindattic.github.io/Unity.Multiplayer/articles/hosting/local.html).

## One command

```powershell
./deploy.ps1 -DryRun                     # see every step without building or touching a server
./deploy.ps1                             # GenericVps (the default): your own UDP-capable Linux VPS
./deploy.ps1 -Profile RunPod -RendezvousUrl http://<vps>:8080   # a RunPod pod over WebSocket
./deploy.ps1 -Profile Local              # server + rendezvous on this PC with Docker Desktop
./deploy.ps1 -Profile Local -Down        # stop the local stack
```

`deploy.ps1` does two things and reimplements neither:

1. **Builds the server package** with Unity in batch mode:
   `-executeMethod Unity.Cursory.EditorTools.ServerBuild.BuildFromCommandLine`. That opens
   `Scenes/Main.unity` and calls the library's `ServerPackageBuild` (the same code as the editor menu
   **MindAttic > Multiplayer > Build Server Package > GenericVps / RunPod**). The output is a headless
   Linux x86_64 Dedicated Server build plus `server.json`, `start.sh`, a `Dockerfile` and `server-package.json`, written to
   `Builds/ServerPackage/<GenericVps|RunPod>` (git-ignored). The Unity log goes beside it. This step needs
   **Linux Dedicated Server Build Support** for Unity 6000.4.3f1.
2. **Hands the package to `mp-host`**: `mp-host deploy --profile GenericVps|RunPod` builds the Docker image,
   pushes it to GHCR and boots it next to rendezvous. `mp-host local up` renders a compose file for this machine.

`mp-host` is whatever `-MpHost` / `$env:MP_HOST` names, then `mp-host` on PATH, then a `dotnet run` of
`Unity.Multiplayer/src/Hosting.Cli`. The Unity.Multiplayer checkout is found through
`Packages/manifest.json`'s `file:` path, or `$env:UNITY_MULTIPLAYER_ROOT`. The Unity editor is `-Unity` /
`$env:UNITY_EDITOR`, else Unity Hub's install of the version in `ProjectSettings/ProjectVersion.txt`.

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

`-Name` (default `cursory`) is the vault name of the SSH target. `-DryRun` reads these credentials too, so the
printed login and SSH lines are accurate. The rendezvous shared secret is `-RendezvousSecret` or
`$env:MINDATTIC_RENDEZVOUS_SECRET`. Use the same value on every server that registers with one rendezvous.
`deploy.ps1` masks it in everything it prints.

## Per profile

| Profile | Package | Rendezvous | Notes |
|---|---|---|---|
| `GenericVps` (default) | GenericVps (UDP 7777) | beside the game on the box; `-RendezvousUrl` defaults to `http://<PublicAddress>:8080` | `-PublicAddress` is required (the box's IP). Updates are an in-place `docker compose up -d` |
| `RunPod` | RunPod (WebSocket, TCP 7777) | elsewhere, so `-RendezvousUrl` is required (e.g. a GenericVps box) | The pod advertises `RUNPOD_PUBLIC_IP`/`RUNPOD_TCP_PORT_7777`. An update replaces the pod and drops its players |
| `Local` | GenericVps | `docker build` of Unity.Multiplayer's `server/Rendezvous/Dockerfile` as `mindattic-rendezvous:local`, unless you pass `-RendezvousImage` | The game registers at `http://rendezvous:8080` inside the compose network. Players join by LAN IP, or resolve codes at `<LAN IP>:8080`. UPnP is off unless you pass `-Upnp`. A throwaway secret is made per run |

Use `-SkipBuild` to reuse the package that's already built, `-Image`/`-Tag` to choose the GHCR repository
(default `mindattic/unity-cursory:latest`), and `-Development` for a development player.
