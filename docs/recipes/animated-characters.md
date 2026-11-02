# Animated (rigged) characters

Rigged characters work with character selection: the character model is a normal
**visual prefab** instantiated under the player by `PlayerAppearance`, so
`SkinnedMeshRenderer`, bones and `Animator` are all preserved. What needs extra care is the
**animation state replication**.

The package provides two blocks for that (no NGO `NetworkAnimator` needed):

| Component | Where | Role |
|---|---|---|
| `PlayerAnimator` | player prefab (root) | owner drives the Animator from movement speed / grounded |
| `SyncedAnimator` | player prefab (root) | replicates the selected Animator parameters to every machine |

`PlayerAppearance` is already on both generated player prefabs and instantiates the chosen
character visual.

## 1. Character visual prefab

Each roster entry's `Visual Prefab` should be:

- a rigged model with `SkinnedMeshRenderer` + `Animator` + Animator Controller;
- **no** `Collider`, **no** `Camera`, **no** `NetworkObject`/`NetworkBehaviour`;
- **Apply Root Motion = off** (the networked player root owns position);
- feet at local `(0,0,0)`; the player root keeps its own collider;
- optional: a `Visual Root` empty on the player for offsets, assigned to
  `PlayerAppearance.Visual Root`.

## 2. Animator Controller parameters

Create these parameters in the Animator Controller (names can differ, configure them):

```
Speed      (Float)    0 = idle, 1 = run (or metres/second)
Grounded   (Bool)     true on the floor
Attack     (Trigger)  optional, fired explicitly
```

A simple blend tree `Idle <-> Walk/Run` on `Speed` is enough; `Grounded` can drive a
jump/fall state.

## 3. Components on the player prefab

Add to the player root (next to `NetworkObject`, `SyncedTransform`, `PlayerAppearance`):

**`PlayerAnimator`**

- `Animator` — leave empty; it finds the Animator in the spawned visual and rebinds when the
  chosen character changes;
- `Speed Parameter` = `Speed`, `Grounded Parameter` = `Grounded` (leave empty to skip);
- `Ground Mask` / `Ground Check Distance` for the grounded raycast.

**`SyncedAnimator`**

- `Animator` — leave empty; `PlayerAnimator` calls `RebindAnimator()` when the visual spawns;
- `Authority` = **Owner** (the player drives their own animation);
- `Float Parameters` = `Speed`;
- `Bool Parameters` = `Grounded`;
- `Int Parameters` / `Trigger Parameters` = optional (`Attack`, `WeaponType`, …);
- `Full Sync Interval` = `0.5` (late-join/packet-loss recovery).

That is the whole setup. Press Play: the owner's Animator is polled, changes are sent to the
server and broadcast; remote machines apply them; a late joiner receives the next full
snapshot within half a second.

## 4. One-off actions (attack, cast)

Triggers cannot be polled, so call the block instead of the Animator:

```csharp
[SerializeField] private SyncedAnimator syncedAnimator;

void Attack()
{
    syncedAnimator.SetTrigger("Attack");   // fires on every machine
}
```

For a persistent state (aiming, crouching) use a bool parameter; for gameplay effects
(damage, score) use `SyncedBool`/`Health`, not the Animator.

## 5. NPCs / server-driven characters

Set `SyncedAnimator.Authority = Server`. The server animates the NPC (AI script), and the
block pushes the parameter changes to clients. Keep `PlayerAnimator` off server-driven NPCs
unless you also want movement-driven animation.

## 6. What not to do

- Do not add NGO `NetworkAnimator` to the visual prefab: it requires an `Animator` on the
  same GameObject as a `NetworkObject`, and the visual child is created at runtime.
- Do not let non-owners write the Animator; their values would fight the replicated ones.
  `PlayerAnimator` already writes only on the owner.
- Do not put colliders on the visual model; the root capsule/collider is the physics shape.
- Triggers are events: a late joiner will not replay an attack that happened before they
  joined (which is correct).

## 7. Optional: one Animator per character slot

Students usually make one visual prefab per player slot (one per character). Each prefab can
have its own rig, Animator Controller and animation set; `PlayerAppearance` instantiates the
right one and `PlayerAnimator`/`SyncedAnimator` rebind automatically. If all characters share
one rig, different materials/colour variants are enough.

See also: [character-select.md](character-select.md) for the roster/select flow and
[rigidbody-sync.md](rigidbody-sync.md) for physics-based movement.
