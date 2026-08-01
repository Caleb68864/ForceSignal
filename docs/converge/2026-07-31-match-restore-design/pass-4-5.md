# Pass 4 — standard (full round trip): CLEAN

Rebuilt the design's round-trip scenario against the running API with ordnance, a carrier,
a fighter group and a fired weapon, then restored it and diffed field by field.

- Ship field diffs across all four hulls: **none** (21 fields each).
- Weapons identical including ammo state.
- Counts match: ordnance 1/1, firing 1/1, movement 4/4, fleets 2/2, participants 2/2,
  log 34 -> 35 (the one appended restore entry).
- Carrier link and ordnance source/target remapped to the restored hulls.
- Fleet colours preserved; seats report per-fleet ship counts; room code minted because the
  source match still held the original.

**Verdict:** CLEAN. clean_streak = 1.

# Pass 5 — standard (normalizer probe)

Hand-edited every field the design says a normalizer guards, then restored.

Correctly normalized: blank match/fleet/ship/participant names, `javascript:` fleet colour,
`../../etc/passwd` icon key, fighter fields on a non-fighter, bogus fighter status, escalated
role (`SuperAdmin` -> `Player`), blank marker type, bogus marker status, course 99, speed 9999.

## Gaps found: 1

8. A weapon mount with a blank name was **silently dropped**, leaving an armed cruiser with no
   weapons. `NormalizeWeapons` filters blank-named mounts, which is correct for an empty form row
   but is silent data loss on a recovery path, and the design's Errors section says "No partial
   restores". Restore now keeps every mount, naming an unnamed one `Unnamed Mount`.

**Verdict:** 1 gap, fixed. clean_streak = 0.
