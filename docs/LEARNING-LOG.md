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
