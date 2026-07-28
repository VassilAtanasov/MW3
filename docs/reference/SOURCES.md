# Sources

> Where every claim in this folder came from, how much to trust it, and how to re-fetch it.
> Researched **28-07-2026**.

## Ranked by usefulness

### 1. `mw2.su` — the only source with real numbers ★★★★★

<https://mw2.su/en/> — a fan-maintained competitive-player site, originally Russian, with an English
translation. **This is the single source that matters.** Everything marked **[T]** in this folder
comes from its data-table images.

| Section | URL |
|---|---|
| Game physics index | `https://mw2.su/en/physics_en/` |
| Buildings and units | `https://mw2.su/en/physics_en/buildings/` |
| — Villages | `https://mw2.su/en/physics_en/buildings/villages/` |
| — Towers | `https://mw2.su/en/physics_en/buildings/towers/` |
| — Forges | `https://mw2.su/en/physics_en/buildings/forges/` |
| — Domination village | `https://mw2.su/en/physics_en/buildings/domination/` |
| — King of the Hill village | `https://mw2.su/en/physics_en/buildings/kingofthehill/` |
| Defence and attack | `https://mw2.su/en/physics_en/defence_and_attack/` |
| Morale | `https://mw2.su/en/physics_en/morale/` |
| Energy and skills | `https://mw2.su/en/physics_en/energy_and_skills/` |
| Passive skills | `https://mw2.su/en/physics_en/passive-skills/` |
| Artifacts | `https://mw2.su/en/physics_en/artifacts/` |
| Map movement | `https://mw2.su/en/physics_en/mapmovement/` |
| Trophy road | `https://mw2.su/en/physics_en/trophy-road/` |
| Hero roster | `https://mw2.su/en/heroes-en/` |
| Each hero | `https://mw2.su/en/heroes-en/<slug>/` — `rudo`, `ayner`, `kenor`, `wilford`, `odur`, `grokk`, `marty-o`, `cree`, `zik`, `pix-o`, `scar`, `stella`, `trini`, `dora`, `chia`, `utii`, `satoshii`, `banshii`, `pahom`, `ankh`, `klotz`, `mouro`, `boggi`, `bek` |
| Tips | `https://mw2.su/en/tips/snake/`, `/multiselect/` |

**Caveats.** English is machine-assisted and occasionally wrong (Trini's tribe reads "Natural"
rather than Shii'Mori). The data tables date from **2018**; heroes added later (Grokk, Scar, Utii,
Sato'Shii, Ban'shii, Boggi, Bek, Odur, Pix-O) are prose-only with no numbers. Some column headers
are under-labelled — see [MW2-RULES.md](MW2-RULES.md) §10.

#### ⚠️ The numbers are inside images, not HTML

`WebFetch` reads the prose and **silently misses every table**, which is why a first pass reports
"no specific numeric tables". Download the images and read them:

```bash
curl -A "Mozilla/5.0" -O https://mw2.su/wp-content/uploads/2018/06/villages_en.png
```

Then open the file with the `Read` tool, which renders images.

| Table | Image URL |
|---|---|
| Village levels | `https://mw2.su/wp-content/uploads/2018/06/villages_en.png` |
| Tower levels | `https://mw2.su/wp-content/uploads/2018/06/Towers_en.png` |
| Forge counts | `https://mw2.su/wp-content/uploads/2018/06/Forges_en.png` |
| Morale effects | `https://mw2.su/wp-content/uploads/2018/06/main_morale_en.png` |
| Morale gains | `https://mw2.su/wp-content/uploads/2018/06/grow_moral_en.png` |
| Morale losses | `https://mw2.su/wp-content/uploads/2018/06/decrease_moral_en.png` |
| Inactivity decay | `https://mw2.su/wp-content/uploads/2018/06/bezde_moral_en.png` |
| Ability slot cost/cooldown | `https://mw2.su/wp-content/uploads/2018/06/Energy_levels_en.png` |
| Energy index `k` | `https://mw2.su/wp-content/uploads/2018/06/Index_k_en.png` |
| Combat formula | `https://mw2.su/wp-content/uploads/2017/03/formula_attack_def.png` |

To find images on any other page:

```bash
curl -s -A "Mozilla/5.0" https://mw2.su/en/physics_en/morale/ | grep -oE 'https://mw2\.su/wp-content/uploads/[^"]+\.png' | sort -u
```

### 2. Mushroom Wars 2 Fandom wiki ★★★☆☆

<https://mushroom-wars-2.fandom.com/> — index at `/wiki/Special:AllPages`.

**Strong on items, empty on rules.** ~120 individually-documented artifacts (name, rarity, slot,
exact effect) and a complete 50-level Artifact Forge cost table. But `Gameplay`, `Artifacts`,
`Buildings`, `Movement` and most mechanics pages are literally the text "Coming soon".

**⚠️ Blocks `WebFetch` with HTTP 402.** Use the browser tools instead:
`mcp__Claude_Browser__preview_start` with the URL, then `get_page_text`.

Use it for: the artifact corpus, the forge economy, tribe flavour. Do not use it for rules.

### 3. Wikipedia ★★★☆☆

<https://en.wikipedia.org/wiki/Mushroom_Wars_2> — release history, platforms, engine, reception,
campaign structure, tribe names. Accurate but shallow; no mechanics beyond one paragraph.

### 4. Steam community guides ★★☆☆☆

<https://steamcommunity.com/app/457730/guides/>. Notably
`https://steamcommunity.com/sharedfiles/filedetails/?id=1374357986` on snaking. Player-written,
occasionally patch-stale, useful for *why* a technique matters rather than what a number is.

### 5. Official and commercial ★☆☆☆☆

- <https://mushroomwars2.com/> — marketing, no mechanics.
- <https://store.steampowered.com/app/457730/> — patch notes are the only way to date a change.
- BlueStacks / Touch-Tap-Play / mobile-gaming-hub guides — SEO beginner content, no numbers worth
  quoting. Cited nowhere in this folder.

## What nobody publishes

Searched for and **not found anywhere**. Treat as unknowable without instrumenting the game:

- **AI decision-making.** Nothing describes how the MW2 AI plays. MW3's AI is original work.
- **Tower damage per shot**, and absolute tower range in map units.
- **Map dimensions**, so no MW2 distance converts to MW3's normalized 0..1 space.
- **Whether attack/defence multipliers stack multiplicatively or additively.**
- **Per-skill passive-skill percentages** and rune costs.
- **How level-5 villages are reached** (the upgrade table has no price for that row).
- Any datamined constants, decompilation, or an official design document. The game is a live
  closed-source Unity title; no community datamine surfaced.

## Reproducing this research

1. `mw2.su` prose via `WebFetch`, page by page from the index tables above.
2. `mw2.su` **numbers** via `curl` + `Read` on the image URLs — this is the step that is easy to
   miss and is where all the value is.
3. Fandom via the browser tools, not `WebFetch` (402).
4. Cross-check anything surprising against Wikipedia and a Steam guide before recording it as **[S]**.

Budget: roughly 40 fetches. The 24 hero pages are the bulk and parallelise cleanly.
