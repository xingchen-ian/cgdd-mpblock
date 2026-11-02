# Inspector field help (hover tooltips)

## Where the tooltips come from

Rest the mouse on a field **name** in the Inspector for about a second and a tooltip
pops up. That text is the `[Tooltip]` attribute on the serialized field:

```csharp
[Header("MPBlocks")]
[Tooltip("Shown in the inspector and in [MP] logs. Not part of the network identity.")]
[SerializeField] private string label = "bool";
```

`[Tooltip]` works on any **serialized** field (anything visible in the Inspector),
regardless of type or whether the field is private. `[Header]` is a section title, not a
tooltip.

## What this project provides

- Every `[SerializeField]` in `packages/com.cgdd.mpblocks/Runtime/` has an English
  `[Tooltip]`:
  - Core: `NetConfig`, `NetBootstrap`, `NetSessionUI`, `PlayerSpawnPoint`
  - Sync: `SyncedTransform`, `SyncedBool`, `SyncedInt`, `SyncedFloat`, `NetEvent`
  - Gameplay: `Interactable`, `InteractToggle`, `CoopDoor`, `Health`, `DamageOnInteract`,
    `Scoreboard`, `Spawner`, `PlayerInteractor`, `SimplePlayerMover`, `PlayerShooter`,
    `Projectile`, `PlayerRespawn`, `HealthHUD`, `ScoreboardHUD`
  - Diagnostics: `LatencySimulator`, `NetDiag`, `StateHash`
- Every input in the `MPBlocks > New Game Scene...` window also has a hover tooltip.
- When adding a field, add a tooltip too: 1–3 sentences answering *what it is / how to
  fill it / what to watch out for*.

## Third-party components (cannot be re-worded here)

`NetworkObject`, `NetworkManager`, `UnityTransport`, `Text`/`Button` etc. come from
Unity/Netcode packages; their `[Tooltip]` text is owned by those packages and cannot be
changed by this project. Quick reference for the `NetworkObject` fields you touch most:

| Field | Meaning | For an in-scene pad |
|---|---|---|
| Always Replicate As Root | If true, the object is replicated as a root on clients even when it has a parent (the parent is ignored). | leave off |
| Synchronize Transform | Whether the transform is included when the object spawns. It is **not** continuous movement sync. | leave on (pads do not move) |
| Active Scene Synchronization | Migrates the object into the active scene during scene load/unload (additive scenes). | leave off |
| Scene Migration Synchronization | Mirrors a server-side `MoveGameObjectToScene` on clients. | leave at default (on) |
| Spawn With Observers | If true, the spawn is sent to all clients; if false the object only exists on the server. | **must stay on**, otherwise players never see it |
| Dont Destroy With Owner | If true, the object survives its owner disconnecting and ownership goes to the server. | leave off |
| Auto Object Parent Sync | Automatically synchronizes parent/child relationships. | leave on |
| Sync Owner Transform When Parented | Only matters when parenting is synchronized: whether the owner also applies the authority's transform. | not used, leave default |
| Allow Owner To Parent | Allows the owning client to change the parent locally instead of asking the server. | leave off |
| GlobalObjectIdHash | Netcode's cross-machine identifier for this prefab/scene object. Generated automatically. | **read-only, do not edit** |
| NetworkManager | Reference assigned at runtime. | `null` in edit mode is normal; still `null` in Play mode means the object never spawned |

> A GameObject can have only one `NetworkObject`; every `NetworkBehaviour` in this
> project (`SyncedBool`, `Interactable`, `InteractToggle`, …) must live on the **same
> GameObject** as it, or `tools/validate.sh` reports an error.

## The four Pad components

If you are rebuilding the first template, hover these fields in order:

1. `SyncedBool`: `Label` (log name), `Initial Value` (state at spawn), `On Changed`
   (fires on every machine).
2. `Interactable`: `Label` (log name), `Max Distance` (server-side range check — note the
   client `PlayerInteractor` also has its own 3.5 m search range), `On Approved` /
   `On Denied` events.
3. `InteractToggle`: `Label`, `Interactable` / `State` (drag the ones **on the same
   GameObject**), `Target Renderer`, `Off Color` / `On Color`.
4. `NetworkObject`: see the table above.

## If hovering does nothing

- The mouse must rest on the **field label** (left side), not inside the input box.
- The field must be visible in the Inspector (a private field without
  `[SerializeField]` is not shown at all).
- Wait for Unity to finish recompiling after a script change.
- Third-party components have no project tooltip — see the table above.
