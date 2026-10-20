# UnityCursory
Cursory, the co-op physics puzzle, rebuilt in Unity 6 with dedicated-server and host multiplayer.

Open the project in Unity 6000.4.3f1. It expects MindAttic.Unity.Multiplayer and MindAttic.Capture checked
out next to it (`Packages/manifest.json` references them by `file:../../` path). The game rules that don't
need the engine are tested without Unity: `dotnet test Tests/Core.Tests`.

How the system fits together and the rules that keep it coherent: [docs/BIBLE.md](docs/BIBLE.md).
