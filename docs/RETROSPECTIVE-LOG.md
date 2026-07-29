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

## 2026-07-27 — autopilot run (Core gameplay loop: #24, #25)
- Outcome: 2 shipped (#24 AI opponent reinforces and attacks via PR #26; #25 Victory and defeat end
  the match via PR #27), 0 skipped-for-clarification, 0 circuit breakers tripped, `main` green. This
  clears the entire Core gameplay loop feature backlog - phase 2 is complete.
- Went well: #25 (the more intricate feature - outcome freezing across three layers, a
  press-time-vs-release-time dismissal rule, a `--time-scale` QA lever) was approved and verified on
  the *first* pass with zero findings from either the reviewer or QA - the most complex feature
  shipped so far with the cleanest single-pass result. Both features' headless test suites (94 tests
  by the end) caught real behavioral bugs before they ever reached review: #24's AI heuristic
  idle-locked in an early draft of the passive-human test (turned out to be the match's natural
  end-state, not a bug, once traced) and #25's first hand-authored "prove victory is attainable"
  script failed outright because it used arbitrary send counts a real drag can never produce (every
  drag is `floor(garrison / 2)`, not a chosen number) - both caught by running the test before
  shipping, not by a human or reviewer noticing later.
- Caused rework: #24 needed one review cycle - a Major finding (AI could pick a base sitting at
  exactly zero garrison, left there by a repelled attack, as a reinforcement source, producing a
  command `Match.Execute` would reject) fixed with two regression tests constructing the exact
  scenario, then re-approved on delta re-review. This is now a standing note in
  `docs/CONVENTIONS.md` (see process adjustment below) because it's a *general* shape - a value
  computed from live state used in a command without being re-validated against that same live
  state - not an AI-specific one. Separately, discovering the map's underlying symmetry (any
  full-garrison strike launched at tick T against a base that has produced identically since tick 0
  falls exactly as far short as the attacker's own head start, regardless of T) took two failed
  attempts at scripting #25's victory sequence before landing on a strategy (defend, then attack,
  then a persistent-siege fallback) that reliably overcomes it - genuine design exploration, not a
  mistake to prevent next time.
- Follow-ups filed: #28 MI Pad 4 shows as "unauthorized" in `adb` - device QA has been blocked on
  both #24 and #25 by this (distinct from #7's "no device attached at all" - here one is attached
  and listed, but its USB-debugging authorization dialog hasn't been approved on the device itself,
  which this automation can't do). Every device-blocking acceptance criterion on both features was
  reported as not-verifiable rather than passed or failed.
- Process adjustments applied: added a bullet to `docs/CONVENTIONS.md`'s MW3-specific section
  naming the "computed send size vs. live garrison" validation gap #24's review caught, so the next
  feature that computes a command value from live state gets checked against this pattern explicitly
  rather than relying on the reviewer to notice the asymmetry by inspection again.

## 2026-07-29 — autopilot run (interrupted at #40, resumed for #36)
- Outcome: 2 shipped (#40 build time + recapture grace, PR #43; #36 tower fire, PR #45), 0 skipped
  for clarification, 0 circuit breakers tripped. `main` green (both PRs' CI passed before merge;
  the two subsequent docs-only learning-log pushes each also triggered a full CI run rather than
  being skipped, a known separate issue - see follow-ups).
- Went well: #40's much larger surface (a new construction-state model touching commands, `Advance`'s
  segment logic, capture, the action menu, and presentation) passed code review and QA verification
  on the first pass with zero findings. Both features' new tests caught real bugs before shipping
  rather than after: #36's `RecaptureGraceTests`-style reflection rigging for #40 and the dedicated
  `ABaseConvertingToATower_FiresOnTheExactTickItsBuildCompletes` test for #36 each independently
  proved out a same-tick ordering requirement that a looser test would have missed, and the latter
  went on to catch the real review finding below when it was deliberately re-run against a
  temporarily-reverted fix, confirming the test - not just the fix - was load-bearing.
- Caused rework: #36 needed one review cycle - a Major finding (the first tower-fire implementation
  switched `Match.Advance` to a fully per-tick loop the moment any tower existed, which also forced
  production out of its closed-form batching for the rest of the match - directly contradicting the
  issue's own "production stays closed-form; fire does not" criterion and this project's per-tick
  no-allocation standard). Fixed by keeping the existing boundary-jumping segment structure and only
  sweeping interior ticks for fire (no production call) when a tower exists, restoring one
  closed-form production call per segment; re-approved on delta re-review with no further findings.
  This is a shape worth naming for next time: a feature that must run *something* every tick is not
  the same requirement as running *everything* every tick - only the tick-sensitive piece needs the
  fine-grained path, the rest should keep whatever coarser-grained path it already had.
- Follow-ups filed: #46 `BaseActionMenu`'s cache-invalidation guard (`GarrisonCount`/`Level` only)
  will miss a construction or type change once FR-5 wires up the convert button - flagged by
  code-reviewer on #40 as non-blocking at the time, filed here so it isn't forgotten before FR-5.
  A second gap - `ci.yml` actually runs full CI on docs-only pushes to `main` despite CLAUDE.md
  claiming a `docs/**` path-ignore, observed repeatedly this run (the #40 and #36 learning-log
  commits each triggered a redundant run) - was flagged as a background-task suggestion mid-run
  rather than filed as a GitHub issue here, and the user has already started that fix in a separate
  session.
- Process adjustments applied: none to `CLAUDE.md`/`docs/CONVENTIONS.md` this run - the production-
  batching finding is closer to a one-off algorithmic subtlety of this specific feature (mixing a
  closed-form span computation with a per-tick requirement) than a recurring pattern with an obvious
  standing rule to add; noting it in this log is judged sufficient unless it recurs.
