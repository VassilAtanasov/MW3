# MW2 parity — the gap list

> **The goal is a game as close as possible to Mushroom Wars 2.** Mechanical difference from MW2 is
> a **gap to close**, not a design choice to defend. This file exists to trend toward empty.
>
> The one permanent, deliberate divergence is the **IP layer** — §5. The final game ships as
> **Bug Wars**: insect heroes and armies matched to the region of each geolocated map, original
> branding, original items, and **Fame** in place of MW2's ranking. Everything below the IP layer
> should behave the way MW2 behaves.
>
> Stated by the user 28-07-2026. Reconciled against the repo the same day: phase 3 with FR-1/FR-2/FR-3
> merged (issues #30, #32, #34) and FR-4 (#36) kicked off. Updated 29-07-2026: FR-3a (`f5f3320ec408`,
> issue #38) merged, closing G-8 and G-14 and shipping §3's tick-rate decision. Updated again
> 29-07-2026: FR-3b (`f585a0868ecc`, issue #39) merged, closing G-9 and G-10 and partially closing
> G-7. Updated again 29-07-2026: FR-3c (`a4c8cacb426a`, issue #40) merged, closing G-11 and G-12.
> Updated 30-07-2026: phase 4, "Sending armies the MW2 way" (`docs/army-sending/`), discovered —
> **G-2** and **G-3** are now assigned to it (FR-3 `ed9c0ead836c` and FR-1/FR-2 `fa6d69f05f9d` /
> `4d4a9bac3f90` respectively). Updated again 30-07-2026: FR-1 (#54) and FR-2 (#58) merged, closing
> **G-3**. Updated 31-07-2026: FR-3 (`ed9c0ead836c`, issue #61) closes G-2 for the rules layer — the
> drawn column is FR-4's job (`a3e0351a6c4b`), still open. Updated 04-08-2026: FR-4
> (`a3e0351a6c4b`, issue #63) merged, closing G-2 fully - the column now reads as one send (D-36's
> tapered radius plus shared spine) and a tower's fire is visible as an event.

## 0. How Ivan uses this file

At `/discover` and `/kickoff`, the default answer to "how should this behave?" is **"the way MW2
does it"**, per [MW2-RULES.md](MW2-RULES.md), [MW2-HEROES.md](MW2-HEROES.md) and
[MW2-ITEMS-AND-PROGRESSION.md](MW2-ITEMS-AND-PROGRESSION.md). Ask the user only when:

1. MW2's behaviour is **genuinely unknown** — its AI is undocumented, and
   [MW2-RULES.md](MW2-RULES.md) §10 lists nine other gaps nobody publishes;
2. the reference value is marked **[?]**;
3. the question is about the **IP layer** (§5), which MW2 cannot answer for us; or
4. closing a gap would **contradict a shipped `REQUIREMENTS.md`** — see §4, which is the user's call,
   not a build-mode decision.

**Do not add a row to §2 as a way of recording a design preference.** A new difference from MW2
needs the user's agreement first, and then it belongs in §4 with its reasoning.

**Still true, and unchanged by the new goal:** MW2's tuning *numbers* never get pasted into
`MW3.Core`. MW2's economy is measured in seconds (a level-1 village makes 0.33 units/sec) and MW3's
in ticks. Numbers enter through a kickoff-settled §"Tuning values" table (D-22) after being
recalibrated. Parity means **same behaviour and same ratios**, not same literals — see §3.

## 1. Already at parity ✅

Nothing to do. Recorded so it is not re-litigated.

| Rule | Both games |
|---|---|
| Unit types | Exactly one |
| Units fighting in transit | They do not — opposing waves pass through each other |
| Movement | Straight line, base to base. No pathfinding, no fog of war |
| Army recall / redirect | Not possible |
| Towers damaging armies in transit | Yes (MW3 phase 3 FR-4) |
| Towers producing units | They do not |
| Upgrade paid from | The building's own garrison |
| Capture demotion | Down one level, floored at 1, type preserved |
| Conversion resetting level | Yes, back to level 1 |
| Refund on conversion | None |
| Garrison above cap | Legal via arrivals; blocks production; nothing decays |
| Conquest win condition | Capture all enemy buildings |
| Combat randomness | None in either game |
| Primary input verb | Tap a building; buildings are the only clickable object |
| Base action menu | MW2's radial menu ≈ MW3's arc menu (phase 3 FR-2) |
| Village and tower ladder length | Villages have 5 levels (4 reachable by upgrading, per MW2-RULES.md §2.2's `[?]`), towers have 4 — both games. Closed **G-8**, merged by phase 3 FR-3a (`f5f3320ec408`) 29-07-2026 |
| Conversion and upgrade costs | Conversion costs 30; upgrades cost 5/10/20 (villages) and a flat 20 (towers) — both games. Closed **G-14**, merged by phase 3 FR-3a (`f5f3320ec408`) 29-07-2026 |
| Levels buy defence | Villages 100→140% (+10pp/level), towers 140→200% — both games. A level-1 tower already matches a level-5 village. Closed **G-9** and **G-10**, merged by phase 3 FR-3b (`f585a0868ecc`) 29-07-2026 |
| Build time | Upgrade 5/5/10/15 s, conversion 5 s — both games. Closed **G-11**, merged by phase 3 FR-3c (`a4c8cacb426a`) 29-07-2026 |
| Recapture grace | Retaking a building within 1 second of losing it does not demote it further — both games. Closed **G-12**, merged by phase 3 FR-3c (`a4c8cacb426a`) 29-07-2026 |

## 2. Gaps to close 🔴

Ordered by how much each one changes how the game plays. Each row is a candidate feature for a
future phase.

> **Eight of these were assigned** (28-07-2026), to three correction features sitting between FR-3
> and FR-4 in dependency order. **FR-3a merged 29-07-2026**, closing **G-8** and **G-14** and
> settling §3's tick-rate question — both have moved to §1. **FR-3b merged 29-07-2026**, closing
> **G-9** and **G-10** (moved to §1) and partially closing **G-7** (stays below - see its row).
> **FR-3c merged 29-07-2026**, closing **G-11** and **G-12** — both have moved to §1.
>
> | Feature | wf | Closes |
> |---|---|---|
> | FR-3a — the MW2 ladder, caps, costs, and the 50 ms tick | `f5f3320ec408` | G-8, G-14, §3 — **merged** |
> | FR-3b — levels buy defence; combat becomes `(a/d) × Wu` | `f585a0868ecc` | G-9, G-10, most of G-7 — **merged** |
> | FR-3c — build time and the one-second recapture grace | `a4c8cacb426a` | G-11, G-12 — **merged** |
>
> A row stays in this table until the feature closing it has **merged**, not when it is assigned.
> G-7 stays open after FR-3b because its `a` and `d` terms are only fully populated once morale
> (G-1) and forges (G-6) exist; FR-3b builds the formula that accepts them.

### 2.1 Large — these change the shape of a match

| # | MW2 | MW3 today | Notes |
|---|---|---|---|
| G-1 | **Morale**: a per-player multiplier, 0–5 suns, worth +125% defence / +25% attack / +50% speed, earned by capturing and defending, drained by losses and by **inactivity on a timer that accelerates as you climb** | Nothing | The largest single system in MW2 and the one every source calls the skill differentiator. Anti-turtle, anti-snowball, and it touches combat, speed and production at once. [MW2-RULES.md](MW2-RULES.md) §5 |
| G-2 | **Waves**: a send arrives as successive 8-unit waves that do not strike simultaneously, so defenders regenerate, towers fire, and reinforcements land mid-fight | **Fully closed.** Rules by FR-3 (`ed9c0ead836c`, issue #61): `Match.Execute(SendArmyCommand)` splits an accepted send of `n` units into `ceil(n/8)` ordinary `Army` objects staggered by `SendWaveCalculator.WaveIntervalTicks` (5 ticks, MW3's own number — MW2 publishes none, see below). Visual by FR-4 (`a3e0351a6c4b`, issue #63, D-36): the column reads as one send via a per-wave radius taper plus a spine grouped by `Army.SendId`, and a tower's fire is visible as a brief flash keyed to `Base.LastFireTick` | This is what makes defending a larger attack viable, and it is the precondition for G-3 (closed). The wave interval itself is unpublished and upgradeable in MW2 via a "row density" passive skill ([MW2-RULES.md](MW2-RULES.md) §3.3, §10) — the passive-skill modifier is G-20's territory; this gap closes only the fixed baseline interval |
| G-3 | **Send-strength picker**: 25 / 50 / 75 / 100% of the garrison, plus **snaking** (repeated 25% sends producing a tapered column, used for deception and defensive spread) | **Closed** by FR-1 (`fa6d69f05f9d`, #54) and FR-2 (`4d4a9bac3f90`, #58): a persistent 25/50/75/100% control on both input heads, backed by `SendStrengthCalculator`. MW2's sticky tap-the-target selection (`MW2-RULES.md` §3.3) is not adopted — MW3 repeats the same drag gesture instead (§6) | Phase 3 §6 scoped this out as "its own phase"; phase 4 closed it |
| G-4 | **Heroes**: 24 across 4 tribes, 4 abilities each on a shared 500-energy pool, with slot-fixed costs and cooldowns | Nothing | Becomes **insect heroes** (§5). [MW2-HEROES.md](MW2-HEROES.md) — note §"Design patterns worth stealing" |
| G-5 | **Energy**: 2.5/sec passive plus `0.45 × k` per unit lost attacking, where `k` rises as morale falls — so a losing player earns 5× the energy per casualty | Nothing | The game's rubber band. Depends on G-1 for `k` |
| G-6 | **Forges**: a third building type giving a global attack/defence buff to its owner, 125–150% defence and 150–200% attack, capping at 4 | Two building types only | Phase 3 §6 forbids a third type *this phase*. Feeds the combat formula (G-7) |
| G-7 | **Combat formula** `Bu = (a/d) × Wu`, with building defence, morale and forges stacking into `a` and `d` | **Partially closed** by phase 3 FR-3b (`f585a0868ecc`) 29-07-2026: the resolver and building defence are live; morale and forge terms exist in the formula's signature but are fixed at identity | Stays open only because G-1 and G-6 haven't supplied a value yet — closing them populates the remaining terms rather than requiring a rewrite |

### 2.2 Medium — buildings and economy

| # | MW2 | MW3 today | Notes |
|---|---|---|---|
| G-13 | Tower shots have a **damage radius** (implied by Kenor's Explosive Shells) | 1 unit per shot, single target, closest army | MW2's own tower damage is **never published** — closing this needs observation, not research |

### 2.3 Modes, maps, meta

| # | MW2 | MW3 today | Notes |
|---|---|---|---|
| G-15 | **Domination** and **King of the Hill** modes, with their own non-convertible, spell-immune objective buildings | Conquest only | [MW2-RULES.md](MW2-RULES.md) §7 has both rulesets in full |
| G-16 | **Rush Mode** at 2:00 — double energy, half cooldowns, for everyone | Nothing | Guarantees matches resolve. Depends on G-5 |
| G-17 | 1v1, 1v1v1, 1v1v1v1, 2v2 | One human, one AI | Multiplayer needs a server (S-7) |
| G-18 | Many maps, per campaign mission | One hardcoded six-base layout | Becomes **geolocated maps** (§5) |
| G-19 | **4 × 50 mission campaigns**, tribe-locked | None | MW3's cooperative campaign is an *enhancement*; MW2's structure is a starting point, not a spec |
| G-20 | **Artifacts** (4 slots, 6 rarities, ~120 items) and **passive skills** (6 trees) | Nothing | Needs persistence, which needs S-9 to relax. Becomes original items + **Fame** (§5) |

### 2.4 Not closeable from research

| # | Why |
|---|---|
| G-21 | **AI behaviour.** No source describes how MW2's AI plays. MW3's AI is original work and must be described as such — never as a port. Closing this gap means *observing* MW2, not reading about it |
| G-22 | **Absolute tower range, tower damage per shot, map dimensions.** Published only as percentages of unstated bases, so no MW2 distance converts into MW3's normalized 0..1 space. [MW2-RULES.md](MW2-RULES.md) §10 |

## 3. Parity means behaviour, not literals

Two different questions hide inside "as close as possible", separated below and both now settled
and shipped by phase 3 **FR-3a** (`f5f3320ec408`, merged 29-07-2026):

- **Behavioural parity** — five village levels exist; a level buys defence; conversion resets to
  level 1. This is what "close to MW2" plainly means and it is not in question.
- **Numeric parity** — caps are literally 20/40/60/80/100, a village makes literally 0.33 units/sec,
  conversion costs literally 30.

MW3's economy used to be *tick-based and smaller*: caps 20/35/50 against MW2's 20/40/60/80/100, and
1 unit per 10 ticks against 0.33/sec — the staging ladder FR-1 and FR-3 shipped before
`docs/reference/` existed. Phase 3's FR-4 remains the standing warning about copying MW2 numbers
across without checking them against MW3's own speeds — discovery proposed tower ranges that,
checked against MW3's army speed, would have made towers **literally unable to fire** — but for the
village and tower ladders themselves, the answer below is no longer a warning; it is what shipped.

**Settled 28-07-2026, shipped 29-07-2026: MW2's literal numbers, on a tick rate chosen to make them
work.** MW2's published tables are now the values in force — caps 20/40/60/80/100, upgrade costs
5/10/20, conversion 30, five village levels and four tower levels — rather than ratios rescaled to
a smaller economy. This buys the closest possible parity and makes MW2's own balance data directly
consultable. `docs/base-upgrades-and-types/REQUIREMENTS.md` §"Tuning values" carries the ladder now
in force; the reasoning that produced it is retained below and in that same file's superseded
staging-ladder section.

Consequence: **`Match.TickDurationMilliseconds` is 50 ms (20 Hz).** The prior 100 ms (10 Hz)
could not express MW2's production ladder — the level-4 village's 1.33 units/sec needs a
0.75-second period, which is 7.5 ticks at 10 Hz. The tick duration had to be chosen so every MW2
production period lands on a whole tick, since D-24 keeps all simulation arithmetic on integer
ticks.

**[D] 50 ms (20 Hz) is the tick rate that shipped** — the longest integer-millisecond tick that
makes all five MW2 periods whole, and it produces a strikingly clean ladder:

| Village level | MW2 units/sec | Period | Ticks at 50 ms |
|---|---|---|---|
| 1 | 0.33 (1/3) | 3.00 s | 60 |
| 2 | 0.66 (2/3) | 1.50 s | 30 |
| 3 | 1.00 | 1.00 s | 20 |
| 4 | 1.33 (4/3) | 0.75 s | 15 |
| 5 | 1.66 (5/3) | 0.60 s | 12 |

That is exactly `60 / level` ticks. For comparison, 100 ms fails at level 4 and 20 ms fails at
level 4; 25 ms works but doubles the tick count for nothing. Halving the tick duration also meant
halving `ArmySpeedUnitsPerTick` from 0.02 to 0.01 to preserve the 5-second map crossing.

**Settled 28-07-2026 in discovery, shipped 29-07-2026: 50 ms it is.** The proposal above was put to
the user against 25 ms and against keeping 100 ms with rounded periods, and 50 ms was chosen for the
reason derived here — it is the longest integer-millisecond tick making all five periods whole, and
25 ms doubles the tick count for headroom nothing in this phase uses. Rounding at 100 ms was
rejected because it would make numeric parity permanently unreachable while leaving no failing test
to say so. The change shipped as phase 3 **FR-3a** (`f5f3320ec408`) together with the ladder it
exists to express and the full test and QA-script re-authoring both forced; the reasoning lives in
`docs/base-upgrades-and-types/ARCHITECTURE.md` **D-27**. Still true, and still the point of this
section: none of this happened in build mode — it was a discovery decision, only executed there.

## 4. Shipped decisions that the new goal reopens ⚠️

These were settled at kickoff **as permanent, deliberate divergences**, with reasoning, and are
written into `docs/base-upgrades-and-types/REQUIREMENTS.md`. Under the new goal they read as
staging decisions instead. **Nothing here is a defect and nothing should be changed in build mode** —
each needs the user's agreement in a discovery session.

| Decision | Where | Why it was made | Status now |
|---|---|---|---|
| No defence bonus from levels | phase 3 §6 | Keeps every phase-2 combat test meaning what it meant | **Resolved** in discovery 28-07-2026 — reversed by phase 3 FR-3b (G-9, G-10) |
| Three base types forbidden | phase 3 §6 | "MW2 has several; this phase earns two" | Reopened as **G-6** (forge); still unassigned |
| No build time | FR-3 kickoff, 28-07-2026 | A feel benefit the phase could not yet measure | **Resolved** in discovery 28-07-2026 — reversed by phase 3 FR-3c (G-11) |
| Send-strength picker deferred | phase 3 §6 | "A separate decision about how the game plays" | Reopened as **G-3**; **closed** by phase 4 FR-1/FR-2 (`docs/army-sending/`) |
| Three levels, caps 20/35/50 | FR-1 | Tuned for MW3's tick economy | **Resolved** in discovery 28-07-2026 — replaced by phase 3 FR-3a (G-8, §3) |
| Conversion costs 10, not 30 | FR-3 kickoff | Makes towers cheap early, expensive late | **Resolved** in discovery 28-07-2026 — raised to 30 by phase 3 FR-3a (G-14) |

All six are now assigned to a feature — the send-strength picker (**G-3**) closed by phase 4
FR-1/FR-2 (`docs/army-sending/`) as of 30-07-2026, the forge (**G-6**) still awaiting a phase. Each resolution
above was the user's decision in discovery, not a build-mode one, and each is recorded in
`docs/base-upgrades-and-types/REQUIREMENTS.md` §4 and §6 rather than only here.

Phase 3's `REQUIREMENTS.md` §6 was worded as permanent exclusion ("this phase earns two", "deserves
its own phase"), which read as a design position when it was really sequencing. **Corrected
28-07-2026**: §6 now opens with a note reading every bullet as sequencing, the defence-bonus and
build-time bullets are struck through and redirected to FR-3b and FR-3c, and the bullets that remain
genuine exclusions cross-reference the gap that owes them. The exclusions that stayed still bind
phase 3 in full and none may be closed in build mode.

## 5. The IP layer — the divergence that stays 🎨

The one part of the game that is **not** converging on MW2. `docs/ARCHITECTURE.md` S-6 already binds
this: mechanics may follow MW2, assets never do, and the repository is public.

| MW2 | Bug Wars |
|---|---|
| Name, branding, "Mushroom Wars" | **Bug Wars** — MW3 and every "mushroom" name in the repo is placeholder |
| 24 mushroom heroes in 4 tribes (Shrooms, Proteus, Shii'Mori, Grims) | **Insect heroes**, chosen to match the region of the map being played |
| Mushroom armies | **Insect armies**, likewise region-matched |
| Abstract fantasy maps | **Geolocated maps** tied to real-world regions |
| ~120 artifacts | Original items |
| Ranking / trophy road / seasons | **Fame** |

Two consequences worth stating early, because they constrain design rather than art:

- **Region drives roster.** If heroes and armies are picked to match a map's real-world region, then
  map selection and roster selection stop being independent — MW2's model, where you bring any hero
  to any map, does not survive the reskin unchanged. This is a mechanical question wearing an art
  costume, and it deserves its own discovery.
- **Fame replaces ranking**, and MW2's ranking is load-bearing: it gates feature unlocks
  (Domination at 850 points, crafting at 1000, 2v2 at 2000) and drives seasonal decay. Whatever Fame
  is, it inherits that job or those unlocks need another gate. [MW2-ITEMS-AND-PROGRESSION.md](MW2-ITEMS-AND-PROGRESSION.md)
  §3 has the full ladder — note that the *unlock ordering* is worth keeping even though the
  monetisation around it is not.
