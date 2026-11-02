# AGENTS.md — rules for an AI helping a student build a game with MPBlocks

You are helping a student build a multiplayer game on Unity 2022.3 with
Netcode for GameObjects. The blocks are in the `com.cgdd.mpblocks` package;
the student's game code lives in `Assets/`.

## Before you write anything

1. Read [AI_API.md](AI_API.md). It lists every block with its exact signature.
2. Read the student's [DESIGN_SHEET.md](DESIGN_SHEET.md). If the sheet is empty or
   vague, ask for the three tables (states / events / requests) **before** coding.
3. Restate the sheet row by row and say which block implements each row. Wait for
   confirmation, then write code.

## Hard rules

| Never | Why |
|---|---|
| Invent a block or an API that is not in `AI_API.md` | the toolkit is small on purpose; a guessed name compiles into nothing |
| Let a client write shared state | clients call `RequestSet` / `Interactable.Request`; the server decides |
| Hand-edit scene or prefab YAML | generate scenes with `MPBlocks > New Game Scene…`, or a small editor script |
| Put game code inside the package | student code belongs in `Assets/` |
| Add a `NetworkBehaviour` to a UI-only object | it is not networked, and it costs a spawn |

## The model to teach

- **State** is true now and is replayed to late joiners (`SyncedBool/Int/Float`).
- **An event** happened once and is **not** replayed (`NetEvent`, `TargetedNetEvent`).
- If an event's effect must survive, it is state.
- `SyncedTransform` is the only block that defaults to owner authority.

## Verify, do not assume

```bash
./tools/validate.sh     # must print errors=0
./tools/build.sh        # BUILD_OK in the log
./tools/mp-verify.sh    # exit code 0 = one server and two clients agree
```

Logs are the evidence. Every line starts with `[MP]`:

```
[MP] INFO SERVER_READY port=7777
[MP] INFO CLIENT_JOINED clientId=2
[MP] WARN AUTH_VIOLATION block=SyncedBool label=doorOpen caller=client
```

If a claim cannot be checked with one of those commands or a `[MP]` log line, say so
instead of asserting it works.
