# Mushroom Wars 2 — items and out-of-match progression

> Artifacts (the item system), passive skills, the crafting forge, and the trophy road. Confidence
> markers **[T] [S] [D] [?]** are defined in [README.md](README.md).
>
> **None of this exists in the build yet**, and it cannot until persistence does —
> `docs/ARCHITECTURE.md` S-7/S-9 defer identity and persistence until a server exists, and every
> phase to date scopes out save data. It is tracked as [MW2-PARITY.md](MW2-PARITY.md) G-20.
>
> **This is the section where parity with MW2 is least desirable.** Bug Wars replaces artifacts with
> original items and MW2's ranking with **Fame** ([MW2-PARITY.md](MW2-PARITY.md) §5), and much of
> what follows is free-to-play monetisation — three-day craft timers, an 858-day upgrade curve,
> rewards for heroes you do not own — rather than game design. Two things here are genuinely worth
> keeping and are flagged in place: **items that rewrite an ability rather than scale a number**
> (§1.2), and **the feature-unlock ladder** that introduces complexity in a fixed order (§3.1).
> Fame inherits that ladder's job, so read §3 before designing it.

## 1. Artifacts — the item system

### 1.1 Slots and rarity **[S]**

Each hero equips **four** artifacts, one per slot:

| Slot | Notes |
|---|---|
| Weapon | typically attack-flavoured |
| Armor | typically defence-flavoured |
| Amulet | typically utility |
| Spell scroll | typically modifies one ability outright |

Six rarities, ascending:

| # | Rarity | Colour | Craftable |
|---|---|---|---|
| 1 | Common | Gray | — (scrapped for shards) |
| 2 | Uncommon | Green | yes, forge level 1 |
| 3 | Rare | Blue | yes, forge level 10 |
| 4 | Epic | Violet | yes, forge level 30 |
| 5 | Legendary | Yellow | **no** — cannot be crafted **[S]** |
| 6 | Mythical | Red | **no** |

Some artifacts are hero-specific, others fit any hero **[S]**.

### 1.2 What an artifact does **[S]**

An artifact carries up to two kinds of property:

- **Passive** — a flat stat modifier. Published examples: Attack +6% to +10%, Buildings defence +8%,
  Production speed +6% to +8%, Population growth +3%.
- **Active** — a **rewrite of one specific hero ability**. This is the interesting half: an active
  property does not scale a number, it changes what the ability *is*.

Constraints **[S]**: an artifact cannot carry two identical passives or two identical actives. Most
artifacts roll random stats; **Epic and Legendary have fixed stats** that alter a named ability.

### 1.3 Worked examples **[S]**

| Artifact | Rarity / slot | Effect |
|---|---|---|
| **Battle Sword of Destruction** (Rudo) | Legendary weapon | Attack +8%; **Sabotage** gains a specific range and +15 attacking units |
| **Eternal Eye of Invisibility** (Cree) | Legendary amulet | Production speed +8%; **Invisibility** gets double range, **no time limit**, and ×1.3 unit speed |
| **Mythical Scroll of Ice** | Mythical weapon | Attack +10%, Population growth +3%; **unlocks a new skill, "Glacier"**, freezing enemy buildings to level 1 |

The last one is the pattern to notice: a top-rarity item can add an ability the hero does not
otherwise have. The passive percentages are small (+8–10%); the *active* rewrites are what people
chase.

The Fandom wiki carries roughly 120 individually-documented artifacts. They are not reproduced here
— see [SOURCES.md](SOURCES.md) for how to enumerate them if a future phase ever needs the corpus.

### 1.4 The Artifact Forge (crafting) **[T]**

Unlocked at **1000 ranked points**. Levels 1–50, upgraded with **Glyphs** and **Gold**; crafting
consumes **Shards**, obtained by scrapping artifacts.

Crafting an artifact of a rarity costs shards **of the rarity one step below** **[S]**:

| Crafting | Consumes shards of |
|---|---|
| Uncommon (green) | Common (gray) |
| Rare (blue) | Uncommon (green) |
| Epic (purple) | Rare (blue) |

Forge levels unlock rarities and then compound discounts:

| Forge level | Unlocks |
|---|---|
| 1 | craft Uncommon — 25 shards, 3 000 gold, 8.5 h |
| 10 | craft Rare — 55 shards, 25 000 gold, 40 h |
| 30 | craft Epic — 80 shards, 200 000 gold, 72 h |
| 9, 29, 40 | chance of an additional passive property (5%, 10%, 10%) |
| 20, 25, 50 | chance of best-quality result (1%, 1%, 5%) |
| all others | −5% to −25% on gold cost, shard cost, or craft time |

Cumulative cost to reach level 50: **4 290 glyphs and 707 100 gold**, ~858 days of glyph accrual
**[T]**. The player picks the **slot** and **rarity** but **not the hero** — and may receive an
artifact for a hero they do not own **[S]**.

**[D] Read this as a monetisation structure, not a game mechanic.** Three-day craft timers
shortened by gems, an 858-day full-upgrade curve, and rewards for heroes you cannot use are
free-to-play retention design. The *design* idea worth keeping is §1.2's: **items that rewrite an
ability rather than scale a number.** The economy around it is not worth keeping for a personal
single-player project.

## 2. Passive skills **[S]**

A second progression track, unlocked at **650 ranked points**, bought with **Runes** and **Gold**.

Six trees, completed in order: **Wooden → Copper → Iron → Silver → Golden → Diamond**. Cost rises
with tree level and skill power.

Three classes of skill:

| Class | Published examples |
|---|---|
| **Ordinary** | building upgrade speed, building conversion speed, conversion cost, morale effects, energy rate, chest mechanics |
| **Strong** | skill cooldowns, village capacity, tower firing speed, movement speed |
| **Epic** | forge attack, protection, village protection, tower defence |

Runes come from profile level-ups (**4 per level**), hero unlocks, skill upgrades, and chests
**[S]**. Gold comes from chests or gem purchases.

**Known movement-speed tiers**: +3% / +7% / +15% by tier **[S]** (from
[MW2-RULES.md](MW2-RULES.md) §3.1).

**[?] Gap: per-skill percentages, per-tree skill counts, rune costs, and how many passives may be
active at once are not published anywhere we found.** Do not assert them.

**[D] The important observation for MW3:** passive skills are **permanent out-of-match power**, so
two players at the same skill level do not enter a match equal. This is precisely what
`docs/ARCHITECTURE.md` S-7 keeps MW3's door open to and no phase has proposed — and for a
single-player game it would be a pure downside.

## 3. Trophy road **[S]**

Added in update 4.5. Three parts: a reward line, sequential feature unlocking, and seasonal
progression.

Ranked points are the **sum of every hero's rating**, and climbing with one hero yields
progressively fewer points from that hero — an explicit push toward playing the whole roster.

### 3.1 Feature unlock thresholds **[S]**

| Ranked points | Unlocks |
|---|---|
| 200 | Premium |
| 250 | Mushroom Pass |
| 400 | King of the Hill mode |
| 600 | Daily Quests |
| 650 | Passive Skills |
| 850 | Domination mode |
| 1 000 | Artifact Forge / crafting |
| 1 300 | 1v1v1 |
| 1 700 | 1v1v1v1 |
| 2 000 | 2v2 |

Rewards along the road include Runes, Gems, Chests, Gold, and heroes (Pahom, Stella, Marty-O, Ayner,
Chia).

### 3.2 Seasons **[S]**

Monthly. At season end, any hero above **500 ranked points loses 50% of the excess** — a soft reset
that compresses the top without wiping progress.

**[D] The unlock ladder is a genuinely reusable idea**, independent of the monetisation: the game
does not show a new player Domination, 2v2 or passive skills at all. Complexity arrives in a fixed
order tied to demonstrated competence. If MW3 ever builds campaign progression, *this* is the part
of the meta worth borrowing — not the crafting economy.

## 4. Currencies, for completeness **[S]**

| Currency | Source | Spent on |
|---|---|---|
| Gold | chests, gem purchase | ability upgrades, passive skills, forge upgrades, crafting |
| Gems | real money | shortening craft timers, buying gold |
| Runes | profile level-ups (4/level), hero unlocks, chests | passive skills |
| Glyphs | chests | forge upgrades |
| Shards | scrapping artifacts | crafting artifacts |
| Ability cards | chests | upgrading a hero's four abilities 1→5 |
