# Rigidbody movement over the network

**Short answer: yes — with a caveat.** `SyncedTransform` is a `NetworkTransform`, and a
`NetworkTransform` synchronizes the *resulting transform* (position / rotation / scale).
A Rigidbody is just a different way of producing that transform, so the motion replicates.
What is **not** synchronized is the physics state itself (velocity, forces, contacts): the
authority machine simulates the Rigidbody, everyone else's copy follows the synced
transform.

So the rule is simple: **one machine simulates the body, every other machine must not.**

---

## Option A — use the packaged sample block (no code)

`MPBlocks.Gameplay.RigidbodyPlayerMover` ships in the package and does everything below:
owner-only input, `FixedUpdate` velocity movement, jump + ground raycast, owner-only
camera, and kinematic remote copies. The setup script also generates
`Assets/Prefabs/PlayerRigidbody.prefab` with that block already configured.

**Switching a game between transform and physics movement** is one dropdown: select the
`NetConfig` asset (select `Net` in the scene → its **Config** field, or open
`Assets/Settings/NetConfig_<Game>.asset`) and set **Player Movement**:

- `Transform` → spawns `Assets/Prefabs/Player.prefab` (`SimplePlayerMover`)
- `Rigidbody` → spawns `Assets/Prefabs/PlayerRigidbody.prefab` (`RigidbodyPlayerMover`)

Nothing in the scene changes; only the next spawned player prefab does. You can still make
your own variant (the steps below) and point either slot at it.

Configs created before the Rigidbody player existed may have an empty
**Rigidbody Player Prefab** field, which makes the dropdown fall back to the Transform
prefab (and logs `[MP] WARN PLAYER_PREFAB_FALLBACK`). The setup script fills that field on
every `NetConfig`; if yours is still empty, run `MPBlocks > Refresh Project Setup` or drag
`Assets/Prefabs/PlayerRigidbody.prefab` into it.

1. Duplicate `Assets/Prefabs/Player.prefab`.
2. Remove `SimplePlayerMover`, add `RigidbodyPlayerMover` (`Add Component` → search the
   name).
3. Add `Rigidbody` + one `CapsuleCollider` to the root (settings table below).
4. Keep `SyncedTransform` with **Authority = Owner**.
5. On `RigidbodyPlayerMover`: drag the camera into **Player Camera**, set **Ground Mask**
   to your ground/props layer, tune **Move Speed** / **Jump Speed**.

Then assign the duplicate to `NetConfig > Rigidbody Player Prefab` and select
**Player Movement = Rigidbody**. That is the whole setup.

---

## Option B — write your own controller (what the block does)

This matches the sample `SimplePlayerMover` model (a player must not fight their own
latency) and is what most student games want.

### 1. Prefab

Duplicate `Assets/Prefabs/Player.prefab` first (`Cmd/Ctrl + D`) so the demo and the
templates keep working.

| Component | Setting |
|---|---|
| `Rigidbody` | Mass `1`, Drag `0`, Angular Drag `0.05`, Use Gravity ✓, Is Kinematic ✗, Interpolation `Interpolate`, Collision Detection `Continuous`, Constraints **`Freeze Rotation` (all three axes)** |
| `CapsuleCollider` | one collider for the body: Height `2`, Radius `0.5`, Center `(0, 1, 0)` — delete the old body collider so there is exactly one |
| `SyncedTransform` | **Authority = Owner** (default). Interpolate ✓ (default), Sync Position/Rotation ✓ |
| `NetworkObject` | required |
| your controller | the script below; remove/disable `SimplePlayerMover` |

> **Why all rotations are frozen:** the controller owns the yaw. If the physics engine is
> allowed to rotate the capsule (even only around Y), contacts inject angular velocity and
> the child camera shakes left/right when the player bumps into a wall. Freeze all
> rotation, zero `angularVelocity`, and only write `Rigidbody.rotation` when the mouse
> actually changes the yaw. The bundled `RigidbodyPlayerMover` does this automatically.

Optional: add NGO's **`NetworkRigidbody`** (`Add Component > Netcode > Network Rigidbody`).
It does the kinematic bookkeeping for you (it requires `NetworkTransform`, which
`SyncedTransform` is). If you add it, do **not** also set `isKinematic` in your own script.

### 2. Controller

Only the owner reads input and only the owner's Rigidbody is dynamic. Remote copies stay
kinematic and are moved by `SyncedTransform`.

```csharp
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
public sealed class RigidbodyPlayerMover : NetworkBehaviour
{
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float lookSensitivity = 2.5f;
    [SerializeField] private float jumpSpeed = 5f;
    [SerializeField] private Camera playerCamera;

    private Rigidbody _body;
    private float _yaw;
    private float _appliedYaw;
    private bool _grounded;
    private bool _jumpQueued;

    public override void OnNetworkSpawn()
    {
        _body = GetComponent<Rigidbody>();
        _yaw = transform.eulerAngles.y;
        _appliedYaw = _yaw;

        if (!IsOwner)
        {
            // Remote copies must not simulate: SyncedTransform drives their transform.
            _body.isKinematic = true;
            _body.interpolation = RigidbodyInterpolation.None;
            return;
        }

        _body.isKinematic = false;
        _body.interpolation = RigidbodyInterpolation.Interpolate;

        // The script owns the yaw. Freeze all physics rotation (see the note above).
        _body.constraints = RigidbodyConstraints.FreezeRotation;
        _body.angularVelocity = Vector3.zero;
        _body.rotation = Quaternion.Euler(0f, _yaw, 0f);

        if (playerCamera != null) playerCamera.enabled = true;
    }

    private void Update()
    {
        if (!IsOwner) return;

        if (Input.GetMouseButton(1))
        {
            _yaw += Input.GetAxis("Mouse X") * lookSensitivity;
        }

        _grounded = Physics.Raycast(transform.position + Vector3.up * 0.15f,
            Vector3.down, 0.35f);

        // Read the jump key here: GetKeyDown can be missed in FixedUpdate.
        if (_grounded && Input.GetKeyDown(KeyCode.Space)) _jumpQueued = true;
    }

    private void FixedUpdate()
    {
        if (!IsOwner) return;

        float x = Input.GetAxisRaw("Horizontal");
        float z = Input.GetAxisRaw("Vertical");
        var rotation = Quaternion.Euler(0f, _yaw, 0f);
        var move = rotation * new Vector3(x, 0f, z);
        if (move.sqrMagnitude > 1f) move.Normalize();

        // Keep the Rigidbody's own vertical velocity (gravity) and override only X/Z.
        _body.velocity = new Vector3(move.x * moveSpeed, _body.velocity.y, move.z * moveSpeed);

        // Only write the rotation when the mouse changed the yaw; continuously rewriting
        // it while touching a collider makes the camera jitter.
        if (Mathf.Abs(Mathf.DeltaAngle(_appliedYaw, _yaw)) > 0.01f)
        {
            _appliedYaw = _yaw;
            _body.rotation = Quaternion.Euler(0f, _yaw, 0f);
        }

        _body.angularVelocity = Vector3.zero;

        if (_jumpQueued)
        {
            _jumpQueued = false;
            _body.velocity = new Vector3(_body.velocity.x, jumpSpeed, _body.velocity.z);
        }
    }
}
```

### 3. What the other machines see

- The owner's Rigidbody moves; `NetworkTransform` sends the new transform.
- Every remote copy is kinematic and follows the transform (with interpolation enabled).
- A player who joins later receives the current transform — the body appears where it is.
- `StateHash` deliberately ignores owner-authoritative transforms, so movement cannot
  produce `STATE_HASH_MISMATCH`.

---

## Server-authoritative variant (props, vehicles, pushable physics)

Change `SyncedTransform` → **Authority = Server**, and send input to the server instead of
moving locally. Then:

- only the server's Rigidbody simulates; clients' copies are kinematic;
- the server is the one source of truth for pushes/collisions, so shared physics is
  consistent;
- the cost is input delay — a player feels their own body lag by the round trip.

Use this for things like a movable cart or a physics puzzle, not for a player's own body.
If you want it to be owner-authoritative *and* feel physical, use `NetworkRigidbody` on the
player (owner authority) and keep physics puzzles server-authoritative.

---

## Pitfalls

- **Never mix** `transform.position += ...`, `Rigidbody.velocity` and a kinematic copy on
  the same machine — pick one writer per body.
- Don't let a remote body simulate: two simulations drift and the copy snaps back and
  forth. Either set `isKinematic` yourself (as above) or add `NetworkRigidbody`.
- One collider per body (or a proper compound), not the old visual collider plus the new
  capsule.
- Physics interactions between two owner-authoritative players are **not authoritative**:
  each machine simulates its own body and the others are kinematic. If two players must
  push each other reliably, make the interaction server-authoritative (a server-side
  impulse) instead of relying on player bodies.
- `Rigidbody` interpolation on remote bodies is unnecessary; `NetworkRigidbody` turns it
  off for you when `NetworkTransform` interpolates.
- `CharacterController` instead of `Rigidbody` also works (transform sync, owner
  authority); it gives cleaner wall sliding but no physics pushes.

---

## Verify

```bash
TARGETS=mac BUILD_SCENE=Assets/Scenes/<YourGame>.unity ./tools/build.sh
./tools/run-local.sh 2          # walk around, both windows should agree
./tools/mp-verify.sh            # connection + state hash still green
```

Move with the host and with a client; a late-joining third player must appear at the
current position. If remote players slide/snap, check that remote copies are kinematic and
that nothing but the owner writes the transform.

Reference: Netcode for GameObjects docs, `NetworkTransform` and `NetworkRigidbody`.
