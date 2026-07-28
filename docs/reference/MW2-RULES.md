# Mushroom Wars 2 — the simulation

> The rules and numbers of the reference game. Confidence markers **[T] [S] [D] [?]** are defined in
> [README.md](README.md). Sources in [SOURCES.md](SOURCES.md).

## 1. The core loop in one paragraph

Every player owns buildings. Villages generate units by themselves. You send a fraction of a
building's garrison to another building; the units walk there in a wave and either **reinforce** it
(if you already own it) or **attack** it (if you do not). Combat is resolved when a wave arrives, in
one arithmetic step — units never fight each other on the map **[S]**. Emptying an enemy or neutral
building captures it. On top of that sit four multipliers that make the game a game: **building
level**, **building type**, **forges** (a global attack/defence buff), and **morale** (a global
attack/defence/speed buff earned by playing well). Heroes add four active abilities on a shared
energy pool.

## 2. Buildings

Five kinds. Three are the general-purpose set; two are mode-specific objectives.

| Building | Produces units | Levels | Convertible | Purpose |
|---|---|---|---|---|
| Village | yes | 5 (**[?]**, see §2.2) | yes | economy |
| Tower | no | 4 | yes | shoots enemy units in a radius |
| Forge | no | 1 only **[S]** | yes | global attack + defence buff to its owner |
| Domination village | yes | 1, no upgrade **[S]** | **no** | Domination-mode objective |
| King of the Hill village | yes | 1, no upgrade **[S]** | **no** | KotH-mode objective |

### 2.1 Conversion

Villages, towers and forges convert into each other for **30 units** **[T]**. A converted building
starts at **level 1** **[S]** — conversion destroys accumulated levels. This is the mechanic MW3's
phase-3 FR-3 mirrors (at a cost of 10, not 30).

### 2.2 Villages **[T]**

| Level | Defence | Unit capacity | Production (units/sec) | Price | Time (s) |
|---|---|---|---|---|---|
| 1 | 100% | 20 | 0.33 | 30 | 5 |
| 2 | 110% | 40 | 0.66 | 5 | 5 |
| 3 | 120% | 60 | 1.00 | 10 | 10 |
| 4 | 130% | 80 | 1.33 | 20 | 15 |
| 5 | 140% | 100 | 1.66 | — | — |

**Reading the price column [D].** Row 1's `30` is the **conversion price** (§2.1), not an upgrade
cost; rows 2–4 are the cost to *reach* that level. Two independent facts confirm this reading:
prose says villages can be upgraded **three times** **[S]** (1→2→3→4, which is exactly the three
priced rows), and the tower table under the same reading yields "a constant 20 units per upgrade",
which is what the tower page says in words **[S]**. Level 5 has no price because it is **not
reachable by upgrading** **[?]** — presumably granted by map setup, a passive skill, or a hero.

Production is **linear in level**: 0.33 × level **[D]**. Capacity is **20 × level** **[D]**. Defence
is **+10 percentage points per level** **[D]**. Cost and time both rise with level **[S]**.

### 2.3 Towers **[T]**

| Level | Defence | Shooting radius | Shooting speed | Price | Time (s) |
|---|---|---|---|---|---|
| 1 | 140% | 100% | 90 | 30 | 5 |
| 2 | 170% | 110% | 120 | 20 | 5 |
| 3 | 190% | 125% | 150 | 20 | 10 |
| 4 | 200% | 140% | 180 | 20 | 15 |

- Towers **produce nothing** **[S]** and are the most defensible building: a level-1 tower already
  out-defends a level-5 village (140% vs 140%) and a level-4 tower doubles base defence.
- **Shooting radius is relative** — 100% at level 1, with no absolute unit value published **[?]**.
- **"Shooting speed" is under-labelled [?].** It is most likely *projectile* speed in map units per
  second, matching the unit base speed of 90 (§3.1) and the fact that projectiles have a physical
  damage radius (Kenor's Explosive Shells "increases the damage radius of the projectiles by 45–55
  units" **[S]**). The competing reading — rate of fire — is not ruled out. **Do not use this number
  as a tuning input without resolving it.**
- Price column reads as villages do: `30` to convert into a tower, then 20 per upgrade **[D]/[S]**.

### 2.4 Forges **[T]**

A forge is a **global buff to its owner**, not a local one — it applies to that player's attacks and
defences everywhere on the map. Only the count of forges matters; forges have one tier **[S]**.

| Forges owned | Defence | Attack | Conversion price |
|---|---|---|---|
| 1 | 125% | 150% | 30 |
| 2 | 135% | 175% | 30 |
| 3 | 145% | 190% | 30 |
| 4 | 150% | 200% | 30 |

A **5th forge does nothing** **[S]** — 4 is the cap. Returns diminish sharply: the first forge is
worth +50% attack, the fourth only +10%. Rule of thumb from the source: one forge per four
unit-producing buildings **[S]**, and convert something into a forge *before* committing to an
attack **[S]**.

### 2.5 Capture and demotion

- Capturing a building **drops it one level** (towers and villages) **[S]**.
- **The 1-second grace window**: if you retake a building within 1 second of losing it, it does
  **not** lose the next level down **[S]**. This is a deliberate anti-thrash rule and the same
  1-second window governs the Domination loss condition (§6.1).

### 2.6 Objective buildings

Both are immune to *building-targeted* spells but not to *area* spells **[S]** — a real design
lesson: objective buildings are made spell-proof rather than merely tough, so a single ability
cannot decide a mode.

- **Domination village** — one tier, no upgrade, no conversion; immune to village-targeting spells
  (Stella's defence strip, Rudo's defence buff) but area spells like Marty-O's energy shield still
  work on units around it **[S]**.
- **King of the Hill village** — one tier, no conversion, "light armour", immune to
  building-targeting spells, vulnerable to area spells **[S]**.

## 3. Units and movement

There is **one unit type**. Units differ only by owner and by the buffs their owner has.

### 3.1 Speed

- Base speed **90 map units/second** **[T]**.
- **Morale** contributes at most **+50%**, i.e. 135 units/sec at morale 5 **[T]** — consistent with
  the morale table (§5) showing 150% speed at level 5 **[D]**.
- Passive skills: +3% / +7% / +15% by tier **[S]**.
- Hero abilities range from **−300%** (Ankh's Ring of Slow-Mo) to **+65%** (Rudo's Boots of
  Swiftness) **[S]**.

### 3.2 Units do not fight each other

Opposing waves pass straight through one another on the map **[S]**. All combat happens **at a
building**. The only thing that can damage a unit in transit is a **tower**, or a hero ability.
MW3 phase 3 FR-4 adopts exactly this rule.

### 3.3 Waves and the send fraction

- A send is issued as a **percentage of the source garrison**: the game has a `25 / 50 / 75 / 100%`
  setting **[S]**.
- A full wave is **8 units** **[S]**; a larger send arrives as several waves in sequence, which
  "don't strike simultaneously" — giving the defender time to regenerate, letting towers fire
  between waves, and letting reinforcements land **[S]**. **This is load-bearing**: it is why a
  400-unit attack is not simply a 400-unit arithmetic check, and why defending is viable at all.
- **Snaking** — set sending to 25% and tap the target repeatedly. Each successive send is 25% of a
  shrinking garrison, producing a long tapered column instead of discrete waves **[S]**. Thresholds
  for a clean snake: ≤35 units at 25%, ≤17 at 50%, ≤11 at 75% **[S]**. Its competitive value is
  partly **deception** — "the point is to make a 35 unit attack look like a 15 unit attack" **[S]**
  — and partly defensive spread across several threatened buildings.
- Multi-building attacks are timed so waves **converge on the target simultaneously** **[S]**;
  there is a multiselect control for issuing them.

Snaking originated as a **bug in the original Mushroom Wars that was kept as a feature** in the
sequel **[S]**.

## 4. Combat

### 4.1 The formula **[T]**

```
Bu = (a / d) × Wu
```

- `Bu` — units destroyed inside the defending building
- `a` — the **attacker's** total attack index
- `d` — the **defender's** total protection index
- `Wu` — units in the arriving wave

Then `Du_new = Du − Bu`. If `Du_new ≥ 0` the defender holds; if `Du_new < 0` the attacker captures
the building **[S]**.

### 4.2 What feeds the indices **[S]**

| `d` — defender's protection | `a` — attacker's attack |
|---|---|
| the building's own defence (§2.2, §2.3) | morale attack index (§5) |
| morale defence index (§5) | forge attack bonus (§2.4) |
| forge defence bonus (§2.4) | hero abilities (Ayner's Rage) |
| hero abilities (Rudo's Defensive Walls, Stella's Decrease Defense) | |

At morale 0 with no forges and no abilities, `a = d = 100%`, so `Bu = Wu` — a **flat 1:1 exchange**
**[S]**. That 1:1 baseline is exactly the combat rule MW3 uses today (phase 2 FR-4).

### 4.3 Worked example **[D]**

A 100-unit wave, attacker at morale 3 (attack 115%) with 2 forges (attack 175%), against a level-3
village (defence 120%) whose owner is at morale 1 (defence 125%) with 1 forge (defence 125%):

```
a = 1.15 × 1.75 = 2.0125
d = 1.20 × 1.25 × 1.25 = 1.875
Bu = (2.0125 / 1.875) × 100 = 107.3 units destroyed
```

**[?]** The multipliers are assumed to **multiply**; the sources say only "combines". Additive
stacking is not ruled out. The example is arithmetic, not an observation.

## 5. Morale

Morale is a **per-player** global multiplier displayed as 0–5 suns. Half-suns are cosmetic and show
only progress toward the next whole sun **[S]**. It is the game's comeback-and-snowball dial and the
single most-cited skill differentiator in the sources ("always watch your own and your opponent's
morale, at all times" **[S]**).

### 5.1 Effects **[T]**

| Morale | Cost to reach (morale points) | Defence | Attack | Unit speed |
|---|---|---|---|---|
| 0 | — | 100% | 100% | 100% |
| 1 | 500 | 125% | 105% | 110% |
| 2 | 1 000 | 150% | 110% | 120% |
| 3 | 2 000 | 175% | 115% | 130% |
| 4 | 4 000 | 200% | 120% | 140% |
| 5 | 8 000 | 225% | 125% | 150% |

**The asymmetry is the whole design.** Morale buys **+125% defence but only +25% attack** across
its full range. High morale makes you very hard to kill and only slightly harder-hitting, so morale
rewards *not losing* rather than *winning harder* — and the cost doubles each level, so morale 5 is
a genuine achievement rather than a state you drift into.

### 5.2 Gains **[T]**

| Event | Morale points |
|---|---|
| Destroying an enemy attacking soldier | **+10 each** |
| Capture neutral village, level 1 / 2 / 3 / 4 / 5 | +40 / +100 / +160 / +220 / +300 |
| Capture neutral tower, level 1 / 2 / 3 / 4 | +80 / +200 / +320 / +440 |
| Capture neutral forge | +200 |
| Capture **opponent's** village, level 1 / 2 / 3 / 4 / 5 | +100 / +250 / +400 / +550 / +750 |
| Capture **opponent's** tower, level 1 / 2 / 3 / 4 | +200 / +500 / +800 / +1100 |
| Capture **opponent's** forge | +300 |
| Village upgrade to level 1 / 2 / 3 / 4 | +50 / +100 / +150 / +200 |
| Tower upgrade to level 1 / 2 / 3 / 4 | +100 / +200 / +300 / +400 |

### 5.3 Losses **[T]**

| Event | Morale points |
|---|---|
| Your unit dies attacking | **−10 each** |
| Lose village, level 1 / 2 / 3 / 4 / 5 | −50 / −120 / −200 / −280 / −380 |
| Lose tower, level 1 / 2 / 3 / 4 | −100 / −250 / −400 / −550 |
| Lose forge | −100 |

Note the deliberate asymmetries **[D]**: taking a building from an *opponent* is worth roughly 2.5×
taking the same neutral; a tower is worth about double a village of the same level; and **losing a
building costs less than the enemy gains for taking it** — so a trade is net-positive for the
aggressor and morale flows toward whoever is pressing.

### 5.4 Inactivity decay **[T]**

Morale bleeds if you stop playing, and **higher morale bleeds faster and starts sooner**:

| Morale | Idle seconds before decay starts | Points lost per second |
|---|---|---|
| 0 | 10 | −10 |
| 1 | 9 | −20 |
| 2 | 8 | −25 |
| 3 | 7 | −50 |
| 4 | 6 | −100 |
| 5 | 5 | −200 |

**[D]** At morale 5 the decay is −200/sec against an 8 000-point level: sitting still costs a full
morale level in about 40 seconds. This is an explicit anti-turtle rule and the reason the game feels
frantic — morale is a *tempo* stat, not a stockpile.

## 6. Energy and hero abilities

### 6.1 Energy

- Shared pool per hero, **cap 500** **[S]** — enough to hold exactly two abilities' worth (e.g. the
  1st and 4th, or 2nd and 3rd).
- **Passive regeneration: 2.5 energy/second** **[S]**.
- **From losses: `energy = 0.45 × k` per unit that dies attacking**, where `k` is an index of *how
  much morale you lack* **[S]**:

  | Morale | 0 | 1 | 2 | 3 | 4 | 5 |
  |---|---|---|---|---|---|---|
  | Index `k` | 5 | 4 | 3 | 2 | 1 | 1 |

  **[D] The formula checks out** against the source's own worked example: losing 723 units at morale
  4 gives `723 × 0.45 × 1 ≈ 325`, and losing 145 units at morale 0 gives `145 × 0.45 × 5 ≈ 326`.
  Same energy, 5× fewer casualties.
- Abilities that **damage troops generate no energy** for their user **[S]** — but Ayner's Ring of
  Fire explicitly *gives the opponent* energy **[S]**.

**This is the game's rubber band.** A losing player is at low morale, so every unit they lose pays
5× the energy of a winning player's. Losing badly buys you abilities; winning buys you multipliers.

### 6.2 Ability slots **[T]**

Each hero has four abilities, one per slot. Slot number fixes the energy cost and the base cooldown:

| Slot | Energy | Base cooldown (s) | Cooldown range across ability levels 1–5 **[D]** |
|---|---|---|---|
| 1 | 100 | 20 | 20–40 |
| 2 | 200 | 40 | 40–60 |
| 3 | 300 | 70 | 70–100 |
| 4 | 400 | 100 | 100–140 |

**[D]** The right-hand column is reconstructed from the per-hero pages, which quote exactly these
four ranges for every hero. Upgrading an ability makes it **stronger but more expensive and
longer-cooling** — "the higher level of ability, the more it costs and longer for it to cooldown"
**[S]**. Abilities upgrade 1→5 with ability cards and gold **[S]**.

### 6.3 Rush Mode **[S]**

At **2 minutes** into a multiplayer, 2v2 or custom match: **double energy collection and half
cooldowns**, for everyone. Not active in campaign. A hard-coded escalation that forces matches to
resolve.

## 7. Game modes

| Mode | Win condition |
|---|---|
| **Conquest** (standard) | Capture all opponent buildings **[S]** |
| **Domination** | Hold **all** domination villages — or eliminate the opponent first **[S]** |
| **King of the Hill** | Drive your point counter to 0 — or eliminate the opponent first **[S]** |

Player counts: 1v1, 1v1v1, 1v1v1v1, 2v2, plus campaign and skirmish vs AI **[S]**.

### 7.1 Domination **[S]**

Capture every domination village on the map. If your opponent holds them all, you have **1 second**
to retake one or you lose — the same 1-second grace window as building recapture (§2.5).

### 7.2 King of the Hill **[S]**

- Each player starts with **300 points** (600 shared in 2v2).
- Each KotH village you hold **drains 1 point/second** from your own counter. More villages, faster
  win.
- First to 0 wins. Points are **fractional, not integers**, so a counter can display ~0 without
  having won yet.

## 8. Campaign and AI

- **Four story campaigns, 50 missions each** **[S]** — one per tribe, each locked to that tribe's
  heroes.
- Skirmish vs AI exists as a separate mode **[S]**.
- **No source documents the AI's decision-making.** Nothing published describes how the MW2 AI
  chooses to attack, upgrade or convert. MW3's AI heuristics are therefore original work, not a
  reimplementation — see `docs/core-gameplay-loop/` and phase 3 FR-6.

## 9. Platform and release facts **[S]**

Unity engine with a custom C++ renderer. Launched 13 October 2016 on iOS, Android, Windows and tvOS;
Nintendo Switch July 2018; other consoles later. Metacritic 76 (PC) / 72 (Switch). Developed by
Zillion Whales, with staff from the original Mushroom Wars; concept formalised 2014.

Relevant to MW3 only as evidence for `docs/ARCHITECTURE.md` S-1: the reference game runs on
integrated graphics, so rendering capability is not MW3's constraint.

## 10. Known gaps

Things Ivan should **not** claim to know:

- **Absolute tower range in map units** — published only as a percentage of an unstated base (§2.3).
- **Whether "shooting speed" is projectile velocity or rate of fire** (§2.3).
- **Whether attack/defence multipliers stack multiplicatively or additively** (§4.3).
- **How level-5 villages are reached** (§2.2).
- **Tower damage per shot** — never published. Only Kenor's ability implies projectiles have an
  area of effect.
- **Map dimensions**, so no absolute distance in the game can be converted to MW3's normalized
  0..1 `MapPoint` space.
- **AI behaviour** (§8).
- **Per-skill passive-skill percentages** — see [MW2-ITEMS-AND-PROGRESSION.md](MW2-ITEMS-AND-PROGRESSION.md).
