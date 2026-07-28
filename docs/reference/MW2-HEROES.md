# Mushroom Wars 2 — heroes and abilities

> All 24 heroes, 4 tribes, 96 abilities. Confidence markers **[T] [S] [D] [?]** are defined in
> [README.md](README.md). The energy and cooldown system that governs every ability below is in
> [MW2-RULES.md](MW2-RULES.md) §6.
>
> **Heroes are a planned gap, not an exclusion** — [MW2-PARITY.md](MW2-PARITY.md) G-4. The build has
> none today, and phase 3 explicitly scopes out "tribe abilities, a rage meter, and any active power
> the player triggers outside a base", but that is sequencing: the goal is near-exact MW2 parity, so
> a hero-and-ability system is owed by some future phase and this file is its design survey.
>
> **The roster below is the mechanical reference only.** Bug Wars replaces all 24 mushroom heroes
> with **insects matched to the region of each geolocated map** ([MW2-PARITY.md](MW2-PARITY.md) §5).
> Read the abilities, the slot economics and the design patterns; the names, tribes and flavour are
> MW2's IP and do not carry over.

## How to read every entry

Slot number determines cost and cooldown for **every** hero identically ([MW2-RULES.md](MW2-RULES.md)
§6.2), so the table is stated once and never repeated:

| Slot | Energy | Cooldown (ability level 1 → 5) |
|---|---|---|
| 1 | 100 | 20 → 40 s |
| 2 | 200 | 40 → 60 s |
| 3 | 300 | 70 → 100 s |
| 4 | 400 | 100 → 140 s |

Ranges written like "45–65%" are **ability level 1 → level 5** scaling **[S]**. Where a hero's page
gives no numbers, none are published — the newer heroes are documented in prose only **[?]**.

## The tribes **[S]**

| Tribe | Character | Heroes |
|---|---|---|
| **Shrooms** | the starting tribe; straightforward, builds toward nuance | Rudo, Ayner, Kenor, Wilford, Odur, Grokk |
| **Proteus** | alien; mystical, defies the game's physics | Marty-O, Cree, Zik, Pix-O, Scar |
| **Shii'Mori** | amazonian; nature, ice and terrain control | Stella, Trini, Dora, Chia, Utii, Sato'Shii, Ban'shii |
| **Grims** | necromantic; death, theft and denial | Pahom, Ankh, Klotz, Mouro, Boggi, Bek |

The campaign gives each tribe 50 missions and locks them to that tribe's heroes. The eight
campaign-era heroes are Rudo/Ayner, Marty-O/Cree, Stella/Trini and Pahom/Ankh **[S]**; the other
sixteen were added over the game's life.

**[?]** Trini's own page states her tribe as "Natural"; the roster index places her under
Shii'Mori. Treat Shii'Mori as correct — likely a translation artefact on a Russian-origin site.

---

## Shrooms

### Rudo — armoured and cunning warrior · defensive · free starter hero **[S]**
| # | Ability | Effect |
|---|---|---|
| 1 | **Defensive Walls** | Increases a building's defence to 125–135% for 5–10 s |
| 2 | **Boots of Swiftness** | +45–65% movement speed to **all** units in an area — enemies included, and only affects units already moving |
| 3 | **Magic Star** | +1 morale star, and blocks morale decline for 5–10 s |
| 4 | **Sabotage** | 30–45 units of attack, split evenly across **all** enemy buildings, delayed 2.2 s |

Recommended for beginners. Note ability 2 hits everyone — a cheap speed buff with a real cost.

### Ayner — "Commander-in-Chief pyrokinetic" · pure attack **[S]**
| # | Ability | Effect |
|---|---|---|
| 1 | **Shackles of War** | Chains enemy units to a building — nothing may leave it for 8–12 s |
| 2 | **Equate Morale** | **Averages all players' morale.** Called one of the most powerful and cheapest spells in the game |
| 3 | **Rage** | ×1.5–1.75 attack and ×1.1–1.3 speed nearby; scales further with forges owned |
| 4 | **Ring of Fire** | Kills 100–1000 units in range, expanding outward. Grants **no** morale to the caster and **gives the opponent energy** |

Equate Morale is the cleanest anti-snowball tool in the game — it deletes a leader's biggest
multiplier for 200 energy.

### Kenor — defensive artillery specialist **[S]**
| # | Ability | Effect |
|---|---|---|
| 1 | **Tower Defense** | Turns friendly villages/forges into towers (max level 1–4) for 6–8.5 s. Defence +50/60/70/70/60% on villages, +15/40/44/53/60% on forges |
| 2 | **Explosive Shells** | Towers only: +45–55 units of projectile damage radius for 5–7 s |
| 3 | **Close Ranks** | The building's next wave (max 100–200 units) becomes tightly packed and moves 125–150% faster |
| 4 | **Bomb** | Kills 75–95% of units in an enemy building **and** everything within 150–250 units. One of the strongest offensive skills in the game |

### Wilford — builder **[S]**
| # | Ability | Effect |
|---|---|---|
| 1 | **Builder** | Upgrades or converts a building **free and instantly** (own or allied) |
| 2 | **Production Transfer** | Routes production from all your villages into one building, 8–12 s |
| 3 | **Arithmetical Mean** | Equalises unit counts across all of a chosen player's buildings |
| 4 | **Golden Morale** | 5 morale stars for 3–5 s, then reverts to the previous level |

### Odur — universal · medium difficulty **[S]**
| # | Ability | Effect |
|---|---|---|
| 1 | **To arms** | An allied building arms its units with extra weapons |
| 2 | **Shrinking** | Shrinks an enemy building; its units deal less damage and move slower |
| 3 | **BFG9000** | Turns an allied building into a portal — units fly out on cannonballs with extra attack and speed, **immune to skills and untargetable by towers**. Not usable on special buildings |
| 4 | **Fortification** | Turns **all** the player's buildings into level-4 towers temporarily |

### Grokk — universal **[S]**
| # | Ability | Effect |
|---|---|---|
| 1 | **Tactical Maneuver** | Sends 25% of a chosen building's units to your nearest building — **and they become yours** |
| 2 | **Life Cycle** | On an allied level-2+ building: if it hits zero units under attack, it drops one level and 20 units spawn inside instead of falling |
| 3 | **Army of Slugs** | Your units become slugs leaving sticky slowing trails; a dying slug may spawn up to two more |
| 4 | **Dome of Moths** | A growing dome over your building kills enemy troops while units remain inside. Halts production if used on a village |

---

## Proteus

### Marty-O — telekinetic and hypnotist · easy **[S]**
| # | Ability | Effect |
|---|---|---|
| 1 | **Spy** | Reveals an enemy building's unit count for 5–7 s and slows its production 5–25% |
| 2 | **Fog of War** | 15 s of near-blindness in an area; **towers cannot shoot into the fog** and enemy units are slowed 5–30% |
| 3 | **Hypnosis** | Take control of 70–80% of the units in an enemy or neutral building, with a 5 s window to direct them |
| 4 | **Energy Shield** | An area shield killing any enemy unit crossing it, for 7–10 s or until 75–150 kills |

### Cree — spellcaster · medium **[S]**
| # | Ability | Effect |
|---|---|---|
| 1 | **Teleport** | Teleports 50–75% of a building's units to any allied building, in two 2-second stages |
| 2 | **Invisibility** | Friendly units invisible in a radius for 10–15 s |
| 3 | **Reverse** | Sends all enemy troops in an area back where they came from, 1.5–4 s |
| 4 | **Assault Force** | Adds 75–100 troops to a building over 4–7 s |

Defensive by default, but invisibility plus reverse is a building-taking combination **[S]**.

### Zik — lord of time and lightning · medium **[S]**
| # | Ability | Effect |
|---|---|---|
| 1 | **Secret Service** | 350–500 unit radius; marks buildings under incoming attack with an SOS |
| 2 | **Time Loop** | Rewinds all enemy and neutral troops in a 250–320 radius to where they were 2.5–3.5 s ago |
| 3 | **Shortcut** | The building's next wave delivers 25–35 units to the target in 3–4 s |
| 4 | **Chain Reaction** | Instantly destroys 70–100 units of **one colour** anywhere on the map — including your own |

### Pix-O **[S]**
| # | Ability | Effect |
|---|---|---|
| 1 | **UFO** | Shoots enemy units in an area |
| 2 | **Accelerating Pulse** | Instantly speeds up units in an area |
| 3 | **Energy Aura** | Shields each of your units in an area; the shield kills enemies on contact and expires over time or under tower fire |
| 4 | **Gravity** | Enemy units in the area float upward, then fall — killing a portion of them |

### Scar — universal · easy **[S]**
| # | Ability | Effect |
|---|---|---|
| 1 | **Deposit** | Your units stop and go invisible until other friendly units pass; some are cloned on cast |
| 2 | **Barrier** | The first order from an allied building lays a barrier that kills enemy units crossing it |
| 3 | **Hopping Heads** | Allied units in a radius become balls that fly to the nearest enemy/neutral building for **increased damage but no capture** |
| 4 | **Cloning** | Clones units in a radius; the clones head the opposite way |

---

## Shii'Mori

### Stella — "The Invincible Queen" · high difficulty **[S]**
| # | Ability | Effect |
|---|---|---|
| 1 | **Speed Up Production** | +150–200% production in an allied village for 7.5–10 s |
| 2 | **Silence** | **No player may cast any spell** for 3.5–6 s |
| 3 | **Decrease Defense** | Strips 75–100% of a building's defence for 1.5–2.4 s |
| 4 | **Treason** | Converts 30–40 enemy units in an area to your side (random selection above that count) |

Decrease Defense is the counterpart to the whole defence stack in
[MW2-RULES.md](MW2-RULES.md) §4.2 — a 2-second window in which a level-4 tower defends like nothing.

### Trini — magician · medium **[S]**
| # | Ability | Effect |
|---|---|---|
| 1 | **Freeze Building** | 5–8 s: villages stop producing, towers stop shooting, forges stop buffing |
| 2 | **Beehive** | Flies 50–100 troops from a village straight into an enemy building at +30–70% speed, **ignoring terrain** |
| 3 | **Ring of Frost** | Freezes all units in range for 8–12 s, radiating outward over ~1 s; hits allies and neutrals too |
| 4 | **Tornado** | Circles a point for 10–15 s, killing everything it touches and **pulling units out of buildings** |

Freeze Building is the only published ability that shuts off a forge.

### Dora — earth-based tactician · medium **[S]**
| # | Ability | Effect |
|---|---|---|
| 1 | **Accelerating Vortex** | 200–250 radius, 5.5–7.5 s, units move at 150% — allies **and** enemies |
| 2 | **Freeze Morale** | Locks every player's morale at its current level for 10–15 s |
| 3 | **Poultry Yard** | Turns enemy units in a 300–400 radius into chickens: strips their spell and morale effects and cuts attack 20–40% |
| 4 | **Magnet** | Drags every unit within 400–500 of a building into it, for 5–8.5 s |

### Chia **[S]**
| # | Ability | Effect |
|---|---|---|
| 1 | **Totem of Rage** | Allied buildings and units in the area gain an **extra forge effect** |
| 2 | **Rolling Stones** | 20–40 units in the area become rolling stones dealing increased damage but unable to capture |
| 3 | **Sleep** | Slows enemy or neutral units passing through the area |
| 4 | **Sandstorm** | Enemy units get lost in the storm; some become neutral sand men |

### Utii — universal · medium **[S]**
| # | Ability | Effect |
|---|---|---|
| 1 | **Quicksand** | Slows enemy units near a building |
| 2 | **Unity** | The first order to another of your buildings **merges the two garrisons** |
| 3 | **Wind** | The first order from an allied building launches a wind speeding or slowing everything moving that way |
| 4 | **Worm** | Eats units inside an enemy/neutral building and magnetises nearby units into it; everything eaten becomes sand people that attack the building |

### Sato'Shii — universal · easy **[S]**
| # | Ability | Effect |
|---|---|---|
| 1 | **Temporary Downgrade** | Reduces an enemy building to level 1 temporarily |
| 2 | **Wave of Cold** | An allied building's first send generates a freezing wave along its path |
| 3 | **Glaciation** | Freezes the field and **blocks energy accumulation** for all opponents |
| 4 | **Ice Rain** | Kills all units in a zone and partially damages units sheltered inside buildings |

### Ban'shii — universal · medium **[S]**
| # | Ability | Effect |
|---|---|---|
| 1 | **Haunted House** | Summons temporary ghost units into a building; they vanish when it ends |
| 2 | **Possession** | An enemy building becomes yours temporarily — you may **rebuild or upgrade it but not send from it** — then reverts with its current garrison |
| 3 | **Spirited Away** | Redirects incoming damage to your building or an enemy's; cannot capture the redirected building |
| 4 | **Ghost Army** | Summons ghost warriors with your units' stats into any building. **Immune to abilities and to tower fire** |

---

## Grims

### Pahom — development denial · medium **[S]**
| # | Ability | Effect |
|---|---|---|
| 1 | **Downgrade** | Drops an enemy building one level, or cancels an in-progress upgrade |
| 2 | **Ressurection** | Restores 80–100% of the units killed at a building in the last 5 s |
| 3 | **Home** | Sends all units in an area back to their starting points, radiating from the centre |
| 4 | **Panic** | Forces 85–100% of a building's units to flee into other enemy buildings. 1–2 s cast; cannot be used on a player's last building |

### Ankh — Master of Souls · easy to use, hard to master **[S]**
| # | Ability | Effect |
|---|---|---|
| 1 | **Soul Hunter** | +120–150% tower range for 10–15 s, and **every unit shot is reborn as Ankh's unit inside the tower** |
| 2 | **Invincibility** | A building is invincible for as long as it kills **6 of your own units per second** |
| 3 | **Ring of Slow-Mo** | Slows all units in a radius by 2–3×; persists until they enter a building, die to a tower, or are hit by Dora's Poultry Yard |
| 4 | **Plague** | Infects an enemy building: 2–4 units die per second inside and production drops 0–50% |

### Klotz **[S]**
| # | Ability | Effect |
|---|---|---|
| 1 | **Turncoats** | For 8–10 s, an enemy or neutral village's production walks to Klotz's nearest building **as his units** |
| 2 | **Charm** | 8–10 s shield stopping new attacks on a building and sending 40–60 already-inbound units home |
| 3 | **Exchange** | Swaps an enemy building for a random one of yours, clearing all buffs and debuffs on it |
| 4 | **Concomitant Casualties** | Tallies damage dealt to a target building during the skill, then applies that damage to the 3–5 nearest enemy buildings |

### Mouro **[S]**
| # | Ability | Effect |
|---|---|---|
| 1 | **Unholy Attack** | Raises skeletons beside an enemy building and attacks it |
| 2 | **Skill Steal** | Copies an enemy's last-used skill for one use |
| 3 | **Bloody Victim** | While active, **every unit you lose adds morale** |
| 4 | **Cauldron** | Turns an enemy building neutral level 1 and caps it — nothing can be sent to it. Recasting moves the cauldron |

Bloody Victim inverts the morale rule in [MW2-RULES.md](MW2-RULES.md) §5.3 outright.

### Boggi — defence · easy **[S]**
| # | Ability | Effect |
|---|---|---|
| 1 | **Seal** | Kills a percentage of an enemy building's units per second and halts village production |
| 2 | **Mines** | Turns enemy units in a radius into mines that kill other enemy units passing through |
| 3 | **Morale Thief** | Steals up to **1 morale star** from an opponent's building |
| 4 | **Grim Reaping** | Returns the souls of dead units in a radius to an allied building |

### Bek — universal · hard **[S]**
| # | Ability | Effect |
|---|---|---|
| 1–4 | **Random Slot N** | Each slot grants a **random other hero's** ability for that slot |

Bek is the deliberate chaos pick: four slots of randomness, hardest difficulty rating in the roster.

---

## Design patterns worth stealing

Observations for any future MW3 phase that proposes abilities. These are **[D]** — read off the
roster, not stated by any source.

1. **Fixed slot economics.** Cost and cooldown come from the slot, never from the hero. Every hero
   is balanced inside the same four price points, which makes 24 heroes tractable and makes an
   ability's *slot* the primary statement of its power. This is a strong architectural pattern: one
   table, no per-ability tuning of cost.
2. **Upgrading an ability is a real trade-off,** not a straight buff — more power, more energy, more
   cooldown. A maxed ability is not obviously better than a level-1 one.
3. **Almost every ability targets a building or an area, not a unit.** The one clickable object in
   the game is a building, so abilities inherit the same input verb the base game already has. MW3's
   action-menu-on-a-base (phase 3 FR-2) is the same instinct.
4. **Friendly fire is common and deliberate** — Rudo's Boots, Dora's Vortex and Trini's Ring of Frost
   all hit everyone. It makes cheap abilities cost skill instead of energy.
5. **Every global system has a hero who breaks it.** Morale has Ayner (average it), Dora (freeze
   it), Wilford (max it), Mouro (invert it), Boggi (steal it). Energy has Sato'Shii. Towers have
   Marty-O and Ankh. Forges have Chia and Trini. Nothing in the core loop is beyond a counter.
6. **The strongest effects are denial, not damage** — Silence, Freeze Building, Cauldron, Glaciation.
   Damage abilities are the exception and are priced at slot 4.
