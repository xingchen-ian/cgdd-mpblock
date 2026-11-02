# Build the arena from scratch

Four players, one arena: shoot, lose 100 HP in four hits, respawn after 3 seconds, first to
score wins. Every object is placed by hand — no kit.

This is the same loop as `Template3_ArenaCombat`, built step by step.

---

## 0. The loop

```
LMB → PlayerShooter spawns a Projectile (server-authoritative)
    → Projectile hits a Health, calls ApplyDamage(25, shooterClientId) on the server
    → Health reaches 0 → Died → KillScore awards slot[shooter] +1 on the Scoreboard
    → PlayerRespawn waits 3 s, then ResetHealth + teleport to a spawn point
```

Only the server changes health and scores. Clients see the result through `NetworkVariable`s.

---

## 1. Scene skeleton

`MPBlocks > New Game Scene...`

| Field | Value |
|---|---|
| Name | `Arena` |
| Max Players | `4` |

That writes the scene plus its own `NetConfig_Arena.asset` and the infrastructure
(`NetworkManager`, `NetBootstrap`, session panel). Do **not** reuse another game's config: the
`Game Id` must match or clients are rejected.

## 2. The floor and cover

| Object | Transform |
|---|---|
| `Ground` | Cube, scale `(6, 1, 6)`, position `(0, -0.5, 0)` |
| `Cover_North` | Cube, scale `(2, 1.5, 0.5)`, position `(0, 0.75, 6)` |
| `Cover_South` | same, position `(0, 0.75, -6)` |
| `Cover_East` | scale `(0.5, 1.5, 2)`, position `(6, 0.75, 0)` |
| `Cover_West` | same, position `(-6, 0.75, 0)` |

Add one `Directional Light` (any rotation you like).

## 3. Spawn points

Four empty objects `Spawn_A` … `Spawn_D` at `(±4, 0, ±4)`, each with a `PlayerSpawnPoint`
component. The server sorts them by name and hands them out round-robin — this is what makes
players start apart instead of inside each other.

## 4. The projectile prefab

`GameObject > 3D Object > Sphere`, then:

| Step | Value |
|---|---|
| Scale | `(0.2, 0.2, 0.2)` |
| Remove | the `Sphere Collider` (hits are distance-based, not physics) |
| Add | `NetworkObject` |
| Add | `SyncedTransform` → **Authority = Server** (the server owns the bullet) |
| Add | `Projectile` |

Drag it into `Assets/Prefabs/Projectile.prefab`, then delete it from the scene.

**Register it:** select `Assets/Settings/NetConfig_Arena.asset` → `Spawnable Prefabs` →
add `Projectile.prefab`. A prefab the server never registered cannot be spawned.

## 5. The player prefab

Duplicate `Assets/Prefabs/Player.prefab` → `Assets/Prefabs/PlayerCombat.prefab`, then add:

| Component | Field | Value |
|---|---|---|
| `Health` | Max Health | `100` |
| `HealthHUD` | — | shows HP top-centre |
| `PlayerShooter` | Projectile Prefab | `Projectile.prefab` |
| | Fire Interval | `0.35` |
| | Damage | `25` |
| `PlayerRespawn` | Health | the `Health` above |
| | Respawn Seconds | `3` |
| | Visual | the `Body` child |
| `KillScore` | Health | the `Health` above |
| | Points Per Kill | `1` |

Then set `NetConfig_Arena > Player Prefab` to `PlayerCombat.prefab`.

Keep the existing `SyncedTransform`, `PlayerAppearance`, `PlayerInteractor`, `PlayerNameplate`
from the copied prefab. `PlayerShooter` uses the player camera that is already there
(`WASD` move, `RMB` look, `LMB` fire).

## 6. The scoreboard

`NetConfig_Arena > Scoreboard Prefab` → `Assets/Prefabs/Scoreboard.prefab`.

The toolkit's default scoreboard has 2 slots; for 4 players you want 4. Either duplicate the
prefab and set `Slot Count = 4`, or use the 4-slot prefab that `Generate Teaching Templates`
creates (`Assets/Prefabs/Scoreboard_4P.prefab`). Slot index = client id, so host = 0.

## 7. Build settings

`File > Build Settings > Add Open Scenes`.

## 8. Test

Play → Host, then join with a second player.

| What to check | Expected |
|---|---|
| HP bar top-centre | `100 / 100`, drops by 25 per hit |
| `HEALTH_CHANGED label=player value=75 damage=25` | in the Console |
| four hits | the victim disappears for 3 s, then returns at a spawn point |
| killer's score | +1 in the top-right scoreboard |
| `KILL_SCORE killer=1 points=1` | the credit line |
| a late joiner | sees the current scores, not zeros |

---

## 9. Tune it

| Want | Change |
|---|---|
| one-shot kill | `PlayerShooter.damage = 100` |
| faster fights | `Fire Interval 0.15`, `Damage 20` |
| short respawn | `PlayerRespawn.respawnSeconds = 1` |
| team deathmatch | add `Team` + `TeamManager`, award points to the killer's team slot |
| AI targets | put `Health` + `KillScore` on any prefab and shoot it |

## 10. If it does not work

| Symptom | Cause |
|---|---|
| LMB does nothing | `PlayerShooter.projectilePrefab` empty, or the prefab is not in `Spawnable Prefabs` |
| bullets never appear for other players | the projectile has no `SyncedTransform`, or it is not server-authority |
| hits do nothing | `ProjectileScript.hitRadius` too small, or no `Health` on the target |
| HP drops on one screen only | `Health` is not on the **networked** player object |
| no score | `KillScore.health` not wired, or no `Scoreboard Prefab` on the config |
| killer gets the point but slot is wrong | scoreboard slot = client id; check `Slot Count` covers 0..3 |
| everyone spawns in the middle | no `PlayerSpawnPoint` in the scene |
| players cannot see each other move | `SyncedTransform` missing on the player prefab |
