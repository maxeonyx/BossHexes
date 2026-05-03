# Boss Hexes — Plan

## Design Decisions

- **Always 3 hexes per fight** — one flashy, one modifier, one constraint. Player count doesn't affect hex count. Individual categories can be disabled via config.

## Known Issues

### Hex architecture audit

- Damage-denial constraints now belong in boss-side hit hooks using the actual hitter as the source of truth: `ModifyHitByItem` + `item.CountsAsClass(...)` for direct hits, `ModifyHitByProjectile` + `projectile.CountsAsClass(...)` for projectile hits. Do not infer class from `Player.HeldItem` or raw `DamageType` equality.
- Worm / multi-segment boss coverage now uses a principled root-aware fight check: resolve the boss root via `npc.boss` or `npc.realLife`, then compare the root's type to `BossHexManager.CurrentBossType`. Boss-side visuals, scaling, speed changes, GlassCannon bonus, and damage-denial constraints now apply to matching worm segments too.
- Cross-cutting issue: `BossHexManager.Current` / `CurrentBossType` assume one active boss fight, which is shaky for multi-boss encounters and overlap/despawn edge cases.

#### Rollable flashy hexes

- `InvisibleBoss` — partial. Correct draw hook, but only hides the boss sprite; projectiles, dust, minimap presence, and worm segments still leak the boss.
- `WingClip` — shaky. Zeroing `wingTime` / `rocketTime` hits the real flight resource, but it is still a per-tick suppression rather than a complete no-air-mobility rule.
- `Blackout` — shaky. Applying vanilla `Blackout` is principled, but it still needs real gameplay verification for whether it creates the intended darkness effect.
- `TinyFastBoss` / `HugeBoss` — shaky. Size changes are real, but the "fast" part is only velocity scaling, not attack cadence / AI timing, and worm segments are unaffected.
- `UnstableGravity` — good. Server/world schedules flips and tells each client to mutate its own gravity, matching Terraria's authority split.
- `MeteorShower` — shaky. Server-side spawning is right, but boss-damage reduction currently identifies hex meteors by `ProjectileID.FallingStar`, which is too broad a source of truth.

#### Rollable modifier hexes

- `SwiftBoss` — shaky. Same velocity-only problem as the flashy speed hexes; this is not a true 25% faster boss AI / attack rate change.
- `Sluggish` — shaky. Uses vanilla `Slow`, which is principled, but the exact player-facing effect is only approximately "movement -25%".
- `Frail` — shaky. Numeric intent is right, but rewriting `statLifeMax2` in `PostUpdate` is a fragile hook.
- `BrokenArmor` — good. Reapplying vanilla `BrokenArmor` is principled and matches the intended effect well.
- `GlassCannon` — broken. Only the boss-takes-more-damage half exists; the player-takes-more-damage half is still missing.

#### Rollable constraint hexes

- `NoRangedDamage` / `NoMeleeDamage` / `NoMagicDamage` — now principled in hook choice and hit classification, but still incomplete for worm / multi-segment bosses because those hits often land on NPCs without `boss=true`.
- `Grounded` — shaky. It cancels obvious upward jump velocity, but it is still a heuristic rather than a true "cannot jump" implementation.
- `NoGrapple` — shaky. Blocking `CanUseItem` is the right general area, but identifying hooks by `aiStyle == 7` is heuristic and only blocks new grapples.

### Eater of Worlds hexes not appearing (reported, cause uncertain)

**Symptom:** EoW spawned from breaking a shadow orb, no hex announcement appeared.

**Investigation findings:**
- `OnSpawn` in `BossHexGlobalNPC` gates on `npc.boss`. EoW head (NPCID 13) has `boss=true`, so hexes should roll.
- The announcement logic looks correct — runs on server, syncs to clients via `SyncHexes`, broadcasts chat message.
- Could not identify a definitive root cause.

**Possible causes to investigate:**
1. **Config issue** — `EnableBossHexes` was false, or individual hex categories disabled.
2. **Persistence edge case** — if EoW type was already in `_persistedHexes` with all `None` hexes, the early return at line 262 (`if (CurrentBossType == bossType && Current.HasAnyHex)`) wouldn't trigger, but `_persistedHexes.TryGetValue` would return a hex set with no active hexes. This can happen if all three category configs are disabled when a boss first spawns, then re-enabled later — the empty roll is persisted.
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

**Current fix:** `BossHexManager.TryGetBossRoot(...)` resolves the boss root via `npc.boss` or `npc.realLife`, and `IsPartOfCurrentBossFight(...)` compares that root type to `CurrentBossType`. `BossHexGlobalNPC` now uses that helper for visuals, scaling, speed changes, GlassCannon bonus, and damage-denial constraints.

**Remaining limitation:** `CurrentBossType` still models only one boss head type at a time, so true multi-boss encounters and overlap/despawn edge cases are still architecturally shaky.

Already noted in `BossHexManager.cs` TODO block (line 37-38 area).

## Future Work

See `BossHexManager.cs` top-of-file TODO block for full list of unimplemented and partially implemented hexes.
