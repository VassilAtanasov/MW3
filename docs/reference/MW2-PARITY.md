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
> merged (issues #30, #32, #34) and FR-4 (#36) kicked off.

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

## 2. Gaps to close 🔴

Ordered by how much each one changes how the game plays. Each row is a candidate feature for a
future phase.

### 2.1 Large — these change the shape of a match

| # | MW2 | MW3 today | Notes |
|---|---|---|---|
| G-1 | **Morale**: a per-player multiplier, 0–5 suns, worth +125% defence / +25% attack / +50% speed, earned by capturing and defending, drained by losses and by **inactivity on a timer that accelerates as you climb** | Nothing | The largest single system in MW2 and the one every source calls the skill differentiator. Anti-turtle, anti-snowball, and it touches combat, speed and production at once. [MW2-RULES.md](MW2-RULES.md) §5 |
| G-2 | **Waves**: a send arrives as successive 8-unit waves that do not strike simultaneously, so defenders regenerate, towers fire, and reinforcements land mid-fight | An army is one object arriving whole | This is what makes defending a larger attack viable, and it is the precondition for G-3. Probably the highest-leverage gap after morale |
| G-3 | **Send-strength picker**: 25 / 50 / 75 / 100% of the garrison, plus **snaking** (repeated 25% sends producing a tapered column, used for deception and defensive spread) | Fixed at half the garrison, rounded down, minimum 1 | Phase 3 §6 scopes this out as "its own phase". Under the new goal that phase is now required, not optional |
| G-4 | **Heroes**: 24 across 4 tribes, 4 abilities each on a shared 500-energy pool, with slot-fixed costs and cooldowns | Nothing | Becomes **insect heroes** (§5). [MW2-HEROES.md](MW2-HEROES.md) — note §"Design patterns worth stealing" |
| G-5 | **Energy**: 2.5/sec passive plus `0.45 × k` per unit lost attacking, where `k` rises as morale falls — so a losing player earns 5× the energy per casualty | Nothing | The game's rubber band. Depends on G-1 for `k` |
| G-6 | **Forges**: a third building type giving a global attack/defence buff to its owner, 125–150% defence and 150–200% attack, capping at 4 | Two building types only | Phase 3 §6 forbids a third type *this phase*. Feeds the combat formula (G-7) |
| G-7 | **Combat formula** `Bu = (a/d) × Wu`, with building defence, morale and forges stacking into `a` and `d` | Plain 1:1 | MW3 already **equals** MW2 at morale 0 with no forges and no abilities — so this gap closes largely by closing G-1 and G-6 rather than by rewriting arithmetic |

### 2.2 Medium — buildings and economy

| # | MW2 | MW3 today | Notes |
|---|---|---|---|
| G-8 | Villages have **5 levels** (4 reachable by upgrading **[?]**); towers have **4** | 3 levels, uniform across types | See §3 — closing this is partly a tuning question |
| G-9 | **Levels buy defence**: villages +10pp/level (100→140%), towers 140→200% | No defence bonus at all | Phase 3 §6 refused this deliberately, to keep every phase-2 combat test meaningful. Now a gap — §4 |
| G-10 | Towers are **far more defensible** than villages — a level-1 tower already matches a level-5 village | Towers defend identically to producers | Follows from G-9. Currently MW3's tower trades production for range only |
| G-11 | **Build/upgrade time**: 5 / 5 / 10 / 15 s, and conversion takes time too | Instant | Settled as "no build time" at FR-3's kickoff. Now a gap — §4 |
| G-12 | **Recapture grace**: retake a building within 1 second and it does not demote further | No such rule | Small, cheap, and a genuinely good anti-thrash rule. The same 1-second window governs the Domination loss condition |
| G-13 | Tower shots have a **damage radius** (implied by Kenor's Explosive Shells) | 1 unit per shot, single target, closest army | MW2's own tower damage is **never published** — closing this needs observation, not research |
| G-14 | Conversion costs **30**; upgrades cost 5 / 10 / 20 (villages) and a flat 20 (towers) | Conversion 10; upgrades 6 / 16 | Tuning, not behaviour — §3 |

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

Two different questions hide inside "as close as possible", and they need separating before G-8 or
G-14 can be actioned:

- **Behavioural parity** — five village levels exist; a level buys defence; conversion resets to
  level 1. This is what "close to MW2" plainly means and it is not in question.
- **Numeric parity** — caps are literally 20/40/60/80/100, a village makes literally 0.33 units/sec,
  conversion costs literally 30.

MW3's economy is deliberately *tick-based and smaller*: caps 20/35/50 against MW2's 20/40/60/80/100,
and 1 unit per 10 ticks against 0.33/sec. Phase 3's FR-4 is the standing warning about copying
across — discovery proposed tower ranges that, checked against MW3's army speed, would have made
towers **literally unable to fire**. The numbers were recalibrated at kickoff and the reasoning is in
`docs/base-upgrades-and-types/REQUIREMENTS.md` §"Tuning values".

**Settled 28-07-2026: adopt MW2's literal numbers, and choose the tick rate to make them work.**
MW2's published tables are the target values — caps 20/40/60/80/100, upgrade costs 5/10/20,
conversion 30, five village levels and four tower levels — rather than ratios rescaled to MW3's
current economy. This buys the closest possible parity and makes MW2's own balance data directly
consultable; the price is that the shipped phase-1-to-3 tuning is provisional, and the tests and QA
scripts pinned to it will be re-authored by whichever phase closes **G-8** and **G-14**.

Consequence: **`Match.TickDurationMilliseconds` is provisional too.** It is 100 ms today (10 Hz),
and 10 Hz cannot express MW2's production ladder — the level-4 village's 1.33 units/sec needs a
0.75-second period, which is 7.5 ticks. The tick duration must be chosen so every MW2 production
period lands on a whole tick, since D-24 keeps all simulation arithmetic on integer ticks.

**[D] 50 ms (20 Hz) is the natural choice** — the longest integer-millisecond tick that makes all
five MW2 periods whole, and it produces a strikingly clean ladder:

| Village level | MW2 units/sec | Period | Ticks at 50 ms |
|---|---|---|---|
| 1 | 0.33 (1/3) | 3.00 s | 60 |
| 2 | 0.66 (2/3) | 1.50 s | 30 |
| 3 | 1.00 | 1.00 s | 20 |
| 4 | 1.33 (4/3) | 0.75 s | 15 |
| 5 | 1.66 (5/3) | 0.60 s | 12 |

That is exactly `60 / level` ticks. For comparison, 100 ms fails at level 4 and 20 ms fails at
level 4; 25 ms works but doubles the tick count for nothing. Halving the tick duration also means
halving `ArmySpeedUnitsPerTick` from 0.02 to 0.01 to preserve the current 5-second map crossing.

This is a **derived proposal, not a decision** — it belongs to the kickoff that closes G-8, which
owns the tick-rate change, the re-tuning, and the test re-authoring together. Nothing changes in
build mode on the strength of this table.

## 4. Shipped decisions that the new goal reopens ⚠️

These were settled at kickoff **as permanent, deliberate divergences**, with reasoning, and are
written into `docs/base-upgrades-and-types/REQUIREMENTS.md`. Under the new goal they read as
staging decisions instead. **Nothing here is a defect and nothing should be changed in build mode** —
each needs the user's agreement in a discovery session.

| Decision | Where | Why it was made | Status now |
|---|---|---|---|
| No defence bonus from levels | phase 3 §6 | Keeps every phase-2 combat test meaning what it meant | Reopened as **G-9** |
| Three base types forbidden | phase 3 §6 | "MW2 has several; this phase earns two" | Reopened as **G-6** (forge) |
| No build time | FR-3 kickoff, 28-07-2026 | A feel benefit the phase could not yet measure | Reopened as **G-11** |
| Send-strength picker deferred | phase 3 §6 | "A separate decision about how the game plays" | Reopened as **G-3**, now required rather than optional |
| Three levels, caps 20/35/50 | FR-1 | Tuned for MW3's tick economy | Reopened as **G-8** / §3 |
| Conversion costs 10, not 30 | FR-3 kickoff | Makes towers cheap early, expensive late | Reopened as **G-14** / §3 |

Phase 3's `REQUIREMENTS.md` §6 is worded as permanent exclusion ("this phase earns two", "deserves
its own phase"). That wording is now **misleading rather than wrong** — the exclusions still bind
phase 3, but they read as design positions when they are really sequencing. Worth correcting the
next time that file is touched, in the same change, the way phase 3 corrects phase 2 in place.

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
