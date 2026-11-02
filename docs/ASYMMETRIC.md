# Building an asymmetric mode

Asymmetry means players do not have the same job. Before writing code, decide **which axis**
you are asymmetric on — each one uses different blocks.

---

## 1. The four axes

| Axis | The players differ in… | Block | Example |
|---|---|---|---|
| **Information** | what they know | `PlayerPrivateState` (+ `TargetedNetEvent` for a one-off hint) | 1 seer knows who the ghost is |
| **Capability** | what they can do | role id + a branch on the server | the cat can catch; rats can only collect |
| **Presentation** | what they see on screen | `ShowForRole` | only the cat sees footprints |
| **Control** | which body they drive | `Pickable` (ownership transfer) | possession; one player drives the legs |
| **Public alignment** | who is on whose side | `Team` / `TeamManager` | 2v2, or 1 vs the rest |

Most student ideas use **two or three at once**. *Stray Rat* = information (cat hears, rats see
scent) + capability (cat catches, rats collect) + presentation (different overlays).

> **Rule of thumb:** decide the axis first, then pick the block. Adding a second controller is
> not asymmetry.

---

## 2. The five steps

### Step 1 — assign a private role

`RoleManager` on a scene object, `PlayerPrivateState` on the player prefab (already there).

```csharp
// server
roles.AssignAll();          // spread roleIds across players
roles.RotateRoles();        // mid-match swap (asymmetry 12)
```

The role lives in `PlayerPrivateState`, which is **owner-read**: only that player and the
server can read it. A late joiner gets one too, because it is state.

### Step 2 — branch on the role, on the server

Everything a role *can do* must be checked where the authority is:

```csharp
// on the server, inside an interaction handler
if (privateStateOf(clientId).ServerRead() != CatRole) return;   // rats cannot catch
```

Never branch on a role the client sent you. The client can read its own role; that is all.

### Step 3 — show each player only what they should see

```csharp
// attach ShowForRole to a child visual, list the roles allowed to see it
visibleForRoles = new[] { CatRole };
```

It reads the local player's private role and toggles the object. It is **presentation only** —
read section 4 before relying on it for secrets.

### Step 4 — give each role a different verb

| Role | Server-side rule | Local control |
|---|---|---|
| cat | only cat may call `catch` | move fast, no scent overlay |
| rat | only rats may call `collect` | see scent trail, no catch |

Keep the *verbs* symmetric where you can (everyone moves) and make only the **outcome** differ.
Games become unlearnable when the controls differ too.

### Step 5 — asymmetric win condition

```csharp
// server
if (catCaughtBoth) scoreboard.AddScore(catSlot, 1);
else if (allCheeseTaken) scoreboard.AddScore(ratSlot, 1);
```

One `Scoreboard`, different slots, different meanings. That is the whole "asymmetric win".

---

## 3. Worked example — 1 cat vs 2 rats

```
Roles:      1 = cat, 2 = rat
Scene:      RoleManager, TeamManager(unused here), Scoreboard
Player:     PlayerPrivateState, SyncedTransform(Owner), Health
CatOnly:    ShowForRole  visibleForRoles = {1}   → a "hunt" reticle
RatOnly:    ShowForRole  visibleForRoles = {2}   → scent-trail particles
Cheese:     Pickable + Interactable              → taking it moves ownership and scores
```

Server rules (one small `CatAndRatsMode` script):

```csharp
void OnInteractApproved(ulong clientId, string what)
{
    int role = StateOf(clientId).ServerRead();

    if (what == "cheese" && role == RatRole)  scoreboard.AddScore(ratSlot, 1);
    if (what == "rat"    && role == CatRole)  caughtRats++, teleportToSpawn(target);
    if (caughtRats == 2)                      scoreboard.AddScore(catSlot, 1);
}
```

Nothing else needs to know about roles. That is the point: **asymmetry concentrates in two
places — who can trigger what (server), and what is rendered (client).**

---

## 4. What this cannot do

Netcode for GameObjects **broadcasts object state to every client**. So:

| Want | Reality |
|---|---|
| "the cat must not *receive* the rats' positions" | not possible with this stack. `ShowForRole` hides the visual; the position is still sent |
| anti-cheat | a modified client can read and show anything it receives |
| different physics per role | possible, but each client would predict differently — expect desync |

For a class project, visual asymmetry is the right call: it is cheap, it reads clearly, and
nobody is cheating. Say this to the students explicitly so nobody thinks they built a secure
hidden-information game.

**Do not** put `ShowForRole` on an object that has a `NetworkObject` — hiding a spawned object
breaks replication. Point it at a child visual.

---

## 5. Pitfalls

| Symptom | Cause |
|---|---|
| everyone sees the same thing | you used `Team` (public) where you needed `PlayerPrivateState` (secret) |
| a player has no role | assigned before their player object existed — `RoleManager` retries, but check `ROLE_ASSIGN_FAILED` in the log |
| role changes but the screen does not | the client bound its UI once at spawn instead of subscribing to `RoleChanged` |
| role works for the host, not for clients | the branch ran on `IsServer` only in a place a client also needed, or the client trusted its own value |
| the hidden prop still blocks movement | you hid the visual but left the collider — hide the collider too, or disable it separately |
