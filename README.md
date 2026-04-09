# Boss Hexes

A tModLoader mod that rolls random hex modifiers for boss fights. Each time you summon a boss, a set of hexes is drawn that change the rules for that fight — until you beat it.

## How It Works

When a boss spawns, one hex is randomly rolled from each enabled category:

**Flashy Hexes** — dramatic fight-changers:
- **Invisible Boss** — the boss sprite is hidden entirely
- **Wing Clip** — no flight allowed
- **Blackout** — extreme darkness
- **Unstable Gravity** — gravity flips periodically for all players
- **Meteor Shower** — falling stars rain down and damage players
- **Tiny Fast Boss** — 1/3 size, 2× speed
- **Huge Boss** — 3× size, 1.75× speed

**Modifier Hexes** — stat adjustments:
- **Swift Boss** — boss moves and attacks 25% faster
- **Sluggish** — player movement −25%
- **Frail** — max HP −20%
- **Broken Armor** — defense halved
- **Glass Cannon** — +50% damage dealt and taken

**Constraint Hexes** — restrictions on what you can do:
- **No Ranged Damage** — ranged weapons deal 0
- **No Melee Damage** — melee weapons deal 0
- **No Magic Damage** — magic weapons deal 0
- **Grounded** — no jumping
- **No Grapple** — hooks disabled

## Persistence

Hexes persist until the boss is defeated. If you die and re-summon the same boss type, you face the same hex roll. Beat the boss and the hexes are cleared — next time you fight it, new hexes are drawn.

Hex state is saved in the world file across sessions.

## Configuration

All settings are server-side, configured through tModLoader's mod config menu:

- **Enable/disable Boss Hexes entirely**
- **Toggle each hex category independently** — flashy, modifier, and constraint can each be turned on or off

## Installation

Subscribe via tModLoader's in-game mod browser, or build from source with `tModLoader`.

## Pairs Well With

- [Panic at Dawn](https://github.com/maxeonyx/PanicAtDawn) — survival horror co-op: sanity, no recall, die at dawn
- [Death Bag](https://github.com/maxeonyx/DeathBag) — persistent death bags for Mediumcore

## Compatibility

Requires [tModLoader](https://store.steampowered.com/app/1281930/tModLoader/). Works in singleplayer and multiplayer. All config is server-side — the host controls the rules.
