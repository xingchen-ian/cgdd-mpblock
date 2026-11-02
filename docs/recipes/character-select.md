# Character selection at session start

Players pick one of several pre-designed characters when a session starts. The choice is
server-authoritative, unique by default, and replicated — a player who joins late sees the
correct characters immediately.

The feature is made of three pieces, all in the package:

| Piece | Type | Role |
|---|---|---|
| `CharacterRoster` | ScriptableObject | ordered list of characters: name, tint colour, optional visual prefab |
| `CharacterSelect` | scene `NetworkBehaviour` | shows the pick panel, validates and stores claims on the server |
| `PlayerAppearance` | component on the player prefab | applies the chosen character on every machine |

## One-click setup

In a scene that already has the MPBlocks infrastructure (a `Net` object with
`NetBootstrap` and a `NetConfig`):

**`MPBlocks > Add Character Selection to Scene`**

This:

1. creates `Assets/Settings/CharacterRoster.asset` with four example characters
   (Red / Blue / Green / Yellow);
2. assigns the roster to the scene's `NetConfig` (`characterRoster`);
3. adds a `CharacterSelect` object with a `NetworkObject` to the scene;
4. adds the `PlayerAppearance` component to the configured player prefabs (it is also
   added automatically by the setup script, so this only helps older prefabs).

Then press **Play → Host**: a small "Choose your character" panel appears for each player;
the player prefab shows the assigned character.

## Designing the characters

Open `Assets/Settings/CharacterRoster.asset` and edit the list.

Each entry has:

| Field | Meaning |
|---|---|
| `Display Name` | shown on the panel and in `[MP]` logs |
| `Color` | tint applied to the player's body when there is no visual prefab (URP `_BaseColor`, built-in `_Color`) |
| `Visual Prefab` | optional model instantiated **under the player**; leave empty to tint the body instead |

Visual prefab rules:

- it is a **visual only**: no `Collider`, no `Camera`, no scripts needing ownership;
- the player root keeps its own collider (`CapsuleCollider` for the Rigidbody player);
- it is instantiated under the player root (or a `Visual Root` you assign on
  `PlayerAppearance`) at local position zero.

Students can make `Character_Red / Character_Blue / ...` as prefabs (modified capsules,
simple models) and drop them into the roster. If they only need colours, leave the prefabs
empty and set `Color`.

## Behaviour

- On spawn the **server** assigns the first free character (or one based on the client id
  when duplicates are allowed). This means the game works even if nobody clicks anything.
- Each client sees the panel; clicking a character sends a `ServerRpc`. The server checks
  that the character is not already claimed by another player (default) and assigns it to
  the requester's `PlayerAppearance`.
- The chosen index lives in a `NetworkVariable<int>` on `PlayerAppearance`, so late joiners
  see the right character.
- When a player disconnects, the server frees their character for the next player.
- `Allow Duplicates` on `CharacterSelect` lets several players pick the same character.
- `Show Panel` can be turned off for a dedicated server; selection still works by default
  assignment.

Try it locally:

```bash
./tools/build.sh                # or run ./tools/run-local.sh 2 from the project
./tools/run-local.sh 2
```

With three players, each should have a different colour; joining a fourth after the others
should show the same colours on everyone.

## Code-first API (no Inspector)

```csharp
// On a scene object with a NetworkObject:
var select = selectGo.AddComponent<CharacterSelect>();
select.SetRoster(roster);
select.SetAllowDuplicates(false);

// On the player prefab:
var appearance = playerPrefab.AddComponent<PlayerAppearance>();
appearance.SetBodyRenderer(bodyRenderer);
```

Runtime server API on a player:

```csharp
player.GetComponent<PlayerAppearance>().SetCharacterServer(index); // server only
```

## Limitations and extension points

- The player object exists while choosing (there is no separate pre-spawn lobby); movement
  is not blocked by the panel. If a round must wait for everyone, add a small server check
  (for example a `SyncedBool roundStarted` that your movement/weapon scripts read) and set
  it when all connected players have a non-default selection.
- The panel is IMGUI placeholder UI, like `HealthHUD` and `NetDiag`. Replace it with your
  own uGUI/Canvas by reading `CharacterSelect.Roster` and calling the same
  `RequestSelectServerRpc` path (expose a public `RequestSelect(int)` if needed).
- Character selection affects **appearance only**. If characters must have different
  components or abilities, give every player the superset of components and switch the
  active ones with another server-owned `SyncedInt`, or extend the spawn path to use
  different player prefabs.

Rigged characters (SkinnedMeshRenderer + Animator + bone animation) fit the `Visual Prefab`
slot directly; see [animated-characters.md](animated-characters.md) for the animation
replication setup (`PlayerAnimator` + `SyncedAnimator`).
