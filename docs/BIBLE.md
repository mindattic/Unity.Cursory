---
codex: 1
project: UnityCursory
code: UC
layer: bible
status: living
updated: 2026-10-20
---

# UnityCursory — Project Bible
> Single source of truth for what UnityCursory IS, is NOT, and the rules that keep it coherent.
> README says how to build and run it; the design and build order live in MindAttic.Unity.Multiplayer's
> [RFC 0001](https://github.com/mindattic/MindAttic.Unity.Multiplayer/blob/main/docs/rfc/0001-architecture.md)
> (summarised for this repo in [rfc/0001-port-plan.md](rfc/0001-port-plan.md)); this says how to think about the system.

Status legend: ✅ done (verified by a test/build) · 🟡 partial · ⬜ planned · `living`.

## 1. The one sentence {#UC-§1}
UnityCursory is [Cursory](https://github.com/mindattic/Cursory)'s wordless co-op physics puzzle rebuilt
in Unity 6 (6000.4.3f1): every player is a cursor on a shared top-down world, heavy bodies need several
cursors pulling together, and the game runs on a dedicated server or a player-hosted listen server
through [MindAttic.Unity.Multiplayer](https://github.com/mindattic/MindAttic.Unity.Multiplayer).

## 2. The product promise {#UC-§2}
- **Cursory's gameplay, unchanged.** The same 14 levels, the same Mass dial, grabs, tethers, goals,
  circuit, votes and whistles. A port, not a redesign: Cursory's constants carry over, mapped onto
  Physics2D (see [UC-LAW-2](#UC-LAW-2)).
- **Wordless co-op.** No chat. A whistle (a coloured ripple and a per-player tone) is the whole vocabulary.
- **Two topologies, one game.** Host from a player's machine or join a headless dedicated server, by
  IP:port or a join code. The topology is configuration, not game code.
- **Server-authoritative, latency-forgiving.** Grabs are soft, force-capped joints, so latency softens
  the feel instead of breaking it.

## 3. What it is NOT {#UC-§3}
- **NOT client-authoritative.** A client says "my cursor is at (x, y)" and "I grab / release / whistle /
  vote"; the server decides what that does ([UC-LAW-1](#UC-LAW-1)).
- **NOT a chat or social app.** There is no text channel.
- **NOT a hand-rolled physics toy.** Bodies are real Physics2D rigid bodies: ground friction is a
  `FrictionJoint2D` and a grab is a force-capped `TargetJoint2D`, so cooperative drag, pivots and
  torque come out of the solver.
- **NOT carrying Cursory's dead types.** `SwitchTile`, `Door` and `ShapeAttachment` are not ported.
- **NOT using Cursory's accounts.** The player's name and colour come from the MindAttic.Unity profile
  (UC-T12), so Cursory's cookie auth laws (CUR-LAW-5, -8, -9) have no counterpart here.

## 4. Architecture canon {#UC-§4}
The full design (physics mapping, body sync, level port, ticket chain) lives in the library's RFC 0001
and is not repeated here. The layout this repo keeps:

```
Assets/UnityCursory/Core      UnityCursory.Core     noEngineReferences: game rules, units, level data
        ▲                                           (compiled by Tests/UnityCursory.Core as netstandard2.1 / C# 9)
Assets/UnityCursory/Runtime   UnityCursory.Runtime  MonoBehaviours: camera, physics sim, networked cursor, HUD
        ▲                                           (references the library, Capture, Input System, URP 2D)
Assets/UnityCursory/Editor    UnityCursory.Editor   ProjectSetup: writes the URP 2D pipeline and Main scene
Tests/Core.Tests              dotnet NUnit (net10.0) over UnityCursory.Core, no editor needed
Packages/manifest.json        file:../../MindAttic.Unity.Multiplayer/unity/com.mindattic.unity.multiplayer
                              file:../../MindAttic.Capture/unity/com.mindattic.capture
```

- **Rendering:** URP with only the 2D renderer (`Assets/UnityCursory/Settings`), a global 2D light, and a
  top-down orthographic camera looking down -Z at the XY world plane (`TopDownCamera`: zoom 1 shows one
  world pixel per screen pixel).
- **Physics:** Physics2D, gravity 0 (top-down). From UC-T6 the server steps it in Script mode at
  1/30 s from the network tick; clients never simulate.
- **Packages** resolve from the sibling checkouts, so the repo must sit next to MindAttic.Unity.Multiplayer
  and MindAttic.Capture (as it does under `D:\Projects\MindAttic`).

## 5. The Laws {#UC-§5}

UnityCursory **inherits the org-wide house rules** in
[`../../MindAttic.HouseRules.md`](../../MindAttic.HouseRules.md). Of note here: [HOUSE-LAW-8] (done is
verified, not asserted), [HOUSE-LAW-12] (artifacts also live in the repo), [HOUSE-LAW-13] (LF-only,
enforced by the root `.gitattributes`). It also inherits the library's laws: UM-LAW-1 (the server owns
state) and UM-LAW-2 (game code talks to MindAttic types, not NGO).

Project-specific laws. Each names the Cursory law it ports.

### UC-LAW-1 — The server owns the simulation {#UC-LAW-1}
*Ports CUR-LAW-1.* A client sends only its cursor position (`[ToServer]`, unreliable, rate-limited) and
one-shot grab / release / whistle / vote intents. It never asserts where a body is. All physics,
collision, tether and goal resolution run on the server (host or dedicated); clients only interpolate
what the server replicates, so every client sees the same world.

### UC-LAW-2 — Mass is the one legible dial {#UC-LAW-2}
*Ports CUR-LAW-2.* A body moves exactly when the pulls on it sum past its printed `Mass`. One grab tops
out at 1.5 mass units (grab force 60 ÷ 40 per unit of Mass), so anything heavier needs cooperating cursors. The Physics2D mapping that keeps
this true (from the RFC): grab `TargetJoint2D` maxForce 60, frequency 5, damping 0.7; ground
`FrictionJoint2D` maxForce `Mass × 40`, maxTorque `× 1.2`; `rb.mass = Mass × 5`; linear damping 1.5,
angular 2.0. Level masses stay clear of the 1.5 and 3.0 thresholds. A tuning change that breaks
"one cursor moves 1.0 but not 2.0; two move 2.0" is a regression, whatever it looks like.

### UC-LAW-3 — World pixels in data and on the wire; metres in the engine {#UC-LAW-3}
*Ports CUR-LAW-3.* Levels, game rules and network payloads speak Cursory's world pixels. Physics2D and
transforms run in metres, one Unity unit per metre, at `WorldUnits.PixelsPerMeter` = 100. The conversion
happens only at the engine boundary, through `WorldUnits` (`Assets/UnityCursory/Core/WorldUnits.cs`).

### UC-LAW-4 — Static geometry is sent once per level {#UC-LAW-4}
*Ports CUR-LAW-6.* Walls and world labels never move, so they go to a client once when it joins and
again on a level switch or reset, never on the per-tick body sync. Only moving state (bodies, cursors,
goal and circuit state, votes) is replicated continuously.

### UC-LAW-5 — Grabs land on the edge {#UC-LAW-5}
*Ports CUR-LAW-7.* A grab projects the click to the nearest point on the body's perimeter (for a
compound shape, on the nearest piece). You grab the rim, not the interior; an edge or corner grab is
what gives a body torque. The raw click position, not a collision-resolved cursor, picks the grab.

### UC-LAW-6 — Core is engine-free and tested without the editor {#UC-LAW-6}
Game rules that don't need Unity live in `Assets/UnityCursory/Core` (`noEngineReferences`, C# 9) and are
covered by `dotnet test Tests/Core.Tests` (NUnit). Runtime code is a thin layer over Core; a rule that
can be decided without a `MonoBehaviour` is decided in Core.

### UC-LAW-7 — Networking goes through MindAttic.Unity.Multiplayer {#UC-LAW-7}
Sessions, RPCs and synced fields use the library (`NetSession`, `NetEntity`, `[ToServer]`,
`[ToClients]`, `[Synced]`). The one recorded exception is body sync: NGO's `NetworkTransform` and
`NetworkRigidbody2D` (x, y, rotation z; half precision; interpolated) sit behind an `IBodySync`
interface so a compact snapshot can replace them without touching gameplay code.

### Cursory laws not carried over
- **CUR-LAW-4** (camelCase JSON wire format): there is no JSON wire; the library's binary RPCs replace it.
- **CUR-LAW-5, CUR-LAW-8, CUR-LAW-9** (cookie claims, seeded users, bespoke auth): there are no accounts;
  identity comes from the MindAttic.Unity profile ([§3](#UC-§3)).

## 6. Verified state {#UC-§6}
- ✅ **Project skeleton** (UC-T3): Unity 6000.4.3f1 project with URP 2D, the Main scene's top-down
  orthographic camera and global 2D light, gravity-free Physics2D, the Input System as the only input
  backend, and the local multiplayer and Capture packages. Opens and compiles in batch mode with no
  errors. Engine-free Core verified by `dotnet test Tests/Core.Tests` (`WorldUnitsTests`,
  `SimulationRateTests`).
- ✅ **Core model and level schema** (UC-T4): `Assets/UnityCursory/Core/Model` ports Cursory's
  `GameState.cs` engine-free: `Block` and compound `Shape`/`ShapePiece` (Mass), `Wall`, `WorldLabel`,
  `BlockGoal`/`ShapeGoal` (centre / every piece centre strictly inside), the circuit (`CircuitComponent`
  with `ComponentKind`, `Terminal` with `TerminalPolarity`, `Wire`), `Vote` (`VoteKind`, 2/3 quorum,
  early rejection, 15 s timeout), `Whistle`, and `Tuning` (Cursory's constants and the Mass rules:
  `CursorsNeeded`, `IsClearOfThresholds`). `Core/Levels/LevelData` is the level schema (Unity-serializable
  fields, deep `Clone`, `Geometry()` for [UC-LAW-4](#UC-LAW-4), `Validate()`). `SwitchTile`, `Door` and
  `ShapeAttachment` are not ported. Verified by `dotnet test Tests/Core.Tests` (`TuningTests`,
  `GoalTests`, `VoteModelTests`, `LevelDataTests` with Cursory's levels 1, 3 and 14 as data).

- ✅ **Levels L1-L14, loader and votes** (UC-T5): `Core/Levels/LevelSeeds` ports Cursory's
  `RoomState.SeedLevel1..14` one for one, with Cursory's own Masses (1, 1.1, 1.3, 2, 2.2, 2.6), all clear of
  the 1.5 and 3.0 thresholds. The editor menu `UnityCursory/Write Level Assets` (`LevelAssetWriter`) writes
  them as `LevelAsset` ScriptableObjects (`Assets/UnityCursory/Levels/L01..L14.asset`) and the ordered
  `LevelLibrary` (`Assets/UnityCursory/Resources/UnityCursory/LevelLibrary.asset`, found with
  `LevelLibrary.Load()`). `LevelLoader` holds the current level, plays a copy (`World`), and raises the
  geometry-resend and level-banner flags on load and reset; `VoteBox` runs reset and level votes against it.
  Verified by `dotnet test Tests/Core.Tests`: `LevelSeedTests`, `LevelAssetTests` (the committed assets read
  from their YAML equal the seeds), `LevelLoaderTests`, `VoteAndLevelTests` (ported from Cursory.Tests) and
  `SolvabilityTests` (ported from Cursory.Tests: the block levels are auto-solved by two cursors and the
  shape levels 3, 9 and 12 make real progress, through `PullRoom`, a headless stand-in for the physics built
  on `Tuning`; UC-T6 checks the real Physics2D sim).

- ✅ **Server physics sim** (UC-T6): `Runtime/Physics/PhysicsWorld` builds a level's bodies in Physics2D,
  in metres at 100 px/m: each block and shape a dynamic `Rigidbody2D` (`mass = Mass × 5`, gravity 0, linear
  damping 1.5, angular 2.0; a shape is one body with a `BoxCollider2D` per piece) held to the world by a
  `FrictionJoint2D` at its centre of mass (maxForce `Mass × 40`, maxTorque `× 1.2`), each wall a static
  collider, and each grab a `TargetJoint2D` (maxForce 60, frequency 5, damping 0.7, target set by hand)
  anchored on the rim by `Core/Physics/GrabGeometry` ([UC-LAW-5](#UC-LAW-5), Cursory's `ProjectToEdge`).
  `Runtime/Physics/ServerSimulation` puts Physics2D in Script mode (also the project setting, so a client
  never simulates) and steps the world 1/30 s per tick of the session clock (`NetSession.Clock`, NGO's
  `NetworkTickSystem.Tick`), attaching only when the bound session runs as a Host or Server. Verified by
  the PlayMode suite `Assets/UnityCursory/Tests/PlayMode` (`ServerPhysicsTests`: one cursor moves Mass 1.0
  but not 2.0, two move 2.0, an edge grab turns a bar, nothing moves without a tick, and a dedicated-server
  session steps once per network tick) and `dotnet test Tests/Core.Tests` (`GrabGeometryTests`).

## 7. Active frontier {#UC-§7}
The UnityCursory tickets UC-T7 to UC-T15 in the library's RFC 0001 ticket chain, tracked in
[USER_STORIES.md](USER_STORIES.md). Next: UC-T7 (the networked cursor as the player object, driving
`PhysicsWorld` grabs, and server-authoritative body sync).

## 8. Glossary {#UC-§8}
- **World pixel** — Cursory's unit; 100 to a metre ([UC-LAW-3](#UC-LAW-3)).
- **Mass** — a body's printed weight and its move threshold ([UC-LAW-2](#UC-LAW-2)).
- **Grab / tether** — a force-capped `TargetJoint2D` from a body's edge anchor to the cursor.
- **Tick** — one 1/30 s server step.
- **Geometry** — the static walls and labels sent once per level ([UC-LAW-4](#UC-LAW-4)).
- **Whistle** — a click ping: a coloured ripple and a per-player tone; the only comms.
- **Vote** — a reset or level switch needing a 2/3 quorum of the roster.
