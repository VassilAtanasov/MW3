# Learning log

Short notes on the C#/.NET concepts each merged feature actually introduced, tied to this
project's stack (MonoGame 3.8.5 on .NET 10). Written by `/learning-coach`, never gates a feature.

## 2026-08-08 — #99 FR-2: Home screen offers three maps, plus a --map flag
Concepts: nullable value type as an optional-parameter sentinel, local static functions, value-tuple arrays, enum-driven switch expression for parsing
- **`MapId?` as "no boot map given"** — `MW3Game`'s constructor takes `MapId? bootMap = null`
  (`src/MW3.Game/MW3Game.cs:25`) rather than adding a separate `bool hasBootMap` flag. Why here: `MapId`
  is a value type (an enum), so it can't natively represent absence the way a reference type's `null`
  can — wrapping it in `Nullable<MapId>` gets a real "did the caller pass one" without a second
  parameter that could disagree with the first. Pitfall: `.Value` on a `null` nullable throws
  `InvalidOperationException` at the access site, not at assignment - `MW3Game.cs` guards every read
  with `_bootMap.HasValue` first (`if (_bootMap.HasValue) { ... MapCatalog.Get(_bootMap.Value) }`), and
  skipping that check is a bug that only surfaces when a caller actually omits the flag.
- **A local `static` function for parsing** — `Program.cs`'s `TryParseMapId` (`src/MW3.Desktop/Program.cs`)
  is declared `static bool TryParseMapId(string raw, out MapId mapId)` inside the same top-level-statements
  file as the argument loop that calls it. Why here: `static` on a local function stops it from capturing
  any enclosing variable (like `args` or `bootMap`) by mistake - the compiler enforces that this parser only
  ever sees what's passed as parameters, which matters here because the file has several mutable outer
  locals (`bootMap`, `timeScale`, `scriptPath`) a non-static local function could otherwise reach into
  accidentally. Pitfall: a local function's `static` keyword is opt-in, not the default - the same bug
  class (an inner function silently reading/mutating an outer variable) compiles cleanly without it.
- **A `switch` over a `string` mapped to an `enum` `out` parameter** — `TryParseMapId`'s body
  (`case "small": mapId = MapId.Small; return true;` for each of the three names, `default: mapId =
  default; return false;`) is the `TryParse` idiom applied by hand, matching `long.TryParse` used two
  lines above it for `--time-scale`. Why here: consistency with the existing validation pattern in the
  same file rather than introducing a dictionary lookup or `Enum.TryParse` (which would accept any
  member name case-insensitively, including ones this phase doesn't want exposed as CLI values, like a
  future non-selectable `MapId`). Pitfall: `default: mapId = default;` sets `mapId` to `MapId.Small`
  (the enum's zero value) even on the *failure* path, because `default` on an enum is its first member -
  harmless here since callers are required to check the `bool` return before reading `mapId`, but easy
  to trip over if a future edit reads `mapId` without checking `TryParseMapId`'s result first.

Try next: add a fourth hypothetical CLI value (e.g. `--map legacy`) to `TryParseMapId`'s `switch` without
adding a matching `MapId` member, and see the compiler accept it silently - a concrete demonstration of
why `TryParseMapId` hand-writes cases instead of using `Enum.TryParse<MapId>` (which would have required
the reverse mistake, a new enum member nobody added a CLI case for, to go equally unnoticed).

## 2026-08-08 — #98 FR-1: Three named maps and obstacles as core map data
Concepts: constructor chaining with `: this(...)`, `readonly struct` vs `sealed class` for validated value types, switch expressions with a throwing default arm, overload ambiguity with `null`
- **Constructor chaining (`: this(...)`)** — one constructor calling another on the same type before
  its own body runs. Why here: `Match(IReadOnlyList<MapSlot>)` now delegates to
  `Match(MapDefinition)` (`src/MW3.Core/Match.cs:57-60`), wrapping the slot list in an obstacle-free
  `MapDefinition` — this keeps exactly one bases-building code path (D-44) instead of duplicating the
  loop that turns slots into `Base` instances. Pitfall: the delegated-to constructor's body runs
  *before* any code after the `: this(...)` line in the delegating constructor, so validation order
  matters — the null check on `layout` had to happen inline in the `: this(...)` expression itself
  (`layout ?? throw new ArgumentNullException(...)`) rather than in the constructor body, or a null
  layout would reach `MapDefinition`'s constructor with a less specific exception.
- **`readonly struct` for `MapObstacle`, `sealed class` for `MapDefinition`** — both types validate
  at construction and are immutable afterward, but one is a struct and one is a class
  (`src/MW3.Core/MapObstacle.cs:8`, `src/MW3.Core/MapDefinition.cs:8`). Why here: an obstacle is four
  `double`s with no distinguishable identity — cheap to copy, so a `struct` avoids allocation for
  something a `MapDefinition` may hold several of. A `MapDefinition` holds two collections instead,
  where copying doesn't help, so it's a `class`. Pitfall: `MapCatalog.Small`, `.Medium`, and `.Big`
  are `static readonly MapDefinition` fields (a `class`) — reference-equal every time they're read,
  which is what makes `MapCatalogTests.Get_ReturnsTheMatchingDefinition_ForEachId` able to use
  `Assert.Same` rather than `Assert.Equal`. If `MapDefinition` were a struct that assertion would
  need to change, since each read would box or copy a new value.
- **Switch expression with a throwing default arm** — `MapCatalog.Get` (`src/MW3.Core/MapCatalog.cs:52-58`)
  maps `MapId` to its definition with `id switch { MapId.Small => Small, ... , _ => throw new
  ArgumentOutOfRangeException(...) }`. Why here: the acceptance criteria required the lookup to throw
  for an out-of-range `MapId` rather than silently returning a default map — a switch expression
  makes "every known case, then an explicit failure" a single readable statement instead of an
  if/else chain with a trailing throw. Pitfall: `_` catches *any* unmatched value, including a valid
  enum member you forgot to add a case for — the compiler's exhaustiveness warning only fires when
  there's no `_` arm at all, so adding a fourth `MapId` later would compile cleanly and only fail at
  runtime through the `_` arm, not at compile time.
- **Overload ambiguity when both candidates are reference types** — adding `Match(MapDefinition)`
  alongside the existing `Match(IReadOnlyList<MapSlot>)` made `new Match(null!)` in
  `tests/MW3.Core.Tests/MapLayoutInjectionTests.cs:110` a compile error (CS0121): with a bare `null`
  literal, the compiler has no way to prefer one reference-type overload over another, since neither
  converts to the other. Why it matters here: this is a general C# gotcha, not specific to this
  feature — any time a second constructor/method overload is added whose parameter is also a
  reference type, every existing `null`-literal call site becomes ambiguous and needs an explicit
  cast (`(IReadOnlyList<MapSlot>)null!`) to say which overload it means. Pitfall: this only surfaces
  as a compile error, so it's easy to miss in a design review and only catch when the build breaks —
  worth checking deliberately whenever a new overload is added to a type with existing null-checking
  tests.

Try next: add a fourth hypothetical `MapId` member without adding a `MapCatalog.Get` case for it, and
confirm the compiler stays silent while `MapCatalogTests.MapId_HasExactlyThreeMembers` catches the
drift at test time instead — a concrete demonstration of the switch-expression pitfall above.

## 2026-07-26 — #1 Solution skeleton with core library, tests, and desktop head that launches
Concepts: readonly struct, tuple returns, multi-targeting (netstandard2.1 vs net10.0), IDisposable/Dispose(bool) override, xUnit Assert.Throws
- **`readonly struct`** — a value type whose fields can't be reassigned after construction. Why
  here: `FixedStepClock` (`src/MW3.Core/FixedStepClock.cs:8`) needed to be immutable and
  allocation-free since it's meant to be advanced every frame — a `class` would allocate on every
  `Advance` call, which the frame-loop convention in `docs/CONVENTIONS.md` explicitly forbids.
  Pitfall: a `struct`'s parameterless constructor can't be suppressed — `default(FixedStepClock)`
  always exists and skips your validating constructor entirely, which is exactly the gap the
  code-reviewer caught (`FixedStepClock.cs:34` now guards against it explicitly).
- **Tuple returns for "new state + result"** — `Advance` returns `(FixedStepClock Clock, long
  Ticks)` (`FixedStepClock.cs:34`) instead of mutating `this` or using an `out` parameter. Why
  here: immutable types can't mutate themselves, so the idiomatic C# shape for "here's the next
  state, and here's what happened" is a named tuple. Pitfall: named tuple elements are a compile-time
  convenience only — nothing stops a caller from writing `var (a, b) = clock.Advance(x)` with the
  names swapped; there's no runtime check tying `Clock` to the first position.
- **Multi-targeting one solution across TFMs** — `MW3.Core.csproj` targets `netstandard2.1` while
  `MW3.Game.csproj`/`MW3.Desktop.csproj` target `net10.0` (D-2/D-6). Why here: `MW3.Core` must stay
  consumable by a hypothetical future Unity project, whose C# toolchain only understands the older,
  frozen `netstandard2.1` surface — so even though `Directory.Build.props` sets `LangVersion=latest`
  repo-wide, `MW3.Core`'s code has to avoid any API that isn't actually in the netstandard2.1
  reference assembly (this bit us directly: `ArgumentOutOfRangeException.ThrowIfNegative` compiles
  fine on `net10.0` but doesn't exist for `netstandard2.1`, so `FixedStepClock` uses plain
  `if (...) throw new ArgumentOutOfRangeException(...)` instead). Pitfall: `LangVersion` and target
  framework are independent knobs — a project can compile with the newest C# *syntax* while still
  being unable to call newer *runtime* APIs, and the compiler only catches the latter at the call site.
- **Overriding `Dispose(bool disposing)`** — `WelcomeGame` (`src/MW3.Game/WelcomeGame.cs`) overrides
  MonoGame's `Game.Dispose(bool)` to dispose its `GraphicsDeviceManager` field. Why here: the
  analyzer (`CA2213`) flags any `IDisposable` field that's never disposed, and the standard .NET
  dispose pattern is to override the protected `Dispose(bool)` hook (called by both `Dispose()` and
  the finalizer) rather than overriding the public `Dispose()` itself. Pitfall: forgetting to check
  `disposing` before touching other managed objects — the `bool` distinguishes a deterministic
  `Dispose()` call (safe to touch other objects) from finalizer cleanup (other objects may already
  be finalized), so the `_graphics.Dispose()` call is gated on `if (disposing)`.
- **`Assert.Throws<T>` for exception-based contracts** — `tests/MW3.Core.Tests/FixedStepClockTests.cs`
  asserts on the exact exception type (`ArgumentOutOfRangeException`, `InvalidOperationException`)
  rather than just "it throws something". Why here: `docs/CONVENTIONS.md`'s "test behaviour, not
  implementation" rule means a bug fix needs a test that actually distinguishes the fix from the bug
  — `Advance_DefaultConstructedClock_ThrowsInvalidOperation` was written specifically because,
  without the fix, the same call throws `DivideByZeroException` instead, so asserting the type
  catches a regression that a generic "throws" check would miss. Pitfall: `Assert.Throws` only
  passes if the delegate throws *during the assert call itself* — wrapping the throwing call in
  `Task.Run` or any deferred execution silently breaks the assertion.

Try next: add a second `MW3.Core` type that composes `FixedStepClock` (e.g. a tick counter that
accumulates total ticks over time) as a `readonly struct` too, and write a test that chains two
`Advance` calls to confirm carry-over composes correctly across three or more calls, not just two.

## 2026-07-26 — #3 Android head installs and launches on a physical device
Concepts: MSBuild TargetFrameworks + per-TFM conditions, explicit component naming via attributes, Activity lifecycle vs IDisposable, CA1725 override parameter naming
- **Multi-targeting one project across platforms with `<TargetFrameworks>`** — `MW3.Game.csproj`
  switched from a single `<TargetFramework>net10.0</TargetFramework>` to
  `<TargetFrameworks>net10.0;net10.0-android</TargetFrameworks>`, with `Condition="'$(TargetFramework)'
  == '...'"` on the `PropertyGroup`/`ItemGroup` that picks the right MonoGame package per platform
  (`src/MW3.Game/MW3.Game.csproj`). Why here: the same C# source (`WelcomeGame.cs`) needs to compile
  against two different platform-specific MonoGame packages (DesktopGL, Android) that both expose
  the same `Microsoft.Xna.Framework` API surface, without duplicating the project. Pitfall: once a
  project is multi-targeted, every `ProjectReference` *to* it (like `MW3.Desktop`'s reference to
  `MW3.Game`) must resolve to exactly one of its TFMs — MSBuild does this automatically when the
  referencing project's own TFM matches one of the referenced project's TFMs, but it silently
  breaks if that match disappears (e.g. renaming a TFM on one side and not the other).
- **Explicit native-component naming via an attribute** — `MainActivity` declares
  `[Activity(Name = "com.vassilatanasov.mw3.MainActivity", ...)]`
  (`src/MW3.Android/MainActivity.cs:11`) instead of leaving the name to be generated. Why here: the
  Android tooling normally generates a hash-prefixed Activity name per build, which would make the
  `adb shell am start -n com.vassilatanasov.mw3/...` command in `ARCHITECTURE.md` §2a break on every
  rebuild (D-8) — an explicit `Name` pins a stable, scriptable identity instead of an
  implementation-detail one. Pitfall: the string is not compiler-checked against the manifest's
  `package` attribute — a typo here silently produces an app that installs fine but that no launch
  command can find, since nothing cross-validates the two at build time.
- **Two different "cleanup" hooks that are not interchangeable** — `MainActivity` overrides both
  `OnDestroy()` and `Dispose(bool disposing)` (`MainActivity.cs`), each disposing the same
  `WelcomeGame` field. Why here: this is the concrete bug the code-reviewer caught — `Dispose(bool)`
  is the standard .NET `IDisposable` hook, but on an Android `Activity` it only fires reliably when
  the *Java peer* is garbage-collected, which is not the same moment the user backs out of the app;
  `OnDestroy()` is the actual Android lifecycle callback for that. The fix keeps both: `OnDestroy`
  gives correct timing, `Dispose(bool)` keeps the `CA2213` analyzer satisfied (MonoGame's
  `Game.Dispose` is idempotent, so calling it twice is safe). Pitfall: assuming a type that
  implements `IDisposable` only needs the standard dispose pattern — framework base classes
  (Activities, Forms controls, etc.) often have their own more specific lifecycle event that fires
  earlier and more predictably than `Dispose`, and relying on `Dispose` alone can leave resources
  live well past when the user thinks the screen is gone.
- **`CA1725` — override parameter names must match the base method's** — `OnCreate` had to be
  renamed from `OnCreate(Bundle? bundle)` to `OnCreate(Bundle? savedInstanceState)`
  (`MainActivity.cs:20`) to match `AndroidGameActivity.OnCreate(Bundle savedInstanceState)`'s
  declared parameter name exactly. Why here: this is purely a readability/consistency rule enforced
  by the analyzer, not a compiler requirement — C# lets override parameter names differ from the
  base method's. Pitfall: because the compiler doesn't enforce this, mismatched names only surface
  as a build-breaking analyzer error under `-warnaserror`, which can be confusing the first time
  since the method still overrides correctly either way.

Try next: add a second Android-specific integration point (e.g. reacting to `OnPause`/`OnResume`,
deferred to a later feature) and write it using the same "which lifecycle hook actually fires when"
question this feature's review raised — check the Android lifecycle docs before assuming `Dispose`
covers a case.

## 2026-07-26 — #4 Welcome screen with game title and inert entry point
Concepts: viewport-derived layout math, RenderTarget2D for off-screen capture, one-shot resource disposal, shared MSBuild content pipeline, MSBuild build parallelism as a correctness hazard
- **Deriving layout from `Viewport` instead of fixed pixels** — `WelcomeScreen.GetButtonBounds`
  (`src/MW3.Game/WelcomeScreen.cs:83`) computes a `scale = viewport.Width / _referenceViewportWidth`
  and multiplies every position/size by it, rather than hardcoding coordinates. Why here: the same
  screen has to look right on the desktop window at any size *and* on an Android device with a
  different aspect ratio, and QA specifically checks that resizing keeps things centred rather than
  drifting. Pitfall: scaling only `X`/`Y`/width/height by one factor derived from *width* assumes a
  roughly-fixed aspect ratio; a genuinely different aspect ratio (very tall/narrow) can still push
  content off-screen even though every number is "viewport-derived" — the fix generalizes but
  doesn't eliminate the need to think about aspect ratio, not just resolution.
- **`RenderTarget2D` to capture a frame instead of what's on screen** — `WelcomeGame.LoadContent`
  creates one sized to the back buffer only when a screenshot path is given
  (`src/MW3.Game/WelcomeGame.cs:34`), and `Draw` redirects rendering into it via
  `GraphicsDevice.SetRenderTarget` before drawing, then reads it back for `SaveAsPng`. Why here:
  MonoGame's back buffer isn't directly readable as pixel data on every platform/driver combination,
  so the standard pattern is to render into an off-screen texture you fully control instead.
  Pitfall (the actual bug the reviewer caught): treating "capture once" as automatic just because
  the call site happens to look like a one-off — nothing stopped `Draw` from re-capturing (and
  rewriting the file, non-atomically) on every single frame until the code explicitly disposed and
  nulled the render target after the first save (`WelcomeGame.cs:66`) to make that guaranteed.
- **Disposing a field and setting it to `null` as a "done, don't do this again" signal** — the fix
  above reuses the existing `_screenshotTarget is not null` checks already guarding the capture
  logic, so disposing-then-nulling the field is simultaneously the cleanup *and* the one-shot latch,
  with no extra boolean flag needed. Pitfall: this only works safely because every use of the field
  is already null-checked; retrofitting this pattern onto a field that's dereferenced unconditionally
  elsewhere would trade one bug (re-firing) for another (`NullReferenceException`).
- **One `.mgcb` content project shared by two head projects** — `src/MW3.Game/Content/Content.mgcb`
  is referenced via `<MonoGameContentReference>` from both `MW3.Desktop.csproj` and
  `MW3.Android.csproj`, and `MonoGame.Content.Builder.Task` overrides the file's own
  `/platform:DesktopGL` line with `/platform:$(MonoGamePlatform)` per consuming project. Why here:
  avoids duplicating the same font/asset list per head while still producing a correctly
  platform-compiled `.xnb` in each head's own output folder. Pitfall: this only works because each
  head sets `$(MonoGamePlatform)` itself (via its `MonoGame.Framework.*` package reference) —
  point two *differently-platformed* heads at the same `.mgcb` without that, and content silently
  builds for the wrong platform.
- **MSBuild's default parallelism as a correctness hazard, not just a speed knob** — building this
  solution's default way (`dotnet build`, multiple MSBuild nodes) reliably crashed
  `MonoGame.Content.Builder.Task` with a raw `IOException` once two independent head projects both
  triggered content builds against the same shared `Content.mgcb` (a known upstream race,
  MonoGame/MonoGame#7409); forcing a single node (`-m:1`, added to `gate.ps1`) made it deterministic.
  Why here: MSBuild parallelizes *across projects* by default for speed, and that's normally safe
  because projects' outputs don't collide — but a shared external resource (the content pipeline's
  own intermediate-file bookkeeping) broke that assumption. Pitfall: `-m:1` is a solution-wide
  sledgehammer; it fixed the actual collision but also serializes every other project in the
  solution that had nothing to do with the race, which is a real (accepted) trade-off worth
  revisiting if the solution grows enough for build time to matter.

Try next: add a third platform-varying asset (e.g. a second `.spritefont` at a different size) to
the same `Content.mgcb` and confirm both heads still pick it up automatically — this exercises the
shared-content pattern again without the font-selection risk already covered here.

## 2026-07-27 — #8 Player, base ownership, and unit production in the core rules library
Concepts: positional records for value-like data, nullable reference types to model absence, readonly record struct, switch expressions, reflection-based public-surface tests, cumulative-counter accumulation for deterministic partial progress
- **Positional `record` for id-plus-behavior-free data** — `Player` (`src/MW3.Core/Player.cs`) is
  `public sealed record Player(int Id, PlayerControllerKind ControllerKind);`. Why here:
  `docs/CONVENTIONS.md` prescribes `record` for value-like data and `class` for identity/behavior,
  and D-11 deliberately keeps `Player` to exactly two fields with no behavior — a record gives free
  value equality (used directly in tests like `b.Owner == match.HumanPlayer`) and `init`-style
  immutability with one line. Pitfall: record equality is structural, not reference — two players
  with the same `Id` and `ControllerKind` compare equal even if they're meant to be distinct match
  participants, which would be a real bug in a design that allowed duplicate ids (it doesn't here,
  since `Match`'s constructor is the only place `Player` instances are created).
- **`Player? Owner` to model "no owner" instead of a sentinel** — `Base.Owner`
  (`src/MW3.Core/Base.cs`) is a nullable reference, so a neutral base is simply one where
  `Owner is null`. Why here: the acceptance criteria explicitly forbid a reserved player id or index
  0 as "neutral" — nullable reference types make the absence a case the compiler tracks (with
  `<Nullable>enable</Nullable>` already on repo-wide), rather than a convention callers have to
  remember. Pitfall: `is null`/`is not null` pattern checks are required by the style rules over
  `== null` for reference types, but more importantly, forgetting a null check on `Owner` anywhere
  new code reads it becomes a compiler warning (promoted to an error under `-warnaserror`), not a
  silent runtime bug — which is the entire point, but only if new code doesn't suppress the warning.
- **`readonly record struct` for a small, frequently-copied value** — `MapPoint`
  (`src/MW3.Core/MapPoint.cs`) is `public readonly record struct MapPoint(double X, double Y);`.
  Why here: like `FixedStepClock` from #1, a normalized position is copied by value constantly
  (every `Base.Position` read) and must never allocate — a `struct` avoids heap allocation, `record`
  gives value equality for the tests that assert exact positions (`MatchTests.cs`), and `readonly`
  documents and enforces that it can't be mutated in place. Pitfall: `record struct` without
  `readonly` still allows field mutation through a non-readonly reference, silently reintroducing the
  mutable-value-type footguns (e.g. surprising copy-on-write behavior in LINQ) that `readonly` exists
  to close off.
- **`switch` expression over an internal enum to resolve a slot to a `Player?`** — `Match`'s
  constructor (`src/MW3.Core/Match.cs`) uses
  `slot.Kind switch { MapSlotKind.HumanStart => HumanPlayer, MapSlotKind.AiStart => AiPlayer, _ => null }`.
  Why here: `MapLayout` only knows *kinds* of starting slot, not player instances (the map is defined
  before any `Player` exists), so the switch expression is the single point that binds the map's
  intent to the match's actual `Player` objects. Pitfall: the discard arm `_ => null` silently
  matches any future `MapSlotKind` value added later (e.g. a hypothetical second AI) — an exhaustive
  switch with named arms for every enum value would fail to compile instead and force the author to
  decide what the new kind means, so widening `MapSlotKind` is a easy place to introduce a silent bug.
- **Reflection to assert a public surface has *no more and no less* than the required members** —
  `PlayerTests.PublicSurface_ExposesOnlyIdAndControllerKind` and the equivalent tests in
  `BaseTests.cs`/`MatchTests.cs` use `typeof(T).GetProperties(...)` and assert the exact name list,
  plus `GetSetMethod(nonPublic: false) is null` to prove no public setter exists. Why here: D-11's
  acceptance criterion is explicitly about the *absence* of extra fields (no name, colour, score) —
  a normal behavioral test can't fail when someone adds an unused property, so reflection is what
  turns "nothing extra was added" into an assertion instead of a code-review hope. Pitfall: this
  style of test is brittle in the good sense (it must be updated whenever the type legitimately
  grows) but brittle in the bad sense too — it says nothing about *behavior*, so it must always sit
  alongside behavioral tests, never replace them.
- **Deriving partial progress from a cumulative counter instead of per-call carry state** —
  `Match.Advance` (`Match.cs`) tracks a single `_elapsedTicks` field and computes
  `unitsProducedNow - unitsProducedSoFar` from `_elapsedTicks / ProductionPeriodTicks` on every call,
  rather than accumulating a remainder per base the way `FixedStepClock` does for its own carry.
  Why here: this is what makes `Advance(7); Advance(3)` produce byte-identical results to
  `Advance(10)` in one call "for free" — the calculation only ever depends on the *total* ticks
  elapsed, never on how the caller chose to chunk them, which is exactly D-12's determinism
  requirement. Pitfall: this only stays correct because every owned base always produces at the same
  rate from tick zero — the moment a base's ownership can change mid-match (the next feature, FR-4),
  a single match-wide counter stops being enough and per-base "ticks owned" bookkeeping becomes
  necessary; this feature's design deliberately doesn't yet solve that harder problem.

Try next: add a `MW3.Core` type that has to interoperate with the shared-map + reflection-test
pattern from this feature — e.g. sketch (without wiring it in) what a `SendArmyCommand` record for
FR-4 would look like, and consider what a reflection test analogous to the ones here would need to
assert once ownership becomes mutable mid-match.

## 2026-07-27 — #9 Play button opens a match screen and back returns to the welcome screen
Concepts: seam interfaces for untestable I/O, closed record hierarchies with pattern matching, edge-detection state machines, Stack<T> for navigation, Android key-event dispatch order, CA2000 vs. ownership transfer
- **An interface as the one seam between untestable I/O and everything else** — `IInputSource`
  (`src/MW3.Game/IInputSource.cs`) is read by every screen; `MouseAndTouchInputSource` wraps
  `Mouse`/`TouchPanel`/`Keyboard`, and `ScriptedInputSource` replays a file instead. Why here: D-17
  rejected injecting synthetic OS events as the way to test navigation, so the seam has to sit one
  level up — screens ask "is the pointer down, where, was back requested" and never care which
  implementation is answering. Pitfall: a seam is only as good as its narrowest consumer — the
  moment a screen reached past `IInputSource` for `Mouse.GetState()` directly (which none do here,
  but it would compile fine if one did), the whole scripted-replay guarantee silently stops applying
  to that screen with no compiler error to catch it.
- **A closed set of variants as a sealed record hierarchy, matched with `switch`** —
  `ScriptDirective` (`src/MW3.Game/ScriptDirective.cs`) is an abstract record with three `sealed`
  cases (`DownDirective`, `UpDirective`, `BackDirective`); `ScriptedInputSource.Update`
  (`ScriptedInputSource.cs:53`) pattern-matches on the concrete type in a `switch` statement. Why
  here: a directive is genuinely one of exactly three shapes with different data (`Down`/`Up` carry
  coordinates, `Back` carries nothing) — a single record with unused fields for the cases that don't
  need them would let a `BackDirective` be constructed with meaningless `X`/`Y` values that compile
  fine but mean nothing. Pitfall: this only stays exhaustive by convention — the `switch` has no
  `default` arm printing a warning if a fourth directive type is added later, so a missed case
  silently does nothing at runtime instead of failing to compile (unlike the `MapSlotKind` enum
  `switch` from #8, a `switch` over an open set of `record` types can't be marked exhaustive by the
  compiler the same way).
- **Edge detection: comparing this frame's state to last frame's, not just reading the current one**
  — `WelcomeScreen.Update` (`WelcomeScreen.cs:44`) tracks `_wasPointerPressed` and
  `_pressStartedInsideButton` so it can tell "just pressed" and "just released" apart from "still
  held down". Why here: the acceptance criterion is specifically about release-within-bounds
  activating the button and release-outside not — a single per-frame `IsPointerPressed` boolean
  can't express *transitions*, only instantaneous state, so the screen has to remember one frame of
  history itself. Pitfall: forgetting to update `_wasPointerPressed` on *every* path through
  `Update` (not just the branches that act on it) desyncs the edge detector permanently — every
  future frame reads a stale "previous" value and the button either never fires again or fires on
  the wrong frame.
- **`Stack<IScreen>` as the entire navigation model** — `ScreenManager`
  (`src/MW3.Game/ScreenManager.cs`) has no separate "current screen" field; `Push`/`Peek`/`Pop` on
  a plain `Stack<T>` *is* the navigation stack (D-16). Why here: the feature's whole shape is
  "push forward, pop back," which is exactly what a stack models with no extra bookkeeping — no
  index to keep in sync, no separate history list. Pitfall: `Stack<T>.Pop()` throws
  `InvalidOperationException` on an empty stack, so every caller (here, only `ScreenManager` itself)
  must check `Count` first; the one guard that matters is the count-of-1 check before popping,
  which is also the exact point this feature's "exit instead of pop" acceptance criterion sits on.
- **Android's key-event dispatch order, and why overriding the "obvious" method didn't work** —
  the first fix for the hardware back button overrode `MainActivity.OnBackPressed()`
  (deleted before merge), which never fired; the working fix overrides `DispatchKeyEvent`
  (`src/MW3.Android/MainActivity.cs`) instead. Why here: `Activity.dispatchKeyEvent` calls into the
  view hierarchy (`Window.superDispatchKeyEvent`) *before* falling back to `onKeyDown`/
  `onBackPressed` — MonoGame's own view was consuming `KEYCODE_BACK` for its polling-based
  `Keyboard` state before the event ever reached that fallback path, on this physical device.
  Pitfall: the "correct-looking" override (`OnBackPressed`, named exactly for this purpose) can be
  entirely unreachable depending on what else is in the view hierarchy — when a platform callback
  silently never fires, the fix usually isn't a different implementation of the same hook, it's a
  hook earlier in the dispatch chain; this was only diagnosable at all because `qa-verifier` could
  reproduce "no effect" against real hardware and rule out the relay logic by inspection first.
- **A justified `#pragma warning disable CA2000` versus an actual `try`/`finally`** — pushing a new
  screen (`WelcomeScreen.cs`, `MW3Game.cs`) suppresses CA2000 with a comment explaining
  `ScreenManager` takes disposal ownership, while the screenshot `RenderTarget2D` in
  `MW3Game.Draw` got a real `try`/`finally` instead of a suppression. Why the difference: the
  screen case is a true false positive — the analyzer can't see that `ScreenManager.Push`/`Pop`
  dispose what they're handed, so suppressing with a comment naming the constraint (per
  `docs/CONVENTIONS.md`) is honest; the render-target case was a genuine gap — an exception thrown
  between creation and the original unconditional `Dispose()` call would have leaked a GPU resource,
  which a suppression would have hidden rather than fixed. Pitfall: CA2000 firing is not by itself
  evidence of which situation you're in — the fix has to start from "does this object's owner
  actually dispose it on every path," and only becomes a suppression once that's verified true.

Try next: add a fourth `ScriptDirective` case (e.g. a `WaitDirective` that does nothing but consume
a frame) and notice the `switch` in `ScriptedInputSource.Update` compiles fine without handling it —
then add a `default` arm that throws, and see which of the four committed `qa/scripts/*.txt` files,
if any, would have hidden the gap by never needing that case at all.

## 2026-07-27 — #13 Match screen draws the map, bases, owners, and live garrison counts
Concepts: procedural Texture2D generation, narrowing a parameter to enforce a boundary, invalidate-on-change caching to avoid per-frame allocation, IReadOnlyList<T> foreach boxing a struct enumerator, tuple deconstruction over an immutable clock
- **Generating a texture in code instead of loading an asset** — `MatchScreen.CreateCircleTexture`
  (`src/MW3.Game/MatchScreen.cs`) builds a 128×128 `Texture2D` by filling a `Color[]` (white inside
  the radius, `Color.Transparent` outside) and calling `texture.SetData(data)` once in
  `LoadContent`. Why here: the acceptance criteria explicitly forbid adding an image asset to the
  content pipeline (D-5 - original art only, and a circle isn't art yet), so the only way to get a
  filled circle onto the screen is to rasterize one directly; `SpriteBatch.Draw`'s tint parameter
  then recolors the same white-on-transparent texture per base (human/AI/neutral) without needing
  three separate textures. Pitfall: `SetData` takes a flat array in row-major order
  (`data[(y * diameter) + x]`) - transposing `x`/`y` in that index silently produces a
  90-degree-rotated (or mirrored, depending on which axis is swapped) circle that still *looks*
  roughly circular at a glance, so a transposition bug here is easy to ship unnoticed without
  either a very close visual check or a symmetry-based test.
- **Narrowing `GameTime` to a `long` at the one boundary that's allowed to see it** —
  `MW3Game.Update` (`MW3Game.cs`) computes
  `(long)gameTime.ElapsedGameTime.TotalMilliseconds` and passes that value into
  `IScreen.Update(..., long elapsedMilliseconds)`; no screen's method signature accepts a
  `GameTime`. Why here: D-12 requires the rules layer (and now the screens that drive it) to never
  read a wall-clock member, and the *type system* is what actually enforces that once `IScreen`'s
  signature no longer has a parameter capable of exposing one - a screen literally cannot call
  `gameTime.TotalGameTime.TotalSeconds` by accident because there is no `gameTime` in scope to call
  it on. Pitfall: this only holds as a boundary as long as *every* implementer's signature is kept
  narrow; adding a second method that takes `GameTime` "just for this one screen" anywhere in
  `MW3.Game` would quietly reopen the exact hole this design closes.
- **Invalidate-on-change caching to satisfy "no allocation per frame"** — the code-review fix in
  `MatchScreen.cs` added `_garrisonText`/`_lastGarrisonCount` arrays so `GarrisonCount.ToString()`
  only runs when `_lastGarrisonCount[i] != b.GarrisonCount`, not on every `Draw` call. Why here:
  `docs/CONVENTIONS.md`'s frame-loop rule exists because the target device is a phone, and a
  garrison count changes at most once every `ProductionPeriodTicks` (10 ticks) - formatting it 60
  times a second for a value that changed once every ~1.6 seconds (at 60 ticks/sec) is pure waste
  the cache eliminates by construction. Pitfall: a cache keyed by *value equality* rather than
  *identity/position* would have been wrong here - two different bases can legitimately hold the
  same garrison count at the same time (both neutrals start at 5), so the cache is indexed by each
  base's stable position in the list, not by the count itself.
- **`foreach` over an `IReadOnlyList<T>`-typed field boxes its enumerator** — the second review
  finding: `foreach (var b in _match.Bases)` (where `Match.Bases` is typed `IReadOnlyList<Base>`,
  backed by a `List<Base>`) forces the compiler to bind through `IEnumerable<Base>.GetEnumerator()`
  rather than `List<Base>`'s own non-boxing struct enumerator, because the *static type* of the
  expression is the interface, not the concrete list. The fix switched to an indexed `for` loop
  (`bases[i]`), which only calls the interface's `this[int]` indexer - no enumerator, no boxing.
  Pitfall: this is invisible at the call site and doesn't show up as a compiler warning; it only
  shows up as GC pressure under profiling, which is exactly why the review convention calls it out
  explicitly rather than trusting it to be self-evident from reading the code.
- **Tuple deconstruction over an immutable "next state + result" return** —
  `MatchScreen.Update` (`MatchScreen.cs`) does `var (clock, ticks) = _clock.Advance(elapsedMilliseconds); _clock = clock;`,
  the same `FixedStepClock` pattern `#1`'s log entry covered, now reused in a second, independent
  context (the match screen's own timing, separate from whatever clock a head might use). Why here:
  proof that the pattern generalizes - `FixedStepClock` doesn't know or care whether its caller is
  a head's smoke-mode loop or a screen's `Update`, because its contract is just "elapsed
  milliseconds in, whole ticks and next state out." Pitfall (same as before, worth restating since
  it bit exactly this shape of code): forgetting the reassignment (`_clock = clock;`) means the
  carry-over remainder silently never advances past whatever the first call computed, and ticks
  stop accumulating correctly with no exception to reveal it.

Try next: the previous entry's exercise (add a `WaitDirective`) is exactly what this feature did -
`src/MW3.Game/WaitDirective.cs` is a one-line `sealed record` and its `switch` case in
`ScriptedInputSource.Update` is an explicit no-op `break`. For the next one, try adding a
`--dump-state` field that reports in-flight armies once FR-4 introduces them, and notice how much
of `MatchScreen.WriteStateDump`'s shape (open a `StreamWriter`, one line per item, an
`Owner`-or-`Neutral` string) can be reused versus what has to change.

## 2026-07-27 — #15 Correct viewport docs: 1920x1200 is the panel, not the MonoGame viewport
No new concepts — docs-only correction (CLAUDE.md, REQUIREMENTS.md), no code changed.

## 2026-07-27 — #12 Document REST fallback for reading full issue/PR bodies
No new concepts — docs-only addition to CLAUDE.md's GitHub access section, no code changed.

## 2026-07-27 — #14 Core rules for sending an army: transit, reinforcement, capture, and losses
Concepts: enum result types instead of bool/exception, nullable `long?` as a "no value yet" sentinel, netstandard2.1's missing `ArgumentNullException.ThrowIfNull`, record equality via `!=` for domain checks, chronological event-segmentation to fix a determinism bug
- **An `enum` return type to make every rejection reason a distinct, exhaustive case** —
  `SendArmyOutcome` (`src/MW3.Core/SendArmyOutcome.cs`) gives `Match.Execute`
  (`src/MW3.Core/Match.cs:61`) six named outcomes instead of a `bool` or a thrown exception. Why
  here: the acceptance criteria explicitly require distinguishing *why* a send was rejected "in the
  type system - not a bool, not an exception for ordinary rejections" — a caller (a future UI, the
  AI, a test) can `switch` on the exact reason without string-matching an exception message or
  losing information behind a single `false`. Pitfall: an `enum` is not automatically exhaustively
  checked by the compiler at every call site the way a closed record hierarchy's `switch` can
  warn on missing arms (see #9's `ScriptDirective` entry) — a `switch` over `SendArmyOutcome`
  missing a case still compiles fine and silently falls through unless a `default` arm is written
  to catch it deliberately.
- **`long?` as "no arrival due yet" rather than a magic sentinel value** —
  `EarliestArrivalTickUpTo` (`Match.cs`) returns `long?`, and `Advance` treats `null` as "nothing to
  resolve in this segment, jump straight to the target tick." Why here: tick numbers are ordinary
  non-negative `long`s, so any in-band sentinel (`-1`, `long.MaxValue`) would either collide with a
  real value or require a second boolean to disambiguate — nullable value types exist for exactly
  this "value, or nothing" shape, matching the same reasoning `Base.Owner` used for "no player" in
  #8's entry. Pitfall: `earliest is null || army.ArrivalTick < earliest` (`Match.cs`) relies on the
  short-circuit `||` evaluating the null check first; reordering the comparison would compile (a
  `long?` supports `<` against a `long?` via lifted operators) but silently changes which armies get
  picked when `_armies` is empty on the first iteration.
- **`ArgumentNullException.ThrowIfNull` doesn't exist on `netstandard2.1`** — the first version of
  `Match.Execute`'s null check used `ArgumentNullException.ThrowIfNull(command);`, which compiles
  and analyzes cleanly as a suggestion from tooling trained on modern .NET, but failed the gate with
  `CS0117: 'ArgumentNullException' does not contain a definition for 'ThrowIfNull'` because that
  static helper was added in .NET 6 and isn't part of the frozen `netstandard2.1` surface `MW3.Core`
  targets (D-2/D-6, the same constraint #1's entry hit with `ThrowIfNegative`). The fix reverted to
  the explicit `if (command is null) { throw new ArgumentNullException(nameof(command)); }`
  (`Match.cs:63`). Pitfall: an IDE or an AI assistant suggesting "modern" null-check syntax has no
  way to know a project multi-targets an old TFM unless it actually tries to build against that
  target — this is exactly the kind of gap `gate.ps1`'s build step exists to catch immediately
  rather than at review time.
- **Comparing a `record`'s `!=` for a domain rule, not just in tests** — `Execute` rejects a send
  with `if (source.Owner != command.IssuingPlayer)` (`Match.cs:75`), and arrival resolution checks
  `target.Owner == army.Owner` (`Match.cs`) to decide reinforcement versus combat. Why here: `Player`
  is a `record` (#8's entry), so `==`/`!=` are structural equality, null-safe by the compiler's
  generated operators - exactly what's needed to compare a nullable `Base.Owner` against a
  non-nullable `SendArmyCommand.IssuingPlayer` without a manual null check first. Pitfall: this is
  only safe because `Match`'s constructor is the sole place `Player` instances are created (#8's
  entry already flagged this) - if two distinct in-match players could ever share an `Id` and
  `ControllerKind`, these equality checks would treat them as the same player.
- **Decomposing a batch operation into chronologically-ordered segments to fix a real determinism
  bug** — the first version of `Match.Advance` applied one flat production diff for the whole call,
  then resolved all due arrivals; this passed every single-scenario test but failed a dedicated
  determinism test comparing `Advance(100)` in one call against the same total split across smaller
  calls, because a base captured partway through a large call got zero production credit for that
  call (production was computed once, before the capture), while the same capture landing in an
  *earlier*, smaller call let the base receive production normally in the *next* call. The fix
  (`Match.Advance`, `Match.cs`) walks forward one segment at a time - from now to the next army
  arrival tick (or the requested end, whichever comes first) - applying production for exactly that
  span, then resolving whatever arrives at that tick, before finding the next segment. Why this
  matters as a concept: "produce the same output regardless of how the caller batches calls" is a
  correctness property that unit tests for individual scenarios can't catch by construction - only a
  test that deliberately varies the batching (this codebase's recurring `Advance(7); Advance(3)` vs.
  `Advance(10)` pattern, going back to #8) exercises it. Pitfall: the fix trades a single O(1)
  arithmetic diff for a loop bounded by the number of *distinct arrival ticks* in the requested
  range, not by the tick count itself - correct and still cheap for a handful of in-flight armies,
  but worth remembering if a future feature ever has hundreds of armies in flight simultaneously.

Try next: add a `--dump-state` line reporting in-flight armies (source, target, unit count, ticks
remaining) as flagged in #13's entry, now that FR-4 actually has armies to report - and, before
wiring it up, sketch a test that advances a match in different chunk sizes with an army mid-flight
to confirm the dump would show identical numbers regardless of chunking, the same property this
feature's `Advance` fix depends on.

## 2026-07-27 — #21 CI builds and publishes the Android APK as an artifact
No new C# concepts — the diff is CI workflow YAML plus one MSBuild property
(`EmbedAssembliesIntoApk`, `src/MW3.Android/MW3.Android.csproj`) that disables Fast Deployment for
Debug builds so the CI-published APK installs standalone.

## 2026-07-27 — #20 Tap and mouse input sends armies between bases on both heads
Concepts: pattern-matching a nullable value type instead of .Value/.HasValue, reflection into a private static method with an out parameter, Dictionary<TKey,TValue>.Keys as a non-boxing concrete-type enumerator, deferred removal to avoid mutating a collection mid-enumeration, the IReadOnlyList<T> boxing pitfall recurring in new call sites
- **`if (x is int id)` instead of `x.HasValue`/`x.Value`** — `MatchScreen.HandleDrag`
  (`src/MW3.Game/MatchScreen.cs`) writes `var pressedBase = pressedBaseId is int id ? FindBase(id) : null;`
  and `if (targetId is int target && target != sourceId)` rather than checking `.HasValue` and then
  dereferencing `.Value` separately. Why here: `HitTester.FindBaseAt` returns `int?` specifically so
  "no base" is a type-level absence (D-18), and the pattern-match form binds the unwrapped `int` to a
  new name in the same expression that tests for presence, so there's no window where code could
  read `.Value` before confirming `HasValue` is true. Pitfall: `is int id` on a `Nullable<int>` only
  matches when the value is present - it's easy to assume (wrongly) that this also somehow matches
  `null` into a default `0`, when in fact `null` simply fails the pattern and falls to the `else`/`:`
  branch, which is exactly what's wanted here but is worth double-checking the first time.
- **Reflecting into a `private static` method that has an `out` parameter** —
  `HitTesterTests.FindNearestBaseId_PointBetweenTwoBases_ResolvesToTheGenuinelyNearerOne`
  (`tests/MW3.Core.Tests/HitTesterTests.cs`) calls
  `method.Invoke(null, new object?[] { point, match.Bases, null })` and never reads the third array
  slot back, because the test only needs the return value here — this extends the `Match.ComputeTravelTicks`
  reflection pattern from #14's entry to a method whose signature includes `out double nearestDistance`.
  Why here: `HitTester.FindNearestBaseId` is deliberately `private` (only `FindBaseAt`'s
  threshold-gated wrapper is public API), but the "resolves to the genuinely nearer one" acceptance
  criterion needs to test the *unthresholded* nearest search directly, the same tension #14 already
  hit with a private helper. Pitfall: when a reflected method has an `out` parameter, the value
  written back is read from the *same array slot* after `Invoke` returns (`MethodInfo.Invoke` mutates
  the `object[]` in place for `ref`/`out` parameters) — passing `null` as a placeholder works only
  because this particular test doesn't need that slot; a test that did would have to read
  `parameters[2]` back out after the call, not assume the local variable it never had access to.
- **`Dictionary<TKey,TValue>.Keys` avoids the boxing that `IReadOnlyList<T>` doesn't** —
  `MatchScreen.PruneResolvedArmyText` (`src/MW3.Game/MatchScreen.cs`) does
  `foreach (var id in _armyUnitText.Keys)` directly against the concrete `Dictionary<int, string>`
  field, which is safe from the enumerator-boxing problem #13's entry already flagged for
  `foreach (var b in _match.Bases)` — because `_armyUnitText`'s *compile-time* type is the concrete
  `Dictionary<int, string>`, not an interface, `.Keys` returns the concrete `KeyCollection` struct
  type and `foreach` binds to its own non-boxing `GetEnumerator()`. Why here: this feature needed a
  frame-loop-safe way to iterate one collection (armies still in flight, exposed as `IReadOnlyList<Army>`
  and iterated by index) while safely enumerating a different one (cached text keyed by army id,
  a plain `Dictionary` field) — the same boxing question has two different right answers depending
  on which concrete type sits behind the variable. Pitfall: this safety only holds as long as the
  field stays declared as `Dictionary<int, string>`; refactoring it to `IReadOnlyDictionary<int, string>`
  for encapsulation (a change that looks purely stylistic) would silently reintroduce the exact boxing
  cost this code was written to avoid.
- **Collecting removals in a second pass instead of mutating during enumeration** — the same
  `PruneResolvedArmyText` method builds `_armyIdsToPrune` (a reused `List<int>` field) while iterating
  `_armyUnitText.Keys`, then removes from `_armyUnitText` in a separate loop afterward. Why here:
  `Dictionary<TKey,TValue>.Remove` while a `foreach` over that same dictionary (or its `Keys`) is still
  in progress throws `InvalidOperationException` at the next `MoveNext()` — a version-check the
  runtime performs specifically to catch this — so the fix has to fully finish reading before it
  starts writing. Pitfall: reusing `_armyIdsToPrune` as a field (rather than a fresh `List<int>` per
  call) avoids a per-tick allocation, but only works correctly because the method clears it at the
  end of every call; forgetting that `Clear()` would silently re-remove already-gone ids on the next
  call (harmless here since `Dictionary.Remove` on a missing key is a no-op) while quietly leaking the
  list's backing array's contents forever.
- **The `IReadOnlyList<T>` `foreach`-boxing pitfall from #13 recurring in brand-new code** —
  `HitTester.FindNearestBaseId` and `MatchScreen.FindBase`/`DrawArmiesInFlight` were first written
  with plain `foreach` loops over `_match.Bases`/`_match.ArmiesInFlight` (both `IReadOnlyList<T>`),
  and the code-reviewer caught the identical boxing issue #13 had already fixed once in this same
  file, just at new call sites this feature added. Why repeat the entry: it's worth noting explicitly
  *because* it recurred despite being documented — the lesson from #13 was "this is invisible at the
  call site," and that held true even while writing the fix for the same file a few weeks later,
  which is a stronger argument for institutionalizing it (e.g. an analyzer rule or a code-review
  checklist item) than for trusting memory of a past finding.

Try next: add an analyzer or `.editorconfig` rule (or, short of that, a `docs/CONVENTIONS.md` checklist
line) that would have caught the `IReadOnlyList<T>` `foreach`-boxing pattern automatically, then
deliberately reintroduce one `foreach` over `_match.Bases` in a throwaway branch to confirm the rule
actually fires — turning a review-time catch into a gate-time one.

## 2026-07-27 — #24 AI opponent reinforces and attacks with simple heuristics
Concepts: readonly record struct wrapping a private field to model a closed "one of two shapes" result, extracting shared logic behind an unchanged private method signature, List<T>.Sort with a multi-key comparator for total ordering, reusing a segment-walking pattern to solve a structurally identical new problem, validating a computed value before it reaches a downstream boundary
- **`readonly record struct BrainDecision` hiding a nullable field behind two named constructors** —
  `BrainDecision` (`src/MW3.Core/BrainDecision.cs`) has a single private `SendArmyCommand? _command`
  field, but its public surface is `BrainDecision.None`, `BrainDecision.Send(command)`, `HasCommand`,
  and a `Command` getter that throws if accessed without checking `HasCommand` first. Why here: the
  acceptance criteria explicitly forbid `null` as the "no command" signal (unlike `Base.Owner`'s
  `Player?` in #8, or `HitTester`'s `int?` in #9/#20) — the AI's decision needs to be a closed
  two-shape result, and record structs give that shape free structural equality (used directly in
  `AiBrainTests.cs`'s `Assert.False(decision.HasCommand)` assertions) without a class allocation per
  decision, which matters since a decision happens on a fixed cadence for the life of a match.
  Pitfall: because the backing field is still nullable underneath, a future maintainer who adds a
  second field to `BrainDecision` and forgets `_command`'s null-guard in the new code reopens exactly
  the hole the type exists to close — the compiler enforces nothing beyond what `Command`'s one
  guarded getter does today.
- **Extracting shared arithmetic into a new type while keeping an old private method's signature
  intact** — `Match.ComputeTravelTicks` (`src/MW3.Core/Match.cs`) now delegates its entire body to
  the new `TravelTimeCalculator.ComputeTicks` (`src/MW3.Core/TravelTimeCalculator.cs`), so `AiBrain`
  can reuse the identical travel-time formula without duplicating it. Why here: `SendArmyTests.cs`
  already reflects into `Match.ComputeTravelTicks` by name via `BindingFlags.NonPublic | Static`
  (#14's entry covers the same reflection pattern) — changing that method's accessibility or
  signature to share it directly would have broken an existing test for a reason unrelated to what
  it actually checks, so the fix keeps the private method as a thin forwarding shim instead. Pitfall:
  this only stays safe as long as `ComputeTravelTicks`'s name and static-method shape never change;
  a reflection-based test has no compile-time link to the method it invokes, so a rename silently
  turns a passing test into one that throws `NullReferenceException` on a missing `MethodInfo`
  rather than a compile error pointing at the actual call site.
- **`List<T>.Sort` with a comparator that always breaks ties on a unique key** — `AiBrain.TryAttack`
  (`src/MW3.Core/AiBrain.cs`) sorts candidate sources by
  `b.GarrisonCount.CompareTo(a.GarrisonCount)` falling back to `a.Id.CompareTo(b.Id)` whenever the
  garrison comparison returns zero, and does the same (by distance, then id) for targets. Why here:
  the acceptance criteria require the heuristic to be "deterministic with no reliance on collection
  ordering" — `List<T>.Sort` is not guaranteed stable, so relying on it to preserve insertion order
  for equal-garrison bases would be relying on an implementation detail `List<T>`'s own documentation
  doesn't promise; breaking every tie on `Id` (which is unique per base) makes the comparator itself
  total, so the sort's stability no longer matters. Pitfall: a comparator that can return `0` for two
  genuinely distinct elements is only safe if *every* caller of `Sort` on that data additionally
  tolerates arbitrary relative order among those equal elements — the moment a second sort call
  reuses a partial comparator (garrison only, no id fallback) elsewhere in the same file, its result
  becomes silently ordering-dependent again.
- **Reusing the "walk to the next boundary tick, act, repeat" pattern for a new kind of boundary** —
  `MatchRunner.Advance` (`src/MW3.Core/MatchRunner.cs`) computes `NextDecisionTickAfter` and calls
  `_match.Advance` up to exactly that tick before consulting the brain, then loops — structurally the
  same shape `Match.Advance` itself already uses internally to walk to the next arrival tick before
  resolving combat (#14's determinism-bug entry). Why here: FR-6's requirement ("every decision tick
  is hit exactly once, however the caller chunks ticks") is the identical determinism problem #14
  solved for arrivals, just for a different kind of event — recognizing the shape let the runner
  reuse "measure the next boundary from absolute elapsed state, not from anything the caller tracks"
  rather than re-deriving a chunking-safe algorithm from scratch. Pitfall: this pattern's safety
  depends entirely on always computing the next boundary from `Match.ElapsedTicks` (state that
  advances monotonically and is the same regardless of chunking) rather than from a counter the
  runner maintains itself — a runner-local "ticks since last decision" counter would look equivalent
  in a single-call test but silently double-count or skip boundaries under irregular chunking, since
  it wouldn't reset consistently across calls.
- **A code-review catch: a computed value used downstream without a floor check** — the reviewer
  caught that `AiBrain`'s largest-garrison source selection (`TryDefend`/`TryConsolidate`,
  `src/MW3.Core/AiBrain.cs`) had no lower bound on `candidate.GarrisonCount`, so a base left at
  exactly zero by a repelled N==M tie (`Match.ResolveArrival`, `Match.cs`) could still be chosen as a
  reinforcement source, producing a `SendArmyCommand` that `Match.Execute` would reject
  (`UnitCountExceedsGarrison`) — the fix adds `candidate.GarrisonCount > 0` (or `<= 0` as an exclusion)
  at both call sites. Why this is worth recording as a concept rather than just a bug: it's the same
  shape as `MatchScreen.HandleDrag`'s existing `if (unitCount <= source.GarrisonCount)` guard for the
  human's send — a value computed from live state (`garrison / 2`, clamped to a minimum of 1) still
  needs validating against the *other* live value (available garrison) before it crosses into a
  command, and the asymmetry between the human path (which had this check) and the AI path (which
  didn't) is exactly what made the gap easy to miss by inspection alone. Pitfall: none of the existing
  tests caught this because they all kept the human fully passive, so no AI base was ever driven down
  to exactly zero while still owned — a property this specific (a repelled tie leaving a nonzero
  owner at a zero garrison) needed a test that deliberately constructs that state, not one that
  merely runs a long match and hopes to stumble into it.

Try next: `MatchRunner.Advance`'s boundary-walking loop and `Match.Advance`'s arrival-walking loop are
now two independent implementations of the same shape (`docs/CONVENTIONS.md`'s "three similar lines is
better than a premature abstraction" would say that's fine at two call sites) — if FR-7's outcome-freeze
work (the next issue) introduces a *third* kind of boundary tick to walk to, that's the point to sketch
whether a small shared "walk to next boundary" helper actually pays for itself, or whether it's still
just two-and-a-bit call sites that don't yet justify one.

## 2026-07-27 — #25 Victory and defeat end the match and return to the welcome screen
Concepts: a monotonic-invariant argument used to justify reflection-constructing an unreachable state, reflecting into an internal property *setter* (not just a private method), a "frozen" guard repeated at every layer boundary, an interface seam growing a second method as a new capability appears, capturing press-time context to decide release-time behavior
- **Proving a state is unreachable through the public API before reflecting past it** —
  `MatchOutcomeTests.Outcome_SimultaneousElimination_DefeatTakesPrecedence` (`tests/MW3.Core.Tests/MatchOutcomeTests.cs`)
  constructs "both players own zero bases" directly via reflection, rather than driving there through
  ordinary `SendArmyCommand`s. Why here: a capture (`Match.ResolveArrival`, `src/MW3.Core/Match.cs`)
  always transfers ownership *to* the attacker, never to neither player, so the combined
  human-plus-AI owned-base count can only stay the same or grow from its starting value of 2 — it
  can never reach 0 for both at once through legitimate play. That's a genuine mathematical proof
  (documented as D-20 in `docs/core-gameplay-loop/ARCHITECTURE.md`), not a hunch, and it's what
  justifies reaching for reflection here instead of treating an unreachable test as a smell to
  eliminate. Pitfall: this kind of argument only holds as long as the invariant it rests on does —
  if a later feature ever added a way for a base to revert to neutral (a "raze" command, say), the
  proof would silently stop applying, and this test would need re-justifying, not just re-passing.
- **Reflecting into a property's *setter*, not just calling a private method** — the same test uses
  `typeof(Base).GetProperty(nameof(Base.Owner))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { null })`
  to null out `Base.Owner` (declared `internal set` specifically so nothing outside `Match` can do
  this normally). This extends the reflection pattern already used elsewhere in this codebase for
  private *methods* (`Match.ComputeTravelTicks` in `SendArmyTests`, `HitTester.FindNearestBaseId` in
  `HitTesterTests`) to a private *setter* instead. Why here: there's no other way to construct this
  exact state, since every public path (`Execute`, `Advance`) enforces the invariant above. Pitfall:
  `GetSetMethod(nonPublic: true)` returns `null` if the property has no setter at all (not even a
  private one) — calling `.Invoke` on that `null` throws `NullReferenceException` with a message that
  gives no hint the property itself was the problem, so a rename or a switch to a computed-only
  property would fail this test in a confusing way rather than a clear one.
- **The same "once decided, do nothing" guard repeated at three layers on purpose** —
  `Match.Advance` and `Match.Execute` (`src/MW3.Core/Match.cs`) both check `Outcome != InProgress`
  and return/reject immediately, and `MatchRunner.Advance` (`src/MW3.Core/MatchRunner.cs`) checks it
  *again* before consulting the brain. Why here: each layer owns a different consequence of being
  frozen — `Match` itself must never let state drift after a decision (D-13), while `MatchRunner`
  must never take an *action* (asking the brain, submitting its answer) even though the underlying
  `Match.Advance` call it makes would already no-op harmlessly on its own. Duplicating the check
  isn't redundancy here: removing the `MatchRunner`-level check would still be *correct* (no state
  would change) but would waste a brain consultation every single tick forever after a match ends,
  for a match a screen might legitimately keep advancing (e.g. while the player looks at the result).
  Pitfall: three independent checks of the same condition is exactly the kind of duplication that
  looks removable at a glance — removing the "wrong" one (the one that only saves work rather than
  preventing a state bug) would pass every functional test while quietly reintroducing pointless work.
- **`IScreenNavigator` growing from one method to two, as a genuinely new capability appears** —
  `Push` (phase 1) was the interface's only member until this feature added `Pop()`
  (`src/MW3.Game/IScreenNavigator.cs`), because until now no screen ever needed to dismiss *itself*
  (back requests were handled centrally in `ScreenManager.Update`, invisible to any screen). Why
  here: `MatchScreen`'s new dismissal rule (pop on a release, not just on a back request) genuinely
  needs a screen-initiated pop, which back-request handling structurally can't provide (it runs
  *before* a screen's own `Update`, so a screen can never trigger it from inside that method).
  Pitfall: growing a seam interface only when a real caller needs the new member (rather than
  speculatively) keeps `ScreenManager`'s two pop paths (`Update`'s back-request branch and the new
  `Pop()` method) doing textually similar but conceptually distinct things — collapsing them into one
  code path later would need to preserve the "don't pop the last screen" guard in both callers, not
  just one.
- **Capturing decision-time context at press, reading it back at release** —
  `MatchScreen.HandleInput`'s `_pressBeganAfterOutcomeDecided` field (`src/MW3.Game/MatchScreen.cs`)
  is set once, at the press edge, from whatever `_match.Outcome` is *at that instant*, and is never
  re-evaluated later. Why here: the acceptance rule is specifically about when the *press* began, not
  when the *release* happens — by the time a release is processed, `Outcome` might already differ
  from what it was at press time, so the only correct way to answer "did this press begin after the
  decision" is to snapshot the answer when it's still true and carry it forward, extending the same
  edge-detection idea `WelcomeScreen`'s `_pressStartedInsideButton` used in #9 to a condition that
  isn't spatial (inside a button) but temporal (before or after an event). Pitfall: exactly like
  `_wasPointerPressed` in #9, forgetting to update this field on every press edge (not just the ones
  that end up mattering) would let a stale decision leak into a later, unrelated press.

Try next: `MatchRunner.Advance` and `Match.Advance` are now two independent "stop once frozen" checks
guarding one property (`Outcome`) — if a third layer ever needs the same guard (a hypothetical
multiplayer session wrapper, say), that's the point to reconsider whether `Match` should expose a
single `ThrowIfDecided()`-style helper instead of three call sites each re-deriving the same
condition, the same "two is fine, watch for three" judgment call this log has hit before (#14's
segment-walking entry, revisited by #24's boundary-walking entry).

## 2026-07-28 — #30 Garrison caps, base levels, and the upgrade command
Concepts: readonly structs as allocation-free multi-value returns, integer division/modulo as carried state, computed properties with no backing field, XML `cref` and overload resolution, record equality vs null in guard clauses

- **`readonly struct` as an allocation-free multi-value return** — a value type whose fields cannot
  change after construction, so it lives on the stack rather than the heap. Why here:
  `ProductionCalculator.Advance` has to return *two* things — a garrison and a progress counter —
  for every owned base on every advance, which on the Android head is every frame. A class would
  allocate six objects per frame and hand the GC a steady drip on the target platform;
  `ProductionState` (`src/MW3.Core/ProductionState.cs:9`) allocates nothing. The alternative,
  `out` parameters, works but reads badly at the call site and can't be composed. Pitfall: the
  "no allocation" guarantee is fragile — box the struct (assign it to `object`, store it in a
  non-generic collection, capture it in a lambda, or expose it through an interface) and the
  allocation comes back silently, with nothing in the type system to warn you. `readonly` also
  matters for a second reason: without it, the compiler makes defensive copies whenever the struct
  is accessed through a `readonly` field, so the "cheap" type quietly gets more expensive.

- **Integer division and modulo as carried state** — using `/` and `%` (here spelled as
  `available - produced * period`) to split a running total into "whole units earned" and "remainder
  to carry". Why here: production had to survive being chunked arbitrarily — `Advance(100)` and a
  hundred `Advance(1)` calls must agree exactly (D-12). Storing the *remainder* rather than a
  timestamp is what makes that fall out of the arithmetic instead of needing a special case:
  `var produced = availableTicks / period` then carry `availableTicks - (produced * period)`
  (`src/MW3.Core/ProductionCalculator.cs:49`). Pitfall: C# integer division truncates *toward zero*,
  not toward negative infinity, so `-1 / 10 == 0` and `-1 % 10 == -1` — carry arithmetic that can
  ever see a negative operand will drift. This code is safe only because the span is guarded
  non-negative first; the guard is load-bearing, not decoration. Second pitfall in the same line:
  `produced` is `long` and the garrison is `int`, so the `(int)` cast is only sound because the
  branch it sits in has already proved `produced < room ≤ 50`.

- **Computed property with no backing field** — `public int GarrisonCap => LevelTable.GarrisonCap(Level);`
  (`src/MW3.Core/Base.cs:49`). Why here: the cap is *derived* from the level, so storing it would
  create two facts that can disagree — and an upgrade would have to remember to update both. As an
  expression-bodied getter it cannot drift, and it satisfies "no public setter" for free because
  there is nothing to set. Pitfall: it recomputes on every read, including inside per-tick loops;
  that is fine for an array index but would not be for anything expensive. A subtler one showed up
  in the tests — you cannot set a computed property by reflection, which is why the demotion tests
  set `Level` and let the cap follow, rather than setting the cap directly.

- **XML `cref` and overload resolution** — adding `Execute(UpgradeCommand)` alongside
  `Execute(SendArmyCommand)` turned every existing `<see cref="Execute"/>` into error CS0419
  ("ambiguous reference"), and with `-warnaserror` that is a *build failure* in five files that had
  nothing to do with the change. The fix is to name the parameter types:
  `<see cref="Execute(SendArmyCommand)"/>` (`src/MW3.Core/Match.cs:56`). Why worth knowing: it is
  the one case where documentation is compiled, so doc comments rot loudly instead of silently —
  which is a feature, but it means adding an overload is never a purely additive change. Pitfall:
  the error points at the *doc comment*, not at the new overload that caused it, so the first
  instinct is to fix the comment rather than to notice that a second method just appeared.

- **Record equality vs `null` in a guard clause** — `Player` is a `record`, so `==` and `!=` are
  value equality, and `null == null` is `true`. That made `target.Owner != command.IssuingPlayer`
  return `false` when *both* were null — so a command with a null issuing player passed the
  ownership gate on a neutral base, whose owner is legitimately absent. Caught in review; fixed by
  rejecting the null issuer up front (`src/MW3.Core/Match.cs:83`, `:152`). Why here: this codebase
  deliberately models neutrality as the *absence* of an owner (D-11) rather than a sentinel id, and
  the cost of that otherwise-good decision is that `null` is a meaningful value on one side of the
  comparison — so any equality test against an owner has to say what it means when both sides are
  absent. Pitfall: the bug was invisible because it was unreachable in practice (neutral garrisons
  never reached the upgrade cost), which is exactly the kind of latent hole a tuning change turns
  into a real one months later.

Try next: `ProductionCalculator.Advance` is now the single source of production arithmetic, shared by
`Match` and `AiBrain` — the review's brute-force check (every level × garrison × progress × chunk
pair, ~1.5M cases, asserting `Advance(Advance(s,n1),n2) == Advance(s,n1+n2)`) is a *property test*
written by hand. Try expressing that same property with a generative testing library (FsCheck or
CsCheck both work with xUnit) and compare: the hand-rolled loop is exhaustive over a bounded space
and dead simple to read, the generative one is shorter and shrinks failures to a minimal case but
only samples. Knowing which of those two you want is the actual skill; this feature is a good place
to feel the difference, because the bounded space here is genuinely small enough to enumerate.

## 2026-07-28 — #32 Tap an owned base to open an action menu offering upgrade
Concepts: records as pure-answer DTOs, enums as a closed set replacing a bool/exception, cached
recomputation gated on a dirty check, a small explicit state machine over boolean flags
- **A `record` as the whole answer, not a partial one** — `BaseAction.cs`:
  `public sealed record BaseAction(BaseActionKind Kind, int Cost, BaseActionAvailability Availability);`.
  Why here: D-25 says the widget must decide nothing, so `Match.AvailableActions` has to hand back
  everything a caller could possibly need to render a button — kind, cost, *and* whether it's
  affordable — as one immutable value, rather than the widget re-deriving `Cost < Garrison` itself.
  A record's structural equality is what makes `Assert.Equal` in `AvailableActionsTests.cs` compare
  by value with no custom `Equals`. Pitfall: it's tempting to hand back a `bool CanAfford` instead of
  a full `BaseActionAvailability` enum, which looks equivalent until "at max level" needs a *third*
  state the caller must still special-case — the enum was chosen precisely to close that door.
- **An enum closing off a set exceptions or bools would leave open** —
  `BaseActionAvailability { Affordable, GarrisonBelowCost, AlreadyAtMaxLevel }`. Why here: the
  acceptance criteria explicitly forbid "never a bool and never an exception" for this exact reason —
  a `bool` can't distinguish "too poor to afford" from "already maxed, nothing to afford," and an
  exception for an expected, everyday state (a base at max level) would violate
  `docs/CONVENTIONS.md`'s "exceptions for exceptional conditions, not control flow." Pitfall: an enum
  switch that isn't exhaustive fails silently at compile time unless every call site pattern-matches
  or the compiler is told to warn on missing cases — `BaseActionMenu.FormatLabel` only handles two of
  the three values explicitly and falls through to a default branch for the rest, which is correct
  today but would misrender silently if a fourth availability value were ever added without touching
  every switch.
- **Recompute-on-change instead of recompute-every-frame** — `BaseActionMenu.Refresh()`
  (`src/MW3.Game/BaseActionMenu.cs`) caches `_lastGarrisonCount`/`_lastLevel` and only calls back into
  `Match.AvailableActions` (and reformats the label strings) when either actually changed since the
  last check. Why here: `docs/CONVENTIONS.md`'s "frame-loop code allocates nothing per frame" rule
  means formatting a string every `Draw` call is a defect, not a style nit — a menu sitting open for
  thousands of ticks must not allocate for frames where nothing changed. Pitfall: the dirty check
  compares the two fields the query result depends on, not the query result itself — if a future
  action's availability ever depended on some *third* piece of base state, `Refresh` would need a
  third comparison, and it is easy to add a new dependency to the Core query without remembering to
  extend the cache-invalidation check that guards it in the widget.
- **A handful of booleans as an explicit (if informal) state machine** —
  `MatchScreen.HandleInput`/`HandlePressWhileMenuOpen` track `_pressBeganOnMenuButtonIndex`,
  `_pressBeganOnGreyedMenuButton`, and `_pressDismissedMenuOnThisPress`, each set once on press and
  read once on the matching release, to encode "what kind of gesture is this" without a formal
  state-machine type. Why here: the acceptance criteria distinguish four outcomes for the *same*
  physical gesture (a press-then-release) purely by where the press landed, so the decision has to be
  captured at press time and carried forward — checking live state again at release time would let
  the world change (menu dismissed, base recaptured) underneath the gesture and answer the wrong
  question. Pitfall: every one of those flags must be reset on *every* new press, not only when it's
  about to be reused — an easy bug is adding a fifth flag for a future gesture and forgetting to
  clear it in the shared reset block, so it silently carries a stale value from two gestures ago.

Try next: `BaseActionMenu.Refresh()`'s dirty check is hand-rolled (two `int` comparisons). Look at
how `MatchScreen` already solves the same problem for garrison text (`_lastGarrisonCount` per base
index) and army text (a `Dictionary<int, string>` keyed by army id) — three different shapes of the
same "don't reformat what hasn't changed" idea in one file. Try sketching a small generic
`ChangeGate<TKey, TState>` that any of the three could use, and notice why it's harder than it looks:
the army cache also needs *pruning* when an army resolves, which the other two don't.

to feel the difference, because the bounded space here is genuinely small enough to enumerate.

## 2026-07-28 — #34 Tower base type: conversion between producer and tower in the core rules
Concepts: skip-at-the-source vs. compute-then-clamp to enforce an invariant, extending an established command/outcome pattern to a third case, orthogonal mutations of the same field from two independent rules, a reflection-based whitelist test as an executable contract
- **Skipping the computation entirely, rather than computing and clamping the result** —
  `Match.ApplyProduction` (`src/MW3.Core/Match.cs`) changed its per-base guard from
  `if (b.Owner is null) continue;` to `if (b.Owner is null || b.Type == BaseType.Tower) continue;`,
  rather than letting `ProductionCalculator.Advance` run for a tower and then zeroing its result
  afterward. Why here: the acceptance criterion is "production progress is zero at every tick, not
  merely frozen at a value" — if the calculator ran and something later reset the field, there would
  be a real (if instantaneous) moment where progress held a nonzero number, and any future refactor
  that removed the reset would silently reintroduce banked progress. Skipping the call is what makes
  "always zero" true by construction instead of true by convention. Pitfall: this only works because
  `ProductionCalculator` is stateless and side-effect-free — the same trick on a method with side
  effects (logging, mutating a second field) would silently skip those too, not just the value you
  meant to suppress.
- **A third command/outcome pair extending, not inventing, the shape** — `ConvertCommand`/
  `ConvertOutcome` (`src/MW3.Core/ConvertCommand.cs`, `ConvertOutcome.cs`) are a positional `record`
  plus a closed `enum`, the same two-type shape `UpgradeCommand`/`UpgradeOutcome` and
  `SendArmyCommand`/`SendArmyOutcome` already established. Why here: `docs/base-upgrades-and-types/
  ARCHITECTURE.md`'s "one command type family for humans and AI" convention means a third command
  has to look exactly like the first two or the pattern itself stops being trustworthy — a caller
  learns the shape once ("submit through `Match.Execute`, get back a rejection reason, never a
  bool") and can then guess correctly at a type it has never seen. Pitfall: mechanically copying an
  existing pattern is only safe if the *reasons* for its shape still apply — `ConvertCommand` needed
  a `BaseType TargetType` field that neither sibling has, because "convert" (unlike "upgrade" or
  "send") is meaningless without saying *to what*; a pattern followed too literally would have
  modeled it as a parameterless toggle instead, which silently breaks idempotent replay (submitting
  the same stale command twice would flip the type back and forth instead of doing the same thing
  twice).
- **Two independent rules writing the same field for different reasons, kept orthogonal** — `Level`
  is reset by two unrelated code paths: `Match.Execute(ConvertCommand)` sets it to
  `LevelTable.MinLevel` unconditionally (a conversion burns the whole ladder, D-22's tuning
  decision), while `Match.ResolveArrival`'s capture branch decrements it by exactly one, floored at
  the minimum (D-23's demotion rule). Why here: the acceptance criteria explicitly call out that the
  conversion reset is "independent of D-23's capture demotion" — a level-3 base converted loses all
  three levels, but the same base captured loses only one, and the two rules coexist correctly only
  because each is written once, at its own call site, with no shared helper trying to unify "reset
  vs. decrement" into one parameterized method. Pitfall: the temptation to write a single
  `AdjustLevel(base, delta)` helper here would make the two call sites shorter but would hide the
  fact that they are answering genuinely different questions ("what level does a fresh tower start
  at" vs. "how much does losing a fight cost") — a future change to one rule's amount could then
  silently affect the other if the helper's parameter were ever miscopied between call sites.
- **A reflection-based whitelist test as an executable contract, not a formality** —
  `BaseTests.PublicSurface_ExposesOnlyTheAgreedMembers_NoneSettableFromOutsideAssembly`
  (`tests/MW3.Core.Tests/BaseTests.cs`, introduced in #8's entry) failed the moment `Base.Type` was
  added, listing the exact property name missing from its expected array — this is the same
  reflection pattern #8 covered, encountered again from the *other* side: not writing the test, but
  being caught by one already in place. Why worth logging: the failure message
  (`Expected: [...] Actual: [..., "Type"]`) pointed at the fix directly (add `nameof(Base.Type)` to
  the array) with no debugging required, which is what "a whitelist test as a contract" is supposed
  to feel like in practice — a `-warnaserror` build failure often points at symptoms one step removed
  from the cause, but this test's failure output *is* the diff. Pitfall: a test like this has to be
  updated in the same commit as the property it's guarding, and nothing enforces that ordering except
  the gate itself — skipping a local `dotnet test` run before committing would have shipped a red
  gate to CI for a one-line, entirely mechanical omission.

Try next: `ApplyProduction`'s guard is now `b.Owner is null || b.Type == BaseType.Tower` — two
independent conditions checked with `||` at one call site. FR-4 adds tower fire, which will need the
opposite selection ("every owned tower", not "every non-tower") over the same `_bases` list on every
tick. Sketch whether that's a third inline condition at a new call site (consistent with this
feature's choice not to introduce a shared "bases matching X" helper) or whether tower fire's
per-tick cost characteristics (evaluated against every in-flight army, not just once per base) make
that call different enough to justify a small filtered-view helper this feature didn't need.

## 2026-07-29 — #38 Realign the level ladder, caps, costs, and tick rate onto MW2's numbers
Concepts: nullable value types (`int?`), switch expressions over an enum, nested static classes as namespacing
- **Nullable value types (`int?`)** — why here: MW2 publishes no unit-capacity column for a tower, so "this base's cap" had to become a value that is genuinely *absent* for one `BaseType` rather than present-and-zero. `LevelTable.GarrisonCap(BaseType type, int level)` returns `int?`, and `Base.GarrisonCap` follows suit (`src/MW3.Core/LevelTable.cs:159-164`, `src/MW3.Core/Base.cs:59-60`). Every reader then has to say so explicitly: `MatchScreen.cs` renders it as `cap is int capValue ? capValue.ToString(...) : "none"` rather than defaulting a missing value to `0` (which would silently read as "already full"). Pitfall: a nullable int still participates in lifted comparison operators (`neutral.GarrisonCount >= neutral.GarrisonCap` compiles and works correctly when the RHS is `int?`), which is convenient right up until someone reaches for `.Value` or an unboxing cast without checking `HasValue`/pattern-matching first — that compiles too, and only fails at runtime on a tower.
- **Switch expressions over an enum** — why here: `LevelTable`'s type-taking overloads (`GarrisonCap`, `UpgradeCost`, `MaxLevel`, `MaxUpgradableLevel`, `RingThicknessFractionOfRadius`) all dispatch on `BaseType` with the same shape: `type switch { BaseType.Producer => ..., BaseType.Tower => ..., _ => throw ... }` (`src/MW3.Core/LevelTable.cs:132-188`). Using an expression (not a statement) keeps each method a one-line return with no local variable, and the `_ => throw` arm makes an unhandled case a hard failure rather than a silent default. Pitfall: because `BaseType` is a plain `enum`, nothing stops a caller from casting an out-of-range `int` into it and hitting that `_` arm at runtime — the exhaustiveness is enforced by the arm you wrote, not by the compiler, unlike a real discriminated union.
- **Nested static classes as namespacing, not encapsulation** — why here: `LevelTable.Village` and `LevelTable.Tower` (`src/MW3.Core/LevelTable.cs:41-125`) group each ladder's own constants and lookup methods under a name a caller can read directly (`LevelTable.Village.MaxUpgradableLevel`) while the type-taking wrapper methods on the outer `LevelTable` pick between them for callers that only have a `BaseType`, not a compile-time choice of ladder. Pitfall: nesting reads like it buys encapsulation, but a nested static class has full access to the outer type's private members and vice versa — it is purely an organizational device here, not an access-control boundary, so it doesn't stop a future change from reaching across the two ladders in ways the split was meant to discourage.

Try next: `LevelTable.MaxLevel(BaseType)` and `LevelTable.MaxUpgradableLevel(BaseType)` are two distinct switch expressions with the same shape, one row apart in the file. Sketch what a table-driven alternative would look like — e.g. a `readonly record struct LadderDescriptor(int MaxLevel, int MaxUpgradableLevel, ...)` per `BaseType`, looked up once — and think through whether it's actually clearer than five parallel switches, or whether the switches are more readable precisely because each one only has to answer one question.

## 2026-07-29 — #39 Levels buy defence; combat resolves by MW2's Bu = (a/d) x Wu
Concepts: `readonly record struct` for a value-type result, widening to `long` to guard against overflow, ternary clamps instead of `Math.Max`/`Math.Min`
- **`readonly record struct` for a value-type result** — why here: `CombatResolver.Resolve` needed to hand back two related facts (did the base fall, and what garrison is left standing) without allocating, since it runs on every arrival and the no-allocation rule binds combat exactly as it binds tower fire. `CombatResult` (`src/MW3.Core/CombatResult.cs:6`) is declared `public readonly record struct CombatResult(bool Captured, int RemainingGarrison)` — `record` gives it structural equality and a generated `ToString()` useful in test failure output, `struct` keeps it stack-allocated, and `readonly` documents that `Resolve` builds it once and hands back a finished value rather than a mutable scratch object a caller might poke at. Pitfall: a `record struct`'s positional properties are still copied by value on every assignment or parameter pass — for a two-`int`-sized type like this one that's free, but the same pattern on a record struct with several large fields would silently start copying more bytes than a class reference ever would, right when a reviewer expects "record" to mean "cheap to pass around".
- **Widening to `long` before multiplying, to guard against overflow** — why here: the capture decision is `waveUnits × attackerIndex > defendingGarrison × defenderIndex`, and both sides are ordinary `int`s from `SendArmyCommand.UnitCount` and `LevelTable`'s percentage columns. `CombatResolver.Resolve` casts each operand explicitly before multiplying — `var attackPower = (long)waveUnits * attackerIndex;` (`src/MW3.Core/CombatResolver.cs:57`) — so the multiplication happens in 64-bit arithmetic even though every input and the eventual comparison stay well inside `int` range for this game's numbers. Pitfall: `(long)waveUnits * attackerIndex` widens before multiplying because the cast binds to the left operand first; writing `waveUnits * (long)attackerIndex` works too, but `(long)(waveUnits * attackerIndex)` would still overflow in 32-bit `int` arithmetic *before* the cast ever runs — the cast has to be on an operand of the multiplication, not on its result.
- **Ternary clamps instead of `Math.Max`/`Math.Min`** — why here: the acceptance criteria specify an exact floor for each branch — capture's remainder floors at a minimum of 1, hold's remainder floors at a minimum of 0 — and `Resolve` writes both as inline ternaries at the point the value is returned: `remaining < 1 ? 1 : remaining` and `held < 0 ? 0 : held` (`src/MW3.Core/CombatResolver.cs:60-64`), rather than reaching for `Math.Max(1, remaining)` / `Math.Max(0, held)`. Both read equivalently here, but the ternary keeps the clamp's *reason* — "this specific rule never goes below X" — visible as a comparison at the call site instead of behind a general-purpose library call a reader has to already know the semantics of. Pitfall: `Math.Max` and a `< low ? low : value` ternary are only interchangeable when there's exactly one boundary; the moment a clamp needs both a floor and a ceiling, the ternary chain gets harder to read than `Math.Clamp(value, low, high)`, which is the point past which reaching for the library function stops being over-abstraction and starts being the clearer choice.

Try next: `CombatResolver.ComposePercentages` multiplies three percentages and divides once by `100 * 100` (`src/MW3.Core/CombatResolver.cs:80`), which only stays exact today because two of the three factors are pinned at the identity value 100. Sketch what happens numerically once G-1 (morale) supplies a real percentage alongside G-6 (forge) — does dividing once at the end versus dividing after each multiplication change any result, and at what input sizes would the intermediate `long` product from three genuinely-variable percentages risk losing precision or overflowing before the final division?

## 2026-07-29 — #40 Build time for upgrades and conversions, and the one-second recapture grace
Concepts: abstract record hierarchy as a poor man's discriminated union, type-pattern switch expressions over records, nullable reference type for optional aggregate state
- **Abstract record hierarchy as a poor man's discriminated union** — why here: a base under construction is becoming *either* a new level *or* a new type, and C# has no built-in sum type to say "exactly one of these two shapes, never both, never neither." `PendingConstruction` is declared `public abstract record PendingConstruction(long CompletionTick)` (`src/MW3.Core/PendingConstruction.cs:12`), with `PendingUpgrade(long CompletionTick, int TargetLevel)` and `PendingConversion(long CompletionTick, BaseType TargetType)` as its only two sealed subtypes, each in its own file per the one-public-type-per-file convention. This is what let `Match.CompleteConstructionsAtTick` (`src/MW3.Core/Match.cs`) avoid a single record with both a nullable `TargetLevel` and a nullable `TargetType` — nullable-both would have let a caller construct (or a bug produce) a state with neither or both set, which is exactly the "model absence in comments instead of in the type system" pattern `docs/CONVENTIONS.md` forbids. Pitfall: C# still won't stop a third subtype from being added later without every existing `switch` over the hierarchy being revisited — there's no `sealed`-on-the-base-type exhaustiveness check the way a real union type would give you; the `_ => ...` default arm in every switch is doing that job by hand, and it's easy to reach for a generic default there instead of a deliberate "unreachable" throw.
- **Type-pattern switch expressions over records** — why here: reading which subtype a `PendingConstruction?` holds, and pulling its subtype-specific data out, happens in three unrelated places — completing a build (`Match.cs`'s `case PendingUpgrade upgrade: ... case PendingConversion conversion: ...`), and formatting the `--dump-state` line (`src/MW3.Game/MatchScreen.cs`'s `b.Construction switch { null => "Building=none", PendingUpgrade upgrade => ..., PendingConversion conversion => ... }`). The switch pattern binds a typed local (`upgrade`, `conversion`) directly from the type test, so there's no separate cast or `as` + null-check step. Pitfall: pattern-matching on a `record`'s *type* is a runtime `is`-check under the hood, not a compile-time exhaustiveness guarantee — swapping `PendingUpgrade`/`PendingConversion` for a third sibling type compiles cleanly everywhere and just silently falls through to whatever the `_`/default arm does, which is why every one of these switches needed its own explicit "what if it's neither" decision rather than assuming the two known cases are the only ones forever.
- **Nullable reference type for optional aggregate state** — why here: `Base.Construction` is `PendingConstruction? Construction { get; internal set; }` (`src/MW3.Core/Base.cs`) — null *is* the "not building anything" state, rather than a sentinel object or a separate `bool IsUnderConstruction` flag that could drift out of sync with a "construction details" field. Every reader (`Match.Advance`'s boundary/completion logic, `MatchScreen`'s dump and draw code, `BaseActionMenu`'s greying) tests `is not null` / pattern-matches rather than trusting a paired flag. Pitfall: because `Construction` has an `internal set`, nothing in `MW3.Core` itself stops two different call sites from writing to it without checking the current value first — the actual "only one build at a time" rule lives entirely in `Match.Execute(UpgradeCommand)`/`Execute(ConvertCommand)`'s explicit `if (target.Construction is not null) return ...UnderConstruction;` guard, not in the type of `Construction` itself; the nullable field only models *that* a base can be idle, not that starting a second build while one is active is illegal.

Try next: `Match.EarliestBoundaryTickUpTo` now merges two independent "what's the next interesting tick" queries — army arrivals and construction completions — by computing both and taking the smaller (`src/MW3.Core/Match.cs`). Sketch what a third such source (tower fire's own per-tick evaluation, once FR-4 lands) would need to look like here: does it fit the same "compute earliest tick, take the min" shape, or does tower fire's "must run every tick, not just at a computed boundary" requirement (stated explicitly in issue #36) mean this merge point can't simply grow a third term the same way it grew from one to two?

## 2026-07-29 — #36 Towers shoot enemy armies passing within range, in the core rules
Concepts: converting a record to a mutable class with an internal setter, a tuple as a dictionary value to pair cached text with its cache key, hybrid closed-form/per-tick control flow
- **Converting a `record` to a mutable `class` with an internal setter** — why here: `Army` used to be `public sealed record Army(int Id, ..., int UnitCount, ...)` on the premise that nothing in flight ever changes (`src/MW3.Core/Army.cs`, phase 2). This feature makes `UnitCount` mutable state a tower can decrement, and C#'s positional-record syntax only generates `init`-only properties - there is no way to make one property in a record's primary constructor settable later without hand-writing the whole property list anyway. The rewrite keeps every other property `{ get; }` (still immutable) and gives only `UnitCount` an `internal set`, mirroring `Base.GarrisonCount`'s existing pattern rather than inventing a new one. Pitfall: switching from `record` to `class` silently drops structural equality and the generated `ToString()` - any code relying on `army1 == army2` comparing values instead of references, or printing an army in a test failure message, changes behaviour with no compiler warning to catch it.
- **A tuple as a dictionary value, to pair cached text with the value it was formatted from** — why here: `MatchScreen`'s per-army label cache used to be `Dictionary<int, string>`, valid only because the count it displayed never changed. Now that it can, the cache needs to know not just the text but *what count produced it*, so it can tell "still current" from "stale" without reformatting every frame to check. `Dictionary<int, (string Text, int Count)>` (`src/MW3.Game/MatchScreen.cs`) stores both together, and the read site does `if (!cache.TryGetValue(id, out var cached) || cached.Count != army.UnitCount)` to decide whether to refresh. Pitfall: a named tuple's field names (`Text`, `Count`) exist only at the call site that declared them - the dictionary's static type is `Dictionary<int, (string, int)>` underneath, so a second piece of code reading the same dictionary without importing the same names sees `.Item1`/`.Item2` instead, silently losing the readability the names were there for.
- **Hybrid closed-form/per-tick control flow, chosen per segment rather than for the whole call** — why here: `Match.Advance` normally jumps straight to the next "boundary" tick (an arrival or a construction completing) and applies production in one closed-form call across the whole gap - correct because nothing else can change state in between. Tower fire breaks that assumption: it can hit on *any* tick, not just boundaries, so the acceptance criteria required evaluating it every tick once a tower exists. The first implementation over-corrected by switching to a fully per-tick loop for the rest of the match the moment any tower existed, which also dragged production down to being recomputed on every single tick - a real regression a code-review pass caught, since `docs/CONVENTIONS.md`'s per-tick-allocation rule and this issue's own "production stays closed-form; fire does not" criterion both existed to prevent exactly that. The fix (`src/MW3.Core/Match.cs`) keeps the boundary-jumping structure and inserts a cheap interior sweep - `EvaluateTowerFireAtTick` only, no production call - for the ticks strictly between the current position and the next boundary, so production is still applied once per segment in closed form regardless of whether a tower exists. Pitfall: once a computation is split across two code paths that are supposed to produce the same *ordering* of side effects (here: construction completion always before that tick's own fire check, arrivals always after), it becomes very easy for one path to evaluate a boundary tick's event twice or not at all when the two paths meet - which is exactly what the dedicated `ABaseConvertingToATower_FiresOnTheExactTickItsBuildCompletes` test in this diff was written to catch, and did catch, once.

Try next: `Match.EvaluateTowerFireAtTick` and `Match.PositionAtTick` are both private instance methods with no dependency on anything outside the single tick and army they're given. Sketch what it would take to pull the geometry math (`PositionAtTick`'s interpolation, and the distance-and-range comparison in `EvaluateTowerFireAtTick`) out into a small internal static helper class the way `TravelTimeCalculator` and `HitTester` already separate their own single-purpose geometry from `Match` - would it make the ordering bug from this feature easier or harder to have caught with a focused unit test, compared to only being reachable through a full `Match`?

## 2026-07-29 — #46 BaseActionMenu's cache invalidation will miss a construction/type change once convert is wired up
Concepts: `<InternalsVisibleTo>` as an MSBuild item, reflection to invoke a non-public property setter, `PrivateAssets="All"` blocking transitive `PackageReference` flow
- **`<InternalsVisibleTo>` as an MSBuild item** — why here: `BaseActionMenu` is `internal sealed class` (D-25 - the widget decides nothing, so it was never meant to be a public surface), but its own change-detection cache in `Refresh()` had no other route to a unit test, since `MW3.Game` had no test project at all before this fix. Rather than hand-writing `[assembly: InternalsVisibleTo("MW3.Game.Tests")]`, `src/MW3.Game/MW3.Game.csproj` adds `<InternalsVisibleTo Include="MW3.Game.Tests" />` as a plain item - the .NET 8+ SDK generates the assembly attribute from it, so the grant lives in project configuration next to the other `ItemGroup`s rather than in a source file. Pitfall: the item's `Include` is a bare assembly name, not a project path or a version - a typo doesn't fail the build, it just silently leaves the internals invisible, and the consuming test project fails instead with a confusing "inaccessible due to its protection level" error that points nowhere near the actual cause.
- **Reflection to invoke a non-public property setter** — why here: the new tests need to pin `Base.GarrisonCount` (`internal set`) back to an exact value after a command changed it, to construct a scenario where garrison and level are provably unchanged across a construction/type transition - the same trick `MW3.Core.Tests` already uses (`UpgradeTests.cs`, `ConvertTests.cs`), reused here from a different assembly: `typeof(Base).GetProperty(nameof(Base.GarrisonCount))!.GetSetMethod(nonPublic: true)!.Invoke(b, new object?[] { garrison })` (`tests/MW3.Game.Tests/BaseActionMenuTests.cs:9-10`). This works without `InternalsVisibleTo` for `MW3.Core` at all, because reflection bypasses accessibility checks at the metadata level - only JIT-visibility rules (not compile-time ones) apply. Pitfall: this compiles and calls successfully even if the property is renamed to something reflection can't find until runtime - `GetProperty` returns `null` for a typo, and the `!` null-forgiving operator turns that straight into a `NullReferenceException` on `.GetSetMethod`, with no build-time signal that the test now targets nothing.
- **`PrivateAssets="All"` blocking transitive `PackageReference` flow** — why here: `MW3.Game.csproj` marks its own `MonoGame.Framework.DesktopGL` reference `<PrivateAssets>All</PrivateAssets>`, which is why `MW3.Desktop.csproj` and `MW3.Android.csproj` each carry their own copy of the same package instead of inheriting it - the property tells NuGet "consumers of my project shouldn't see this as their dependency too." The new `MW3.Game.Tests.csproj` hit this directly: referencing `MW3.Game` alone produced a runtime `FileNotFoundException` for `MonoGame.Framework` the moment a test touched `BaseActionMenu` (its constructor calls `Refresh()`, which the type-loader has to resolve against), fixed only by adding the same `PackageReference` to the test project (`tests/MW3.Game.Tests/MW3.Game.Tests.csproj`). Pitfall: this failure is a runtime one, not a compile-time one - the test project builds cleanly and only blows up in `dotnet test`, which makes `PrivateAssets="All"` look like it broke something days after the actual cause (adding a project reference) if you don't already know to check for it.

Try next: `MW3.Game.Tests` currently references `MonoGame.Framework.DesktopGL` for the same reason `MW3.Desktop` does. FR-5 will add a convert button and `AiBrain` will eventually decide when to convert (FR-6) - sketch whether the reflection-based `SetGarrison`/`SetLevel` pattern this fix borrowed from `MW3.Core.Tests` will still be the right tool once there's a `ConvertCommand` path reachable from `MW3.Game` itself, or whether a scriptable in-process harness (rather than `--script`'s external-process one) becomes worth building once `BaseActionMenu` has more than one piece of cached, hard-to-reach state.

## 2026-07-29 — #48 The action menu gains convert, and towers, ranges, and transit losses drawn
Concepts: a nullable field to carry a second action's variant-specific data without widening the type, ellipse-via-non-uniform-texture-stretch for a normalized-space geometry rule, shared vs. per-button layout clamping, MonoGame's fixed-timestep catch-up as a hidden source of non-determinism
- **A nullable field on an existing record to carry one variant's extra data** — `BaseAction` (`src/MW3.Core/BaseAction.cs`) gained `BaseType? ConvertTargetType` alongside its existing `Kind`/`Cost`/`Availability` fields, rather than a second `ConvertAction` type or a `Kind`-keyed lookup elsewhere. Why here: D-25 requires the action to carry everything the widget needs so it never computes anything itself, and Convert is the one action whose meaning depends on data (which type it's converting *to*) that Upgrade doesn't have — a nullable field that's simply unused (`null`) for every other `BaseActionKind` was cheaper than a closed hierarchy for a single differing field. Pitfall: this only stays safe because exactly one `Kind` ever reads `ConvertTargetType` — the moment a second action needed its own extra, variant-specific data, this shape would need either a second nullable field (fine for two) or a real discriminated union (once the "just add another nullable" pattern threatens to repeat a third time), and nothing in the type system flags that threshold being crossed.
- **Drawing a normalized-space circle as an on-screen ellipse via non-uniform texture stretch** — `MatchScreen`'s tower range outline (`src/MW3.Game/MatchScreen.cs`) reuses the same "stretch a circular texture into a `Rectangle`" trick the base fill/rings already used, but here it's load-bearing rather than incidental: `Match` measures range as plain Euclidean distance in normalized `MapPoint` units (X and Y both 0..1), while the viewport maps X by width and Y by height independently (D-14) — so a circle of normalized radius R is genuinely an ellipse on a non-square viewport, with half-width `R * viewport.Width` and half-height `R * viewport.Height`. Sizing the destination `Rectangle` with those two different values, from one texture, draws exactly the shape the simulation's own in-range test describes. Pitfall: this only matches Core's rule as long as both axes are read from the *same* normalized radius — computing width and height from two different level-table lookups (or rounding one before the other) would silently draw a range that agrees with neither axis of the actual rule.
- **Shared-group clamping instead of per-element clamping, once elements can be close enough to overlap regardless of position** — `BaseActionMenu.GetButtonRect` (`src/MW3.Game/BaseActionMenu.cs`) used to clamp each button's rectangle into the viewport independently; adding a second always-present button exposed that two buttons on a 50° arc could sit closer together than a single button's own width, at *every* anchor position, not just near an edge — independent clamping could then slide both onto the same clamped position and overlap. The fix widens the arc spread and clamps the whole button group with one shared shift, so relative spacing between buttons is preserved even when the group as a whole needs to move to stay on-screen. Why the distinction matters as a concept: "clamp each item independently" and "clamp the group, preserving relative layout" are both valid-looking approaches to keeping N things on-screen, and they only diverge once items can be close enough together that independent clamping can push them past each other — a case that a menu with exactly one button (this project's own state before this feature) can never exercise. Pitfall: a regression test for this has to actually check pairwise distance between rendered rectangles, not just "each rectangle is inside the viewport" — both of this feature's implementations before the fix passed an inside-viewport check while still overlapping each other.
- **MonoGame's fixed-timestep catch-up as a hidden, load-dependent source of non-determinism** — QA found `qa/scripts/army-shrinking-early.txt`'s screenshot wasn't byte-for-byte reproducible across individual re-runs, even though `MW3Game.Update` (`src/MW3.Game/MW3Game.cs`) already derived simulated ticks only from `gameTime.ElapsedGameTime`, never a true wall-clock read (D-12's own rule). The gap: MonoGame's default fixed-timestep mode can call `Update()` a variable number of times to catch up on real elapsed time when a frame runs slow (host load, first-frame JIT costs) — each individual call's `ElapsedGameTime` stays the nominal step, but the *count* of calls before a script's frame-counted stop condition is reached isn't guaranteed constant, and `--time-scale` (a plain multiplier) turns a one-call difference into a multi-tick, visibly-different screenshot. The fix anchors scripted ticks to `TargetElapsedTime` (a constant) and disables the fixed timestep during scripted playback (`IsFixedTimeStep = false`), so Update and Draw pair 1:1 with no catch-up — the call count becomes exactly the script's own frame count and nothing else. Why this is worth knowing as a general lesson: "never read the wall clock" is necessary but not sufficient for determinism when the *scheduling* of your deterministic-looking calls is itself timing-dependent — the non-determinism was one level up from where the existing rule was looking. Pitfall: this fix is correctly scoped to scripted mode only (`_scriptedInput is not null`) — turning off the fixed timestep for real, unscripted play would change actual gameplay feel (variable frame pacing) for a problem that only exists in the QA harness.

Try next: `BaseActionMenu`'s two-button-overlap bug was only caught because *this* feature happened to add a second permanent button to an arc that had only ever rendered one before — the same class of bug (independent per-element clamping silently overlapping once elements are close enough) could still exist anywhere else in `MW3.Game` that lays out more than one thing from a shared anchor. Grep for other `Math.Clamp` calls in layout code and check whether any of them clamp elements independently that could, under some future state, end up closer together than their own size — the `TwoButtons_NeverOverlap` test this feature added is a template for how to check.

## 2026-07-30 — #49 The AI opponent upgrades its own bases and respects garrison caps
Concepts: a discriminant-enum-backed record struct to widen a closed two-shape result to three shapes, extracting a shared geometry helper used with opposite selection criteria by two callers, a compile-checked either/or accessor pattern that throws on the wrong branch, fixing a prediction routine to match a simulation special-case it had silently drifted from
- **A discriminant enum plus multiple nullable backing fields, to widen a closed result type without breaking its existing call sites** — `BrainDecision` (`src/MW3.Core/BrainDecision.cs`) used to wrap one nullable `SendArmyCommand?` field with two named factories (`None`, `Send`). Adding "or an `UpgradeCommand`" as a second possible payload couldn't reuse the old single-nullable-field trick, because a decision now needs to say *which* of two different command types it's carrying, not just whether it's carrying one — so the type gained an internal `Kind` enum (`None`/`Send`/`Upgrade`) alongside two nullable fields, one per payload type, with each accessor (`Command`, `Upgrade`) checking `Kind` before returning. Why here: this keeps the type a `readonly record struct` (no allocation per decision, matching `docs/CONVENTIONS.md`'s frame/tick-loop rules) while staying honest that "carries a `SendArmyCommand`" and "carries an `UpgradeCommand`" are mutually exclusive, machine-checked states rather than two independently-nullable fields that could theoretically both be set. Pitfall: this shape scales to "one more case" reasonably (as FR-7's future convert is expected to need), but each additional case means one more nullable field sitting unused for every other case — past three or four variants, a proper closed hierarchy (like `PendingConstruction`'s `PendingUpgrade`/`PendingConversion` subtypes, or `ScriptDirective`'s sealed records from earlier features) becomes the better shape, and nothing in this design forces that reconsideration to happen automatically.
- **Extracting one geometry helper that two callers use with opposite selection criteria** — `TryConsolidate`'s "which of my own bases is nearest to any base I don't own" computation (used to pick the *front* — the base needing reinforcement) and the new `TryUpgrade`'s "which of my own bases is furthest from any base I don't own" computation (used to pick the *safest* rear base to invest in) turned out to be the exact same per-base distance calculation, just minimized in one caller and maximized in the other. `NearestNotOwnedDistance(Match, Base)` (`src/MW3.Core/AiBrain.cs`) factors that shared arithmetic into one method; each caller keeps its own min/max comparison loop around it. Why here: D-31 explicitly calls this out as "one distance rule in the brain, not two" — the two clauses have opposite *purposes* (defend the front vs. develop the rear) but identical *geometry*, and duplicating the loop would have meant a future change to how "nearest not-owned base" is measured (e.g. accounting for tower range in FR-7) would need to be made correctly in two places instead of one. Pitfall: the refactor's biggest risk wasn't writing the new clause, it was silently changing `TryConsolidate`'s existing, already-tested behavior while extracting shared code from it — the safeguard was running every pre-existing `AiBrain` test unchanged after the extraction, not just adding new tests, since a helper method used by two callers can pick up an accidental behavior change (a different tie-break, a different floating-point comparison order) that only one caller's tests would catch.
- **An either/or accessor that throws on the wrong branch, rather than returning a nullable/default value** — both `BrainDecision.Command` and the new `BrainDecision.Upgrade` property throw `InvalidOperationException` if read while `Kind` doesn't match, instead of returning `null` (which the underlying field already technically holds when unset). Why here: a caller (specifically `MatchRunner.Advance`, which dispatches on `decision.IsUpgrade`) is expected to check the discriminant first — a getter that quietly returned `null` for "wrong branch" would let a dispatch bug (checking `HasCommand` but reading `.Upgrade`, say) silently produce a `NullReferenceException` three call frames later instead of a clear, immediate failure exactly at the misuse site. This is the same "fail loud and exactly where the mistake was made" instinct that `docs/CONVENTIONS.md`'s "model absence in the type system" rule already pushes toward, applied to a *branch* of a type rather than to a whole missing value. Pitfall: a throwing accessor only protects a caller that's actually written to check the discriminant first — it does nothing to stop a *new* caller from being written without that check in the first place; the safety here is entirely in "you'll find out immediately, with a clear message" rather than "the compiler stops you from writing the bug," which a real closed-hierarchy `switch` (with no default arm, deliberately left non-exhaustive so a missing case is at least a runtime surprise rather than silently doing nothing) would get closer to.
- **A prediction routine silently drifting from the simulation rule it's supposed to mirror, until a new feature makes the gap observable** — `AiBrain.PredictGarrison` (`src/MW3.Core/AiBrain.cs`) called `ProductionCalculator.Advance` unconditionally for any owned base, which was *correct* for every base that existed before FR-5, because every base was a producer. FR-5 added towers, and `Match.ApplyProduction` (`src/MW3.Core/Match.cs`) already had the right special-case (`if (b.Owner is null || b.Type == BaseType.Tower) continue;`) — but nothing forced `PredictGarrison`, a second, independent read of "what will this base's garrison be," to be updated in lockstep, because the two functions live in different files with no shared test asserting they agree on every input. The fix makes `PredictGarrison` return the current garrison unchanged for a tower, mirroring the simulation's own rule rather than re-deriving it. Why this is worth naming as its own concept: whenever a codebase has two independent computations that are supposed to always agree (a simulation's real behavior, and a prediction/estimate of that behavior used for decision-making), adding a new case to the simulation's rule is a silent trap for the prediction code, because nothing about adding the case *looks* like it touches the predictor at all. Pitfall: the fix here only checked the one predictor this codebase currently has (`AiBrain.PredictGarrison`); if a second AI, a hint system, or a "what would happen if" UI feature ever reads production the same way, each one needs the identical audit, and there's no single choke point (unlike `ProductionCalculator.Advance` itself, which both `Match.ApplyProduction` and `PredictGarrison` already correctly share) forcing all of them to be found and fixed at once.

Try next: `AiBrain.PredictGarrison`'s tower fix was caught because FR-5 (towers) landed just before FR-6 (this feature) and the review/QA passes for FR-6 happened to think to check it — but the same "two independent reads of what a base will do" risk exists for garrison *cap* logic too (`Base.GarrisonCap`, read directly by `TryUpgrade`'s saturation gate). Sketch what it would look like to write a property-based or table-driven test that asserts `PredictGarrison`'s eventual output, for every `BaseType`/level combination in `LevelTable`, agrees with running `Match.Advance` the same number of ticks on an equivalent isolated match — turning "the two computations must agree" from an unstated invariant discovered by inspection into something a test actually checks.

## 2026-07-30 — #53 The AI opponent builds towers and routes armies around enemy ranges
Concepts: widening a discriminant record struct past two cases into a genuine third branch, solving a line/circle intersection with the quadratic formula and clamping the result to a segment, re-authoring a test whose failure exposes a real (and correct) behavior change rather than patching around it
- **Widening `BrainDecision` from two payload cases to three** — `#49`'s entry above already flagged this exact spot as the place the "one more nullable field" trick would start to strain: `BrainDecision` (`src/MW3.Core/BrainDecision.cs`) now carries `Kind.None`/`Send`/`Upgrade`/`Convert`, three nullable backing fields, and three throwing either/or accessors (`Command`, `Upgrade`, `Convert`). At three cases the pattern still reads cleanly — every accessor's error message lists all three discriminants to check (`"check HasCommand, IsUpgrade, and IsConvert first"`), which is itself a small tell that the check surface is growing linearly with the case count. Why it stayed this shape rather than becoming a closed hierarchy here: `MatchRunner.Advance`'s dispatch (`src/MW3.Core/MatchRunner.cs:84-97`) is a flat `if (IsUpgrade) ... else if (IsConvert) ... else ...` with three arms and no `switch`, so a fourth case would need the same mechanical edit at every call site (`AiBrain` itself and every test that dispatches a decision) regardless of which shape backs it — the record-struct-plus-discriminant didn't cost anything extra here that a sealed hierarchy would have avoided. Pitfall: nothing enforces that every dispatch site's `if`/`else if` chain stays exhaustive as cases are added — three call sites in this diff alone (`MatchRunnerTests.cs`, `AiBrainTests.cs`'s `AssertingBrain`, and the production `MatchRunner`) each needed the same new `else if (decision.IsConvert)` arm added by hand, and a missed one fails at runtime (an `InvalidOperationException` from reading the wrong accessor) rather than at compile time.
- **Line/circle intersection via the quadratic formula, parameterized along a segment and clamped to `[0, 1]`** — `TowerThreatEstimator.ChordLengthWithinRange` (`src/MW3.Core/TowerThreatEstimator.cs`) needed the length of a straight flight path that falls inside a tower's circular range, to convert into an estimated unit loss. Parameterizing the segment as `from + t * (to - from)` for `t` in `[0, 1]` turns "where does this line cross this circle" into a plain quadratic in `t` (`a*t² + b*t + c = 0`, with `a = d·d`, `b = 2·d·f`, `c = f·f - range²`), solved with the textbook `(-b ± √discriminant) / 2a`. The two roots are the line's crossing points in *parametric* segment-distance, not real-world units yet — clamping each root to `[0, 1]` before subtracting them is what turns "where an infinite line crosses the circle" into "how much of *this specific segment* is inside it," since a tower can be near enough to the line's extension to satisfy the equation while the actual flight path never gets that close. Why here: this needed to be pure geometry with no allocation and no engine type (D-2, D-15) — `MapPoint` is the same plain `X`/`Y` struct `Match`'s own tower-fire distance check already uses, so the estimate and the simulation can never disagree about what "in range" means. Pitfall: `discriminant <= 0` (not `< 0`) is treated as "no crossing," deliberately folding the tangent case (`discriminant == 0`, the line just grazes the circle at one point) into "zero loss" rather than a single-point chord of zero length — mathematically these give the same answer here (a zero-length chord contributes nothing), but a reader expecting `<= 0` to mean strictly "outside" would be wrong; it means "outside, or exactly touching."
- **Treating a broken pre-existing test as a signal to re-verify the behavior, not a signal to patch the test** — adding `TryConvert` made `MatchRunnerTests.AiLaddersPastLevelTwo_ReachingLevelThreeOnAtLeastOneBase_OverALongMatch` fail, and the fast wrong move would have been loosening its tick budget or its assertion until it passed again without understanding why. Instead the failure was reproduced with temporary logging (tracking every `Base.Type`/`Base.Level` transition during the run) before touching anything, which showed a level-2 base converting to a tower at a garrison of 41 — one over its own level's cap of 40 — because `TryConvert`'s candidate rule (`garrison >= LevelTable.ConversionCost`, a flat 30) has no cap/level gate the way `TryUpgrade`'s does, so a reinforcement-inflated garrison well under its cap can still qualify. That is exactly what the issue's own acceptance criteria specify, so the fix was re-authoring the test's *claim* (`AiInvestsItsSurplus_ReachingLevelThreeOrBuildingATower_OverALongMatch`, `tests/MW3.Core.Tests/MatchRunnerTests.cs`) to match the now-true property, with the diagnostic finding written into its doc comment as the record of *why*. Why this is worth naming: "a test broke" is evidence a behavior changed, not evidence of which side (the test's old assumption, or the new code) is wrong — treating every red test as a bug in the new code, or reflexively relaxing the assertion, both skip the step that actually decides that question. Pitfall: the diagnostic logging that found the real cause was throwaway code, written directly in the failing test and deleted once the cause was clear — worth doing deliberately rather than leaving it behind, since a logging `StringBuilder` accumulating every tick's state is exactly the kind of thing `docs/CONVENTIONS.md`'s no-unnecessary-abstraction rule would flag as debug scaffolding masquerading as a permanent test.

Try next: `TotalExpectedTowerLoss` (`src/MW3.Core/AiBrain.cs`) loops every base on the map on every candidate target `TryAttack` considers, calling `TowerThreatEstimator.EstimateUnitsLost` for each enemy tower found - fine on this phase's fixed six-base map, but sketch what happens to that cost if a later phase's map has dozens of bases and towers: would precomputing "which towers exist and where" once per decision, rather than re-scanning `match.Bases` inside a loop already scanning targets inside a loop scanning sources, change the *shape* of the computation (not just its constant factor) - and is there an existing shared helper in this codebase (the way `NearestNotOwnedDistance` centralized the front/rear distance rule) that this loop should have reused instead of writing its own?

## 2026-07-30 — #54 FR-1: Send strength as an explicit percentage command in the core rules
Concepts: an enum whose members carry meaningful integer values rather than being an opaque discriminant, a pure static calculator extracted to stop two call sites re-deriving the same arithmetic, keeping a "should this action happen" check on unclamped arithmetic while a separate "how big is the action" computation clamps
- **An enum backed by meaningful integer values, read via a cast rather than a lookup table** — `SendStrength` (`src/MW3.Core/SendStrength.cs`) declares `Quarter = 25, Half = 50, ThreeQuarters = 75, Full = 100` explicitly, and `SendStrengthCalculator.Compute` (`src/MW3.Core/SendStrengthCalculator.cs`) reads that value straight back out with `(int)strength` rather than switching on the enum member to look up a percentage elsewhere. Why here: MW2's four send-strength options *are* percentages (`MW2-PARITY.md`), so there's no independent "percentage for this option" fact to keep in sync with the enum - the enum member's numeric value **is** the fact, and giving it that value up front means a future caller (FR-2's human-facing picker) can format `(int)strength` directly as a percentage label without another lookup. Pitfall: this only works cleanly because the values genuinely need to be exposed as their literal magnitude; an enum used purely as an opaque tag (say, `BaseType.Producer`/`Tower`) would be actively confusing if given meaningful-looking numbers, since a reader would reasonably expect that number to be usable somewhere and it wouldn't be.
- **Extracting one pure static calculator instead of leaving `Math.Max(1, garrison / 2)` duplicated at each call site** — `AiBrain` had this exact expression twice: once as a private `ClampedSendSize` helper, once inlined as a local `unclampedHalf` variable in `TryAttack`. `SendStrengthCalculator.Compute(int garrison, SendStrength strength)` factors the shared arithmetic (`Math.Max(1, garrison * (int)strength / 100)`) into one public, engine-free method both callers now use, and that FR-2's human send path will call too. Why here: the issue (`#54`) framed this explicitly as removing a second copy before a *third* caller (the human picker) would otherwise need to introduce a *third* copy of the same floor-and-clamp rule - one pure function is the point at which "the AI's send size" and "a human's send size" are provably the same rule rather than two implementations that happen to currently agree. Pitfall: integer division makes `garrison * (int)strength / 100` order-sensitive - multiplying before dividing avoids `50 / 100` truncating to `0` for every garrison size, which `garrison / 2 * 50` (divide-then-multiply, or reordering the literal `/2` version) would not have avoided at `Half`.
- **Keeping a decision's gating check on unclamped arithmetic, separate from the clamped value used once the decision is made** — the first pass at this feature routed `TryAttack`'s winnability comparison (`attackingUnitCount > predictedGarrison`) through the same clamped `SendStrengthCalculator.Compute` call used for the final send size, and `code-reviewer` caught that this silently changed behavior: the old code's *unclamped* `garrison / 2` could be `0` for a 0/1-garrison source, which can never exceed a non-negative predicted garrison, so such a source could never appear winnable - `Compute`'s clamp-to-1 broke that guarantee, letting a near-empty base "win" against an empty target. The fix keeps a separate, deliberately unclamped `unclampedHalfGarrison = source.GarrisonCount * (int)SendStrength.Half / 100` for the comparison, and only calls `SendStrengthCalculator.Compute` once a winnable target is already found, to size the actual command. Why this is worth naming as its own concept: "the same number, clamped for display/execution but unclamped for a threshold check" are two different values with two different correctness requirements, and merging them into one shared helper because they're usually equal is a trap the diff walked straight into - the regression test added (`TryAttack_Declines_WhenSourceGarrisonIsOneAndTargetIsEmpty`, `tests/MW3.Core.Tests/AiBrainTests.cs`) is what makes the distinction a checked invariant rather than a comment. Pitfall: this is easy to miss precisely because most test scenarios use garrisons well above 1, where the clamp never engages and the bug is invisible - the tests that exist already (328 of them) passed unchanged with the bug present.

Try next: `SendStrengthCalculator.Compute`'s signature takes `SendStrength` by value and returns a plain `int` - FR-2's picker will be the first caller passing a strength the *user* chose interactively rather than a hardcoded `SendStrength.Half`. Sketch what FR-2's UI-side code would look like calling `Compute` directly versus wrapping it in a UI-layer helper, and whether `MatchScreen`'s existing inline `Math.Max(1, source.GarrisonCount / 2)` (left untouched this feature, per the issue's own out-of-scope list) is a preview of the exact duplication `Compute` exists to prevent, once FR-2 actually rewires it.

## 2026-07-30 — #58 FR-2: Send-strength picker on both input heads, plus snaking
Concepts: an analyzer forcing a method's static/instance shape to match what it actually touches, reflection-invoking a private static method from a test (vs. an instance one), clamping a point into a rectangle to get the true nearest-point distance instead of measuring from a shape's center
- **CA1822 forcing `HitTestButton` to become `static` once it stopped touching instance state** — the first draft of `SendStrengthSelector.HitTestButton` (`src/MW3.Game/SendStrengthSelector.cs`) was an instance method, mirroring `BaseActionMenu.HitTestButton`'s shape, but `MW3.Game.csproj`'s `-warnaserror` build failed it with CA1822 ("does not access instance data and can be marked as static") - unlike `BaseActionMenu`'s version, which reads `_actions` (per-anchor-base state), this control's four buttons are the same `_strengths` static array for every instance, so the method never touches `this` at all. Why here: the fix is `static int? HitTestButton(...)`, called as `SendStrengthSelector.HitTestButton(point, viewport)` from `MatchScreen` rather than through `_strengthSelector.HitTestButton(...)` - a `CS0176` error at the call site is the compiler's way of confirming a static member can't be reached through an instance reference once it's actually static. Pitfall: this analyzer only catches a method that *currently* touches no instance state - if `SendStrengthSelector` later needed the hit-test to depend on, say, a per-instance "control is momentarily disabled" flag, the method would need to go back to being an instance method, and CA1822 gives no warning either direction about that future coupling; it only checks the code as it stands today.
- **Reflection-invoking a `static` private method from a test, versus an instance one** — `SendStrengthSelectorTests.GetButtonRect` (`tests/MW3.Game.Tests/SendStrengthSelectorTests.cs:8-11`) calls `.Invoke(null, new object[] { index, viewport })` — passing `null` as the target — because `GetButtonRect` is `private static`; `BaseActionMenuTests.GetButtonRect` (the pattern this mirrors) instead constructs a `BaseActionMenu` and passes it as the first argument to `Invoke`, because that method is a private *instance* method. Why here: `MethodInfo.Invoke`'s first parameter is "the object to invoke it on," and reflection enforces the static/instance distinction exactly like normal C# does — passing a real instance to a static method's `Invoke` (or `null` to an instance method's) throws a `TargetException` at test-run time rather than failing to compile, since the compiler can't see through the string method name. Pitfall: because this only fails at runtime, a copy-pasted reflection helper that doesn't notice the target method changed from instance to static (or vice versa) will compile cleanly and only blow up when the test actually runs.
- **Measuring "is this rectangle far enough from this point" from the rectangle's nearest point, not its center** — the first version of `EveryButton_IsFarEnoughFromEveryBase` measured normalized distance from each button's *center* to each base; `code-reviewer` pointed out this doesn't actually prove the acceptance criterion ("no press on the control is ever contested with a press on a base"), since a press can land on any point inside the button, including a corner closer to a base than the center is. The fix (`tests/MW3.Game.Tests/SendStrengthSelectorTests.cs`, `EveryButton_IsFarEnoughFromEveryBase`) converts the base's normalized position to pixels, clamps it into the button's `Rectangle` with `Math.Clamp` on each axis independently, and measures distance from *that* clamped point — the standard "closest point on an AABB to an external point" technique, since clamping each axis separately to the rectangle's own min/max is exactly what produces the nearest point whether the external point is beyond a corner, an edge, or already inside the rectangle. Why this is worth naming: a center-to-point distance check silently assumes the shape being tested is a circle (where every boundary point actually is the same distance from the center) - the moment the shape is a rectangle, "distance from center" and "distance from nearest point" only agree when the rectangle happens to be small relative to the threshold being checked, which is exactly the kind of assumption that holds today's constants but wouldn't be re-verified if someone later shrank the margin. Pitfall: `Math.Clamp(basePixelX, rect.Left, rect.Right)` clamps in *pixel* space and must be converted back to normalized coordinates before subtracting from the base's normalized position - clamping in normalized space against a rectangle whose bounds were computed in pixels (or vice versa) would silently compare two different units.

Try next: `SendStrengthSelector` and `BaseActionMenu` now both implement the identical "remember which button a press landed on at down, activate it unconditionally at up" state machine (`_pressBeganOnStrengthButtonIndex` / `_pressBeganOnMenuButtonIndex` in `MatchScreen.cs`), with near-identical reset-on-every-press-down and consume-on-release logic duplicated between them. Sketch what a small shared helper (say, `PressTrackedButtonGesture`, holding "which index was pressed" and exposing `BeginPress`/`CompleteRelease`) would look like, and whether `MatchScreen`'s growing pile of `_pressBeganOn*` fields is the same kind of "duplicated arithmetic across call sites" problem `SendStrengthCalculator` was extracted to solve for FR-1/FR-2's Core-side rule - just on the presentation side instead.

## 2026-07-31 — #61 FR-3: A send arrives as successive waves in the core rules
Concepts: a private "not yet visible" queue kept separate from the collection callers actually read, reflection reaching past a property setter into a private field and a private method (not just a non-public setter), deriving an expected test value from the production constants instead of hardcoding an approximation
- **Keeping "exists but not yet active" state out of the collection callers read, instead of filtering that collection everywhere** — `Match`'s `_pendingWaves` (`src/MW3.Core/Match.cs`) holds waves 2..N of a multi-wave send until their own `LaunchTick`, and only then moves them into `_armies` - the same list `ArmiesInFlight` exposes. The alternative (add every wave to `_armies` immediately, with a future `LaunchTick`, and have tower fire / the dump line / `PositionAtTick` all skip anything not yet launched) was rejected precisely because it would have required that same "is this actually active yet" guard at every reader, rather than once at the boundary that promotes a wave into visibility. Why here: this is the same shape as keeping a job in a "not started" queue rather than putting it on the "running jobs" list with a future start time - callers of the running list get to assume everything in it is really running. Pitfall: because `LaunchPendingWavesAtTick` (`Match.cs`) is itself an `Advance` boundary (evaluated after construction completion, before tower fire and arrivals), a caller who advances one tick at a time and checks `ArmiesInFlight` mid-loop needs to advance *past* a wave's launch tick, not merely up to it, before that wave becomes visible - `PendingWaves_AreInvisibleAndUntargetable_UntilTheirOwnLaunchTick` (`tests/MW3.Core.Tests/SendWaveTests.cs`) exists specifically to pin that boundary.
- **Reflection reaching past a property's non-public setter into a private field, and invoking a private method directly** — every earlier test file in this repo reflects into a *property's* setter (`typeof(Base).GetProperty("Level")!.GetSetMethod(nonPublic: true)`), which still goes through whatever the property itself does. `PendingWaveOfTheirs_KeepsAPlayerAlive_EvenWithNoBasesAndNoLaunchedArmiesLeft` (`tests/MW3.Core.Tests/SendWaveTests.cs`) needed something no property exposes at all — direct mutation of the private `List<Army> _armies` field — so it uses `GetField(BindingFlags.NonPublic | BindingFlags.Instance)` to get the live list object and calls `.Clear()` on it, then separately reflects `Match`'s private `EvaluateOutcome()` method (`GetMethod(...).Invoke(match, null)`) to force the elimination check to run without needing a real `Advance` boundary to trigger it. Why here: the race this test proves (a player's last base falling on the same tick their last launched army resolves, while a later wave of their own send is still pending) needs two independent events to land on the same tick, which the fixed map's real travel times can't produce - reflection was the only way to construct the exact state, following the same "rig what real play can't reach" idiom `RecaptureGraceTests` already established for property setters, extended one level further. Pitfall: reflecting a field returns a *live reference* to the actual collection object (not a copy), so `.Clear()` mutates the real match state immediately — reflecting the wrong field name fails at runtime with a `NullReferenceException` on the `GetValue` call, not a compile error, so a typo'd field name only surfaces when the test actually runs.
- **Deriving an expected test value from the same production constants the code uses, instead of a hand-picked approximate number** — the pre-existing `TuningSanity_UnitsLostFlyingStraightAtATower_IsRoughlyTheStatedApproximation` (`tests/MW3.Core.Tests/TowerFireTests.cs`) used to hardcode "roughly 3/4/6/9 units lost" per tower level; re-authoring it against an 8-unit wave (the largest a single send can be without splitting) meant level 4's old expectation of 9 was no longer reachable at all (you can't lose more than you sent). The fix computes `expectedShots` from `LevelTable.Tower.RangeUnits(level) / Match.ArmySpeedUnitsPerTick / LevelTable.Tower.FirePeriodTicks(level)` - literally "how many fire periods fit in the time spent inside range" - capped at the sent unit count, and asserts the observed loss is within one of that. Why here: a magic approximate number in a test is only as trustworthy as whoever picked it and why; deriving it from the same constants `EvaluateTowerFireAtTick` actually reads means the test re-verifies the *relationship* between range, speed, and fire period rather than a snapshot someone once observed. Pitfall: this technique only strengthens a test if the derivation is independent of the code path being tested — computing `expectedShots` by calling into `Match`'s own tower-fire logic (rather than recombining the public tuning constants directly, as this test does) would make the assertion circular, unable to catch a bug in that logic since it would compute the same wrong answer both times.

Try next: `Match.IsEliminated` (`src/MW3.Core/Match.cs`) now checks three separate collections in sequence - owned bases, `_armies`, `_pendingWaves` - to decide "does this player have anything left." Sketch what a single `AnyOf(bases, armies, pendingWaves)`-style helper (or a lazily-evaluated `IEnumerable<Army>` that concatenates `_armies` and `_pendingWaves.Select(p => p.Army)`) would look like, and whether it would have made the bug this feature's code-reviewer caught (the elimination check missing `_pendingWaves` entirely) structurally harder to reintroduce the next time something needs "every army this player still has, launched or not."

## 2026-08-04 — #63 FR-4: Waves and the send column drawn distinctly from a single-arrival army
Concepts: nullable value types with `is` pattern matching as a null-check-and-bind, value tuples as a lightweight multi-value return from a pure function, reusing a mutable buffer field instead of returning a fresh collection to satisfy a zero-per-frame-allocation constraint
- **`long? eventTick` plus `eventTick is long tick` as a combined null-check and unwrap** — `WaveColumnPresentation.IsFlashing` (`src/MW3.Game/WaveColumnPresentation.cs`) takes `long? eventTick` (mirroring `Base.LastFireTick`'s own type) and tests it with `eventTick is long tick && elapsedTicks - tick < durationTicks` in one expression, rather than `eventTick.HasValue && elapsedTicks - eventTick.Value < durationTicks`. Why here: this predicate is called from `Draw`/`Update` code paths that must allocate nothing per frame, and `is T x` pattern matching on a `Nullable<T>` compiles to the same `HasValue`/`GetValueOrDefault` machinery as the verbose form - it's purely a readability win, but the specific reason it reads well *here* is that the null case (a tower that has never fired, or an army never observed to be hit) is a real, common, first-class case this codebase always models as `null` rather than a sentinel tick like `-1`, so the pattern match doubles as documentation of that choice. Pitfall: `is long tick` only binds `tick` inside the `&&`'s right-hand side and anywhere after it in the same expression/block - reordering to `elapsedTicks - tick < durationTicks && eventTick is long tick` would not compile, since `tick` wouldn't be definitely assigned yet at the point it's used.
- **A value tuple, `List<(int FromIndex, int ToIndex)>`, as the return shape for "pairs of related indices" instead of a small class** — `WaveColumnPresentation.ComputeSpineSegments` (`src/MW3.Game/WaveColumnPresentation.cs`) fills its caller-supplied list with `(int FromIndex, int ToIndex)` tuples, and `MatchScreen.DrawArmiesInFlight` (`src/MW3.Game/MatchScreen.cs`) immediately destructures each one with `var (fromIndex, toIndex) = _spineSegmentScratch[i]`. Why here: a `SpineSegment` class (or even a `readonly record struct`) would be the "proper" named type, but the two integers have no behavior of their own and are consumed within a few lines of being produced - the named-element tuple (`FromIndex`/`ToIndex` rather than the default `Item1`/`Item2`) gives the same self-documentation at the call site without a new type declaration, matching how `FixedStepClock.Advance`'s `(Clock, Ticks)` tuple return already established the pattern in this codebase (see #1's entry above). Pitfall: `(int FromIndex, int ToIndex)` is a *value* type (`System.ValueTuple`), so `List<(int, int)>.Clear()` followed by re-`Add`ing on every call does not leak or retain references the way a `List<SomeClass>` holding stale objects could - but it also means each element is copied by value on every read/assignment, which is fine at this list's size but would be a real cost if the list ever grew to thousands of entries per frame.
- **A `private readonly List<T>` field, cleared and refilled every call, instead of a method returning `IReadOnlyList<T>`** — `MatchScreen` holds `_armyCenterScratch` and `_spineSegmentScratch` as instance fields (`src/MW3.Game/MatchScreen.cs`), and `DrawArmiesInFlight` calls `.Clear()` then repopulates them each `Draw`, rather than having `WaveColumnPresentation.ComputeSpineSegments` return a freshly-allocated `List<(int,int)>`. Why here: `docs/CONVENTIONS.md`'s "frame-loop code allocates nothing per frame" rule is not a style preference in this codebase - it's enforced by the same discipline that already produced `_armyIdsToPrune`, `_garrisonText`, and `_lastGarrisonCount` as reused fields, so a new per-frame collection would have been a regression the very next `code-reviewer` pass should catch. The `void ComputeSpineSegments(IReadOnlyList<Army> armiesInFlight, List<(int,int)> output)` signature - writing into a caller-owned buffer instead of returning a new one - is the shape that makes "zero allocation" achievable for a helper that still needs to be pure and headlessly testable (tests just pass their own fresh `List` and inspect it after the call). Pitfall: because `output.Clear()` happens *inside* the pure helper rather than at the call site, a caller that accidentally passes the same list into two different computations expecting to accumulate across them will silently lose the first computation's results - the "clears first" contract has to be documented (it is, in the method's doc comment) since nothing in the type signature enforces it.

Try next: `WaveColumnPresentation.ComputeSpineSegments` is `O(n²)` in the number of in-flight armies (`src/MW3.Game/WaveColumnPresentation.cs`'s nested loop over `armiesInFlight`), traded deliberately for zero allocation at the armies-in-flight counts this game actually reaches. Sketch what an allocation-free `O(n log n)` version would need - likely sorting a reused `int[]` of indices by `(SendId, WaveIndex)` in place with `Array.Sort` and a static `Comparison<int>` - and whether the added complexity would actually pay for itself below, say, 50 concurrent armies, or whether "reused buffer, quadratic scan" is the right tradeoff to keep as the default for small-n presentation code in this codebase going forward.

## 2026-08-04 — #56 FR-7's determinism test now proves the tower-aware attack branch fires
Concepts: an interface-based spy/decorator as a test double, exposing a discriminated union's tags as named boolean predicates, `Assert.Contains` with a predicate lambda
- **An interface-based spy that wraps the real implementation** — why here: the determinism test needed to prove *which* command the AI actually sent during a run, not just what the match's end state looked like, but `MatchRunner` only ever talks to whatever `IPlayerBrain` it's given (`src/MW3.Core/MatchRunner.cs:84`). `RecordingBrain` (`tests/MW3.Core.Tests/AiTowerRoutingDeterminismTests.cs:31-51`) implements `IPlayerBrain` itself, holds a real `AiBrain` internally, and forwards every `Decide()` call to it - recording the command before returning the same decision unchanged. Because `MatchRunner` depends only on the interface, the test substitutes the spy with no production code change beyond what the interface already exposed. Pitfall: a spy that forwards *unconditionally* is safe, but it's easy to instead let the wrapper's own bookkeeping (a `List<T>.Add`, a counter) throw or short-circuit before the delegating call happens - if that ever moved above `_inner.Decide(match)` instead of after it, a broken spy would also break the real decision it's supposed to be transparently observing.
- **Exposing a discriminated union's internal tag through named boolean predicates** — why here: `BrainDecision` (`src/MW3.Core/BrainDecision.cs`) already modeled "send, upgrade, convert, or nothing" as a private `Kind` enum plus `IsUpgrade`/`IsConvert` properties, precisely so callers never switch on the enum directly. `RecordingBrain` originally re-derived "is this a send" as `decision.HasCommand && !decision.IsUpgrade && !decision.IsConvert` - logically correct today, but it duplicates knowledge the type already owns. Adding `IsSend => _kind == Kind.Send` (mirroring the existing two) let the test read `decision.IsSend` directly. Pitfall: the negative-elimination version does not fail to compile if `BrainDecision` ever grows a fourth `Kind` - it silently starts treating the new kind as "not upgrade, not convert" (i.e. as if it were a send), whereas a named `IsSend` predicate at least keeps the true/false meaning explicit at every call site, even though neither form gets a compiler error for the missing case; only a real exhaustive switch would.
- **`Assert.Contains` with a predicate lambda over a recorded list** — why here: the test doesn't know or care *which* decision tick issued the qualifying send, only that one of them did, across a list built up over many ticks (`Assert.Contains(oneCallBrain.SentCommands, c => c.TargetBaseId == neutral4Id)`, line 129). This reads more directly than filtering with LINQ's `Any()` first and asserting the boolean, because a failing `Assert.Contains` prints the whole candidate collection in its failure message - useful here since `SentCommands` also holds every non-matching send the AI issued along the way. Pitfall: the predicate closes over `neutral4Id`, a `const int` declared just above it - if that had instead been a mutable local reused later in the same method, the closure would capture the *variable*, not its value at declaration time, and a later reassignment before the assertion actually runs (unlikely here, but easy in a longer test) would silently change what the lambda checks.

Try next: `RecordingBrain` only records sends, since that's what issue #56 needed. If a future determinism test needed to prove an upgrade or convert decision fired at a specific point (not just that the run converged), sketch what `RecordingBrain` would need to become - a single `List<BrainDecision>` instead of `List<SendArmyCommand>`? - and whether that's still a spy or has become closer to the wrapper doing something the real `AiBrain` doesn't.

## 2026-08-04 — #68 AiBrain's winnability and threat checks ignore building defence percentages
Concepts: extracting a shared predicate so two call sites can't drift, explicit `(long)` widening to guard an `int` multiplication against overflow, deriving test boundary values from the real constant table instead of an approximate comment
- **Extracting the exact boolean condition an existing method already computed, so a second caller can reuse it instead of re-deriving it** — `CombatResolver.WouldCapture` (`src/MW3.Core/CombatResolver.cs:61-62`) is `(long)attackingUnits * attackerIndex > (long)defendingGarrison * defenderIndex` - literally the same expression `Resolve` builds into `attackPower > defensePower` two lines below, now factored out and called from both `Resolve` (`CombatResolver.cs:74`) and `AiBrain`'s two prediction sites. Why here: before this fix, `AiBrain` had its own inline comparison that *happened* to agree with `Resolve` only because both assumed 100% defence - the two independently-written conditions had already silently diverged the day defence percentages shipped (phase 3 FR-3b), and nothing would have caught a second divergence either. Pitfall: extracting a predicate only prevents drift for callers that actually go through it - `Resolve` still recomputes `attackPower`/`defensePower` as separate `long`s for the remainder-of-garrison arithmetic afterward, so `WouldCapture`'s internal multiplication and `Resolve`'s are two separate computations of the same numbers; a future change to one side's rounding or casting could still reintroduce disagreement unless both paths route through the same computed values, not just the same formula.
- **Casting to `long` before multiplying two `int`s that could overflow `int` range** — both `WouldCapture` and `Resolve` write `(long)attackingUnits * attackerIndex` rather than `attackingUnits * attackerIndex` cast afterward. Why here: `attackerIndex`/`defenderIndex` can be a composed percentage above 100 (a level-5 village defends at 140%, and morale/forge terms will multiply in further), and `attackingUnits` can be in the thousands over a long match - `int * int` in C# computes in 32-bit and only *then* would be widened if you cast the result, by which point an overflow has already silently wrapped. Casting the *first* operand to `long` before the multiplication forces the whole expression into 64-bit arithmetic, per C#'s numeric promotion rules. Pitfall: casting only one operand is sufficient and idiomatic here, but it's easy to instead cast the *result* (`(long)(attackingUnits * attackerIndex)`) by reflex - that compiles, looks similar, and is simply wrong, since the overflow already happened inside the parentheses before the cast ever runs.
- **Computing a test's boundary numbers from the actual table the code reads, not a remembered approximation** — the pre-existing `TryAttack_OnlyViableTargetBehindATower_IsStillAttacked_...` test used to pass a garrison of 18 based on a comment assuming "the ~3-unit estimated loss" against a tower defending at 100%; re-deriving `AiBrain`'s check to use the real `LevelTable.Tower.DefencePercentage(1)` (140%, not 100%) meant that boundary silently became false without any code in the test itself being wrong - the *assumption* baked into its numbers was. The fix (source garrison 22, not 18) was found by writing out `defendingGarrison × defenderIndex` (5 × 140 = 700) and solving for the smallest `attackingUnits` that clears it (8), then working back through the known tower-loss constant (3) to the source garrison that produces it. Pitfall: a test that encodes "roughly enough" margins from a comment, rather than deriving them from the same table the production code reads, looks passing right up until an unrelated change to that table (or, as here, a bug fix that starts reading it correctly for the first time) silently invalidates the scenario the test thought it was covering - the test still runs and still asserts something, just not the boundary its name claims.

Try next: `CombatResolver.WouldCapture` and `Resolve` still each build their own `(long)x * y` products rather than `Resolve` calling `WouldCapture` and then separately recomputing `attackPower`/`defensePower` for the remainder math. Sketch what a small `CombatMargin` struct (or a tuple) returned by one internal helper - holding `AttackPower`, `DefensePower`, and `Captured` together - would look like, and whether it would fully close the "two computations of the same numbers" gap this entry's first pitfall describes, or just move the duplication somewhere else.

## 2026-08-04 — #66 FR-1: Morale points, the sun ladder, and gains and losses
Concepts: netting deltas before a single clamped write, `is Player x` pattern-matching over a nullable owner, `xUnit` `[Theory]`/`[InlineData]` boundary tables, reflection-set private setters in tests
- **Netting two deltas per recipient before one clamped write, instead of applying each delta as its own clamped write** — `Match.ResolveArrival` (`src/MW3.Core/Match.cs:859-943`) computes a capture's gain/loss and the attacking-unit-death swing as separate `int` deltas, but only ever calls `AwardMorale` once per player per event, after summing whatever deltas that player earned. Why here: `AwardMorale` clamps to `[0, 8000]` on every write (D-38); a capture that nets a large gain and a death swing that nets a loss can land on the very same arrival, and if each delta were its own clamped call, whichever ran first would clamp against the floor or ceiling before the second delta had a chance to offset it - a capturer already at the point ceiling would show a real morale *drop* from the attacking losses that a same-event capture gain should have absorbed. Pitfall: netting fixes the single-event case but does nothing for deltas that arrive as genuinely separate events (a capture now, a tower shot ticks later) - those are correctly two independent clamped writes, so the fix is specifically "don't split one event's own multiple deltas across multiple writes," not "always combine everything."
- **`if (defenderOwnerAtCombat is Player defender)` as both a null check and a cast, over a `Player?`-typed field** — used at both the death-swing and capture-loss sites (`Match.cs:869`, `921`) to charge the previous owner only when one exists; a neutral base's `Owner` is `null` (D-11), and this pattern is the same one already established for the ownership checks elsewhere in `Match`. Pitfall: the pattern silently does nothing on the `null` branch - correct here, since "neutral scores nothing for nobody" is the actual requirement - but it's easy to reach for the same one-liner somewhere a `null` case should instead be a defect signal, and get a quiet no-op instead of a thrown exception.
- **`[Theory]`/`[InlineData]` tables to pin every threshold and its off-by-one boundary** — `MoraleTableTests.cs:5-16` runs the same assertion across `(0,0)`, `(499,0)`, `(500,1)`, `(999,1)`, `(1000,2)` … proving the sun-level derivation is exactly "highest threshold reached" rather than trusting one or two spot checks. Why here: `MoraleTable`'s ladder has five thresholds and each one has an off-by-one on both sides that a single passing test could easily miss (e.g. `<=` where `<` was intended). Pitfall: a `[Theory]` with a wrong expected value in one `[InlineData]` row still reports as one failing test among many passing ones - easy to skim past in a large suite unless the failure output is actually read row by row.
- **Reflecting into a `private set` to set up a state ordinary play can't quickly reach** — `MoraleAccrualTests.SetMoralePoints` and `SetLevel`/`SetOwner`/`SetOwnerBeforeLastChange` (`tests/MW3.Core.Tests/MoraleAccrualTests.cs:15-31`) use `GetProperty(...).GetSetMethod(nonPublic: true)!.Invoke(...)` to give a test headroom before a clamp-adjacent assertion, or to rig a base into a mid-grace retake without playing out the moves that would produce it - the same style `CaptureDemotionTests` already used. Pitfall: reflection bypasses whatever invariants the constructor or normal mutation path would have enforced, so a rigged state can accidentally be one the real game could never produce (e.g. inconsistent `Level`/`Type` combinations) - the test is only meaningful if the rigged fields are exactly the ones a real sequence of moves would have set, and nothing else.

Try next: `Match.ResolveArrival`'s netting fix works because the two morale deltas share a resolution point in one method call. Sketch what would need to change if a future feature (energy, say) wanted to net a delta against morale's *own* accrual within the same tick from a completely different call site - would `Match` need an explicit "pending deltas for this tick" buffer flushed once at the end of `Advance`, or does the current "compute both before either write" pattern only work because everything currently affecting morale already funnels through `ResolveArrival` and the two other accrual sites directly?

## 2026-08-05 — #67 FR-2: Morale feeds the combat formula's attack and defence indices
Concepts: switching a fixed-point scale to avoid truncation bias, a single shared predicate preventing two call sites from disagreeing, widening before multiplying to keep headroom at a larger scale
- **Moving a composed index from percent (1/100) to basis points (1/10000) to eliminate a rounding bias** — `CombatResolver.ComposePercentages` (`src/MW3.Core/CombatResolver.cs:95` area) used to floor its two-term product at 1% grain; once morale gave the defence side a second non-identity term, a real combination (a level-2 village's 110% defended at morale-1's 125%) landed on 137.5, which percent-scale truncation always rounds toward the attacker. Why here: a capture decision that's a strict inequality (`WouldCapture`) is exactly the kind of check a systematic rounding bias can flip on a knife-edge case, and it would flip the *same way* every time rather than randomly - a silent, reproducible unfairness. Moving the scale to 1/10000 makes today's two-term product (baseline × morale, forge still fixed at identity) exact with zero division loss, deferring the truncation question to whenever a real third term (forge, G-6) actually appears. Pitfall: a finer fixed-point scale only *defers* truncation, it doesn't eliminate the need to reason about it - the same bias will reappear the day forge stops being identity, just one order of magnitude smaller, and the doc comment says so explicitly rather than letting the fix read as permanent.
- **One predicate function reused by both the real resolver and a predictive caller, so they cannot independently drift** — `CombatResolver.WouldCapture` (already extracted by #68) is now fed morale-composed indices from both `Match.Resolve`'s real combat and `AiBrain`'s two prediction sites (winnability, threat estimation) rather than each computing its own comparison. Why here: before FR-1/FR-2, `AiBrain`'s predictions and `Resolve`'s real outcome each built the attacker/defender indices inline - #68 found they'd already silently diverged once (ignoring building defence percentages) purely because nothing forced them through the same code path, and morale composition was a second chance for that same class of bug to reappear if each site remembered to add the morale term independently. Pitfall: sharing the predicate only prevents drift in the comparison itself - if `AiBrain` composed its indices with a different rounding order or omitted a term the real resolver includes, the two would still disagree despite calling the same `WouldCapture`, since the shared function trusts its inputs are already composed consistently.
- **Casting to `long` before multiplying, kept even as the multiplied values got larger** — `(long)attackingUnits * attackerIndex` (`CombatResolver.cs`, `WouldCapture` and `Resolve`) was already `long`-cast pre-FR-2 for headroom against thousands of units at up to ~140% (a level-4 tower's defence ceiling); moving the same expression's `attackerIndex`/`defenderIndex` from a percent scale (max a few hundred) to a basis-point scale (max tens of thousands) made the `int`-overflow margin the cast protects against roughly 100× tighter than before, even though nothing about the cast syntax changed. Pitfall: a widening cast that was comfortably conservative at one scale can become merely *sufficient* at a larger one without any code change flagging it - the margin shrank silently as a side effect of the basis-point migration, and the only thing keeping it safe is that `long` still has enormous headroom over the actual unit-count and index ranges in play; a future scale change deserves a fresh check, not an assumption that "it was fine before."

Try next: `ComposePercentages` still takes exactly three fixed arguments (baseline/morale, and now morale, and forge). Sketch what its signature would need to become if a fourth term (e.g. a hero attack bonus, per parity G-4) arrived later - a `params int[]`? An accumulator the caller folds over? - and whether basis-point precision would still be enough headroom for a four-term product before `long` cross-multiplication in `WouldCapture` itself needed reconsidering.

## 2026-08-05 — #69 FR-3: Inactivity decay drains morale, faster the higher it is
Concepts: deriving a schedule on demand instead of storing "next occurrence" state, integer modulo as a period-boundary test, extending an existing closed-form-segment boundary list rather than adding a parallel tick loop
- **Computing "when does the next period boundary fall" from first principles every time, rather than caching a `NextDecayTick` field** — `EarliestDecayTickUpTo` (`src/MW3.Core/Match.cs:712-726`) re-derives the next multiple of `MoraleTable.DecayPeriodTicks` after `LastSendTick` on every call: `periodsElapsed = sinceLastSend / DecayPeriodTicks; nextBoundary = lastSend + (periodsElapsed + 1) * DecayPeriodTicks`. Why here: `LastSendTick` can jump backward in wall-clock terms relative to the *previous* decay schedule the moment a new send resets it (D-38's rule: only a send resets the idle timer) - a cached "next decay tick" field would need to be invalidated and recomputed at exactly that moment, which is one more place a reset could be forgotten. Deriving it fresh from `LastSendTick` and the current tick means there is no cached value that can go stale; the schedule is a pure function of state that already exists. Pitfall: recomputing from scratch is only cheap here because the formula is `O(1)` integer arithmetic - the same "derive, don't cache" instinct applied to something requiring a scan or a sort every call would trade a correctness risk for a real performance one, and the choice should be revisited if that ever stops being true.
- **`idleTicks % DecayPeriodTicks != 0` as the guard for "is this exactly a period boundary"** — `EvaluateDecayForPlayerAtTick` (`Match.cs:750-754`) only applies decay when the ticks since last send divide evenly by `MoraleTable.DecayPeriodTicks` (20). Why here: `Advance` can be called with arbitrary tick chunks (a UI frame, a test's `Advance(1000)`, anything) rather than one tick at a time, so decay can't just be "an event that fires every 20th call" - it has to recognize the boundary by its absolute tick value, however it's reached. This is the same shape as the pre-existing `EarliestBoundaryTickUpTo` machinery (arrivals, construction completions, wave launches) already uses to stay correct under chunked advancement (D-12, D-14, D-15) - FR-3 slots `EarliestDecayTickUpTo` into that same list (`Match.cs:608, 620`) rather than writing a parallel "tick through every tick and check modulo" loop, which would have been both slower and a second, possibly divergent, implementation of chunk-safety. Pitfall: the modulo guard is only correct because decay's period (20 ticks) is checked against a boundary the caller is already guaranteed to land on exactly - a boundary tick that arrived through the *wrong* list (e.g. skipped because a caller bypassed `EarliestBoundaryTickUpTo`) would silently never satisfy the modulo check and decay would just never fire, with no exception to say why.
- **A period-boundary event added to a list of segment-boundary reasons a closed-form-integration engine already needed** — production and combat resolve a whole tick-span in one closed-form computation per segment (D-21a) rather than looping tick-by-tick, so *any* event that changes behavior mid-span (an arrival, a completion, a wave launch, and now decay) has to be a segment boundary or the closed-form math would silently apply the wrong rate across the point where the event should have taken effect. Why here: FR-3 could have evaluated decay as a special case bolted onto `Advance`'s outer loop instead of teaching `EarliestBoundaryTickUpTo` about it, but that would have created two different definitions of "when does a segment end," which is exactly the kind of parallel-implementation drift the codebase's shared predicates elsewhere (`CombatResolver.WouldCapture`, `TravelTimeCalculator`) already exist to prevent. Pitfall: adding a new boundary reason to a `min()`-style aggregator (`EarliestBoundaryTickUpTo` takes the earliest of four candidate ticks) is easy to get right when the new reason genuinely is a boundary; it's easy to get *silently* wrong if a future addition to that list has an off-by-one in its own "earliest tick at or before X" helper, since the aggregator has no way to sanity-check the sub-results it's minimizing over - it trusts each contributor completely.

Try next: `EarliestDecayTickUpTo` computes each player's next boundary independently and `EarliestBoundaryTickUpTo` takes the `min` of the two implicitly (via `EvaluateDecayAtTick` evaluating both players at whatever tick the aggregator picked). Sketch what would need to change if a future feature made the two players' decay periods genuinely different (e.g. a passive skill that shortens one player's decay interval) - would `EarliestDecayTickUpTo` need to become per-player and get called twice from `EarliestBoundaryTickUpTo`, and would that still fit cleanly into the single `long?` "earliest boundary" the aggregator returns today?

## 2026-08-05 — #71 FR-4: Morale raises unit speed, locked at the send's submission tick
Concepts: converting an internal-state read into a parameter to freeze a value at the right moment, "read once, pass down" as the mechanism for a lock-at-submission invariant, a static helper as the one legal place a formula is allowed to live
- **Turning `TravelTimeCalculator.ComputeTicks`'s internal read of a `Match` constant into a `speedUnitsPerTick` parameter** — before FR-4, travel time was computed from a single fixed constant (`Match.ArmySpeedUnitsPerTick`) that never changed, so reading it directly inside `ComputeTicks` was harmless; once speed became morale-dependent and therefore *time-varying*, the same internal read would have silently picked up whatever morale happened to be true at the moment `ComputeTicks` was called - which, deep inside `Advance`'s tick-by-tick resolution, is not necessarily submission time. Why here: `ComputeTicks` (`src/MW3.Core/TravelTimeCalculator.cs:14-21`) now takes `speedUnitsPerTick` as a parameter instead, and its doc comment states directly why: "read once by the caller at submission and passed in, never recomputed here, so this stays a pure function of its inputs (D-39)." The type signature itself is what makes the lock-at-submission rule impossible to violate by accident - there's no `Match` reference inside the function left to misuse. Pitfall: a pure function only stays pure if *every* caller actually reads the value once and passes the same one through - the parameter change moves the responsibility for "when do we read speed" to the caller, and a caller that recomputed `EffectiveArmySpeedUnitsPerTick` fresh on each wave (instead of once per send, as `Match.Execute` correctly does at `Match.cs:151`) would silently reintroduce the exact bug the parameter was meant to prevent, with nothing in `ComputeTicks`'s signature able to catch it.
- **"Read once, pass down" as the actual mechanism implementing a business rule (D-39), not just a performance choice** — `Match.Execute(SendArmyCommand)` reads `EffectiveArmySpeedUnitsPerTick(MoraleOf(command.IssuingPlayer).Level)` exactly once (`Match.cs:151`) before the loop that builds every wave of the send, and the resulting `travelTicks` is reused for each wave's `ArrivalTick` computation. Why here: D-39 requires the whole send (every wave) to share one locked-in speed from the moment of submission - not a live-refreshed speed to a per-wave-refreshed one that could let a later wave, launched into an already-higher morale, travel faster and overtake an earlier wave still in flight. The "read once, hoist above the loop" shape is the same pattern that FR-1's netted-delta fix and FR-3's derive-not-cache scheduling both leaned on elsewhere in this codebase - reading a value once at the correct moment and threading it through, rather than letting each consumer re-derive its own answer from live state. Pitfall: this only enforces the lock if the read genuinely happens before anything that could observe or act on a changed morale value in between - if a future refactor moved the morale read into the per-wave loop "for clarity," the code would still compile and run, just quietly reintroduce a bug identical to the one D-39 exists to prevent.
- **A `static` composition function (`Match.EffectiveArmySpeedUnitsPerTick`, `Match.cs:29-30`) as the single place morale-speed math is allowed to exist** — `ArmySpeedUnitsPerTick * MoraleTable.UnitSpeedPercentage(moraleLevel) / 100.0` is the entire formula, called from `Match.Execute` and from all three of `AiBrain`'s prediction sites, with no other call site anywhere permitted to reconstruct it inline (D-22's "no morale literal outside the table" rule, extended here to "no *formula* duplicated outside one function" too). Why here: `AiBrain` needs to predict what speed a not-yet-submitted command *would* lock in, using its own live morale - correct per this feature's semantics, since prediction time and eventual submission time are close enough that "the AI's current morale" is the best available proxy - but that means five call sites (one real, four predictive) all need the exact same arithmetic, which is exactly the scenario `WouldCapture` and `TravelTimeCalculator` were already extracted to prevent for combat and travel time respectively. Pitfall: because the function is `static` and takes only a `moraleLevel` int, nothing stops a caller from passing the *wrong player's* level (e.g. predicting with the defender's morale instead of the attacker's) - the function's purity guarantees consistency of the formula, not correctness of which morale value gets fed into it; that responsibility still sits entirely with each call site.

Try next: `AiBrain.TryDefend`'s candidate loop now hoists `EffectiveArmySpeedUnitsPerTick(match.MoraleFor(Player).Level)` above the loop (found and fixed during this feature's review) because the AI's own morale can't change mid-loop. `TryAttack` and `TotalExpectedTowerLoss` also call the same helper - check whether either of them has the same redundant-recomputation shape inside a loop, and if the codebase would benefit from a small internal convention ("read once per `Decide()` call, pass down") the way `Match.Execute`'s single upfront speed read already models for real sends.

## 2026-08-05 — #77 FR-5: The morale meter drawn for both players
Concepts: viewport-relative geometry instead of resolution-specific constants, a stateless static class as the boundary between engine-free rules and MonoGame drawing, `ArgumentNullException.ThrowIfNull` as a guard clause
- **Computing every drawn rectangle as a fraction of `Viewport.Width`/`Height` rather than a fixed pixel offset** — `MoraleMeter.GetHumanSunRect`/`GetAiSunRect` (`src/MW3.Game/MoraleMeter.cs:27-51`) derive `sunSize`, `spacing`, `margin`, and each sun's position from `Math.Min(viewport.Width, viewport.Height)` multiplied by fixed fractions (`_sunSizeFraction = 0.028f`, etc.), never a literal pixel count. Why here: the acceptance criteria required the meters to stay legible and correctly positioned at both the desktop window's size and the Android device's ~1808x1018 surface, two resolutions with a different aspect ratio and pixel density - a fixed-pixel layout tuned for one would either overflow or look tiny on the other, while a fraction-of-min-dimension layout scales proportionally to whichever is the tighter constraint. Pitfall: `Math.Min(Width, Height)` assumes the UI should scale with the *smaller* dimension (right for elements that must stay clear of both a portrait and landscape edge); an element meant to hug one specific axis (e.g. always a fixed height regardless of width) would need a different fraction basis, and reusing this exact pattern there would produce inconsistent apparent sizes.
- **A `static` class with zero mutable state as the seam between `MW3.Core`'s engine-free rules and MonoGame's rendering types** — `MoraleMeter` (`MoraleMeter.cs:16`) takes `Viewport`, `SpriteBatch`, and `Texture2D` (all MonoGame types) as parameters into a `Draw` method, but never stores a reference to `Match` or any morale state between calls - `MatchScreen.Draw` passes `_match.HumanMorale.Level`/`_match.AiMorale.Level` in fresh every frame (`MatchScreen.cs`, the two new `MoraleMeter.Draw` calls). Why here: `MW3.Core` (S-1) can't reference MonoGame at all, so any code that needs both morale state and pixel geometry has to live in `MW3.Game` and take the morale value as a plain `int` parameter rather than reaching into `Match` itself - the class boundary is what makes "read every frame, never cached" true by construction rather than by convention, mirroring `WaveColumnPresentation`'s existing shape for the same reason. Pitfall: because nothing forces a caller to pass a *fresh* level each frame, a future `MatchScreen` refactor that read `_match.HumanMorale.Level` once in a field and reused it across frames would compile fine and silently violate the "same-frame reflection" acceptance criterion - the stateless design only holds as long as every caller keeps calling it correctly.
- **`ArgumentNullException.ThrowIfNull(spriteBatch)` / `(circleTexture)` at the top of `Draw`** — a one-line static guard clause that throws immediately if either MonoGame object is null, before any drawing is attempted (`MoraleMeter.cs`, start of `Draw`). Why here: `Draw` is called every frame from `MatchScreen`, so a null `SpriteBatch` (e.g. called before `LoadContent` finished) would otherwise fail deep inside `SpriteBatch.Draw` with a less obvious `NullReferenceException`; failing fast at the entry point with a named-parameter message makes the actual cause immediately visible. Pitfall: `ThrowIfNull` only validates *presence*, not *validity* - a `SpriteBatch` that exists but was never `Begin()`-called, or a disposed `Texture2D`, both pass this guard and still throw later, so it narrows one failure mode without covering the others.

Try next: `MoraleMeter` duplicates `sunSize`/`spacing`/`margin` computation identically between `GetHumanSunRect` and `GetAiSunRect` (flagged as a non-blocking nit in review). Try extracting that shared geometry into one private helper returning `(int sunSize, int spacing, int margin)`, and check whether the two `Get*SunRect` methods still read clearly once they're reduced to just the human/AI-specific left/right or top/bottom placement math.

## 2026-08-05 — #78 FR-6: The AI opponent plays for morale and against decay
Concepts: sequential (lexicographic) comparison keys kept as separate variables instead of one blended score, an early-`continue` guard restructuring a single compound condition into ordered stages, a private static helper as the one place a derived heuristic is allowed to compute itself
- **Two separate `best*` fields compared in sequence, not folded into one weighted score** — `TryAttack`'s candidate loop (`src/MW3.Core/AiBrain.cs`, around line 344-396) tracks `bestExpectedTowerLoss` and `bestMoraleSwing` as two independent `int` locals, and replaces the current best only via `expectedTowerLoss < bestExpectedTowerLoss || moraleSwing > bestMoraleSwing` guarded behind an earlier `if (expectedTowerLoss > bestExpectedTowerLoss) continue;`. Why here: the issue explicitly rejected "a weighted single-utility combination of tower loss and morale" at kickoff (D-22's spirit of not inventing an unpublished constant - a blend would need an arbitrary weight, which is exactly the kind of unsourced number the project avoids), so the two criteria had to stay comparable in strict priority order rather than summed into one number that a weight could quietly bias. Pitfall: this shape is easy to get backwards without noticing - reviewing `expectedTowerLoss < bestExpectedTowerLoss || moraleSwing > bestMoraleSwing` in isolation looks like an OR of two independent wins, and only reads correctly as "primary key, then secondary key" because of the `continue` above it that already filtered out anything worse on the primary key; deleting that guard would silently turn the tiebreak into a real blend.
- **An early `continue` per stage instead of one large compound `if`** — the same loop iteration first `continue`s past non-winnable candidates (`WouldCapture` false), then `continue`s past candidates strictly worse on tower loss, before ever computing `moraleSwing` at all. Why here: computing `PredictedMoraleSwing` calls `CombatResolver.Resolve` again (a second combat simulation beyond the `WouldCapture` check), so gating it behind the cheaper tower-loss comparison first avoids the more expensive call for candidates that were already going to lose on the primary key - correctness and a minor cost saving from the same restructuring. Pitfall: `continue`-per-stage reads clearly for *this* candidate but hides the overall selection logic from anyone skimming just the final `if` - understanding *why* a candidate was or wasn't chosen now requires reading the whole loop body in order, not just the last comparison line, which is a real readability cost the review judged acceptable given the compound alternative would have hidden the priority order even harder.
- **A `private static` helper (`PredictedMoraleSwing`) as the one place this feature's formula is allowed to exist** — the morale-swing computation isn't inlined into the loop; it's a separate method taking exactly the values the loop already has in scope (`attackerIndex`, `defenderIndex`, `attackingUnitCount`, `predictedGarrison`, `expectedTowerLoss`, `target`) and returning one `int`. Why here: this mirrors the same "one legal place a formula is allowed to live" shape `Match.EffectiveArmySpeedUnitsPerTick` established for FR-4 (see #71's entry above) - `TryAttack` is already the only call site today, but keeping the formula in a named, independently readable method (with an XML doc citing D-41 and the exact `MoraleTable` members it reads) makes it something a future second call site could reuse instead of re-deriving, and makes the "no new literal morale number" acceptance criterion checkable by reading one function instead of auditing the whole loop for embedded arithmetic. Pitfall: being `private` and `static` guarantees purity of *this* function (no hidden `this` state to read), but says nothing about whether its caller passes the *right* values - the same caveat #71's log entry raised for `EffectiveArmySpeedUnitsPerTick` applies identically here: `PredictedMoraleSwing`'s correctness is about the formula, not about whether `TryAttack` computed `attackerIndex`/`defenderIndex` for the right player.

Try next: `code-reviewer` flagged (non-blocking) that the full-equality case - a candidate tied with the current best on *both* `expectedTowerLoss` and `moraleSwing` - isn't covered by an explicit test, only verified by manual trace of the `||` condition. Write that test: two candidates with identical `expectedTowerLoss` and identical `moraleSwing` (same type/level/ownership and same predicted deaths), and assert the existing distance-then-id fallback order picks the same target the pre-FR-6 code would have.


## 2026-08-05 — #82 FR-1: Forge base type, explicit-target conversion, and an injectable map layout
Concepts: constructor chaining, readonly record struct, static readonly lookup array over an enum
- **Constructor chaining with `: this(...)`** — the parameterless `Match()` now reads `: this(MapLayout.Slots)` and does nothing else, so all base-building logic lives in exactly one place, the new `Match(IReadOnlyList<MapSlot> layout)` constructor (`src/MW3.Core/Match.cs:45-48`). Why here: the acceptance criterion required the default match to be provably identical to one built from an explicit layout — chaining makes that a language-level guarantee (there is only one code path to diverge) rather than something a test has to keep re-proving as the two constructors evolve. Pitfall: chained constructors run in a fixed order (the target constructor's body executes *before* the caller's remaining statements) — if `Match()` needed anything set up before delegating (it doesn't here), that ordering constraint would bite silently.
- **`readonly record struct` for `MapSlot`** (`src/MW3.Core/MapSlot.cs`) — a small, immutable, value-typed data carrier (position, kind, garrison, and now type/level) that's compared and copied by value. Why here: `MapSlot` needed to go from `internal` to `public` so `Match`'s new constructor could accept caller-built layouts, and a record struct gives free structural equality and an immutable API surface for a five-field bag of data with no behaviour of its own — exactly what a test-authored layout literal needs. Pitfall: record structs are copied by value on every pass — fine for a five-field struct like this one, but the same pattern on a struct with a large array field would silently start copying that array's reference-holding struct on every method call, which is easy to miss since the syntax looks identical to a cheap copy.
- **A `private static readonly BaseType[] _convertibleTypes`** (`src/MW3.Core/Match.cs:32-35`) as the single source of "every base type, in a stable order" — `AvailableActions` loops over it and skips the base's own type, rather than switching on `target.Type` to compute one hardcoded opposite. Why here: the old code (`target.Type == BaseType.Producer ? BaseType.Tower : BaseType.Producer`) was a two-way toggle that had no way to express "one of N other types"; iterating a declared-order array scales to any number of `BaseType` members without a new branch per type, and keeps the convert-button order stable even if the enum's ordinal values are reordered elsewhere in the future. Pitfall: the array's *order* is now load-bearing (D-48 pins it to enum declaration order for UI button stability) but nothing in the type system enforces that the array actually matches `BaseType`'s declaration order — a future added `BaseType` member that isn't also added here would silently vanish from every base's convert menu instead of failing to compile.

Try next: add a test that asserts `_convertibleTypes` (or equivalently, `AvailableActions`'s convert-action ordering) actually matches `Enum.GetValues<BaseType>()` in order, so a future `BaseType` member addition fails a test immediately instead of silently under-offering conversions — the kind of enforced-invariant gap FR-4's kickoff (#83) explicitly called out as worth avoiding for the analogous "forge always validates its level" case.

## 2026-08-06 — #83 FR-4: Morale gains and losses for capturing and losing forges
Concepts: a nested static class delegated to from a switch expression arm, a private guard method shared by two public members, `FormattableString.Invariant` for culture-safe interpolated exception messages
- **`MoraleTable.CaptureGain`'s switch expression delegates its `BaseType.Forge` arm to a same-shaped nested class** (`src/MW3.Core/MoraleTable.cs:113`) — `BaseType.Forge => Forge.CaptureGain(level, wasOpponentOwned)`, exactly mirroring the existing `Village`/`Tower` arms, even though `Forge`'s own methods hold no per-level ladder (just two constants and a level check). Why here: the acceptance criteria required forge morale to enter through `MoraleTable` the same way every other type does (D-22), and keeping `Village`/`Tower`/`Forge` structurally parallel — three nested classes with the same `CaptureGain(level, wasOpponentOwned)`/`CaptureLoss(level)` shape — means a reader who already understands one understands all three, even though `Forge`'s internals (fixed constants instead of an array lookup) are simpler than its siblings'. Pitfall: structural parallelism is a readability choice, not an enforced one — nothing stops a future edit from giving `Forge` a different method shape than `Village`/`Tower` while the switch expression still compiles fine, so the "matches its siblings" property has to be maintained by convention, the same way `MoraleTable`'s own doc comment says D-22 must be maintained by convention rather than the compiler.
- **A `private static void RequireForgeLevel(int level)` shared by `Forge.CaptureGain` and `Forge.CaptureLoss`** (`src/MW3.Core/MoraleTable.cs:218-227`) — both public methods call it as their first line before touching `level` again. Why here: the acceptance criteria required *both* capture-gain and capture-loss to throw for any level other than `LevelTable.MinLevel`, and writing the same `if (level != LevelTable.MinLevel) throw ...` twice would let the two checks silently drift (different messages, or one updated without the other) the next time this class changes; factoring it into one guard method makes "both methods enforce the identical invariant" true by construction. Pitfall: a shared guard method only stays correct if every new caller that needs the same validation actually calls it — a third method added later to `Forge` that forgot to call `RequireForgeLevel` would compile fine and silently skip the invariant, exactly the kind of gap the `_convertibleTypes` entry above (#82) already flagged for a different array-shaped invariant.
- **`FormattableString.Invariant($"A forge is only ever at level {LevelTable.MinLevel}.")`** (`MoraleTable.cs:225`) instead of plain string interpolation — used for the exception message inside `RequireForgeLevel`, matching the pattern `MoraleTable.IndexOfLevel` and `IndexOfCaptureLevel` already use elsewhere in this file. Why here: a plain `$"..."` string formats its interpolated values using the *current thread's culture*, which on a machine with a non-`.`-decimal locale could format an embedded number differently between runs — `FormattableString.Invariant` forces invariant-culture formatting so the message text (and the `qa/scripts/morale-forge-capture.txt` derivation that must reproduce byte-for-byte) is guaranteed identical on every machine. Pitfall: it's easy to reach for plain `$"..."` out of habit since both compile and both look identical at the call site — the difference only shows up as a test or QA script that occasionally fails on a machine with different regional settings, which is exactly the kind of intermittent, hard-to-repro failure invariant formatting exists to rule out entirely.

Try next: `ForgeMoraleTests.cs` builds its neutral-forge layout as a `private static readonly MapSlot[] _layoutWithNeutralForge` field shared across several tests. Compare it against `MapLayoutInjectionTests.cs`'s equivalent inline array (declared fresh inside one test method) and decide whether the shared-field version risks one test's mutation of a `Base` leaking state into another via the same `MapSlot[]` — `MapSlot` is a `readonly record struct` (see #82's entry above), so the array itself can't be mutated, but check whether `Match`'s constructor could ever hold a reference into it rather than copying.

## 2026-08-06 — #86 FR-2: The map gains a contested neutral forge and neutral tower
Concepts: `is not Player x`/`is null` pattern matching over a nullable owner, reflection against a compiler-enforced-immutable auto-property's backing field, a type-pattern binding removed to widen a guard
- **Type-pattern binding removed to widen a guard, plain `is null` kept for the narrower one** (`src/MW3.Core/Match.cs`, `EvaluateTowerFireAtTick`) — the old guard read `tower.Owner is not Player towerOwner` (binds `towerOwner` only when non-null, `continue`s otherwise); the new code reads `var towerOwner = tower.Owner;` unconditionally, because a neutral tower (`Owner is null`) must now *fall through* and still fire, just with no player to award. The bind-and-filter pattern only makes sense when "null" means "skip"; once null became a legitimate case to keep processing, the pattern match had to be replaced with a plain assignment plus a later `if (towerOwner is not null)` at the one call site (`AwardMorale`) that genuinely can't take a null player. Pitfall: `is not Player x` reads as "skip if null" so naturally that it's easy to reach for it reflexively on any nullable-reference guard — the tell that it's wrong here is that the *rest* of the method still needs the tower even when the pattern would have filtered it out.
- **Reflection against `<PropertyName>k__BackingField` to construct a state real code can never reach** (`tests/MW3.Core.Tests/NeutralForgeAndTowerTests.cs`, `NeutralTower_NeverFires_AtAnArmyWithNoOwner`) — `Army.Owner` is `public Player Owner { get; }` (no setter at all, not even `private set`), because every real army always has an owner; the acceptance criterion needed a test that pins the tower's null-owner guard even though nothing in the game can currently produce a null-owner army. `typeof(Army).GetField("<Owner>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(army, null)` reaches past the property entirely to the compiler-generated field auto-properties still compile down to. Pitfall: the backing-field name is an implementation detail of how the C# compiler lowers auto-properties (`<Owner>k__BackingField` today) — nothing guarantees it survives a future compiler version or a switch to a manually-backed property, so this specific reflection call is more fragile than the `GetSetMethod(nonPublic: true)` reflection this codebase's other tests already use against properties that at least declare a private setter.
- **A guard consolidated from three clauses to two, gated on a different condition than before** (`src/MW3.Core/AiBrain.cs`, `TotalExpectedTowerLoss`) — `candidate.Type != BaseType.Tower || candidate.Owner is null || candidate.Owner == Player` shrank to `candidate.Type != BaseType.Tower || candidate.Owner == Player`, because an unowned tower is now a real threat and must stop being filtered out, while a friendly tower must still be. Why here: this is the same "null used to mean skip, now it means keep processing" shift as the `Match.cs` guard above, but on the *reading* side rather than the *firing* side — `Owner == Player` still works unchanged when `Owner` is null (`null == Player` is simply `false`), so only the now-redundant `Owner is null` clause needed removing, not a restructure. Pitfall: `Owner == Player` silently doing the right thing for a null `Owner` is a nice property of reference-type equality, but it's easy to assume equality checks against a nullable need an explicit null guard first — here that guard would have been actively wrong (it would have excluded the exact case the feature needed to include).

Try next: the reflection-based backing-field test above is the only place in this codebase that reaches past a *fully-immutable* auto-property (no setter of any accessibility) rather than one with a private setter. Try writing the equivalent test a different way — e.g. a test-only internal constructor overload on `Army`, or a small test double — and compare readability/fragility against the reflection approach; decide which convention this codebase should standardize on for "state real play can never produce."

## 2026-08-06 - #87 FR-3: Forge count buffs attack and defence globally, capped at four
Concepts: a breaking signature change chosen over a defaulted parameter, clamping inside a private index-mapper, derive-on-read over a cached field, mutation testing as the check that a test is load-bearing
- **A required parameter added deliberately so call sites stop compiling** - `ComposeAttackerIndex(int moraleAttackPercent, int forgeAttackPercent)` and `ComposeDefenderIndex(int, int, int)` (`src/MW3.Core/CombatResolver.cs:46,63`) each gained a parameter with **no default value**, and `ForgeContributionPercent` was deleted rather than kept as a fallback. C# would happily have accepted `int forgeAttackPercent = 100` and left every existing call site compiling untouched. Why here: the whole risk this feature was written against is a call site that silently keeps composing two terms when the rules now say three - a default value makes that failure invisible and permanent, whereas a required parameter converts it into a compile error the developer must answer. The build listing every broken call site *was* the audit. Pitfall: this only works when the parameter has no sensible default. Reach for it reflexively and you get churn; the test is whether a wrong-but-compiling call would be a real defect, which is exactly the case here and exactly why `docs/forges/REQUIREMENTS.md` FR-3 pre-settled it rather than leaving it to build mode.
- **Clamping inside the private index-mapper, not at the public boundary** - `ForgeTable.IndexOfCount` (`src/MW3.Core/ForgeTable.cs:50`) throws for `forgeCount < MinForgeCount` but returns `MaxContributingForges` for anything above it, so both public lookups get clamp-above/throw-below behaviour from one place. Why here: the two directions mean genuinely different things - holding five forges is legal play that simply buys nothing, while a negative count is a caller bug - and encoding that asymmetry once in the shared mapper stops the two public methods from ever drifting apart on it. Note the contrast with this file's siblings: `LevelTable.IndexOfLevel` and `MoraleTable.IndexOfLevel` throw in **both** directions, because a level above the ladder really is nonsense. Same shape, deliberately different policy. Pitfall: a private helper that both validates and maps is doing two jobs, so its name has to carry the policy or a reader will assume it matches its siblings - which here it deliberately does not.
- **Deriving state on read instead of caching it in a field** - `Match.ForgeCountFor` (`src/MW3.Core/Match.cs:1124`) walks `_bases` on every call rather than maintaining an `_forgeCount` updated at capture, loss, conversion-in and conversion-out. Why here: a cached count is a second source of truth needing a write on four separate paths, and the first path added later without one drifts silently - the exact desync follow-up #68 was filed about for building defence. Deriving makes all four correct by construction. The cost is real (this is the per-tick combat path), so it is an index loop with no LINQ and no temporary list, and `AiBrain.TryAttack` hoists the loop-invariant read out of its target loop (`src/MW3.Core/AiBrain.cs:327`). Pitfall: "derive, don't cache" is right until the derivation is genuinely hot - the honest version of this decision is that an 8-element loop is free, not that caching is always wrong.
- **Mutation testing as the check that a test is load-bearing** - the `code-reviewer` replaced each forge argument with a literal `100` and re-ran the suite: three of `AiBrain`'s four terms could be neutered with all 574 tests still green. The desync test recomputed the indices inline and never constructed `AiBrain`, so it pinned the arithmetic while its own doc comment claimed it pinned the wiring. Why it matters here: "the AI's prediction agrees with the resolution" is a claim about *wiring*, and only a test that drives `AiBrain.Decide` can hold it - the fix was three such tests (`tests/MW3.Core.Tests/ForgeCombatTests.cs`), each confirmed to fail when and only when its own term is neutered. Pitfall: a green suite proves the tests pass, never that they would fail if the code were wrong. Any test whose assertions are computed by the same code path under test is at risk of this, and a passing test is the worst place for it to hide.

Try next: apply the same mutation check to the two `Match.ResolveArrival` forge terms and to the older morale terms beside them - replace each with `100` and confirm exactly one named test goes red. Where a mutation survives, the interesting question is whether the missing test is worth writing or whether the argument itself is unreachable in practice; both answers are useful, and the second one occasionally deletes code.
## 2026-08-07 - #89 FR-5: Forges drawn on both heads, with per-type convert labels and a count
Concepts: deriving a shape's own centre from closed-form geometry instead of its bounding box, anchoring one widget's layout off another's public accessors instead of a duplicated constant, a rasterizer sampling a pixel row's far edge instead of its centre, a structural (type-system) uniqueness guarantee versus a tested one
- **Deriving the incenter and inradius from the standard triangle identities, in code, rather than eyeballing a magic offset** - `TriangleGeometry.IncenterYFraction`/`InradiusFraction` (`src/MW3.Game/TriangleGeometry.cs`) compute `(a*0 + b*D + c*D) / (a+b+c)` and `Area / semiperimeter` from the triangle's own three side lengths, rather than hardcoding the `0.691`/`0.309` fractions those formulas evaluate to. Why here: the acceptance criterion was "the garrison digits stay fully inside the triangle," and the bounding-box centre the circle/square glyphs already use sits *outside* an apex-heavy triangle's true middle - centring text there would overhang the sloped sides exactly as the issue warned. Deriving the fractions from geometry rather than measuring them off a screenshot means the formula stays correct if the triangle's own shape (apex angle, aspect) ever changes, and a reviewer can re-check the math from first principles instead of trusting a decimal. Pitfall: it is tempting to skip the derivation and just tune a constant until it "looks right" on one test viewport - that constant then silently stops being correct the moment anything about the shape's proportions changes, with no compiler or test to say so unless (as here) a headless test checks the *geometric property* (the incircle actually fits inside the rasterized shape) rather than merely a specific number.
- **Anchoring a new widget's position by reading a sibling widget's own public rects, not a duplicated spacing constant** - `ForgesReadout`'s private `SunSpacing` (`src/MW3.Game/ForgesReadout.cs`) computes the gap as `MoraleMeter.GetHumanSunRect(1, viewport).Left - MoraleMeter.GetHumanSunRect(0, viewport).Right` instead of copying `MoraleMeter`'s private `_sunSpacingFraction` into a second literal. Why here: the readout has to sit "immediately right of the fifth sun," and if that spacing were re-declared as its own constant, the two widgets' actual visual gap could drift out of sync the next time `MoraleMeter`'s own spacing was tuned - exactly the kind of duplicated-source-of-truth bug this codebase's own conventions (`ForgeTable`, `LevelTable`, `MoraleTable` all as single sources, D-22) exist to prevent, just applied to layout instead of game rules. Pitfall: deriving a value from two calls to a public accessor is doing real work at draw time (two rect computations) purely to avoid duplicating a float - for a hot per-frame path with many such reads this cost could matter; here it doesn't, because both accessor calls are cheap arithmetic with no allocation.
- **The triangle rasterizer samples each row's far (bottom) edge instead of its centre, unlike the circle/square rasterizers it sits beside** - `TriangleGeometry.Contains` (see its own doc comment) computes `t = (pixelY + 1) / diameter` rather than `(pixelY + 0.5) / diameter`. Why here: a centre-sampled apex row would need a half-width of at least 0.5 pixels to include even the single centre column, and the true triangle's half-width at a row's *centre* y-coordinate is smaller than that until several rows down - centre-sampling left the apex looking flat-topped instead of pointed (caught by a failing test, not by inspection). Far-edge sampling is the standard conservative rule for rasterizing anything that must come to a point. Pitfall: mixing sampling conventions across shapes in the same file is a real inconsistency a future reader could "fix" by making them uniform, which would silently blunt the triangle's apex again - the doc comment on `Contains` exists specifically to explain why this one shape's convention differs from its neighbours.
- **A uniqueness guarantee proved structural rather than merely tested** - `BaseActionMenuLabelTests.NoTwoButtonsOnOneMenu_EverCarryTheSameText` passes today, but the *reason* it will keep passing isn't the test itself: `Match.AvailableActions` (phase 6 FR-1, D-48) offers exactly one convert action per `BaseType` other than the base's own, so `BaseActionMenu.FormatLabel`'s convert arm can never see two actions with the same `ConvertTargetType` on one menu - the type system and the enum's fixed membership make the collision unreachable, not just unobserved. Why it matters: a test that merely asserts "these three specific labels differ today" would need updating (or worse, wouldn't catch a regression) if a label-formatting change accidentally introduced a case where two targets could render the same text; a test that also documents *why* the guarantee holds gives a future reader the actual invariant to preserve, not just today's snapshot of it passing. Pitfall: this only works because the guarantee is genuinely structural - dressing up a coincidental pass as if it were structural (without checking the invariant that makes it so) is worse than not commenting on it at all, since it teaches false confidence.

Try next: `TriangleGeometry.Contains` is checked for correctness by `TriangleGeometryTests` against synthetic pixel/angle grids, but nothing pins it against `MatchScreen`'s actual on-screen text placement end to end - the closest thing is the QA screenshot a human reviews by eye. Try writing a headless test that renders the same incenter/inradius math against a few representative garrison digit-counts (1, 2, 3 digits) and asserts the *measured* text bounding box (`SpriteFont.MeasureString` scaled the way `MatchScreen.Draw` scales it) stays within the incircle - the code-reviewer's one Minor finding on this feature was exactly that an uncapped forge's garrison could in principle grow past what today's scenario exercises, and a test like this would turn "not exercised by any current script" into "provably safe at N digits."

## 2026-08-07 - #93 FR-6: The AI opponent builds, contests, and defends forges
Concepts: null-coalescing to pick the first match from an ordered scan, integer (floor) division as a deliberate threshold rule, splitting one method into two named strategies instead of a parameter flag, the gap between "the test passes" and "the property is actually guaranteed"
- **`??` to keep "first match in id order" without an early `break`** - `TryDefend`'s rewritten scan (`src/MW3.Core/AiBrain.cs:85-113`) walks every own base once, and `threatenedForge ??= ...` / `if (threatenedOther is null) threatenedOther = ...` each fire only on their group's *first* hit, because `ownBases` is already ascending by id. Why here: the old code could `break` the moment it found any threatened base, because there was only one priority group; adding a second group (forge outranks non-forge) means the scan can no longer stop early - it has to keep going in case a lower-priority hit turns out to belong to the higher-priority group later in the list. `??=`/`is null` express "keep the first, ignore the rest" without a second flag variable or an early return, at the honest cost (a Minor review note, not a defect) of always finishing the loop instead of short-circuiting. Pitfall: `??=` here works *because* the list is pre-sorted; the same one-liner on an unsorted collection would silently keep whichever element happened to be first in iteration order, not the lowest id - the ordering guarantee lives one method up (`CollectOwnBasesAscendingById`) and is easy to lose sight of while reading this method alone.
- **Integer division as the rule, not a rounding compromise** - `ForgeCountFor(Player) < producerCount / ForgeTable.ProducersPerForge` (`AiBrain.cs:292`) relies on C#'s `int / int` truncating toward zero: 3 producers gives `3/4 = 0` (never owed), 4 gives `4/4 = 1` (owed once), 7 gives `7/4 = 1` (still owed only once, not 1.75). This is MW2's own published ratio (`MW2-RULES.md` §2.4, "one forge per four producers") expressed exactly as a threshold rather than approximated - there is no rounding error to reason about because the floor *is* the rule the AI must follow. Pitfall: it's easy to read `a < b / c` and reflexively worry about the division happening before the comparison losing precision (as with the basis-point forge/morale composition elsewhere in this codebase, D-46) - here that's backwards: the truncation is exactly what "one forge per four, not per fraction of four" means, and rewriting it as `a * c < b` to "avoid" the division would silently change the rule at large producer counts (e.g. 4 vs 7 producers no longer give the same 1-forge answer once cross-multiplied against a naive `<` un-floored).
- **One method split into two named strategies instead of one method with a type parameter** - `TryConvert` (`AiBrain.cs:271-297`) now only computes the ratio and dispatches to `TryConvertToForge` or `TryConvertToTower`, each a near-identical loop over the same `IsConvertCandidate` guard but comparing `NearestNotOwnedDistance` in opposite directions (rear-most vs. front-most). The two loops share structure but not a single line differs into a shared helper - they're kept as two named methods rather than one parameterized by "which comparison direction" or "which `BaseType`". Why here: `TryConvertToTower` had to stay *byte-for-byte identical* to the pre-FR-6 method (so existing behaviour when no forge is owed provably doesn't change), and a shared parameterized version would make that claim require re-reading the parameterization logic instead of a diff showing "this method didn't move." The duplication is the thing that makes "unchanged" checkable by inspection. Pitfall: this trade only pays off because the two bodies are genuinely expected to diverge further later (forge candidacy already differs subtly - see the next point) - collapsing two things that happen to look alike today into one parameterized method is a common premature-abstraction mistake when the methods are actually two different policies that will keep drifting apart.
- **A test that measures the code's actual invariant, not a value it happened to output once** - the first version of `ZeroForgeBaseline_...` capped the AI's producer count by pre-assigning two neutral *bases* to the human (`Owner`, not `Type`); it passed, but only because the passive human was never actually eliminated within the test's tick budget by coincidence of the AI's conversion cadence, not because ownership was ever a real barrier - once extended past that budget the AI went on to conquer everything, including those two bases, yet still (for an unrelated reason) never quite reached 4 producers. Code review re-ran the same scenario to 200k ticks specifically to falsify the docstring's claim, and found it false in the way that mattered even though the assertion itself still passed. The fix changed the *map* so only 3 `Producer`-typed slots exist anywhere on it (`AiForgeBrainTests.cs`) - a bound on `Base.Type`, which nothing in `AiBrain`'s conversion vocabulary can ever increase, rather than a bound on `Base.Owner`, which combat freely reassigns. Pitfall: "the test is green" and "the property the test's own docstring claims is true" are different claims, and the gap between them is invisible from the test run itself - it only shows up when someone asks "what would make this test go red, and does that path actually reach here" rather than "does this test pass."

Try next: `TryConvertToForge` and `TryConvertToTower` (`AiBrain.cs`) differ today only in comparison direction and target `BaseType` - write a small internal test (or just trace it by hand) confirming there's no other silent divergence between them (e.g. both call the same `IsConvertCandidate`, both break ties toward the lower id the same way) now, while it's cheap to compare two short methods side by side. If a future feature gives forges their own candidacy rule (say, a garrison floor different from a tower's), that comparison won't be side-by-side anymore, and this is the last easy moment to confirm the two are aligned on everything except what's supposed to differ.

## 2026-08-08 - #102 FR-3: Armies detour around obstacles on a computed path
Concepts: a recursive local function closing over mutable state instead of an explicit stack, `ref` parameters for a boundary-clip algorithm that must report both a value and "keep going or stop", a nullable reference type (`List<int>?`) as the "no result" sentinel, exhaustive search deliberately chosen over a textbook shortest-path algorithm
- **A recursive local function (`Visit`) closing over `visited`, `path`, `best`, and `bestLength` instead of threading them as parameters or building an explicit stack** - `PathCalculator.FindShortestRouteIndices` (`src/MW3.Core/PathCalculator.cs:213-247`) declares `void Visit(int current, double lengthSoFar)` inside the outer method and has it read/mutate the enclosing locals directly, backtracking by adding a node to `path`/`visited` before recursing and removing it after. Why here: a DFS that enumerates *every* simple path (needed because D-52's tie-break has to compare whole candidate routes, not just track a running minimum) naturally wants recursion, and a local function lets the backtracking bookkeeping (`path.Add`/`RemoveAt`, `visited[next] = true/false`) sit right next to the recursive call instead of being threaded through five extra parameters or hidden inside a hand-rolled stack of frames. Pitfall: because `Visit` mutates the *same* `path` and `visited` collections on every call rather than each frame getting its own copy, forgetting either half of a mutate/undo pair (e.g. leaving `visited[next] = true` without the corresponding reset in a future edit) corrupts every sibling branch's view of the graph, not just the branch being edited - the local function's closure makes this convenient but also makes the bug's blast radius the entire search, not one call frame.
- **`ref double t0, ref double t1` in `ClipTest` to report both "should the caller keep clipping" (a `bool` return) and "here are the two updated bounds" from one call** - `PathCalculator.ClipTest` (`PathCalculator.cs:168-202`) is called four times in a row inside `SegmentCrossesInterior`, once per rectangle edge, each call narrowing `t0`/`t1` in place and returning `false` the moment the interval becomes empty. Why here: the classic Liang-Barsky clip algorithm is stated in terms of mutating a running `[t0, t1]` interval and bailing out early - returning a tuple `(bool, double, double)` and reassigning both locals after every call would say the same thing with more ceremony, and the four call sites read cleanly as a short-circuiting chain (`if (!ClipTest(...)) return false;`) precisely because the interval narrowing is a side effect rather than the return value. Pitfall: `ref` parameters make a method's true output invisible at a glance from the call site alone - `ClipTest(-dx, p0.X - obstacle.MinX, ref t0, ref t1)` doesn't visually distinguish "reads t0/t1" from "may rewrite them", so a reader has to already know the algorithm (or read the method body) to know `t0`/`t1` can change; this is a reasonable trade only because the four calls are packed together with no other statement between them, keeping the mutation window small and obvious.
- **`List<int>?` as the sentinel for "no path exists" instead of throwing or returning an empty list** - `FindShortestRouteIndices` returns `null` when no route connects the two nodes, and its caller (`ComputePath`) checks `if (routeIndices is null) { return StraightPath(from, to); }` rather than the caller having to distinguish "found the empty path" (a meaningless answer here - a route always has at least the two endpoints) from "found nothing at all". Why here: the acceptance criteria explicitly forbid throwing on a blocked send ("a send is never rejected for being blocked"), so the internal helper needs a way to say "I searched exhaustively and truly found nothing" that's unambiguous with "I found a valid answer", and `null` on a reference type does exactly that without inventing a wrapper type or an out-parameter. This mirrors #99's nullable-*value*-type-as-sentinel note, but here it's a nullable *reference* type (`List<int>` already permits null), which is why the `?` needed explicitly opting into nullable-reference-type annotations rather than being implicit. Pitfall: a `null` return only communicates "nothing found" to a caller that actually checks for it - `ComputePath` does, immediately, but a future caller of the internal helper that forgot the null-check would get a `NullReferenceException` deep inside whatever it did next, with no compiler help beyond the nullable-annotation warning (which is a warning, not a guarantee, unless the project treats warnings as errors - which this one does, `-warnaserror`, so the omission would actually have been caught at build time here).
- **Exhaustive DFS enumeration chosen over Dijkstra specifically because the tie-break rule can't be expressed as "shortest so far wins"** - `FindShortestRouteIndices`'s doc comment states the reason directly: D-52 requires that an *exact* length tie be broken by comparing the two full candidate routes' node-index sequences lexicographically, and Dijkstra's relaxation order has no notion of "the whole path so far" once a node is settled - it would resolve ties by whichever path happened to relax the node first, an accident of algorithm internals rather than a rule anyone could read off the node indices. Enumerating every simple path (bounded by "at most one obstacle's four corners plus two endpoints" per this phase) makes the comparison a plain post-hoc sort over completed candidates instead of a constraint baked into the search itself. Pitfall: exhaustive enumeration is only affordable because the problem size is small and stays small by explicit decision (Out of scope: "Caching or precomputing paths per map" is declined for the same reason - the graph is cheap enough that memoizing it would be solving a problem that doesn't exist yet); reusing this approach against a hypothetically larger obstacle count without re-checking that assumption would silently turn a cheap calculation into an exponential one.

Try next: `SegmentCrossesInterior`'s epsilon (`_epsilon = 1e-9`) appears in three different roles in the same method - as a tolerance for "did the clip produce a real interval or a single touching point", and twice more as a margin for "is the midpoint strictly inside the rectangle." Try writing a test with an obstacle and segment deliberately constructed so the clip interval's width is just under `1e-9` (rather than exactly zero) - does the method still classify it correctly, or does reusing one epsilon for two conceptually different tolerances (interval width vs. distance-from-boundary) turn out to matter at the edges of double precision?
## 2026-08-08 - #104 FR-4: Obstacles and detoured paths drawn on both heads
Concepts: extracting a shared private helper so two public readers can't drift apart, stretching a shared 1x1 texture into new shapes instead of allocating a new one, a direction-aware pure walk chosen over sorting an intermediate list, substituting a caller's own already-computed values for a helper's endpoint output to sidestep a second implementation's drift risk
- **`FractionAtTick` pulled out so `PositionOf` and `ProgressOf` can't quietly disagree with tower fire's own arithmetic** - `Match.cs`'s old `PositionAtTick` computed its clamped launch/arrival fraction inline; this feature split that one line out into a private `static double FractionAtTick(Army, long)` that `PositionAtTick` (tower fire's own reader), the new public `PositionOf`, and the new public `ProgressOf` all call. Why here: the issue's own history names three prior instances of exactly this failure mode (#68, D-45, D-53) - two independent call sites computing "how far along is this army" and slowly drifting apart as one is edited without the other. Making the three callers share one three-line private method turns "keep these in sync" from a discipline into a compiler-enforced fact: there is only one place the fraction is computed, so there is nothing left to keep in sync. Pitfall: extracting a shared helper only closes the gap for callers that actually go through it - `WaveColumnPresentation.PointAtDistance` (added in the same feature, see below) still reimplements the analogous arc-length walk itself, because it needed distance-based indexing rather than fraction-based, and the code-reviewer's one finding on this PR was exactly that near-miss: two copies of "walk a polyline" that agree today only because both were written carefully, not because the language forces them to.
- **`MatchScreen.DrawObstacles` reuses the existing 1x1 `_buttonTexture`, stretched into a `Rectangle`, instead of creating a new `Texture2D` for the obstacle** (`src/MW3.Game/MatchScreen.cs`) - `spriteBatch.Draw(_buttonTexture, destination, Color.SaddleBrown)` is the same trick the range-ring and spine-segment drawing already use: a single white 1x1 pixel, tinted by the `Color` argument and stretched to whatever `Rectangle`/scale the caller supplies, MonoGame's own resizing doing the rest. Why here: the acceptance criteria explicitly forbid a new `Texture2D` and any per-frame allocation (`docs/CONVENTIONS.md`) - textures are GPU resources with real setup/teardown cost, so a codebase that draws many different tinted rectangles benefits from having exactly one shared 1x1 source texture rather than one per shape. Pitfall: this only works for solid-colour, axis-aligned-rectangle (or, with rotation, line-segment) fills - the moment a shape needs its own gradient, pattern, or non-rectangular silhouette (as the base/tower/forge shapes do, via `CreateCircleTexture`/`CreateSquareTexture`/`CreateTriangleTexture`), the 1x1-stretch trick stops applying and a real texture is the correct choice; reaching for it reflexively for every new shape would be a mistake in the other direction.
- **`ComputeSpinePoints` picks its scan direction from `toDistance >= fromDistance` instead of collecting into a list and sorting** (`src/MW3.Game/WaveColumnPresentation.cs`) - because `ArmyPath.Waypoints` is already stored in monotonically-increasing-distance order (source to target), a caller asking for the reversed direction (trail wave "from", lead wave "to") is handled by walking the *same* array backward and decrementing a running `cumulative` total, rather than collecting matching waypoints into a temporary list and reversing or sorting it afterward. Why here: `docs/CONVENTIONS.md`'s no-per-frame-allocation rule applies here too - this runs from `MatchScreen.Draw` every frame - and an intermediate `List<(double, MapPoint)>` (the first draft the code briefly considered) would allocate on every call; iterating the existing array in whichever order matches the requested direction needs no allocation at all, at the cost of duplicating the loop body once for each direction. Pitfall: this only stays correct because the two loops are kept in lock-step with the *same* invariant (cumulative distance strictly between the two bounds) - a future edit to one loop's boundary condition without the matching edit to the other's would silently break only the reversed-call case, which is exactly the kind of asymmetry a single sorted-list implementation wouldn't be able to get wrong.
- **The spine's drawn first/last points come from `_armyCenterScratch` (`Match.PositionOf`'s own pixel output), not from `ComputeSpinePoints`' own endpoint computation** (`MatchScreen.cs`'s `DrawArmiesInFlight`) - even though `WaveColumnPresentation.ComputeSpinePoints` computes a lead and trail point internally (via its own `PointAtDistance`), the loop that draws the spine segments substitutes `_armyCenterScratch[fromIndex]`/`[toIndex]` for the first and last entries of the returned point list, using `ComputeSpinePoints`' own math only for the interior waypoints. Why here: this guarantees the spine's endpoints land in the exact same pixels as the two army markers it connects, immune to any tiny floating-point disagreement between `Match.PositionOf`'s fraction-based walk and `WaveColumnPresentation.PointAtDistance`'s distance-based one - a visible one-pixel gap between a marker and the line touching it would be an obvious defect no test would likely catch. Pitfall: this is a targeted patch over the deeper issue (two independent implementations of the same arithmetic) rather than a fix for it - it silences the *visible* symptom of drift without removing the risk that the two computations diverge somewhere `_armyCenterScratch` doesn't cover, which is exactly why the code-reviewer flagged the duplication itself as worth a follow-up rather than closing it out as "handled."

Try next: the follow-up task already filed against this feature proposes moving the shared arc-length walk onto `ArmyPath` or `PathCalculator` so both `Match` and `WaveColumnPresentation` call one implementation. Before or alongside that refactor, try writing a property-based (or just densely-parametrized) test that constructs a handful of `ArmyPath`s and asserts `Match.PositionOf`'s pixel output and `WaveColumnPresentation.PointAtDistance`'s output agree at every waypoint boundary and at several fractions in between - a test like that would have caught the near-miss this feature's review flagged, and would keep catching it automatically once the two implementations become one.

## 2026-08-08 — #105 FR-5: Base shapes shrink by about half on both heads
Concepts: reflection against `private const` fields for otherwise-untestable presentation constants, re-deriving a value as a ratio of another constant instead of an independent literal
- **`typeof(MatchScreen).GetField(name, BindingFlags.NonPublic | BindingFlags.Static).GetRawConstantValue()`** (`tests/MW3.Game.Tests/MatchScreenTests.cs:11-14`) - `MatchScreen` draws with a `SpriteBatch`/graphics device and exposes no public surface for its sizing constants, so the test reaches past `private` via reflection rather than widening the field's visibility just to make it testable. `GetRawConstantValue()` specifically (not `GetValue()`) is what a C# `const` field needs: a `const` is burned into IL as a literal at every call site and has no runtime storage slot for `GetValue()`'s "read this instance/static field" semantics to inspect - `GetRawConstantValue()` reads the metadata token instead. Pitfall: this test is coupled to the exact field *names* as string literals (`"_radiusFraction"`, `"_armyRadiusFractionOfBase"`) - a rename inside `MatchScreen.cs` compiles cleanly and silently stops the test from finding the field (a `NullReferenceException` at the `!` only surfaces at test run, not at build time), so a renamed private const needs a matching, easy-to-forget edit in the test file.
- **`_armyRadiusFractionOfBase * _radiusFraction` instead of a second independent literal** (`src/MW3.Game/MatchScreen.cs:21-28`) - the pre-FR-5 code had `_armyRadiusFraction = 0.08f` as its own viewport fraction, unrelated in the source to `_radiusFraction`; this feature's whole defect (#94) was that two unrelated literals can drift out of the relationship a human assumed between them (the army marker was meant to always read smaller than a base, until the base shrank and the assumption silently broke). Expressing the army radius as a multiplier of the base radius makes that relationship a runtime invariant enforced by the multiplication itself, not a comment reminding a future editor to keep two numbers in proportion. Pitfall: a ratio constant only protects the relationship it's written into - `_armyTrailingRadiusFractionOfBase` still has to be kept separately less than `_armyRadiusFractionOfBase` by convention (guarded here by `TrailingArmyRadius_IsStrictlyLessThanLeadArmyRadius`, a second, independent assertion), so multiplying by a shared base doesn't remove the need to test every invariant a set of related constants is supposed to hold.

Try next: `MatchScreenTests.GetPrivateConst` is a small, reusable pattern for any future `MatchScreen` sizing constant that needs a regression test without becoming public API - try adding one more call site (e.g. the selection-highlight or construction-ring scale) and writing a test that asserts it stays a *strict* multiple of `_radiusFraction` less than some sensible upper bound, mirroring the two tests this feature added.
