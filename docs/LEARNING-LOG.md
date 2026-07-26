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
