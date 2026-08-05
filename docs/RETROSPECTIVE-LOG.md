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

## 2026-07-29 — autopilot run (FR-5, FR-6)
- Outcome: 2 shipped (#48 FR-5 as PR #50, #49 FR-6 as PR #51), 0 skipped, `main` green.
- Went well: both features delegated to a single background implementation agent each, then taken
  through review + QA in parallel exactly as designed - zero circuit-breaker trips, zero
  clarification skips (both issues' acceptance criteria were fully sourced from the reference and
  the pre-written `docs/base-upgrades-and-types/ARCHITECTURE.md` D-25..D-31 sections, so nothing
  was ambiguous enough to need a question). #48's review caught a genuine pre-existing layout bug
  (two action-menu buttons could overlap once a second one joined the arc) that the feature itself
  exposed rather than introduced - fixed, tested, and re-verified in one delta cycle without a
  fresh full review.
- Caused rework: two distinct, unrelated frictions, both self-inflicted by Ivan's own delegation
  briefs rather than by the implementers:
  1. #48's QA pass found `qa/scripts/army-shrinking-early.txt`'s screenshot wasn't byte-for-byte
     reproducible across individual re-runs (~1-in-5) - a real MonoGame fixed-timestep
     non-determinism, not a flaky test, root-caused and fixed at the source (anchoring scripted
     ticks to a nominal step and disabling the fixed-timestep catch-up during scripted playback).
     One delta review + re-verify cycle, no fresh full passes needed.
  2. #49's delegation brief told the implementer this feature added "no new QA mechanism," which
     was true for script directives/CLI flags but wrong for the issue's own requirement of one new
     `qa/scripts/` script - both code-reviewer and qa-verifier independently caught the same gap.
     Fixed post-hoc (added `qa/scripts/ai-upgrades.txt`, re-gated) without needing to touch the
     implementer's own work at all. This is the higher-value signal of the two: it's a coordination
     mistake in how Ivan scopes a delegation brief, not implementation rework, and it's exactly the
     kind of thing a standing rule prevents from recurring - addressed directly in `CLAUDE.md`
     (see below) rather than only logged here.
  3. Both features needed one blocking device-QA pass on the MI Pad 4 that Ivan performed itself
     after the implementer/QA agent finished (per the issue's own device criteria) - not rework in
     the review/fix sense, but real wall-clock cost each time (repeated unlock/wake/launch cycles,
     and for #48 specifically, three attempts to catch a live post-conversion tower frame before the
     AI's attack timing captured the passive human's only base first). #48 shipped with that one
     frame left unresolved rather than blocking further, filed as follow-up #52 instead.
- Follow-ups filed: #52 "Capture the completed tower's square+range shape live on the MI Pad 4" -
  the one device-QA criterion from #48 that stayed unresolved through three attempts, closed out as
  an explicit gap rather than left as a caveat buried in a merged issue's history.
- Process adjustments applied: added a standing note to `CLAUDE.md`'s Definition of Done clarifying
  that "no new QA mechanism" means no new script directive/CLI flag, never "no new `qa/scripts/`
  file" - the exact conflation that caused friction 2 above. Framed as a rule for any future
  `/implement` delegation brief to check the issue's own Verification checklist for new-script
  requirements before writing "no new QA mechanism" into a brief.

## 2026-07-30 — autopilot run (Base upgrades and types: #53) — phase 3 complete
- Outcome: 1 shipped (#53 FR-7, "The AI opponent builds towers and routes armies around enemy
  ranges", via PR #55), 0 skipped-for-clarification, 0 circuit breakers tripped, `main` green. This
  was the last open `feature` issue on phase 3's board (20) - **phase 3 ("Base upgrades and types")
  is now fully shipped**, FR-1 through FR-7 all merged. Next work per CLAUDE.md's project registry
  is `/discover`/`/kickoff` on phase 4 ("Sending armies the MW2 way", board 21), whose FR-1 (#54)
  already has an open issue but has not been through this run.
- Went well: single review/QA cycle, no rework - `code-reviewer` returned `APPROVE` and
  `qa-verifier` returned `VERIFIED` on the first pass, with only one non-blocking coverage
  observation (filed as follow-up #56, not a fix-now finding). The issue's own acceptance criteria
  were unusually implementation-ready (down to the exact winnability formula and geometry model),
  which is itself a signal that phase 3's mid-phase MW2 correction (FR-3a/3b/3c) and the accumulated
  `docs/reference/` material are paying off in kickoff quality.
- Caused rework: none within the PR - but implementing FR-7 exposed a genuine, previously-invisible
  interaction between two AI clauses. `TryConvert`'s candidate rule (a flat
  `garrison >= LevelTable.ConversionCost` threshold) has no cap/level gate the way `TryUpgrade`'s
  does, so a base that receives a large reinforcement stack well under its level's cap can now
  legitimately convert to a tower instead of continuing to upgrade - confirmed via a diagnostic run
  where a level-2 base (cap 40) converted at garrison 41. This broke a pre-existing test
  (`AiLaddersPastLevelTwo_ReachingLevelThreeOnAtLeastOneBase_OverALongMatch`) that had never been
  exercised against a scenario where two AI self-investment clauses could compete for the same
  saturated base. Diagnosed with temporary instrumentation before touching anything (rather than
  guessing), confirmed as spec-sanctioned rather than a bug, and the test was re-authored to the
  wider, still-meaningful property ("reaches level 3 or builds a tower") rather than loosened.
- Follow-ups filed: #56 "FR-7's determinism test doesn't confirm the tower-aware attack branch
  actually fired" - `code-reviewer`'s one non-blocking finding, a test-coverage gap in
  `AiTowerRoutingDeterminismTests.cs` (asserts single-call/chunked agreement but not that the
  tower-loss-aware attack branch specifically executed during the rigged run).
- Process adjustments applied: none - no recurring friction pattern emerged from a single-feature
  run; the `TryConvert`/`TryUpgrade` interaction above was investigated and resolved within the PR,
  not left as a standing risk requiring a CLAUDE.md change.

## 2026-07-30 — autopilot run (Sending armies the MW2 way: #58)
- Outcome: 1 shipped (#58 FR-2: Send-strength picker on both input heads, plus snaking, via PR #59),
  0 skipped-for-clarification, 0 circuit breakers tripped, `main` green.
- Went well: gate passed clean on the first attempt (dotnet format, build -warnaserror, 344 tests
  including 15 new headless SendStrengthSelectorTests). Every acceptance criterion on the issue was
  independently confirmed by qa-verifier against the running app on both the desktop head and the
  physical MI PAD 4 device, rebuilt and reinstalled fresh from the branch per follow-up #28's
  standing lesson. Only one review cycle was needed - both findings were fixed in a single delta
  pass without spawning a fresh reviewer.
- Caused rework: one Minor review finding (a headless test measured distance from a button's
  rectangle center rather than its nearest point to a base, which doesn't actually prove the "never
  contests a press with a base" invariant for a non-circular shape) - fixed by clamping the base's
  position into the rectangle before measuring. No other rework; this was a clean single-pass build.
- Notable process event: the issue's own acceptance criteria asked for a QA-script demonstration
  ("three armies with strictly decreasing Count=") that, once actually run against the shipped
  calculator's real arithmetic, produced a tied pair (2, 2, 1) instead - a genuine conflict between
  the literal acceptance text and what the (correct, verified) numbers do, discovered only by
  actually running the script rather than reasoning about it abstractly. Handled per the never-guess
  rule: recorded as an open question on the issue and in the PR body rather than silently choosing
  an interpretation, with both code-reviewer and qa-verifier independently confirming the behavior
  was real and correctly implemented before shipping anyway, since it is a QA-script wording
  precision question rather than a functional defect blocking every other acceptance criterion.
- Follow-ups filed: #60 "Decide: is snaking's 2,2,1 count sequence an acceptable demo, or should
  tuning change?" - carries forward the open question above so it isn't lost once #58 is closed;
  purely a product/tuning decision, no code proposed.
- Process adjustments applied: none - a single clean-build run doesn't show a recurring pattern
  worth a standing rule; the "run the script, don't just reason about the arithmetic" instinct that
  caught the 2,2,1 tie was applied in the moment rather than needing a new CLAUDE.md rule to enforce.

## 2026-08-05 — autopilot run (Morale phase, FR-1 through FR-4)
- Outcome: 4 shipped (#66, #67, #69, #71 — PRs #72, #73, #74, #75), 0 skipped for clarification, 0 circuit-breaker trips, main in_progress on CI (last push's run, not red) at time of writing.
- Went well: each feature's implementer correctly identified and fixed non-obvious defects the acceptance criteria didn't spell out - FR-1's implementer found and fixed a real floor/ceiling clamping-order bug (two separate clamped writes on one combat event could swallow each other at the D-38 boundary) rather than just making tests pass; FR-2 and FR-4's code-reviewer passes both came back clean or near-clean (zero and one Minor finding respectively), suggesting the FR-1 groundwork (shared MoraleTable, shared WouldCapture predicate) gave later features less surface for review findings.
- Caused rework: two of four features (#66, #69, #71) had their implementing agent interrupted mid-task by a session API limit, requiring a resume from a preserved worktree rather than a clean single pass - not a defect in the work itself, but real wall-clock cost or wasted the agent's own diagnostic loop restarting from scratch. Separately, FR-2 surfaced a real regression (qa/scripts/victory.txt no longer reaching HumanVictory after the combat formula changed) that was flagged in-file rather than fixed, and the same anomaly had to be independently re-confirmed as pre-existing (not a new regression) during FR-4's QA pass - a second QA investigation of a fact the first one had already established, because nothing outside the file's own comment recorded "this is known, already triaged."
- Follow-ups filed: #76 qa/scripts/victory.txt no longer reaches HumanVictory (stale since FR-2)
- Process adjustments applied: none directly to CLAUDE.md - the session-interruption pattern is an environment constraint (session API limits), not a process gap this repo's own standards can fix, and the victory.txt gap is now captured as a tracked follow-up rather than a recurring silent one.
