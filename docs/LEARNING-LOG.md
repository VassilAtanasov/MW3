# Learning log

Short notes on the C#/.NET concepts each merged feature actually introduced, tied to this
project's stack (MonoGame 3.8.5 on .NET 10). Written by `/learning-coach`, never gates a feature.

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
