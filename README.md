# Unity.Cursory
Cursory, the co-op physics puzzle, rebuilt in Unity 6 with dedicated-server and host multiplayer.

Open the project in Unity 6000.4.3f1. It expects MindAttic.Unity.Multiplayer and MindAttic.Capture checked
out next to it (`Packages/manifest.json` references them by `file:../../` path). The game rules that don't
need the engine are tested without Unity: `dotnet test Tests/Core.Tests`.
The server's Physics2D sim has a PlayMode suite, `Unity.Cursory.PlayMode.Tests`: run it from the editor's Test
Runner, or `Unity -batchmode -nographics -projectPath . -runTests -testPlatform PlayMode -assemblyNames Unity.Cursory.PlayMode.Tests`.

Dedicated server: `mp-host game local` (or `./deploy.ps1 -Profile Local`) builds the headless Linux server package and
runs it, with no Cursory build code: Unity.Multiplayer's `mp-host game` does it all (`-Profile GenericVps|RunPod|Local`,
`-DryRun`); see [docs/hosting.md](docs/hosting.md).

End-to-end: two headless bot clients join a dedicated server by code and solve level 2 together, in process
(`BotEndToEndTests` in the PlayMode suite) and nightly under Docker Compose (`.github/workflows/nightly.yml`,
locally `powershell -File tools/run-smoke.ps1`); see [docs/hosting.md](docs/hosting.md#end-to-end-test-bots).

How the system fits together and the rules that keep it coherent: [docs/BIBLE.md](docs/BIBLE.md).
