---
codex: 1
project: Unity.Cursory
code: UC
layer: rfc
status: accepted
updated: 2026-10-20
---

# RFC 0001 — Port Cursory to Unity 6

Unity.Cursory rebuilds [Cursory](https://github.com/mindattic/Cursory)'s co-op physics puzzle game in
Unity 6 (6000.4.3f1) on top of [Unity.Multiplayer](https://github.com/mindattic/Unity.Multiplayer).
It supports a dedicated server (hub-and-spoke) and a player-hosted listen server, and players join by IP:port or a join code.

The full design lives in one place: MindAttic.Unity.Multiplayer's `docs/rfc/0001-architecture.md`.
That includes the physics mapping from Aether to Physics2D, body sync, the level port and the ticket chain.
This repo's tickets are UC-T3 to UC-T15, and each requires the one before it:
- UC-T3 waits for UM-T10 (the hand-written NGO adapter).
- UC-T7 waits for UM-T20 (library v1.0.0).
- UC-T12 waits for MAU-T2 (MindAttic.Unity profiles).
