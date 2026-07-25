# Architecture — system baseline

> The system-wide decisions shared by **every** phase of MW3. Each phase's
> `docs/<project-slug>/ARCHITECTURE.md` records what that phase adds or changes and defers here for
> everything else. Established during `/discover Welcome screen` (phase 1); see
> `docs/welcome-screen/ARCHITECTURE.md` for the full reasoning behind each decision.

## 1. Overview

MW3 is an enhanced clone of Mushroom Wars 2: a 2D real-time strategy game, Android-first, adding
new single-player cooperative campaigns. It is a personal project, built by Claude Code through the
Ivan pipeline, and it is optimized for a cheap and fast build/run loop above all else.

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

## 4. Phase index

| Phase | Docs | Adds |
|---|---|---|
| 1 — Welcome screen | `docs/welcome-screen/` | Solution skeleton, both heads, placeholder welcome screen, Android CI artifact |
