# Retrospective log

Dated entries from `/retrospective`, run automatically at the end of each `/autopilot` pass (or
standalone). Records outcome and lessons; never gates or reopens a shipped feature.

## 2026-07-26 — autopilot run (Welcome screen: #3, #4)
- Outcome: 2 shipped (#3 Android head installs and launches on a physical device via PR #5, #4
  Welcome screen with game title and inert entry point via PR #6), 0 skipped-for-clarification,
  0 circuit breakers tripped, `main` green.
- Went well: both features needed exactly one review cycle each — every Major finding was fixed
  and re-approved on the first delta re-review, with no fresh full reviews needed. QA never failed
  a single checkable criterion on either issue; the only gaps were the pre-flagged, out-of-control
  hardware ones (see follow-up below). Both issues' `/kickoff`-authored architecture notes
  (D-7..D-10, the exact §2a commands) matched what actually got built, with zero rework caused by
  the plan itself being wrong.
- Caused rework: both features' Major review findings were the same underlying shape - **a
  resource/lifecycle event assumed to be one-shot or correctly-timed without an explicit guard**.
  #3: `MainActivity` disposed `WelcomeGame` from `Dispose(bool)`, which is driven by the Java-peer
  GC lifecycle rather than the actual Android teardown event, so cleanup wasn't reliably prompt.
  #4: the screenshot capture re-fired and rewrote its PNG on every single `Draw` call instead of
  once, with no atomic write. Neither was caught until the code-reviewer's adversarial pass; both
  were one-line-scale fixes once named. Also: adding the first real MonoGame content exposed a
  genuine upstream `MonoGame.Content.Builder.Task` parallel-build race
  (MonoGame/MonoGame#7409) that made the literal `dotnet build MW3.slnx -warnaserror` acceptance-
  criterion wording (without `-m:1`) flaky - not this repo's bug, but worth a standing note so it
  isn't rediscovered.
- Follow-ups filed: #7 Verify Android head + welcome screen on a physical device (both issues'
  device-attached criteria - `adb` install/launch/screencap/pidof - are unverified, not failing;
  this dev machine has no physical Android device attached).
- Process adjustments applied: added a standing note to `CLAUDE.md`'s Quality gate section that
  this solution must always build with `-m:1`, citing MonoGame/MonoGame#7409, so a future feature
  or reviewer doesn't have to rediscover the race from scratch.
