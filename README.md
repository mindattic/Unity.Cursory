# Unity.Cursory
Cursory, the co-op physics puzzle, rebuilt in Unity 6 with dedicated-server and host multiplayer.

Open the project in Unity 6000.4.3f1. It expects MindAttic.Unity.Multiplayer and MindAttic.Capture checked
out next to it (`Packages/manifest.json` references them by `file:../../` path). The game rules that don't
need the engine are tested without Unity: `dotnet test Tests/Core.Tests`.
The server's Physics2D sim has a PlayMode suite, `Unity.Cursory.PlayMode.Tests`: run it from the editor's Test
Runner, or `Unity -batchmode -nographics -projectPath . -runTests -testPlatform PlayMode -assemblyNames Unity.Cursory.PlayMode.Tests`.

Dedicated server: `./deploy.ps1` builds the headless Linux server package with Unity.Multiplayer and hands it
to `mp-host` (`-Profile GenericVps|RunPod|Local`, `-DryRun`); see [docs/hosting.md](docs/hosting.md).

How the system fits together and the rules that keep it coherent: [docs/BIBLE.md](docs/BIBLE.md).
