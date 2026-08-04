# Architecture — system baseline

> The system-wide decisions shared by **every** phase of MW3. Each phase's
> `docs/<project-slug>/ARCHITECTURE.md` records what that phase adds or changes and defers here for
> everything else. Established during `/discover Welcome screen` (phase 1); see
> `docs/welcome-screen/ARCHITECTURE.md` for the full reasoning behind each decision.

## 1. Overview

MW3 is a clone of Mushroom Wars 2: a 2D real-time strategy game, Android-first, adding new
single-player cooperative campaigns. It is a personal project, built by Claude Code through the Ivan
pipeline, and it is optimized for a cheap and fast build/run loop above all else.

**The target is to be as close as possible to MW2 mechanically**, and to ship as **Bug Wars** with
the IP layer replaced: insect heroes and armies matched to the region of each geolocated map,
original branding and items, and **Fame** in place of MW2's ranking. `MW3` and every "mushroom" name
in this repository is placeholder. Mechanics may follow MW2; assets never do (S-6).

MW2 is documented in `docs/reference/` — rules, buildings, units, heroes and items, with sources and
confidence markers — and therefore serves as close to a specification for everything except the IP
layer. `docs/reference/MW2-PARITY.md` lists where the build does not yet match, is read at
`/discover` and `/kickoff` so known answers are not re-asked, and is meant to trend toward empty.
Product truth stays in each phase's `REQUIREMENTS.md`, which outranks the reference on any
disagreement.

One .NET solution. Game rules live in an engine-free library; MonoGame game code sits on top of it;
thin platform heads launch it.

```
MW3.Desktop ---+
               +--> MW3.Game (MonoGame) --> MW3.Core (rules, no engine) <-- MW3.Core.Tests
MW3.Android ---+
```

## 2. Stack

- Client framework: **MonoGame 3.8.5** — DesktopGL and Android heads
- Language/runtime: C# on the .NET 10 SDK; `MW3.Core` targets `netstandard2.1`
- Primary platform: **Android** (physical device). Windows desktop exists as the QA surface
- Build prerequisite: the **`android` .NET SDK workload** — required to build the solution at all,
  because `MW3.Game` multi-targets `net10.0;net10.0-android` and the Android head is in the
  solution so the gate covers it (see `docs/welcome-screen/ARCHITECTURE.md` D-7)
- Tests: xUnit, over `MW3.Core`
- Data: none yet — local JSON on device when saves/settings arrive; no database until a server exists
- Server / API / auth: none yet — introduced with multiplayer, not before

## 3. Standing decisions

**S-1: MonoGame is the client framework.** Rendering capability is not the constraint (the
reference game's own minimum spec is DirectX 9.0c on integrated graphics); the codebase is
agent-authored, so text-only C# beats editor-authored scenes for diffing, review, and automated
editing; and a pure `dotnet` toolchain keeps CI free and fast. Revisit only if a phase demonstrates
a rendering need MonoGame genuinely cannot meet.

**S-2: rules stay engine-free and portable.** `MW3.Core` targets `netstandard2.1`, uses
conservative C# (Unity's language support lags the SDK), and references no engine types. This keeps
rules unit-testable without a graphics device and keeps a future Unity migration possible — such a
migration would rewrite presentation and keep `MW3.Core`.

**S-3: dependency direction is one-way.** `Core` <- `Game` <- platform heads. If logic can live in
`MW3.Core`, it must. Heads contain wiring only.

**S-4: every feature must be verifiable unattended.** The desktop head exists so `qa-verifier` can
exercise the real app without a device or a human; Android is verified on hardware at feature
boundaries and via the CI APK artifact.

**S-5: the pipeline must stay free and scriptable.** No engine binary, license server, or paid
runner may become part of the build. The quality gate is `dotnet` commands only:
`dotnet build -warnaserror`, `dotnet format --verify-no-changes`, `dotnet test`.

**S-6: original art only.** Mechanics may follow Mushroom Wars 2; all assets are recreated. The
repository is public, and copied assets — not cloned mechanics — carry the real IP exposure.

**S-7: single-player first, but never single-player-only by construction.** Cooperative campaigns
are single-player; multiplayer comes later. Nothing may be designed in a way that forecloses a
future authoritative server reusing `MW3.Core`.

**S-8: the simulation is deterministic and command-driven.** State changes only by advancing whole
fixed-step ticks and by applying explicit commands — no wall-clock read, no ambient randomness, no
frame-rate dependence inside `MW3.Core`. Human input and AI produce the same command types, so
neither can express anything the other cannot. This is what makes rules headlessly testable,
interactive features verifiable unattended, and a future authoritative server able to re-run the
same code. Any randomness a later phase needs enters as a seeded PRNG owned by the simulation.
Established in phase 2; see `docs/core-gameplay-loop/ARCHITECTURE.md` D-12.

**S-9: a player is a rules-level owner, not an account.** Until a server exists, a player is an
in-match id plus a controller kind (human or AI). Identity, names, and persistence arrive with
authentication and not before; a future server maps its accounts onto in-match player ids. See
`docs/core-gameplay-loop/ARCHITECTURE.md` D-11.

## 4. Phase index

| Phase | Docs | Adds |
|---|---|---|
| 1 — Welcome screen | `docs/welcome-screen/` | Solution skeleton, both heads, placeholder welcome screen, Android CI artifact |
| 2 — Core gameplay loop | `docs/core-gameplay-loop/` | Match simulation in `MW3.Core`, match screen, send-army mechanic, AI opponent, victory/defeat |
| 3 — Base upgrades and types | `docs/base-upgrades-and-types/` | Garrison caps and base levels, a tower base type that shoots armies in transit, the base action menu, an AI that invests — plus the mid-phase MW2 correction (FR-3a/3b/3c): MW2's literal economy on a 50 ms tick, levels buying defence with combat on `Bu = (a/d) × Wu`, and build time with a recapture grace |
| 4 — Sending armies the MW2 way | `docs/army-sending/` | Send strength as an explicit 25/50/75/100% choice (with snaking) in place of a fixed half, and a send resolving as successive 8-unit waves instead of one arrival — closing parity G-2 and G-3 |
| 5 — Morale | `docs/morale/` | A per-player 0–5 multiplier earned by capturing and defending and bled by standing still, feeding the combat formula's attack and defence indices and raising unit speed — closing parity G-1 and G-7's morale term. The first simulation state that is per-player and global rather than per-building or per-army |
