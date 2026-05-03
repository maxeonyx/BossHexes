# BossHexes

Guidance specific to the `BossHexes` mod.

## Workflow

- Use `C:\Users\maxeo\terraria-mods\deploy.ps1 -Mod BossHexes` when you need to sync this repo into tModLoader `ModSources` for local testing.
- Steam Workshop publishing happens manually in tModLoader. The `deploy.ps1` script is only local source sync, not release.
- Commit and push each logical change in this repo, then update the workspace repo submodule pin in `C:\Users\maxeo\terraria-mods`.

## Architecture

- Prefer the principled source of truth for hit classification: for direct item hits, classify the `Item`; for projectile hits, classify the `Projectile`.
- Use `CountsAsClass(...)`, not raw `DamageType == ...`, so derived and custom classes behave correctly.
- Be careful with Terraria authority boundaries: player state changes belong on the player side; world and boss state belong on the server/world side.
- Worm and multi-segment bosses are a known architectural trap here. `npc.boss` usually only applies to the head, so boss-side effects often need segment-aware logic.

## Useful Resources

- tModLoader `ModPlayer` hooks reference, especially hit and hurt hooks: https://github.com/tModLoader/tModLoader/blob/stable/patches/tModLoader/Terraria/ModLoader/ModPlayer.cs
- tModLoader projectile docs, especially `CountsAsClass(...)` and projectile data storage: https://github.com/tModLoader/tModLoader/wiki/Projectile-Class-Documentation
- tModLoader ExampleMod max-stat example for principled player stat modification hooks: https://github.com/tModLoader/tModLoader/blob/stable/ExampleMod/Common/Players/ExampleStatIncreasePlayer.cs
- tModLoader AI guide note on passing parent `whoAmI` through projectile `ai[]` when a projectile needs to remember its spawning NPC: https://github.com/tModLoader/tModLoader/wiki/Intermediate-Guide-to-AI
