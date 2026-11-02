# Structured Design Sheet

Fill this in **before** asking an AI to implement anything. Every row maps to a
block in `docs/AI_API.md`. If a state is not listed here, it does not get synced.

Group: ______  Game: ______  Players: 2 / 3 / 4

---

## 1. The pitch

**Core loop (one sentence):**

**How a round ends / win condition (one sentence):**

**Why it needs to be multiplayer (what breaks if a player is alone):**

---

## 2. States — things that are *true right now*

Everything here is persistent: a player who joins late must see it correctly.
Each row becomes one `Synced*` block.

| State name | Type | Authority | Initial value | Who changes it | Visible to |
|---|---|---|---|---|---|
| e.g. `doorOpen` | bool | Server | false | server, when both levers held | everyone |
| e.g. `teamScoreA` | int | Server | 0 | server, on capture | everyone |
| e.g. `playerPosition` | transform | Owner | spawn | owning client | everyone |
|  |  |  |  |  |  |
|  |  |  |  |  |  |
|  |  |  |  |  |  |

*Authority rules:* `Server` for anything a player could cheat by setting.
`Owner` only for a player's own transform.

---

## 3. Events — things that *happen at a moment*

Events are **not** replayed for late joiners. If an event's effect must persist,
it belongs in section 2 as a state instead.

| Event name | Sent by | Received by | Payload | Used for |
|---|---|---|---|---|
| e.g. `OnLeverPulled` | any client | server | lever index | registering a request |
| e.g. `OnRoundStart` | server | all clients | — | starting the round animation |
|  |  |  |  |  |
|  |  |  |  |  |

---

## 4. Requests — player intents that need server approval

Anything a player can trigger that changes shared state. These use `Interactable`
and follow request → validate → result.

| Intent | Precondition checked on server | Result |
|---|---|---|
| e.g. "pick up flag" | flag not carried, player not carrying | flag attaches, teleports to base on death |
|  |  |  |
|  |  |  |

---

## 5. Players and spawning

| Item | Value |
|---|---|
| Players per round | |
| Spawn points | |
| Respawn rule (if any) | |
| Player prefab parts | body, camera, input, `SyncedTransform`, ... |

---

## 6. Controls

| Key / button | Intent |
|---|---|
| WASD | move |
|  |  |

---

## 7. Scene contents

| Object | Prefab / block | Notes |
|---|---|---|
|  |  |  |

---

## 8. Explicitly out of scope

List what this game will **not** do, so the AI does not invent it.

- (e.g. no client-side prediction, no lobby, no accounts)
-
-
