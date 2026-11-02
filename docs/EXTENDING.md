# Extending the toolkit without changing it

Yes — a group can add any mechanic they want by writing their **own** scripts that use the
blocks. Nobody needs to edit `packages/com.cgdd.mpblocks/`.

---

## The one rule

> **Your code may reference the blocks. The blocks never reference your code.**

That is why the package can keep improving without breaking student games. Two consequences:

1. Never edit files inside `packages/com.cgdd.mpblocks/`. If a block does not do what you
   need, wrap it — do not fork it.
2. Your scripts live in `unity/Assets/` (`Scripts/`, `Prefabs/`, `Scenes/`). They compile
   into `Assembly-CSharp`, which can see every block automatically.

---

## Three ways to add behaviour, cheapest first

### 1. Inspector wiring — no code at all

Every block exposes `OnChanged` / `onApproved` / `onDenied` / `onDied` UnityEvents.
"when the door opens, play a sound" is a drag-and-drop.

### 2. Your own MonoBehaviour that drives the blocks — the normal case

```csharp
using MPBlocks.Sync;
using UnityEngine;

public sealed class DoorSound : MonoBehaviour
{
    [SerializeField] private SyncedBool doorOpen;
    [SerializeField] private AudioSource source;

    private void Awake() => doorOpen.OnChanged.AddListener(Play);

    private void OnDestroy() => doorOpen.OnChanged.RemoveListener(Play);

    private void Play(bool open) { if (open) source.Play(); }
}
```

That is 90% of "custom mechanics": listen to a block, do something local.

### 3. Your own NetworkBehaviour — only when you need new replicated state

Follow the same rules the package follows, or multiplayer will misbehave in ways that are
hard to debug:

| Rule | Why |
|---|---|
| RPC methods must end in `ServerRpc` / `ClientRpc` | NGO's ILPP rejects them otherwise, **only at build time** |
| `NetworkVariable` write permission: `Server` | clients ask, the server sets |
| Never write state in `Update()` on a client | it will fight the server and desync |
| Put the component on a GameObject that has a `NetworkObject` | silently does nothing otherwise |
| Run `./tools/validate.sh` after adding components | catches the two mistakes above |

```csharp
using MPBlocks.Core;
using Unity.Netcode;
using UnityEngine;

public sealed class SyncedTimer : NetworkBehaviour
{
    private readonly NetworkVariable<float> _remaining = new NetworkVariable<float>(
        60f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public float Remaining => _remaining.Value;

    private void Update()
    {
        if (!IsServer) return;                       // only the server advances time
        _remaining.Value = Mathf.Max(0f, _remaining.Value - Time.deltaTime);
    }
}
```

---

## Recipes

### Only a player with the key may open a door

```csharp
interactable.Validator = clientId => playerHasKey[clientId];
```

`Validator` runs **on the server, in addition to** the distance check. Never trust a value
the client sent to decide this.

### Tell one player something (a role, a hint, "you are the cat")

```csharp
private void Awake() => targeted.Received += (from, payload) => ShowRoleMyScreenOnly(payload);

// server side:
targeted.SendToClient(clientId, roleId);   // only that client's machine receives it
targeted.Broadcast(roundNumber);           // everyone
```

### Give an object to a player (pick up / possess)

```csharp
pickable.GrantTo(clientId);   // server only; ownership moves, HoldPoint attachment follows
pickable.Picked += ownerId => UpdateOutline(ownerId);
pickable.ReturnToWorld();
```

### Private, persistent per-player data (role card, hand of cards)

Use `PlayerPrivateState`. The value is only readable by its owner (and the server), and a
late joiner still receives it:

```csharp
// owner side
private void Awake() { state.RoleChanged += role => ShowRoleOnlyOnMyScreen(role); }

// server side
state.SetServerRole(seerRole);
int theirs = state.ServerRead();     // the server may read anyone
```

Do **not** add it to `StateHash`: each machine can only read its own value, so the hashes
would legitimately differ.

### Rotate roles mid-match

```csharp
roles.AssignAll();     // give everyone a role
roles.RotateRoles();   // shift every role by one position
```

### Player names

The session screen already has a "Your name" field. Behind it:

```csharp
LocalPlayerName.Set("Ian");          // remembers it in PlayerPrefs
nameTag.RequestName("Ian");          // any machine asks; the server decides
var shown = syncedName.Value;        // everyone reads the server's value
```

`PlayerNameTag` submits the saved name when the player spawns; if nothing was submitted the
server fills in "Player N" after two seconds. The value is sanitised (control characters
stripped, 20 characters max) on both ends, because the client is only making a request.

### Teams

```csharp
teamManager.AssignTeams();                 // server: spread players across teams
int myTeam = team.TeamId;                  // public: everyone can read everyone's
Color c = teamManager.ColorFor(myTeam);    // display colour
```

### Spawn something on the server

```csharp
spawner.SpawnOne();      // or drop a prefab in and let the timer do it
```

The prefab must be listed in `NetConfig > Spawnable Prefabs`.

---

## The verification loop (do this every time)

```bash
./tools/validate.sh     # must print errors=0  (catches the silent-mistake class)
./tools/build.sh        # must build
./tools/mp-verify.sh    # must exit 0          (catches "connected but wrong")
```

`mp-verify.sh` is the one that matters. Something that compiles and runs is not the same as
something that stays in sync.

---

## Working with an AI

Give it three things and it will produce code that fits:

1. **This file** — the rules above.
2. **`docs/AI_API.md`** — every block's signature and a minimal example.
3. **The design sheet** (`docs/DESIGN_SHEET.md`) filled in, with the **state table** and
   **event table**. Without them the AI invents state, and invented state is where
   desync comes from.

Then say: *"Write a script in `unity/Assets/Scripts/` that does X, using only the blocks in
AI_API.md. Do not modify anything under `packages/`. Run `./tools/validate.sh` when done."*

---

## Things that are still genuinely hard

| Want | Why it is hard | Do instead |
|---|---|---|
| see the exact same thing on both screens with different privacy rules | Netcode for GameObjects sends state to everyone; there is no per-client visibility filter | hide visually on the client, and only *predict* nothing important |
| voice chat | needs a separate service | sit next to each other; it is a LAN class |
| save/load across sessions | no backend | keep it in one session |
| matching players over the internet | out of scope on purpose | LAN |
