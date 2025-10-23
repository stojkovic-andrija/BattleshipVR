# Battleships VR

Two-player Battleships prototype for Desktop and VR in one build. Server authoritative with FishNet. Follows the test flow: lobby, placement, confirmation, alternating turns, hit or miss feedback, timer, win or lose, restart.

---

## Table of Contents
- [Deliverable Summary](#deliverable-summary)
- [Core Decisions](#core-decisions)
- [Architecture Overview](#architecture-overview)
- [Implementation by Phase](#implementation-by-phase)
- [How to set up](#how-to-set-up)
- [Controls](#controls)
- [Known Limitations](#known-limitations)
- [Effort and Conclusions](#effort-and-conclusions)

---

## Deliverable Summary
- **Goal:** Battleships prototype with placement, confirmation, alternating turns, hit or miss markers, optional timer, win or lose, restart.
- **Engine and stack:** Unity 6000.1.11f1, URP, C#, FishNet, Zenject, UniTask where helpful, OpenXR, Unity Input System.
- **Platforms:** Desktop PC and VR in the same build. Cross play between PC and VR.
- **Networking mode now:** Listen host for development. Join via IP or short code field. Relay planned but not shipped sadly.
- **End to end:**
  - Lobby connect and host or join
  - Placement with validity feedback
  - Ready confirmation and first turn assignment
  - Turn timer for placement and battle
  - Select then confirm shot, cancel supported
  - Server validates hit or miss, markers and VFX
  - Win or lose screen and simple restart

---

## Core Decisions
### Server authority and messages
- All validation on host. Clients send intent only. Turn loop remains deterministic.
- Minimal payloads. Shots are grid indices. Placements are ship type and coordinates. Results are compact result codes.
- Observers targeting so only necessary clients receive events unless a broadcast is needed. Cosmetic effects are local.
- There are no networked spawned objects except players.

### Lobby and transport
- Listen host with IP and code fields to speed iteration.
- Relay planned. UI already supports a transport swap without gameplay changes.

### Scene layout and DI
- Simple boot into gameplay scene. Networking initialized once.
- Gameplay managers live in the gameplay scene for clear lifetimes.
- Zenject used for settings and services to avoid hard references and reduce coupling.

### Cross platform input
- One interaction abstraction used by Desktop and VR.
- World space UI for seated VR. Minimal floating UI.
- Single camera path reused for both modes to keep complexity low.

### Turn order and timers
- First shooter is the player who confirms placements first. Encourages quick fleet placement.
- Shared timer logic for placement and battle to keep pacing and handle edge cases. You can lose your turn if you take more than what was alloted by the timer.

### Orientation and UX
- Local player fixed to the same side for clarity. Opponent board coordinates flipped for across the table play.
- Two step shot flow. Select and then confirm. Cancel before confirm is allowed.

### Visuals and performance
- Custom grid shader for valid or invalid placement and per cell coloring with low cost in VR.
- Lightweight hit or miss feedback, simple water shader, restrained post effects.

---

## Architecture Overview
**Authoritative gameplay services**
- **GameStateService:** Phase machine and transitions. Notifies clients.
- **TurnService:** Shot validation, turn advance, result broadcast.
- **BoardService:** Placement validation, ship state, sink resolution.
- **PlayerRegistryService:** Connection and role tracking.

**Interaction**
- DesktopGridInteractor and VRGridInteractor behind a shared interface. New Input System. Mouse, controller ray, or hands target world space UI.

**UI**
- Lobby host or join, IP or code.
- In game turn info, timer, markers, results screen. Mostly world space.

**Testing hooks**
- Debug sink one ship and debug win to shorten verification cycles (only available in editor).

---

## Implementation by Phase
### Placement
- Drag or point placement with grid shader feedback. Green is valid. Red is invalid.
- Timer runs. On expiry current state is kept. Auto placement on disconnect was planned but backlogged.

### Battle
- Turn indicator and timer.
- Select a cell, confirm, or cancel.
- Server validates. Misses are white markers. Hits are red markers with simple VFX.
- Camera toggles between local and opponent boards to keep targets readable.

### Resolution
- Server broadcasts outcome.
- Basic win or lose screen. Simple rematch loop prepared.

---

## How to set-up
1. Open in Unity **6000.1.11f1**.
2. Build or run in editor with two instances.
3. In the lobby, start one instance as **Host**.
4. On the second instance, **Join** using IP or code (default 127.0.0.1:7770 should work out of the box if port is available).
5. Place ships, confirm, and play with alternating turns.

---

## Controls
- **Desktop**
  - Placement and targeting by mouse. (LMB to place or shoot, RMB to cancel shoot lock in).
  - Q, E keys on keyboard for rotating the ship placement.
- **VR**
  - World space UI with right controller ray.
  - Same two step select and confirm flow. (Controller Select - LMB; Controller Activate - RMB; Q - B, E - A)

Note: If the controller is unable to grab after a scene load, toggle to hands and back as a temporary workaround.

---

## Known Limitations
- Relay transport not yet integrated, can't play outside of local.
- VR right controller requires rebind after scene change.
- Desktop interaction for both phases lives in one script, same as VR Interactorr.
- Auto placement on disconnect not implemented yet, try to place in under the first minute on both players in order to work properly.
- Visuals focus on clarity and cost. Presentation is intentionally light.


---

## Effort and Conclusions
- **Effort:** About 100 hours including research, multiple architecture iterations, DI and networking experiments and cross platform input.
- **Conclusions:**
  - Clear scene lifetimes and simple ownership make FishNet with Zenject predictable.
  - Server authority with compact intent messages kept the turn loop reliable.
  - Timers improved pacing and reduced edge cases.
  - Cross platform interaction works if the abstraction seam is defined early. (Boat interfaces)
  - Define the network architecture at the beginning. FishNet templates skew to simple FPS cases, so complex flows need upfront planning.
  - Create small standalone prototypes or custom scenes to prove behavior before touching main. Use branches per milestone to keep working releases safe.
  - When using DI, plan global vs scene lifetimes from the game design. Avoid networked globals where possible to prevent execution order and late binding issues.
  - Decide early how players are displayed and how boards are laid out. This choice drives architecture.
  - For two players, pick one of two object strategies and commit:
    - **Ambiguous shared objects with server mirroring:** both players use the same objects, server mirrors results across sides. Simple visuals but needs translation of results.
    - **Static per-side objects:** players spawn on opposite sides and own their objects. No result translation, clearer ownership, but code paths are duplicated per side.
  - For cross platform, use world space canvases and keep the Input System active with Desktop checked. Ensure each canvas has the XR Raycast component so VR interaction works. Beware of scene switching, controllers will not work properly.
