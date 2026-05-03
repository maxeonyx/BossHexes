# Boss Hexes — Plan

## Design Decisions

- **Always 3 hexes per fight** — one flashy, one modifier, one constraint. Player count doesn't affect hex count. Individual categories can be disabled via config.

## Known Issues

### Hex architecture audit

- Damage-denial constraints now belong in boss-side hit hooks using the actual hitter as the source of truth: `ModifyHitByItem` + `item.CountsAsClass(...)` for direct hits, `ModifyHitByProjectile` + `projectile.CountsAsClass(...)` for projectile hits. Do not infer class from `Player.HeldItem` or raw `DamageType` equality.
- Worm / multi-segment boss coverage now uses a principled root-aware fight check: resolve the boss root via `npc.boss` or `npc.realLife`, then match that root type against the active boss-fight registry. Boss-side visuals, scaling, speed changes, GlassCannon bonus, and damage-denial constraints now apply to matching worm segments too.
- Active fight tracking is now keyed by boss root type instead of a singleton `Current` / `CurrentBossType`. Boss-side matching, player-side fight-active checks, projectile provenance, and MeteorShower state now all route through that per-boss active-fight model, which is a better fit for overlap/despawn edge cases and multi-boss encounters.

#### Rollable flashy hexes

- `InvisibleBoss` — partial. The boss sprite, boss-owned projectiles, boss-linked spawned NPCs, boss head / minimap icon, and boss UI bars / hover name surfaces are now hidden using the real draw hooks plus explicit boss-fight provenance, and worm segments are covered by the root-aware fight check. Linked spawned NPCs now also get a conservative fallback from explicit parent references on the spawned NPC itself (`realLife` / validated NPC `ai[]` references) when the spawn source is incomplete. Dust and entities with no clean or explicit parent provenance can still leak the boss.
- `WingClip` — implemented. It now blocks sustained flight without collapsing into `Grounded`: wing and rocket resources are zeroed on the owning player, and an explicit set of flying / hovering mounts is cleanly dismounted. Normal jumps and extra jumps are still allowed.
- `Blackout` — reviewed. Applying vanilla `Blackout` is already the principled implementation, so there is no custom darkness logic for now. It still needs real gameplay verification for whether vanilla `Blackout` creates the intended darkness effect.
- `Reversal` — implemented. Control inversion belongs at the player-input layer, so it now uses `ModPlayer.SetControls()` and swaps left/right input on the owning player only. Up/down, jump, and mount semantics are intentionally left alone for now because `BossHexManager.cs` only suggested them as a possibility, not a settled requirement.
- `TinyFastBoss` / `HugeBoss` — implemented with physical size changes. Bosses now resize through both `npc.scale` and hitbox dimensions while preserving center position, so the visible body and actual contact / hittable area stay aligned. The speed effect is still intentionally post-`VanillaAI` velocity nudging rather than claimed attack cadence / AI timing. Still needs gameplay testing and tuning across bosses. Worm coverage is now handled by the root-aware current-fight check.
- `UnstableGravity` — good. Server/world schedules flips and tells each client to mutate its own gravity, matching Terraria's authority split.
- `MeteorShower` — implemented. Server-side spawning stays authoritative, reduced boss damage uses per-projectile MeteorShower provenance captured at spawn time instead of guessing from `ProjectileID.FallingStar`, and fight state is now tracked per boss type instead of through a singleton current-fight assumption.

#### Rollable modifier hexes

- `SwiftBoss` — movement-only for now. Uses the same post-`VanillaAI` velocity-only approach as the flashy speed hexes and no longer claims a generic boss attack-rate change. Still needs gameplay testing and tuning. There does not currently seem to be a generic tModLoader hook for "run arbitrary vanilla boss AI faster," so true cadence work would require an explicit supported-boss / boss-family audit.
- `Sluggish` — reviewed. Uses vanilla `Slow`, which is already the principled implementation. The remaining question is gameplay feel: the exact player-facing effect is only approximately "movement -25%" and still needs real testing.
- `Frail` — implemented. The max-life reduction now lives in `ModPlayer.ModifyMaxStats(...)`, which is the principled player-stat hook instead of rewriting `statLifeMax2` in `PostUpdate`.
- `BrokenArmor` — good. Reapplying vanilla `BrokenArmor` is principled and matches the intended effect well.
- `GlassCannon` — implemented. Boss damage taken is handled in boss-side hit hooks; player damage taken is handled in player-side hit hooks keyed to the actual attacking boss NPC or projectile, so overlapping fights do not leak the damage-taken bonus across bosses. Hostile projectiles are attributed to the boss fight via explicit projectile source tracking at spawn time rather than projectile-type guesses.
- `Marked` — implemented. Boss outgoing damage now uses the actual attacking NPC or projectile as the source of truth, so the +25% player-damage bonus only applies to attacks from the boss fight that actually rolled `Marked`.
- `ManaDrain` — implemented. Mana cost changes belong on the player side, so it now uses `ModPlayer.ModifyManaCost(...)` as a fight-wide debuff keyed to active modifier state. It applies once when any active boss fight has `ManaDrain`, rather than trying to guess which boss a spell was "for".
- `ExtraPotionSickness` — implemented. Potion delay belongs on the player side, so it now uses the actual `BuffID.PotionSickness` buff as the source of truth in `ModPlayer.PostUpdateBuffs()`. Newly applied or refreshed potion sickness is tripled once while the modifier is active, without retroactively stretching an already-running timer when the fight starts.
- `SlowAttack` — implemented. Attack speed belongs on the player side, so it now uses `ModPlayer.UseSpeedMultiplier(Item)` and treats the actual item use speed as the source of truth instead of approximating with the vanilla `Slow` movement debuff. It applies to attack items as a fight-wide debuff keyed to active modifier state.
- `Inaccurate` — implemented. Projectile spread belongs at shot creation time, so it now uses `ModPlayer.ModifyShootStats(Item, ...)` and treats the actual ranged attack item plus outgoing shot velocity as the source of truth. It applies once per fired ranged shot as a fight-wide debuff keyed to active modifier state.

#### Rollable constraint hexes

- `NoRangedDamage` / `NoMeleeDamage` / `NoMagicDamage` — implemented. They now use boss-side hit hooks with principled item / projectile classification, and worm / multi-segment coverage follows the root-aware fight check instead of raw `npc.boss`.
- `NoBuffPotions` — implemented. Buff-potion denial belongs on the player side, so it now uses `ModPlayer.CanUseItem(Item item)` and classifies the actual item being used via `item.buffType > 0`, while exempting life and mana restores. It behaves as a fight-wide constraint keyed to active constraint state instead of trying to infer from applied buffs or item names.
- `Grounded` — implemented. Jump input is now blocked in `ModPlayer.SetControls()`, extra jumps are denied in `ModPlayer.CanStartExtraJump(...)`, and ongoing jump state is canceled via `Player.jump = 0` plus `Player.StopExtraJumpInProgress()` instead of zeroing upward velocity.
- `NoGrapple` — implemented. New grapple attempts are now blocked in projectile grapple hooks (`CanUseGrapple`), in-flight hooks are prevented from latching via `GrappleCanLatchOnTo`, and already-active hooks are cleared from player state while the hex is active. Grapple classification now uses Terraria's `Main.projHook` source of truth instead of inferring from `aiStyle == 7`.

### Empty persisted hex rolls

- Empty all-`None` rolls are no longer persisted or reloaded. If hex categories are disabled when a boss first spawns, the mod now leaves that boss without persisted hex state instead of saving an empty roll that could later be reused after categories are re-enabled.

### Eater of Worlds hexes not appearing (reported, cause uncertain)

**Symptom:** EoW spawned from breaking a shadow orb, no hex announcement appeared.

**Investigation findings:**
- `OnSpawn` in `BossHexGlobalNPC` gates on `npc.boss`. EoW head (NPCID 13) has `boss=true`, so hexes should roll.
- The announcement logic looks correct — runs on server, syncs to clients via `SyncHexes`, broadcasts chat message.
- Could not identify a definitive root cause.

**Possible causes to investigate:**
1. **Config issue** — `EnableBossHexes` was false, or individual hex categories disabled.
2. **Persistence edge case** — previously, if EoW type was already in `_persistedHexes` with all `None` hexes, the spawn path would reuse that empty persisted roll. This happened if all three category configs were disabled when a boss first spawned, then re-enabled later. That empty-roll persistence path is now fixed, but it may not have been the cause of the reported incident.
3. **Multiplayer sync issue** — `SyncHexes` packet didn't reach the client, so hexes were rolled on server but announcement never appeared client-side.
4. **NPC spawn order** — body/tail segments spawn first (they don't have `boss=true`), then head spawns. If something about the spawn source or timing prevents `OnSpawn` from firing on the head, hexes wouldn't roll.

**Next steps:** Add logging to `OnBossSpawn` and `OnSpawn` to trace the flow. Try to reproduce by breaking another shadow orb.

### Worm boss segments not affected by hex effects

**Affected bosses:** Eater of Worlds (NPCID 13/14/15), Destroyer (134/135/136)

**Previous problem:** Boss-side hex hooks used to gate on `npc.boss`. Only the head segment has `boss=true`. Body and tail segments used to miss:
- InvisibleBoss (body/tail still visible)
- TinyFastBoss / HugeBoss (body/tail normal size)
- SwiftBoss (body/tail normal speed)
- GlassCannon damage bonus (hits on body/tail don't get +50%)

**Current fix:** `BossHexManager.TryGetBossRoot(...)` resolves the boss root via `npc.boss` or `npc.realLife`, and active-fight lookup is now keyed by that root type. `BossHexGlobalNPC` now uses that helper for visuals, scaling, speed changes, GlassCannon bonus, and damage-denial constraints.

**Follow-up to test:** multi-boss encounters and despawn transitions should now behave more cleanly because active hex state, projectile provenance, and MeteorShower state are tracked per boss type rather than through one singleton current-fight model.

Already noted in `BossHexManager.cs` TODO block (line 37-38 area).

## Future Work

See `BossHexManager.cs` top-of-file TODO block for full list of unimplemented and partially implemented hexes.
