---
codex: 1
project: Unity.Cursory
code: UC
layer: stories
status: living
updated: 2026-10-20
---
<!-- GENERATED from MindAttic.Tickets/data/Tickets.db by `tickets db render`. Do not edit: this file is rewritten from the database; change stories and tickets with the tickets CLI. -->

# Unity.Cursory — User Stories
> ✅ done (shipped & tested) · 🟡 partial · ⬜ planned. Every ✅ cites the test.

## Priority backlog
_Nothing recorded yet._

## Tickets
> Permanent log of every change and fix — this repo's ticket system. Rows are append-only: never
> delete or reword one; only move its Status (open → closed / wont-do) and fill in Ref. Status is
> a word, not a glyph, so tickets don't count toward the story index. Keep this section last.

| ID | Date | Type | Status | Summary | Ref |
|---|---|---|---|---|---|
| UC-T1 | 2026-10-20 | docs | closed | Start the permanent ticket log in the workflow database | — |
| UC-T2 | 2026-10-20 | chore | closed | Bootstrap repo: AGENTS.md/CLAUDE.md forwarders and docs/rfc/0001-port-plan.md (port Cursory to Unity 6 on MindAttic.Unity.Multiplayer; points at that repo's architecture RFC) | — |
| UC-T3 | 2026-10-20 | chore | closed | Unity 6000.4.3f1 project: URP 2D, top-down orthographic camera, Packages/manifest.json referencing file:../../MindAttic.Unity.Multiplayer/unity/com.mindattic.unity.multiplayer and com.mindattic.capture, engine-free Assets/UnityCursory/Core + Tests/Core.Tests dotnet NUnit pattern (as in UnitySpace/GridGameGaiden), docs/BIBLE.md porting Cursory's CUR-LAW rules (server authoritative; geometry once per level), .gitattributes LF, .gitignore for Unity. Design: MindAttic.Unity.Multiplayer docs/rfc/0001-architecture.md. | 5d05eca |
| UC-T4 | 2026-10-20 | feature | closed | Engine-free Core model ported from Cursory.Core/Models/GameState.cs: blocks (Mass), compound shapes, walls, block/shape goals, circuit (battery, resistor, bulb, terminals, wires), votes (VoteKind), whistles, world labels, level data schema; drop dead SwitchTile/Door/ShapeAttachment. NUnit tests. Design: MindAttic.Unity.Multiplayer docs/rfc/0001-architecture.md. | 8c49823 (LevelDataTests, TuningTests, GoalTests, VoteModelTests) |
| UC-T5 | 2026-10-20 | feature | closed | Levels L1-L14 as ScriptableObjects ported from RoomState.SeedLevel1..14 (masses kept clear of 1.5 and 3.0 thresholds), level loader, and SolvabilityTests/VoteAndLevelTests ported from Cursory.Tests. Design: MindAttic.Unity.Multiplayer docs/rfc/0001-architecture.md. | 358f4ed; dotnet test Tests/Core.Tests (SolvabilityTests, VoteAndLevelTests, LevelAssetTests) |
| UC-T6 | 2026-10-20 | feature | closed | Server physics sim: Physics2D.simulationMode=Script stepped 1/30 s from NetworkTickSystem.Tick on server only; TargetJoint2D grabs (maxForce 60, frequency 5, damping 0.7, autoConfigureTarget off), FrictionJoint2D to world (maxForce Mass*40, maxTorque *1.2), rb.mass=Mass*5, gravity 0, linear damping 1.5, angular 2.0, 100 px/m, edge-snapped grab points for torque. PlayMode test: one cursor moves mass 1.0 not 2.0; two cursors move 2.0. Design: MindAttic.Unity.Multiplayer docs/rfc/0001-architecture.md. | 625939a ServerPhysicsTests (PlayMode, 11/11) |
| UC-T7 | 2026-10-20 | feature | open | Networked cursor as NGO player object (NetEntity): [ToServer] Move (unreliable, rate-limited), Grab, GrabShape, GrabWall, GrabWireEnd, Release; server-authoritative body sync via NetworkTransform+NetworkRigidbody2D (x,y,rotZ, half precision, interpolated) behind IBodySync; static geometry sent once per level. PlayMode host+client and server+2 clients. Design: MindAttic.Unity.Multiplayer docs/rfc/0001-architecture.md. | — |
| UC-T8 | 2026-10-20 | feature | open | Tethers: rope rendering (MaxPullPx 300), optional segmented tether catching on body corners, optional cursor collision (CursorRadius 10) - both per-player settings synced via [Synced] (fixes Cursory's room-wide-boolean mismatch). Tests. Design: MindAttic.Unity.Multiplayer docs/rfc/0001-architecture.md. | — |
| UC-T9 | 2026-10-20 | feature | open | Goals and win detection: block goal (centre inside rect), shape goal (every piece centre inside), goal-solved state synced to clients; circuit: wire snapping (WireSnapRadius 90), bulb lit only when series loop closes through bulb and resistor; CircuitTests ported. Design: MindAttic.Unity.Multiplayer docs/rfc/0001-architecture.md. | — |
| UC-T10 | 2026-10-20 | feature | open | Votes and whistles: reset and level-switch votes, quorum ceil(roster*2/3), early reject when quorum unreachable, 15 s timeout, Yes/No via [ToServer]; whistles broadcast [ToClients] with per-player tone; tests ported from VoteAndLevelTests. Design: MindAttic.Unity.Multiplayer docs/rfc/0001-architecture.md. | — |
| UC-T11 | 2026-10-20 | feature | open | Client presentation: camera pan (drag empty space) and wheel zoom 0.1-4, click-vs-drag thresholds (5 px / 250 ms -> whistle), HUD (level dropdown, Reset, connection pill), vote overlay, level banner, pause menu (Esc) with per-player toggles, minimap, synthesized whistle tones, player colours. Design: MindAttic.Unity.Multiplayer docs/rfc/0001-architecture.md. | — |
| UC-T12 | 2026-10-20 | feature | open | Front end: player name and colour from the MindAttic.Unity profile (replaces Cursory cookie accounts), main menu Host / Join by IP / Join by code / Quit, host shows its join code, disconnect-reason messages, reconnect flow. Design: MindAttic.Unity.Multiplayer docs/rfc/0001-architecture.md. | — |
| UC-T13 | 2026-10-20 | feature | open | Dedicated server: Linux headless build via the library's Build Dedicated Server menu, Docker image + docker-compose with the rendezvous service, deployed per the library hosting guide (VPS default, RunPod over WebSocket), deploy.ps1. Design: MindAttic.Unity.Multiplayer docs/rfc/0001-architecture.md. | — |
| UC-T14 | 2026-10-20 | chore | open | End-to-end test: dedicated server + 2 headless bot clients join by code and solve L2 (mass 2.0, needs both) automatically; runs in CI nightly. Design: MindAttic.Unity.Multiplayer docs/rfc/0001-architecture.md. | — |
| UC-T15 | 2026-10-20 | docs | open | README to codex-standard/README-STYLE.md with screenshots and Quick start (host, join by IP/code, run a server), a 'How UnityCursory uses MindAttic.Unity.Multiplayer' page linked from the library's DocFX 'Porting a game' guide, tag v1.0.0. Design: MindAttic.Unity.Multiplayer docs/rfc/0001-architecture.md. | — |
| UC-T16 | 2026-10-20 | chore | closed | Rename UnityCursory to Unity.Cursory: part of the Unity-fleet naming pass (all Unity game repos get a Unity. prefix). Covers GitHub repo, local folder, git remote URL, ticket DB key (tickets repos rename), C# namespaces/asmdefs/class comments throughout Assets/, README/AGENTS/CLAUDE docs, docs/rfc references to UnityCursory in MindAttic.Unity.Multiplayer (which UC-T7 depends on by name), and any other repo that references UnityCursory by name (grep workspace-wide). Owner-approved 2026-10-20 as part of the Unity Fleet Rename effort; run crossrepo from a General (workspace-root) window so the folder move is included. | 7be95f1 |
| UC-T17 | 2026-10-20 | chore | closed | Bug hunt: run an exhaustive /bughunt over the whole repo and fix every real bug found (each fix under its own fix ticket), then hunt again; close this only when a full pass finds nothing | dotnet test Tests/Core.Tests (204 passing, 0 warnings) at b11d645 — exhaustive manual review of every tracked source file, test, asmdef, csproj, .gitattributes, Packages/manifest.json, ProjectSettings/* and docs/BIBLE.md invariants found no real bug; no fix needed, nothing to land |
| UC-T18 | 2026-10-21 | fix | open | Fix Unity.Cursory's dependency on MindAttic.Unity.Multiplayer, which was renamed to Unity.Multiplayer (repo folder, C# namespaces Unity.Multiplayer/Unity.Multiplayer.Core, asmdef names) under UM-T35, leaving Packages/manifest.json and packages-lock.json pointing at a nonexistent ../../MindAttic.Unity.Multiplayer folder and ServerSimulation.cs/ServerPhysicsTests.cs/the Runtime and PlayMode asmdefs still naming the old namespace and assemblies, so the project cannot resolve its multiplayer package or compile | — |
| UC-T19 | 2026-10-21 | docs | closed | Update USER_STORIES.md | — |
