# Reference — Mushroom Wars 2

> **What this is.** The goal is a game **as close as possible to Mushroom Wars 2**
> (`docs/ARCHITECTURE.md` §1), shipping as **Bug Wars** with the IP layer reskinned. This folder is
> the researched record of *how MW2 actually works* — every rule and every number we could source —
> and it therefore functions as close to a specification for everything except the IP layer. Ivan
> answers design questions from it instead of asking the user.
>
> **What this is not.** It is not product truth. `docs/<project-slug>/REQUIREMENTS.md` is, and it
> outranks this folder on any disagreement. Where the build does not yet match MW2, that is a **gap**
> in [MW2-PARITY.md](MW2-PARITY.md) §2 to be closed by a future phase — not a divergence to defend,
> and not something to fix in build mode.

Researched 28-07-2026. Reframed the same day, when the user set near-exact MW2 parity as the goal.

## Files

| File | Contents |
|---|---|
| [MW2-RULES.md](MW2-RULES.md) | The simulation: buildings, units, movement, combat, morale, energy, game modes. Every sourced number. |
| [MW2-HEROES.md](MW2-HEROES.md) | All 24 heroes across 4 tribes, their 4 abilities each, costs and cooldowns. |
| [MW2-ITEMS-AND-PROGRESSION.md](MW2-ITEMS-AND-PROGRESSION.md) | Artifacts (items), passive skills, the crafting forge, trophy road. |
| [MW2-PARITY.md](MW2-PARITY.md) | **The one Ivan reads at kickoff.** What already matches MW2, what does not yet (the gap list), and the Bug Wars IP layer. Meant to trend toward empty. |
| [SOURCES.md](SOURCES.md) | Every source, how reliable it is, and how to re-fetch it. |

## Confidence markers

Every non-obvious claim carries one. Do not promote a claim to a higher marker without new evidence.

- **[T]** — **Tabulated.** Read directly off a published data table (usually an image on `mw2.su`).
  Treat as fact for the game version that table describes.
- **[S]** — **Stated.** Written in prose by a source that generally has the numbers right.
- **[D]** — **Derived.** Computed or inferred here from **[T]**/**[S]** values. The derivation is
  always shown so it can be checked.
- **[?]** — **Uncertain.** Sourced but ambiguous, self-contradictory across sources, or a plausible
  reading of an under-labelled table. Never use a **[?]** value as a tuning constant without saying
  so out loud.

## How Ivan uses this

**In discovery and kickoff (interactive).** The default answer to "how should this behave?" is
**"the way MW2 does it"**. Before asking the user a design question, look it up here and in
[MW2-PARITY.md](MW2-PARITY.md); if the reference answers it, **do not ask** — state the rule and its
source and move on. The never-guess rule (`CLAUDE.md`) bans *guessing*; reading a sourced reference
is not guessing.

Ask the user only when:

1. MW2's behaviour is **genuinely unknown** — its AI is undocumented, and
   [MW2-RULES.md](MW2-RULES.md) §10 lists the other published gaps; or
2. the reference value is marked **[?]**; or
3. the question is about the **IP layer** — Bug Wars' insects, geolocated maps, items, or Fame
   ([MW2-PARITY.md](MW2-PARITY.md) §5) — or about the cooperative campaign, which MW2 has no
   equivalent of; or
4. closing a gap would **contradict a shipped `REQUIREMENTS.md`** ([MW2-PARITY.md](MW2-PARITY.md)
   §4). That is the user's call, never a build-mode decision.

Do **not** record a new difference from MW2 as though it were a design choice. A deliberate
divergence needs the user's agreement first.

**In build mode (autonomous).** This folder is read-only context, and a gap in
[MW2-PARITY.md](MW2-PARITY.md) is never a licence to close it mid-build — the issue is the contract.
Never let an MW2 number enter `MW3.Core` because it is written here; a tuning constant enters only
through a `REQUIREMENTS.md` §"Tuning values" table settled at kickoff (D-22). **Parity means same
behaviour and same ratios, not same literals**: MW2's numbers are calibrated for a seconds-based
economy — 0.33 units/second against MW3's 1 unit per 10 ticks — and copying one across without
recalibrating is how a phase ships a non-functional mechanic. Phase 3's FR-4 tower recalibration is
the worked example, recorded in `docs/base-upgrades-and-types/REQUIREMENTS.md` §"Tuning values".

## Keeping it honest

MW2 is a live service and has been patched for a decade; the tables here are dated where the source
dates them. If a number here is ever contradicted by direct observation of the game, correct it here
in the same change, mark it **[?]** if the contradiction is unresolved, and note the date — never
leave two readings standing without saying which one was observed.
