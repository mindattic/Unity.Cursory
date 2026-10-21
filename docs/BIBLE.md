---
codex: 1
project: Unity.Cursory
code: UC
layer: bible
status: living
updated: 2026-10-20
---

# Unity.Cursory — Project Bible
> Single source of truth for what Unity.Cursory IS, is NOT, and the rules that keep it coherent.
> README says how to build and run it; the design and build order live in MindAttic.Unity.Multiplayer's
> [RFC 0001](https://github.com/mindattic/MindAttic.Unity.Multiplayer/blob/main/docs/rfc/0001-architecture.md)
> (summarised for this repo in [rfc/0001-port-plan.md](rfc/0001-port-plan.md)); this says how to think about the system.

Status legend: ✅ done (verified by a test/build) · 🟡 partial · ⬜ planned · `living`.

## 1. The one sentence {#UC-§1}
Unity.Cursory is [Cursory](https://github.com/mindattic/Cursory)'s wordless co-op physics puzzle rebuilt
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
Assets/Unity.Cursory/Core      Unity.Cursory.Core     noEngineReferences: game rules, units, level data
        ▲                                           (compiled by Tests/Unity.Cursory.Core as netstandard2.1 / C# 9)
Assets/Unity.Cursory/Runtime   Unity.Cursory.Runtime  MonoBehaviours: camera, physics sim, networked cursor, HUD
        ▲                                           (references the library, Capture, Input System, URP 2D)
Assets/Unity.Cursory/Editor    Unity.Cursory.Editor   ProjectSetup: writes the URP 2D pipeline and Main scene
Tests/Core.Tests              dotnet NUnit (net10.0) over Unity.Cursory.Core, no editor needed
Packages/manifest.json        file:../../MindAttic.Unity.Multiplayer/unity/com.mindattic.unity.multiplayer
                              file:../../MindAttic.Capture/unity/com.mindattic.capture
                              file:../../MindAttic.Components/unity/com.mindattic.components.unity (every menu and HUD widget)
                              file:../../Unity.Shared/unity/com.mindattic.unity.shared (the player's profile: name, colour)
```

- **Rendering:** URP with only the 2D renderer (`Assets/Unity.Cursory/Settings`), a global 2D light, and a
  top-down orthographic camera looking down -Z at the XY world plane (`TopDownCamera`: zoom 1 shows one
  world pixel per screen pixel).
- **Physics:** Physics2D, gravity 0 (top-down). From UC-T6 the server steps it in Script mode at
  1/30 s from the network tick; clients never simulate.
- **Packages** resolve from the sibling checkouts, so the repo must sit next to MindAttic.Unity.Multiplayer,
  MindAttic.Capture and MindAttic.Components (as it does under `D:\Projects\MindAttic`).

## 5. The Laws {#UC-§5}

Unity.Cursory **inherits the org-wide house rules** in
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
happens only at the engine boundary, through `WorldUnits` (`Assets/Unity.Cursory/Core/WorldUnits.cs`).

### UC-LAW-4 — Static geometry is sent once per level {#UC-LAW-4}
*Ports CUR-LAW-6.* Walls and world labels never move, so they go to a client once when it joins and
again on a level switch or reset, never on the per-tick body sync. Only moving state (bodies, cursors,
goal and circuit state, votes) is replicated continuously.

### UC-LAW-5 — Grabs land on the edge {#UC-LAW-5}
*Ports CUR-LAW-7.* A grab projects the click to the nearest point on the body's perimeter (for a
compound shape, on the nearest piece). You grab the rim, not the interior; an edge or corner grab is
what gives a body torque. The raw click position, not a collision-resolved cursor, picks the grab.

### UC-LAW-6 — Core is engine-free and tested without the editor {#UC-LAW-6}
Game rules that don't need Unity live in `Assets/Unity.Cursory/Core` (`noEngineReferences`, C# 9) and are
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
- ✅ **Core model and level schema** (UC-T4): `Assets/Unity.Cursory/Core/Model` ports Cursory's
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
  the 1.5 and 3.0 thresholds. The editor menu `Unity.Cursory/Write Level Assets` (`LevelAssetWriter`) writes
  them as `LevelAsset` ScriptableObjects (`Assets/Unity.Cursory/Levels/L01..L14.asset`) and the ordered
  `LevelLibrary` (`Assets/Unity.Cursory/Resources/Unity.Cursory/LevelLibrary.asset`, found with
  `LevelLibrary.Load()`). `LevelLoader` holds the current level, plays a copy (`World`), and raises the
  geometry-resend and level-banner flags on load and reset; the room's reset and level votes act on it through
  `Core/Levels/LevelVotes` (the vote rules themselves moved to Unity.Multiplayer's votes kit in UM-T54).
  Verified by `dotnet test Tests/Core.Tests`: `LevelSeedTests`, `LevelAssetTests` (the committed assets read
  from their YAML equal the seeds), `LevelLoaderTests`, `LevelVotesTests` (UC-T5's `VoteAndLevelTests`, ported from Cursory.Tests, until UM-T54) and
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
  the PlayMode suite `Assets/Unity.Cursory/Tests/PlayMode` (`ServerPhysicsTests`: one cursor moves Mass 1.0
  but not 2.0, two move 2.0, an edge grab turns a bar, nothing moves without a tick, and a dedicated-server
  session steps once per network tick) and `dotnet test Tests/Core.Tests` (`GrabGeometryTests`).

- ✅ **Networked cursor and body sync** (UC-T7): `Runtime/Net/CursorAvatar` is the player object (a
  `NetEntity`, one per connection, prefab `Resources/Unity.Cursory/CursorAvatar`). Its owner sends `[ToServer]`
  intents only ([UC-LAW-1](#UC-LAW-1)): `Move(Vector2)` (unreliable, 30/s), `Grab`, `GrabShape`, `GrabWall`,
  `GrabWireEnd`, `Release`; the server applies them to the bound `ServerSimulation`'s `PhysicsWorld` and writes
  the `[Synced]` `Position` every peer draws. `Held`/`HeldId`/`HeldAnchor`/`HeldWireEndA` are the server's record
  of what a cursor holds (a wall or wire end is bookkeeping for UC-T8/UC-T9). Code drives a cursor the same way
  input does: `CursorAvatar.Local(session)`, `ForClient`, `All` and the static `Spawned`/`Despawned` events find
  cursors on any peer. Bodies replicate through `IBodySync` (`NgoBodySync`: `NetworkTransform` x, y, rot z, half
  precision, interpolated, plus `NetworkRigidbody2D`, prefab `Resources/Unity.Cursory/NetworkedBody`; the
  [UC-LAW-7](#UC-LAW-7) exception). Each body carries Unity.Multiplayer's `NetKey` with its level block or shape id
  (UM-T60): the server spawns it with `NetSession.Objects.Spawn(prefab, id, ...)` and despawns it at once when the
  world is torn down (`IBodySync.DespawnBody`, so a reset can respawn the same ids that tick), and every client finds
  it by that id in `NetSession.Objects` (`ClientBodies.Collect`), never by spawn order. Static geometry goes out once per level as the `[ToOwner]`
  `ReceiveGeometry` on spawn and after a load or reset (`GeometryBroadcast`, [UC-LAW-4](#UC-LAW-4)).
  `NetPrefabs.RegisterAll(session)` registers both prefabs and returns the body sync; the editor menu
  `Unity.Cursory/Write Networked Prefabs` (`NetPrefabWriter`) writes them. Verified by the PlayMode suites
  `CursorAvatarHostAndClientTests` (host + client) and `CursorAvatarServerAndClientsTests` (dedicated server
  + two clients).

- ✅ **Goals, circuit and win detection** (UC-T9): `Core/Rules/CircuitRules` ports Cursory's `SnapWireEnd`
  (a released wire end plugs into the nearest terminal within `Tuning.WireSnapRadius` = 90 px, inclusive, and
  moves onto it; else it stays loose where dropped) and `EvaluateCircuit` (the bulb is lit only when the plugged
  wires close battery+ to battery− and removing either the bulb's or the resistor's internal edge breaks the
  loop). `Core/Rules/LevelProgress` is the server's goal pass over a played level: block goals (centre strictly
  inside), shape goals (every piece centre strictly inside), the bulb, the win (every goal solved and, with a
  circuit, the bulb lit), and the wires' live state (a cursor's grabbed end unplugs, follows the cursor, snaps on
  release). `Runtime/Levels/LevelStateServer` runs that pass after every `ServerSimulation` step against the
  Physics2D poses and publishes it through `Runtime/Levels/LevelStateSync`, a server-owned `NetEntity`:
  `[Synced]` level number, goal count, solved bitmask (block goals then shape goals), circuit, bulb and win,
  plus the wires (ends and plugged terminal indices) by a reliable `[ToClients]` call on change and a
  `[ToClient]` answer to a late joiner's `[ToServer]` request. `LevelServer` spawns that entity (prefab
  `Resources/Unity.Cursory/LevelState`, written by `NetPrefabWriter`) and runs the pass on a server session;
  the tracked `LevelProgress` is `ServerSimulation.Progress`, re-tracked after a load or reset, and the UC-T7
  cursor's `GrabWireEnd`, `Move` and `Release` pick a wire end up, drag it and snap it through it. Verified by `dotnet test Tests/Core.Tests`
  (`CircuitTests`, ported from Cursory.Tests, and `GoalProgressTests`) and PlayMode `LevelStateServerTests`
  (a real grab drags a block onto its pad; wiring level 14 lights the bulb on the next tick) and
  `LevelStateSyncTests` (host and client over loopback: goal, win, bulb and wires reach the client; a late
  joiner gets the wires), `CursorAvatarServerAndClientsTests.GrabWireEnd_FollowsTheCursor_AndSnapsOntoATerminalOnRelease`
  and `LevelServerTests`.

- ✅ **Votes and whistles** (UC-T10; votes on the library since UM-T54): the room's votes are Unity.Multiplayer's
  votes kit. `LevelServer` spawns the library's `VoteBoard` (its own prefab, registered by `NetPrefabs.RegisterAll`
  through `VoteBoard.RegisterPrefab`) on every server session and registers Cursory's two kinds with
  `LevelServer.AddVoteKinds`: `reset` ("Reset the level?", re-seeds the level) and `level` ("Switch to Level N?",
  the number payload; refused for the current level or one that doesn't exist), worded and checked by
  `Core/Levels/LevelVotes`. The library runs the rules Cursory had in its own `VoteBox` (2/3 quorum rounded up,
  early rejection once the quorum is unreachable, 15 s on the session's tick clock, a leaver dropped and the quorum
  recomputed, one vote at a time) and the HUD's toolbar starts votes with `StartVote`; the vote itself shows in the
  library's `VoteOverlay` (question, tally, Yes/No, result toast). Whistles stay Cursory's: `Runtime/Whistles/WhistleBoard`,
  a scene-level, server-owned `NetEntity` (prefab `Resources/Unity.Cursory/WhistleBoard`) whose `Whistle(worldPx)`
  (`[ToServer]`, no ownership, rate-limited) is broadcast `[ToClients]` with the whistler's colour and tone
  (`Core/Model/WhistleTone`: Cursory's 12 tones, 440 Hz + 35 Hz a step, keyed by seat). Verified by
  `dotnet test Tests/Core.Tests` (`LevelVotesTests`, `WhistleToneTests`), Unity.Multiplayer's own vote suites, and the
  PlayMode suite (`WhistleBoardTests`: Cursory's level and reset votes over loopback with a host and two clients,
  refusals with their reasons, and a whistle reaching every peer; `PresentationTests`: the HUD starting votes and the
  library overlay showing them).

- ✅ **Level reload rebuilds the physics** (UC-T20): on the tick after the `LevelLoader` loads or resets a level
  (a passed vote, or any caller), `ServerSimulation.RebuildWorld` makes every cursor let go
  (`CursorAvatar.ReleaseAll`), tears down the old `PhysicsWorld` (its rigid bodies leave the simulation at once,
  the networked copies despawn) and builds the new level's with the same `IBodySync`, then raises `WorldRebuilt`
  and the geometry resend. Verified by the PlayMode `LevelReloadTests` (a reset mid-grab and a level switch
  mid-grab, host + client).

- ✅ **Tethers and per-player settings** (UC-T8): each player's own `[Synced]` `SegmentedTether` and
  `CursorCollision` on their `CursorAvatar` (both off by default), set by the owner through the `[ToServer]`
  `SetSegmentedTether(bool)` / `SetCursorCollision(bool)` (the in-game menu's switches call these), replacing Cursory's
  room-wide booleans that every player's toggle overwrote. The server honours each cursor's own choice: with
  collision, a `Move` is pushed out of walls and shapes by the 10 px cursor disc (`Core/Physics/CursorCollision`,
  blocks stay non-solid as in Cursory); with a segmented tether, the rope catches on body corners
  (`Core/Physics/TetherWrap`) and `PhysicsWorld.UpdateTether` re-anchors the grab's `TargetJoint2D` on the last
  caught corner each tick, so pulling round a corner turns the body. Every grab's cursor is leashed within
  `MaxPullPx` 300 of the rope's contact. The rope (anchor, caught corners) replicates as the `[Synced]` `Tether`
  (`TetherSync`, world pixels); `CursorAvatar.TryGetRope` gives any peer the polyline and `TetherRenderer` draws
  it with a `LineRenderer` in the player's colour on hosts and clients. One change from Cursory: its unwind test
  never fired (the rope starts on the rim, inside its 6 px margin), so a caught corner now lets go when the rope's
  bend flips. Verified by `dotnet test Tests/Core.Tests` (`TetherGeometryTests`, `TetherWrapTests`,
  `CursorCollisionTests`) and the PlayMode suites `TetherPhysicsTests` and `TetherAvatarTests` (dedicated server +
  two clients).

- ✅ **Client presentation** (UC-T11): `Runtime/Presentation/CursoryClient` is everything a player sees and does,
  bound to one `NetSession`; a player build creates it at scene load next to `NetBootstrap`, and a headless process
  (`-batchmode`, a dedicated server build, `-bot`) creates none (`CursoryClient.ShouldPresent`). Input is the Input
  System mouse, turned into intents only ([UC-LAW-1](#UC-LAW-1)) by `PointerInput`: a press on a wire end, shape,
  block or wall (`Core/Presentation/BodyPicker`, Cursory's pick order, against the client's own copy of the level and
  the replicated poses, found by block/shape id in `ClientBodies`) grabs it and the release lets go; a press on empty
  space follows `Core/Presentation/PointerGesture` (5 px of travel pans the camera, a release within 250 ms whistles
  through the `VoteBoard`); the wheel zooms about the pointer in 0.1 to 4 (`Core/Presentation/CameraView`, which
  `TopDownCamera` now follows). `WorldView` draws goal pads (green when solved), walls, circuit, bodies with their
  Mass, wires, every cursor as an arrow with a name tag, and whistle ripples; `WhistleAudio` plays each player's
  `WhistleTone` as a synthesized clip. **All menus and HUD widgets come from MindAttic Components**
  (`com.mindattic.components.unity`): `CursoryHud` is a `MaToolbar` (level `MaSelect` that proposes a level vote,
  Reset, a connection `MaStatusPill` that pulses while connecting, a pause button), the level banner is an `MaBanner`
  (fades in and out), vote endings and wins are `MaToast`s, and the minimap is an `MaMinimap` that auto-fits the
  level's walls, goals and bodies with 8 px of padding (cursors and the camera's view are drawn but not fitted) and pans
  the camera on a click (UC-T23: the HUD widgets were promoted into MindAttic Components as M.COMP-T46); the per-player switches are `PlayerSettingsItems`, addable to any `MenuScreen` (UC-T12 puts them on the
  library front end's in-game menu). Player
  colours and names come from Unity.Multiplayer's player roster since UM-T55 (`NetSession.Roster`, on every peer, filled
  from the connection approval payload, with the seat's palette colour for a player who kept the default white, and
  following a mid-game `RequestChange`): the server writes only the owner's seat onto the cursor (`[Synced]`
  `SeatIndex`), and `CursorAvatar.Player`/`PlayerColor`/`PlayerName` look the player up by seat, so the rope, arrow,
  name tag, minimap dot and whistle ripple (`WhistleBoard` and `CursoryClient` read the roster too) all follow a rename;
  Cursory's own seat palette is gone. Verified by `dotnet test Tests/Core.Tests` (`PointerGestureTests`,
  `CameraViewTests`, `BodyPickerTests`, `HudTextTests`) and the PlayMode `PresentationTests`
  on a real Host (click vs drag vs grab, the zoom clamp, the HUD following and voting through the board and level
  state, the minimap fitting level 1 with padding and panning the camera, the payload's colour and name, a rename reaching
  cursor and rope) and `FrontEndTests` (a client's rename
  reaching the host's cursor and lobby).

- ✅ **Front end** (UC-T12): Cursory adopts the library's drop-in `NetFrontEnd` exactly as a new game does (Unity.Multiplayer
  `docs/articles/front-end.md`), with no menu code of its own. `Main.unity` has it beside the `NetBootstrap` (MindAttic ▸
  Multiplayer ▸ Add Front End, scripted as `ProjectSetup.SetUpFrontEnd` / menu `Unity.Cursory/Set Up Front End`) with
  `Settings/FrontEndSettings.asset` (title "Cursory", every other field the library default). It gives the main menu
  (Host, Join by IP, Join by code, Settings, Quit), the host's lobby with its join code and roster, the connecting
  dialog, a plain-words dialog for every disconnect reason, the automatic reconnect to the same seat, and the Esc
  in-game menu. The player's name and colour come from the MindAttic profile (`com.mindattic.unity.shared`, the
  library's `ProfileIdentitySource`) into the approval payload, which UC-T11 already copies onto the cursor, rope,
  whistle and minimap. Cursory adds only: `CursoryClient` hands its `PlayerSettingsItems` to `AddPauseMenuItems`, the
  HUD's pause button opens the front end's in-game menu, the HUD (toolbar, vote overlay, banner, minimap) shows only
  while the front end is `InGame`, and the world ignores the pointer while any of its menus is up. Cursory's own
  `CursoryPauseMenu` is gone. A headless process (dedicated server, CI) builds no menus (the library's
  `NetLaunch.ShowsMenus`), and a `-bot` switches the front end off with the bootstrap. Verified by the PlayMode
  `FrontEndTests` (host from the menu and a client joining by the lobby's code into level 1, HUD hidden in the menus
  and shown in game, the in-game menu's switches reaching the server, a version refusal and the host ending the game
  each explained, the scene's wiring) on the library's test kits (`Unity.Multiplayer.Testing`,
  `Unity.Multiplayer.UI.Testing`), with `BotEndToEndTests` unchanged.

## 7. Active frontier {#UC-§7}
The Unity.Cursory tickets UC-T7 to UC-T15 in the library's RFC 0001 ticket chain, tracked in
[USER_STORIES.md](USER_STORIES.md).

## 8. Glossary {#UC-§8}
- **World pixel** — Cursory's unit; 100 to a metre ([UC-LAW-3](#UC-LAW-3)).
- **Mass** — a body's printed weight and its move threshold ([UC-LAW-2](#UC-LAW-2)).
- **Grab / tether** — a force-capped `TargetJoint2D` from a body's edge anchor to the cursor.
- **Tick** — one 1/30 s server step.
- **Geometry** — the static walls and labels sent once per level ([UC-LAW-4](#UC-LAW-4)).
- **Whistle** — a click ping: a coloured ripple and a per-player tone; the only comms.
- **Vote** — a reset or level switch needing a 2/3 quorum of the roster.
