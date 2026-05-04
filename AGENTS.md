# BossHexes

Guidance specific to the `BossHexes` mod.

## Workflow

- Use `C:\Users\maxeo\terraria-mods\deploy.ps1 -Mod BossHexes` when you need to sync this repo into tModLoader `ModSources` for local testing.
- Steam Workshop publishing happens manually in tModLoader. The `deploy.ps1` script is only local source sync, not release.
- Commit and push each logical change in this repo, then update the workspace repo submodule pin in `C:\Users\maxeo\terraria-mods`.
- For this repo workflow, Max explicitly wants commit and push always, and to test all changes at the end rather than between logical changes.

## Architecture

- Prefer the principled source of truth for hit classification: for direct item hits, classify the `Item`; for projectile hits, classify the `Projectile`.
- Prefer the principled source of truth for incoming boss damage too: for direct boss hits, use the actual attacking `NPC`; for projectile hits on the player, use projectile-carried boss-fight provenance captured at spawn time.
- Use `CountsAsClass(...)`, not raw `DamageType == ...`, so derived and custom classes behave correctly.
- Be careful with Terraria authority boundaries: player state changes belong on the player side; world and boss state belong on the server/world side.
- Worm and multi-segment bosses are a known architectural trap here. `npc.boss` usually only applies to the head, so boss-side effects often need segment-aware logic.
- For linked-fight provenance beyond spawn source, attacker provenance, and explicit structural links like `realLife`, do not restore generic `ai[]` guessing. Only add exact vanilla carve-outs one case at a time after a concrete reproduced or decompiled owner/link contract is established.
- There does not currently seem to be a generic tModLoader hook for "run arbitrary vanilla boss AI / attack cadence faster". Generic speed hexes that only adjust `npc.velocity` after vanilla AI are approximations, not true cadence changes.
- There does not currently seem to be a generic public tModLoader source of truth for "this mount is a flying mount". For `WingClip`, treat wing / rocket flight through `Player.wingTime` and `Player.rocketTime`, and treat flying mounts through an explicit blocked-mount set plus `Player.mount.Dismount(Player)` on the owning player.

## Useful Resources

- tModLoader `ModPlayer` hooks reference, especially hit and hurt hooks: https://github.com/tModLoader/tModLoader/blob/stable/patches/tModLoader/Terraria/ModLoader/ModPlayer.cs
- tModLoader `ModPlayer` movement and jump-control hooks, especially `SetControls()` and `CanStartExtraJump(...)`: https://github.com/tModLoader/tModLoader/blob/stable/patches/tModLoader/Terraria/ModLoader/ModPlayer.cs
- tModLoader projectile docs, especially `CountsAsClass(...)` and projectile data storage: https://github.com/tModLoader/tModLoader/wiki/Projectile-Class-Documentation
- tModLoader `Player` extra-jump API docs, especially `StopExtraJumpInProgress()` and `blockExtraJumps`: https://github.com/tModLoader/tModLoader/blob/stable/patches/tModLoader/Terraria/Player.TML.cs
- tModLoader `ExtraJump` docs, especially what counts as starting or canceling an extra jump: https://github.com/tModLoader/tModLoader/blob/stable/patches/tModLoader/Terraria/ModLoader/ExtraJump.cs
- tModLoader ExampleMod max-stat example for principled player stat modification hooks: https://github.com/tModLoader/tModLoader/blob/stable/ExampleMod/Common/Players/ExampleStatIncreasePlayer.cs
- tModLoader AI guide note on passing parent `whoAmI` through projectile `ai[]` when a projectile needs to remember its spawning NPC: https://github.com/tModLoader/tModLoader/wiki/Intermediate-Guide-to-AI
- tModLoader entity source docs for attacker/victim-aware spawn attribution: https://github.com/tModLoader/tModLoader/blob/stable/patches/tModLoader/Terraria/DataStructures/EntitySource_OnHit.cs and https://github.com/tModLoader/tModLoader/blob/stable/patches/tModLoader/Terraria/DataStructures/EntitySource_OnHurt.cs
- tModLoader ExampleMod source-dependent projectile example showing `GlobalProjectile.OnSpawn(IEntitySource source)` with `EntitySource_Parent`: https://github.com/tModLoader/tModLoader/blob/stable/ExampleMod/Common/EntitySources/ExampleSourceDependentTweaks.cs
- tModLoader ExampleMod projectile net-sync example showing per-projectile state via `SendExtraAI` / `ReceiveExtraAI`: https://github.com/tModLoader/tModLoader/blob/stable/ExampleMod/Common/GlobalProjectiles/ExampleProjectileNetSync.cs
- tModLoader `GlobalProjectile` grapple hook reference (`CanUseGrapple`, `GrappleCanLatchOnTo`): https://github.com/tModLoader/tModLoader/blob/stable/patches/tModLoader/Terraria/ModLoader/GlobalProjectile.cs
- tModLoader `GlobalProjectile` draw hooks, especially `PreDrawExtras(...)` and `PreDraw(...)` for hiding boss-owned projectiles under `InvisibleBoss`: https://github.com/tModLoader/tModLoader/blob/stable/patches/tModLoader/Terraria/ModLoader/GlobalProjectile.cs
- tModLoader `ProjectileLoader` grapple dispatch and hook combination semantics: https://github.com/tModLoader/tModLoader/blob/stable/patches/tModLoader/Terraria/ModLoader/ProjectileLoader.cs
- tModLoader ExampleMod grapple example showing the dedicated hook surface: https://github.com/tModLoader/tModLoader/blob/stable/ExampleMod/Content/Items/Tools/ExampleHook.cs
- tModLoader `GlobalNPC` hook reference, especially `PreAI`, `AI`, `PostAI`, `PreDraw(...)`, and `BossHeadSlot(...)`: https://github.com/tModLoader/tModLoader/blob/stable/patches/tModLoader/Terraria/ModLoader/GlobalNPC.cs
- tModLoader `GlobalNPC` UI-related hooks, especially `DrawHealthBar(...)` and `PreHoverInteract(...)` for hiding NPC health/name surfaces under `InvisibleBoss`: https://github.com/tModLoader/tModLoader/blob/stable/patches/tModLoader/Terraria/ModLoader/GlobalNPC.cs
- tModLoader `GlobalNPC` spawn and net-sync hooks, especially `OnSpawn(...)`, `SendExtraAI(...)`, and `ReceiveExtraAI(...)` for carrying boss-linked NPC provenance to clients: https://github.com/tModLoader/tModLoader/blob/stable/patches/tModLoader/Terraria/ModLoader/GlobalNPC.cs
- tModLoader `NPC` class docs, especially `width`, `height`, `scale`, `position`, and `Center` when changing a boss's physical size instead of only its draw scale: https://docs.tmodloader.net/docs/stable/class_n_p_c.html
- tModLoader `NPCLoader` AI call order showing `VanillaAI()` before `GlobalNPC.AI(...)` / `PostAI(...)`: https://github.com/tModLoader/tModLoader/blob/stable/patches/tModLoader/Terraria/ModLoader/NPCLoader.cs
- tModLoader `GlobalBossBar` hook reference, especially `PreDraw(...)` for suppressing the large boss progress bar under `InvisibleBoss`: https://github.com/tModLoader/tModLoader/blob/stable/patches/tModLoader/Terraria/ModLoader/GlobalBossBar.cs
- tModLoader ExampleMod `GlobalBossBar` example showing draw-parameter interception: https://github.com/tModLoader/tModLoader/blob/stable/ExampleMod/Common/GlobalBossBars/ExampleGlobalBossBar.cs
- For true vanilla boss attack-cadence work, expect to read decompiled Terraria NPC AI source; there is not currently a generic tModLoader timing hook for arbitrary vanilla bosses.
- tModLoader `Mount` patch surface, especially `Player.mount.Active`, `Player.mount.Type`, and `Player.mount.Dismount(Player)`: https://github.com/tModLoader/tModLoader/blob/stable/patches/tModLoader/Terraria/Mount.cs.patch
- tModLoader `MountID` patch surface, especially `MountID.Sets.Cart` and the lack of a generic flying-mount classifier: https://github.com/tModLoader/tModLoader/blob/stable/patches/tModLoader/Terraria/ID/MountID.cs.patch
