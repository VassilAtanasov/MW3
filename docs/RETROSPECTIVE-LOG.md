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

## 2026-07-27 — autopilot run (Core gameplay loop: #8, #9)
- Outcome: 2 shipped (#8 Player, base ownership, and unit production via PR #10; #9 Play button
  opens a match screen and back returns to the welcome screen via PR #11), 0
  skipped-for-clarification, 0 circuit breakers tripped, `main` green.
- Went well: #8 needed exactly one review cycle (0 Critical/Major findings; one minor test-coverage
  gap was fixed proactively) and QA verified all 14 acceptance criteria on the first pass - the
  cleanest feature shipped so far. Discovering issue #9's GitHub body was truncated before writing
  any code (rather than discovering it mid-review) meant zero rework from working off an incomplete
  spec - the Workflowy note was fetched and the issue repaired up front, per the never-guess rule.
- Caused rework: #9 took three total verification cycles, all on the same root cause - a wrong
  assumption about how Android surfaces its hardware back button to MonoGame. Attempt 1 assumed
  MonoGame maps the hardware back button to `Keys.Back` in `Keyboard` state - wrong, confirmed by
  `qa-verifier` on a physical MI Pad 4. Attempt 2 overrode `Activity.OnBackPressed()` - also never
  fired, because MonoGame's own view consumes the key event earlier in Android's dispatch chain.
  Attempt 3 (`DispatchKeyEvent`, ahead of the view hierarchy) worked. None of this was reachable
  without real hardware - the desktop-only scripted-input seam (D-17) proved the navigation logic
  itself correct on every attempt, so the entire rework was `qa-verifier` iterating against the
  attached device, not the reviewer or the gate. Separately, a Major review finding (back-triggered
  exit skipping `--script`'s documented frame-count/screenshot contract) was found and fixed in one
  cycle - unrelated to the Android issue, a genuine gap in the initial exit-condition logic.
- Follow-ups filed: #12 Investigate why `/kickoff` truncated issue #9's GitHub body (Workflowy had
  the complete note; the GitHub issue created from it was cut off mid-sentence, missing roughly half
  the acceptance criteria - caught and repaired before implementation, but `/kickoff` hasn't run yet
  for FR-3 through FR-7, so a systemic bug here would keep recurring unnoticed).
- Process adjustments applied: added D-19 to `docs/core-gameplay-loop/ARCHITECTURE.md` recording
  that Android's hardware back button must be intercepted in `MainActivity.DispatchKeyEvent`, never
  `OnBackPressed` or a `Keyboard` check - binding for FR-5 (tap input) and FR-7 (return to welcome),
  which both touch Android back/hardware-key handling again.
