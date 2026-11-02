# Per-player character appearance

Goal: every connected player looks different, and a late joiner sees the right avatar.

The package spawns **one** player prefab for all clients (`NetConfig.playerPrefab`), so the
usual approach is: keep one prefab, add several visual variants as children, and let a
server-owned value select one per player.

---

## Option 1 — one prefab + a synced appearance index (recommended)

Works for colours, models and simple role looks, and it uses blocks you already have.

### 1. Player prefab

Duplicate the player prefab you use for the game (`Player.prefab`, or
`PlayerRigidbody.prefab` for physics movement) and keep the networked blocks on the root.

```
Player (root)                 NetworkObject, SyncedTransform, Health, ..., collider
├── Avatar_Red                capsule / model, only ONE active
├── Avatar_Blue               capsule / model
├── Avatar_Green              capsule / model
├── Avatar_Yellow             capsule / model
└── PlayerCamera              camera + audio listener (never part of a variant)
```

Rules:

- the variants are **visual only**: no colliders on them (the root collider is the physics
  body), no camera, no scripts that need ownership;
- disable every variant except one in the prefab;
- if you want colour-only variants, a single child with `MaterialPropertyBlock` works too.

### 2. Add a `SyncedInt` to the root

`Add Component > SyncedInt`:

- `Label` = `Appearance`
- `Initial Value` = `0`

This is a server-owned state, so late joiners receive the current index automatically.

### 3. Add a small game script (in `Assets/`, not in the package)

```csharp
using MPBlocks.Sync;
using Unity.Netcode;
using UnityEngine;

public sealed class PlayerAppearance : NetworkBehaviour
{
    [SerializeField] private SyncedInt appearance;
    [SerializeField] private GameObject[] variants;

    public override void OnNetworkSpawn()
    {
        if (appearance == null || variants == null || variants.Length == 0) return;

        appearance.OnChanged.AddListener(Apply);
        Apply(appearance.Value);

        // Server decides the look; every client replays it, late joiners included.
        if (IsServer)
        {
            int index = (int)(OwnerClientId % (ulong)variants.Length);
            appearance.SetServerValue(index);
        }
    }

    public override void OnNetworkDespawn()
    {
        if (appearance != null) appearance.OnChanged.RemoveListener(Apply);
    }

    private void Apply(int index)
    {
        for (int i = 0; i < variants.Length; i++)
        {
            if (variants[i] != null) variants[i].SetActive(i == index);
        }
    }
}
```

Wire `Appearance` and the four variant children into the Inspector. Assign the prefab to
`NetConfig > Player Prefab` (or `Rigidbody Player Prefab` when Player Movement = Rigidbody).

### 4. Test

- `./tools/run-local.sh 2` (or a built player twice): each player should be a different
  colour;
- join a third player after the first two are in: the late joiner must see the correct
  colours (this is why the look is a **state**, not an event);
- switch Player Movement to Rigidbody and repeat: the root moves physically, variants do
  not add colliders.

### Variants of the rule

- Fixed roles per slot instead of `OwnerClientId % n`:

  ```csharp
  [SerializeField] private int[] variantBySlot = { 0, 1, 2, 3 };
  // ...
  int slot = Mathf.Clamp((int)OwnerClientId, 0, variantBySlot.Length - 1);
  appearance.SetServerValue(variantBySlot[slot]);
  ```

- Re-randomise at respawn: set `appearance.SetServerValue(Random.Range(0, variants.Length))`
  on the server when the player respawns.
- Health/stats per role: keep those in separate `SyncedInt`/`SyncedFloat` values, written by
  the same server code, instead of creating a prefab per role.

---

## Option 2 — a different player prefab per slot (advanced)

Needed only when players differ in **components/abilities**, not just looks.

Netcode for GameObjects can spawn a different prefab for each client:
`ConnectionApprovalResponse.PlayerPrefabHash` selects the prefab by its
`NetworkPrefab.SourcePrefabGlobalObjectIdHash` (public API), and every variant must be
registered in `NetworkManager.NetworkConfig.Prefabs`.

The current `NetBootstrap` does not expose this: it always uses
`NetConfig.PlayerPrefab`. Supporting it means adding, for example,
`NetConfig.playerPrefabVariants[]` and, inside the approval callback in `NetBootstrap`,
registering each variant and setting `response.PlayerPrefabHash` by client slot. Do this
only if role-specific components are really required; otherwise option 1 is simpler and
keeps one prefab for the whole class.

---

## Common pitfalls

- **Do not add colliders to the variant children.** The root `CapsuleCollider` is the
  physics shape; extra colliders cause double hits and jitter.
- **Do not put the camera inside a variant**, or one player loses their view.
- **Do not edit the shared `Assets/Prefabs/Player.prefab`** for a game-specific look —
  duplicate it first; the demo and templates use the shared one.
- **Late joiners** only see the right look if it is server-owned state (`SyncedInt` /
  `SyncedBool` / `NetworkVariable`), not a one-off `NetEvent`.
- The owner id is stable while connected: host = 0, clients 1.. N. If players reconnect,
  the id can change, so pick the look on spawn/respawn, not by storing a local value.
