# Architecture — Forges (phase 6)

> What **this phase** adds or changes. Everything else defers to `docs/ARCHITECTURE.md` (the system
> baseline, S-1..S-9) and to the earlier phases' files, which stay in force:
> `docs/welcome-screen/`, `docs/core-gameplay-loop/`, `docs/base-upgrades-and-types/`,
> `docs/army-sending/`, `docs/morale/`. Decision numbering continues the repo-wide sequence — phase 5
> ended at **D-41**, so this phase starts at **D-42**.

## 1. Overview

Phase 6 is the smallest simulation this project has added since phase 2, and almost all of its design
effort goes into three structural changes rather than into the forge itself.

The forge is genuinely simple: a third `BaseType` with no levels, no production and no fire, whose
only effect is a lookup keyed on *how many of them a player owns*. Every number it needs is published
at `[T]` confidence in `MW2-RULES.md` §2.4, and `CombatResolver` has carried a
`ForgeContributionPercent` constant pinned at identity — with a comment naming G-6 — since phase 3
FR-3b. Making it live is a parameter change, not a rewrite: phase 5 FR-2 already proved the
composition takes an arbitrary number of multiplicative terms when it populated the morale term.

What is *not* simple is what a third type does to two-valued assumptions made when there were only
two types. Conversion is a boolean toggle (D-43). The map is a hardcoded array with no injection
point, which makes every neutral-forge rule untestable until the shipped map changes (D-44). And the
composed index has never had three non-identity terms at once, so integer truncation has never
actually been reachable — this is the phase where it becomes so (D-46). Each of those is a small
change with a wide blast radius across existing tests and QA scripts, which is why FR-1 carries them
all and ships before anything reads a forge.

The map feature carries a fourth such change of its own. A neutral tower **fires** (D-47), and every
firing path in `Match` is currently gated on a tower having an owner — so the feature that looks like
a layout edit turns out to touch combat, morale attribution, and a per-tick optimisation that has
been valid since phase 3.

The phase is sequenced so the type and the map exist before any rule reads them: FR-1 adds the type
and the two structural seams, FR-2 puts a forge and a tower on the board, and only then do FR-3
(combat), FR-4 (morale) and FR-6 (AI) turn effects on one at a time — the same separable-regression
ordering phase 5 used.

```
MW3.Desktop ---+
               +--> MW3.Game (MonoGame) --> MW3.Core (rules, no engine) <-- MW3.Core.Tests
MW3.Android ---+
                    FR-5 draws it              FR-1..FR-4, FR-6 live entirely here
```

## 2. Stack

Unchanged from the baseline — MonoGame 3.8.5, .NET 10 heads, `netstandard2.1` `MW3.Core`, xUnit.
This phase adds no package reference, no content-pipeline asset, and no platform capability. See
`docs/ARCHITECTURE.md` §2.

## 2a. How to run it

`docs/welcome-screen/ARCHITECTURE.md` §2a, `docs/core-gameplay-loop/ARCHITECTURE.md` §2a,
`docs/base-upgrades-and-types/ARCHITECTURE.md` §2a, `docs/army-sending/ARCHITECTURE.md` §2a and
`docs/morale/ARCHITECTURE.md` §2a are all complete and current; everything there applies verbatim,
including the repo-wide `-m:1` build rule and the `down` / `up` / `wait` scripted-input vocabulary.

**No new script directive and no new command-line flag this phase.** Every forge action is expressed
through commands a script can already issue — a convert, a send, a wait. Per `CLAUDE.md`'s standing
correction, this explicitly does **not** mean no new `qa/scripts/` file: each feature here should
expect to add at least one, and FR-3 in particular needs a scripted scenario proving the cap, since a
fifth forge changing nothing is invisible in a screenshot.

`--dump-state` gains **one** field, at FR-3, fixed exactly at that feature's kickoff rather than
here: a per-player forge count. It is written by `MatchScreen` and never by `MW3.Core` (D-26). Every
existing field keeps its name, order and meaning.

**The eight-base layout is the one compatibility break.** Every committed script and test that keys
on a base index rather than a position must be re-checked at FR-2, not assumed safe. A script
weakened to pass rather than re-authored is a defect (the standing rule since phase 3 FR-3a doubled
every tick count).

## 3. Project layout

No new projects. Within the existing ones:

```
src/MW3.Core/
  BaseType.cs           gains Forge - a third member, level-less (D-42)
  ForgeTable.cs         NEW - the published count-to-percentage ladder and the cap, as pure
                         lookups, mirroring LevelTable and MoraleTable. No call site names a
                         forge number (D-22)
  ConvertCommand.cs     carries an explicit target BaseType; the Producer<->Tower toggle at
                         Match.cs:368 is deleted, not extended (D-43)
  MapSlot.cs            carries a BaseType and, for towers, a level
  MapLayout.cs          grows to eight slots; becomes the default value rather than the only one
  Match.cs              accepts a layout, defaulting to MapLayout (D-44); derives each player's
                         forge count on read (D-45); excludes Forge from production alongside
                         Tower; leaves a captured forge's type and single tier intact (D-42);
                         replaces both ownership guards on the tower-fire path so an unowned
                         tower shoots every player's army and no neutral unit (D-47)
  LevelTable.cs         DefencePercentage/garrison cap gain a Forge arm returning one value, not
                         a ladder
  CombatResolver.cs     ForgeContributionPercent stops being a fixed 100 and becomes a parameter;
                         WouldCapture must carry the forge term on both the resolver and the
                         prediction path (D-45, follow-up #68's lesson)
  AiBrain.cs            FR-6 only: converts toward a forge ratio, contests the neutral forge
src/MW3.Game/
  MatchScreen.cs        FR-5 only: draws the forge, the menu's third entry, and the count
```

File names are intent, not contract — `/kickoff` and `/implement` may split or rename them. What
**is** contract: which project each concern lives in.

## 4. Key decisions

**D-42: a forge is a third `BaseType` member, level-less, and capture does not demote it.** The
alternative — modelling a forge as a separate concept beside `Base` because it has no levels and no
production — was rejected. A forge is captured, converted, garrisoned, attacked and drawn exactly
like the other two, so every code path that already handles a `Base` should keep handling it; only
its *effect* is unusual. The cost is that `LevelTable` gains an arm returning a single value where
the others return a ladder, and that phase 3's "capturing drops a building one level" rule has
nothing to drop. That second point is settled here rather than left to build mode: **capture leaves a
forge a forge, at its single tier.** It does not revert to a producer, and it is not destroyed. FR-3c's
one-second recapture grace applies unchanged, since it keys on ownership rather than level.

**D-43: `ConvertCommand` carries an explicit target type; the toggle is deleted, not extended.**
`Match.cs:368` reads `target.Type == BaseType.Producer ? BaseType.Tower : BaseType.Producer`. With
three types a toggle has no defensible semantics — "convert" from a tower could mean either other
type. The command therefore names its destination. This is an **S-8 interface change**: human input
and AI produce the same command types, so both heads and `AiBrain` change together, and neither can
express a conversion the other cannot. It is also a deliberate *break* rather than an overload — a
defaulted parameter would leave the two-valued assumption alive in call sites that never get updated.
Blast radius: `ConvertTests`, `ConvertDeterminismTests`, `BaseActionAvailability`, `AvailableActionsTests`,
and the `convert-*` and `greyed-convert-*` QA scripts.

**D-44: the map layout is a value `Match` accepts, defaulting to the shipped layout.** `MapLayout` is
`internal static` with a hardcoded array, and `Match` builds its bases from it directly. That means
no rule about a *neutral* forge — including `MW2-RULES.md` §5.2's +200 capture value — can be tested
until the shipped map itself changes, which would force the map feature ahead of the rules that
motivate it and leave a base type undrawable on the QA surface for two features. Passing the layout
in removes the ordering constraint entirely, lets tests build a neutral-forge scenario without
touching the shipped map, and is a real step toward **G-18**. It does **not** introduce a second
shipped map, a map file format, or map selection — those stay out of scope (§6). `MapSlot` gains a
`BaseType` and, for a pre-placed tower, a level.

**D-45: a player's forge count is derived from the board on read, never stored.** The count is a pure
function of the bases a player owns, so storing it would duplicate truth and create a desync class —
exactly the failure mode follow-up #68 found when `AiBrain`'s predictions and `CombatResolver`
disagreed about building defence. With eight bases the scan is trivial, and the no-allocation-per-tick
rule is satisfied by counting in place rather than materialising a collection. The corollary is
binding: **`CombatResolver.WouldCapture` must read the forge term on both the resolve path and the
prediction path.** Phase 5 patched FR-2's issue for the same hazard against morale after #68 closed it
against building defence; this is the third occurrence and it is to be handled at kickoff, not
rediscovered.

**D-46: the composed index keeps integer truncation, and this phase is the first time a remainder is
reachable.** `ComposePercentages` computes `(long)base * morale * forge / 100`, yielding a
basis-point index. Its own comment records that while the forge term sits at identity the division
never floors. That stops being true here: a level-2 village (110%) defended at morale 1 (125%) by an
owner with three forges (145%) gives `110 × 125 × 145 = 1 993 750`, which is not divisible by 100.
Truncation is kept — it is the existing behaviour, it is deterministic as S-8 requires, and the error
is bounded below one basis point, under 0.01% of the index. It is not neutral, though: a truncated
*defender* index makes `Bu = (a/d) × Wu` marginally larger, so the rounding favours the attacker. That
is recorded rather than corrected, and FR-3 must pin a known-remainder triple in a regression test so
a future refactor cannot silently change the rounding mode. Raising precision was rejected as
unjustified complexity for a sub-0.01% effect; rounding-to-nearest was rejected because truncation is
what already ships and a silent change to live combat arithmetic is worse than a documented bias.

**D-47: a neutral forge buffs nobody, but a neutral tower shoots everybody.** Settled with the user
05-08-2026, and the two halves are asymmetric on purpose.

The forge half is free: D-45 counts *owned* forges, so a neutral forge contributes to no one's
indices until taken. Recorded here precisely because it is free, so nobody later "discovers" that
neutral forges need excluding.

The tower half is **not** free, and an earlier draft of this decision had it backwards. A neutral
tower **fires at any player's army in range, and never at neutral units.** Today `Match.cs:485`
(`HasAnyOwnedTower`) and `:508` (`Owner is not Player towerOwner`) both gate firing on ownership, so
the shipped behaviour would be an inert tower; both guards change. Target selection currently skips
armies whose owner equals the tower's owner — for an unowned tower that test degenerates, so the rule
becomes "target any army with a **non-null** owner". No neutral army exists in MW3 today (neutral
bases never send), so the neutral-unit exclusion is a guard that cannot yet fire; it is written
anyway, because the alternative is a rule that silently acquires the wrong behaviour the first time
a later phase gives neutrals a send.

Three consequences the implementer must not rediscover:

- **The early-match optimisation dies.** `HasAnyOwnedTower` exists to skip per-tick tower evaluation
  until a tower is built. With a neutral tower present from tick 0, that guard is true in every match
  from the first tick, so tower fire is now evaluated every tick always. The no-allocation-per-tick
  rule (§5) therefore binds harder than it did.
- **A neutral tower is a morale sink.** `Match.cs:555-559` awards the killer's owner
  `AttackingUnitDestroyedGain` and charges the victim `AttackingUnitDiedLoss`. With no owner there is
  nobody to award, while the victim still pays. That is consistent with D-41 (only attacking units
  generate morale) and is arguably good design — it prices routing through the contested middle — but
  it is a *decision*, not a side effect, and FR-2's kickoff must state it rather than let it fall out
  of a null check.
- **FR-2 stops being a cheap feature.** A firing neutral tower changes how both players cross the
  centre of the map from the opening seconds. It does not threaten the zero-forge baseline
  guarantee — that is scoped to the six original bases — but it makes FR-2 a behavioural feature
  needing its own QA scenario, not a layout edit.

## 5. Cross-cutting conventions

Everything phases 2–5 established stays in force. What this phase adds for build mode:

- **A forge is never given a level.** Any code that reaches for `Base.Level` on a forge is a defect,
  not a style preference. `LevelTable`'s forge arm returns one value by design (D-42).
- **Every forge number comes from `ForgeTable`.** D-22 binds even when an issue's own prose states
  the literal — the standard phase 5 FR-6's implementer applied correctly, refusing to hardcode a
  value the issue text itself named.
- **The zero-forge baseline is a test, not an aspiration.** Each feature must show that a match with
  no forges produces today's numbers on the six original bases.
- **Re-check, never weaken, the eight-base scripts.** Existing `qa/scripts/` files and tests that key
  on a base index must be re-authored against the new layout at FR-2.
- **Both call paths, every time.** Any new term entering the combat formula enters `CombatResolver`'s
  resolve path *and* its prediction path in the same change (D-45).
