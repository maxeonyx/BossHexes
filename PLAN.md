# Boss Hexes — Plan

## Known Issues

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

**Problem:** All boss-side hex hooks (`AI`, `PostAI`, `PreDraw`, `ModifyHitByProjectile`, `ModifyHitByItem`, `OnKill`) gate on `npc.boss`. Only the head segment has `boss=true`. Body and tail segments are not affected by:
- InvisibleBoss (body/tail still visible)
- TinyFastBoss / HugeBoss (body/tail normal size)
- SwiftBoss (body/tail normal speed)
- GlassCannon damage bonus (hits on body/tail don't get +50%)

**Fix approach:** For worm bosses, check if the NPC is a known worm segment type (or follow the `ai[1]`/`ai[3]` chain to find the head) and apply effects to all segments. Could maintain a `HashSet<int>` of worm segment NPC types, or use a more generic "is related to current boss fight" check.

Already noted in `BossHexManager.cs` TODO block (line 37-38 area).

## Future Work

See `BossHexManager.cs` top-of-file TODO block for full list of unimplemented and partially implemented hexes.
