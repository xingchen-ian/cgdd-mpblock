# AI_API — MPBlocks reference

Machine-readable reference for every block. Keep in sync with the code; agents
read this file instead of guessing.

---

## Runtime command-line contract

Both the server and the player read these arguments in `NetBootstrap`. The
tooling in `tools/` depends on them.

| Argument | Meaning |
|---|---|
| `-mp-server` | Start as host/server on `NetConfig.port` |
| `-mp-client <ip>` | Start as client, connect to `<ip>` |
| `-autoconnect` | Skip the session UI and connect immediately |
| `-mp-game <id>` | Override the session id (must match the server's, else the connection is refused) |
| `-mp-players <n>` | Override max players (useful for scripts) |
| `-mp-delay <ms>` | Artificial one-way delay applied to this machine's transport |
| `-mp-jitter <ms>` | Delay jitter |
| `-mp-drop <percent>` | Packet loss percentage |
| `-mp-name <text>` | Name this player submits on connect (saved names live in PlayerPrefs) |
| `-mp-team-demo` | TeamManager assigns teams on start (used by `mp-verify.sh`) |
| `-mp-role-demo` | RoleManager assigns on start and rotates every few seconds (used by `mp-verify.sh`) |
| `-mp-targeted-demo` | Server sends a private event to each client in turn |
| `-mp-spawn-demo` | Spawner runs at 0.8s intervals (used by `mp-verify.sh`) |
| `-mp-interact-demo` | Every player auto-interacts with a random nearby Interactable (used by `mp-verify.sh`) |
| `-mp-combat-demo` | Server damages every player by 15 every 2s (used by `mp-verify.sh`) |
| `-mp-score-demo` | Server adds a point to slot 0 every 1.5s (used by `mp-verify.sh` to prove score replication) |
| `-mp-shoot-demo` | The owning client fires at the nearest other alive player every ~1.2s (used by `mp-verify.sh` for the arena template: `PLAYER_SHOOT` → `PROJECTILE_HIT` → `PLAYER_KILLED` → score) |
| `-mp-quit-after <seconds>` | Exit N seconds after start (used by automation) |

> All MPBlocks flags carry the `-mp-` prefix on purpose. Unity reserves several bare
> names (`-server`, `-client`, `-screen-*`, …) and aborts the player when it sees them
> with unexpected arity.

Standard Unity flags are also honored: `-batchmode`, `-nographics`, `-logFile`.

**Stable log events** (grep targets, do not rename):

```
[MP] INFO SERVER_READY port=7777
[MP] INFO CLIENT_CONNECTING address=127.0.0.1 port=7777
[MP] INFO CLIENT_JOINED clientId=2
[MP] INFO CLIENT_LEFT clientId=2
[MP] INFO STATE_HASH_MATCH frame=120 hash=0x1f3a
[MP] WARN STATE_HASH_MISMATCH server=0x1f3a client=0x1f3b
[MP] WARN AUTH_VIOLATION block=SyncedBool label=doorOpen caller=client
[MP] INFO SESSION_ID_OK gameId=default-game clients=0
[MP] WARN SESSION_ID_MISMATCH expected=default-game got=wrong-game
[MP] INFO VALUE_SET_BY_REQUEST block=SyncedBool label=doorOpen value=True from=2
[MP] INFO EVENT_RECEIVED event=leverPulled by=server from=2
[MP] INFO SCOREBOARD_SPAWNED
[MP] INFO SCORE_CHANGED slot=0 value=1 delta=1
[MP] INFO HEALTH_READY label=health max=100
[MP] INFO HEALTH_CHANGED label=health value=85 damage=15
[MP] INFO INTERACT_REQUESTED label=lever clientId=2
[MP] INFO INTERACT_DENIED label=lever clientId=2 reason=out-of-range
[MP] INFO INTERACT_APPROVED label=Pad_Red clientId=2
[MP] INFO TOGGLE_FLIPPED block=InteractToggle label=Pad_Red value=True clientId=2
[MP] INFO SPAWN_POINTS_READY count=4
[MP] INFO SPAWN_POINT_ASSIGNED clientId=2 point=Spawn_B
[MP] INFO PLAYER_SHOOT owner=2 damage=25
[MP] INFO PROJECTILE_HIT owner=2 victim=1 damage=25
[MP] INFO PLAYER_KILLED killer=2 victim=1
[MP] INFO PLAYER_RESPAWNED clientId=1 point=Spawn_C
[MP] INFO RIGIDBODY_PLAYER_READY label=player clientId=1
[MP] INFO CHARACTER_SELECT_READY label=character-select count=4 unique=True
[MP] INFO CHARACTER_ASSIGNED label=character-select clientId=2 index=1 character=Blue
[MP] INFO CHARACTER_SELECTED label=character-select clientId=2 index=1 character=Blue
[MP] INFO CHARACTER_SELECT_DENIED label=character-select clientId=2 index=1 reason=taken
[MP] INFO CHARACTER_RELEASED label=character-select clientId=2 index=1
[MP] INFO PLAYER_CHARACTER_SET label=player clientId=2 index=1 character=Blue
[MP] INFO SYNCED_ANIMATOR_READY label=animator authority=Owner floats=1 bools=1 ints=0 triggers=1
[MP] INFO PLAYER_ANIMATOR_READY label=player clientId=1 animator=Character_Blue
[MP] WARN SYNCED_ANIMATOR_PARAMETER_MISSING label=animator parameter=Speed type=Float
[MP] WARN PLAYER_PREFAB_FALLBACK reason=rigidbody-player-prefab-missing gameId=template1-world
[MP] INFO AUDIO_ONESHOT label=hit clip=Hit_Damage trigger=OnHealthChanged
[MP] INFO KILL_SCORE label=player killer=1 points=1 reason=kill
[MP] INFO INTERACT_SOUND label=Phone clip=Interact_Approved reason=approved clientId=0
[MP] INFO DOOR_ATTEMPT_RESET reason=window_expired lost=1 window=2.5
[MP] INFO DOOR_PROGRESS pulled=1 required=2
[MP] INFO DOOR_OPENED
[MP] INFO TARGET_HIT damage=20 by=2
[MP] INFO TARGET_DIED slot=0
[MP] INFO SPAWNER_READY prefab=Crate maxAlive=6
[MP] INFO SPAWNED prefab=Crate alive=2 networkObjectId=9
[MP] INFO TARGETED_SENT event=targeted to=2 payload=0
[MP] INFO TARGETED_RECEIVED event=targeted from=0 payload=0
[MP] INFO PICKUP_GRANTED label=phone owner=2 objectId=8
[MP] INFO PICKUP_ATTACHED label=phone to=HoldPoint
[MP] INFO PRIVATE_STATE_SET label=private clientId=2 value=4
[MP] INFO ROLE_ASSIGNED clientId=2 role=4
[MP] INFO NAME_SUBMITTED clientId=1 name=Alice
[MP] INFO NAME_REMEMBERED name=Alice
[MP] INFO NAME_SET clientId=1 name=Player 1
[MP] INFO TEAM_SET label=team clientId=1 team=2
[MP] INFO TEAM_CHANGED clientId=1 team=2
[MP] INFO PLAYER_ANIMATOR_READY label=player clientId=1 animator=none
[MP] INFO ROLE_VISIBILITY label=seer-marker role=1 active=True
[MP] INFO ROLES_ROTATING rotation=1
[MP] INFO LATENCY_APPLIED delay=150 jitter=20 drop=2
```

---

## Blocks

Populated as each block is implemented. Status legend: `planned` / `done` / `folded`.

> `AutoConnect` was folded into `NetBootstrap`: it already parses `-mp-client` and
> connects, so a separate component would add a second place to configure the same thing.

> When a `NetSessionUI` is present, `NetBootstrap` stops auto-hosting in the Editor and
> lets the UI drive the session. Command-line flags still take priority, so exhibition
> launches are unaffected.

### L0 · Core

| Block | Status | Summary |
|---|---|---|
| `NetConfig` (ScriptableObject) | **done** | Port, max players, mode (`HostAndPlay` / `DedicatedServer`), session id, player/scoreboard/spawnable prefabs, player movement (`Transform` / `Rigidbody`), optional character roster, server address source. Code-first setters incl. `SetSpawnablePrefabs(...)`, `SetRigidbodyPlayerPrefab(...)`, `SetPlayerMovement(...)`, `SetCharacterRoster(...)` |
| `NetBootstrap` | **done** | Configures `NetworkManager` + `UnityTransport` from `NetConfig`, parses CLI |
| ~~`AutoConnect`~~ | folded | Connects from CLI/config without UI |
| `NetSessionUI` | **done** | Host / Join(address) / Leave buttons, live player list, refusal reason. Generated into Bootstrap.unity by the setup script |
| `PlayerSpawnPoint` | **done** | Scene markup (plain `MonoBehaviour`) for spawn positions. `NetBootstrap` sorts them by name and hands the next one to NGO's connection-approval response — player objects are owner-authoritative, so the server cannot move a client body afterwards |
| `CharacterRoster` (ScriptableObject) | **done** | Ordered list of characters (name, tint colour, optional visual prefab). Assigned to `NetConfig.characterRoster`; enables the character-select panel. `Assets > Create > MPBlocks > Character Roster`, or `MPBlocks > Add Character Selection to Scene` |

### L1 · Sync

| Block | Status | Summary |
|---|---|---|
| `SyncedTransform` | **done** | Thin wrapper over NGO `NetworkTransform`; `Authority` (`Server`/`Owner`) defaults to `Owner` |
| `SyncedBool` | **done** | Server-owned bool + `OnChanged`. Clients call `RequestSet` |
| `SyncedInt` | **done** | Server-owned int + `OnChanged`. Clients call `RequestSet` |
| `SyncedFloat` | **done** | Server-owned float + `OnChanged`. Clients call `RequestSet` |
| `SyncedString` | **done** | Server-owned string (63 bytes UTF-8 internally). Clients call `RequestSet`; participates in `StateHash` |
| `SyncedAnimator` | **done** | Replicates selected Animator float/bool/int parameters (owner or server authority, default Owner) plus explicit triggers via `SetTrigger`. Supports an Animator on a child (spawned character visual) through `RebindAnimator()`; periodic full snapshot recovers late joiners |
| `TargetedNetEvent` | **done** | Private message to ONE client (`SendToClient`), or server / broadcast. Replaces NetEvent when other players must not see it |
| `NetEvent` | **done** | `SendToServer()` / `SendToAll()` with Inspector callbacks. Not replayed for late joiners |

### L2 · Gameplay

| Block | Status | Summary |
|---|---|---|
| `SimplePlayerMover` | **done** | Sample WASD + right-drag look; enables the camera only for the owner |
| `RigidbodyPlayerMover` | **done (sample)** | Rigidbody variant: owner simulates its body, remote copies are made kinematic and follow `SyncedTransform`; `FixedUpdate` velocity movement, jump + ground raycast, owner-only camera. Requires `NetworkObject` + `Rigidbody` + collider; use instead of `SimplePlayerMover`. See `docs/recipes/rigidbody-sync.md` |
| `PlayerAppearance` | **done (sample)** | Applies the character chosen for a player (tint on the body renderer or an optional visual prefab). Index is a server-owned `NetworkVariable<int>`, so late joiners render the same character. Added to both generated player prefabs automatically |
| `CharacterSelect` | **done (sample)** | Server-authoritative character picker at session start. One scene object with a `NetworkObject`; each player sees a panel, the server claims a unique character (or allows duplicates), assigns it to that player's `PlayerAppearance`, releases it on disconnect, and replicates claimed state to late joiners. `MPBlocks > Add Character Selection to Scene` sets it up |
| `PlayerAnimator` | **done (sample)** | Drives an Animator from movement speed + grounded raycast (owner only) and rebinds when `PlayerAppearance` swaps the character visual. Pair with `SyncedAnimator` (Speed float, Grounded bool) so remote copies animate too. Missing Animator parameters are skipped |
| `Pickable` | **done** | Ownership transfer: `GrantTo(clientId)` / `ReturnToWorld()`; parents to the holder's `HoldPoint`. Wire an `Interactable` for validated pickup |
| `PlayerPrivateState` | **done** | Owner-read `NetworkVariable<int>`: role / hand / secret. Only the owner and the server can read it; late joiners receive it |
| `RoleManager` | **done** | Server distributes and rotates roles across players; supports mid-match role swap |
| `Health` | **done** | Server-only `ApplyDamage` / `Heal` / `ResetHealth`; `Changed`, `Died` events; participates in `StateHash` |
| `HealthHUD` | **done** | IMGUI readout of the local player's HP. Placeholder meant to be replaced |
| `Interactable` | **done** | `Request()` → server validates distance + optional `Validator` hook → `ApprovedClientRpc` to all / targeted `DeniedClientRpc`. Exercised by templates 1 and 2 |
| `Spawner` | **done** | Server-only timer spawner: prefab, interval, maxAlive, radius, `SpawnOne()`, `ClearAll()` |
| `KillScore` | **done** | Kills award the shooter a point on the `Scoreboard` (slot = client id); the killer comes from the projectile, so clients cannot self-award |
| `AudioOneShot` | **done** | Plays one clip on a trigger: `OnEnable` (bullets), `OnHealthChanged` (hit), `OnDeath`, or `Manual`. Several can sit on one object |
| `InteractSound` | **done** | Click/thud audio on an `Interactable`, lives on the object itself; sound plays on approval (and optionally refusal) |
| `CoopDoor` | **done** | N levers must be pulled within `windowSeconds` (default 2.5s) or the attempt resets — a solo player cannot walk between them |
| `Team` | **done** | Public team id (0 = unassigned) with `TeamChanged`; participates in `StateHash` |
| `TeamManager` | **done** | Server splits players across N teams (`AssignTeams`, `ServerReadTeam`, `ColorFor`) |
| `ShowForRole` | **done** | Toggles a child visual by the local player's private role (`visibleForRoles`). Presentation only — state is still broadcast |
| `PlayerAnimator` | **done** | Owner drives the rig: measures speed from position delta, raycasts for grounded, drives `Speed`/`Grounded`; rebinds automatically when the character visual changes |
| `PlayerNameTag` | **done (sample)** | Generates "Player N" into a `SyncedString`; `RequestName` for a name field |
| `Scoreboard` | **done** | Server-owned slots via `NetworkList<int>`; `AddScore` / `SetScore` / `ResetScores` / `RequestAddScore`; participates in `StateHash`. Auto-spawned by the server from `NetConfig.scoreboardPrefab` |
| `ScoreboardHUD` | **done** | Optional read-only panel driven by `Scoreboard.Changed`; generated into Bootstrap.unity |
| `RespawnPoint` | planned | Server-chosen respawn location |
| `PlayerInteractor` | **done (sample)** | Press E to request the nearest `Interactable`; auto-requests under `-mp-interact-demo` |
| `CoopDoor` | **done (sample)** | Counts approved levers (each once), flips a `SyncedBool`, slides a panel locally |
| `DamageOnInteract` | **done (sample)** | Approved interaction → `Health.ApplyDamage`; on death awards a point and respawns. Re-finds the scoreboard at death time because the server spawns it after the scene sweep |
| `InteractToggle` | **done (sample)** | Approved `Interactable` → server flips a `SyncedBool` → every machine repaints a `Renderer` from `OnChanged`; late joiners get the current value in `OnNetworkSpawn` |
| `PlayerShooter` | **done (sample)** | Owner input only; `ShootServerRpc(direction)` (cooldown + alive checks on the server) spawns a registered projectile prefab and launches it |
| `Projectile` | **done (sample)** | Server-simulated bullet: flies forward, distance-tests players, applies `Health.ApplyDamage` on the server, credits a kill to the shooter's scoreboard slot, despawns |
| `PlayerRespawn` | **done (sample)** | Hides the body while dead; the server resets health and tells the owner where to respawn with a targeted `ClientRpc` (the owner applies its own transform) |

### L3 · Diagnostics

| Block | Status | Summary |
|---|---|---|
| `NetworkHUD` | planned | Connection state, ping, client id, role (F1) |
| `LatencySimulator` | **done** | F3 panel: delay / jitter / drop. Unity Transport only accepts these before connecting, so applying restarts the session |
| `StateHash` | **done** | Per-player reporter; hashes connected clients, spawned object ids and every `IStateHashable` value. Logs `STATE_HASH_MATCH` / `MISMATCH` on the server |
| `NetDiag` | **done** | F2 overlay: role, clientId, port, gameId, refusal reason, every local IPv4, connection event log |
| `AuthorityBadge` (Editor) | planned | Scene-view authority labels |
| `BlockValidator` (Editor) | **done** | `MPBlocks > Validate Project` or `tools/validate.sh`; opens **every scene registered in Build Settings** (enabled and disabled), one machine-readable line per problem with `scene=` |

---

## Teaching templates (generated, not hand-edited)

`MPBlocks > Generate Teaching Templates` regenerates all three; `MPBlocks > New
Game Scene...` generates a per-group game from one of them (own scene + own
`NetConfig` + first enabled Build Settings entry + Bootstrap disabled).

| Scene | What it teaches | Key blocks |
|---|---|---|
| `Assets/Scenes/Templates/Template1_WorldInteraction.unity` | client asks, server decides; replicated state | `Interactable`, `SyncedBool`, `InteractToggle` |
| `Assets/Scenes/Templates/Template2_CoopMission.unity` | state vs events; a rule lives on the server | `CoopDoor`, `Health`, `DamageOnInteract`, `Scoreboard` |
| `Assets/Scenes/Templates/Template3_ArenaCombat.unity` | server-authoritative combat, respawn, kill score | `PlayerShooter`, `Projectile`, `PlayerRespawn` |

Acceptance commands per template live in TEMPLATES.md and
the instructor guide (not published).


---

## Example — the minimal working scene

```csharp
// Generated by an editor script; agents should not hand-edit scenes.
var root = new GameObject("Net");
var boot = root.AddComponent<NetBootstrap>();
boot.Config = AssetDatabase.LoadAssetAtPath<NetConfig>("Assets/Settings/Local.asset");
```

Player prefab requirements: `NetworkObject`, `SyncedTransform`
(`Authority = Owner`), a camera, and an input script.
