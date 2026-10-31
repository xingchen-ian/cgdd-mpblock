using MPBlocks.Core;
using MPBlocks.Gameplay;
using MPBlocks.Sync;
using Unity.Netcode;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MPBlocks.Editor
{
    /// <summary>
    /// Template 2 — the co-op mission.
    ///
    /// Two levers must be pulled, each once, before the door opens; behind it a target
    /// with 100 HP dies after five approved hits and awards a point on scoreboard slot 0.
    /// The lesson is the difference between <em>state</em> and <em>events</em>: the door
    /// and the health are state that a late joiner replays, while a hit is an event that
    /// only machines watching at the time ever see — and the rule itself (both levers,
    /// each once) runs on the server in <see cref="CoopDoor"/>.
    ///
    /// Generated from C# (never hand-edited YAML) and callable for both the teaching
    /// template and a per-group game scene: paths and the session id come from
    /// <see cref="GameSpec"/>, never from constants here.
    /// </summary>
    public static class Template2Builder
    {
        private const int SpawnPointCount = 4;
        private const int LeverCount = 2;

        private static readonly Color LeverHandleColor = new Color(0.95f, 0.72f, 0.20f);

        /// <summary>
        /// Builds Assets/Scenes/Templates/Template2_CoopMission.unity (or the equivalent
        /// per-game scene) plus its own NetConfig asset and saves both.
        /// </summary>
        /// <param name="spec">Scene path, config path, game id, player count and port.</param>
        public static void Build(GameSpec spec)
        {
            SceneKit.EnsureProjectScaffolding();
            var playerPrefab = SceneKit.PlayerPrefab();
            var scoreboardPrefab = SceneKit.ScoreboardPrefab();

            var scene = SceneKit.NewEmptyScene();

            // The scoreboard prefab is spawned by the server from this config, so the
            // scene does not place one; DamageOnInteract finds it at runtime.
            var config = SceneKit.CreateNetConfig(spec, playerPrefab, scoreboardPrefab);
            SceneKit.CreateInfrastructure(config);

            SceneKit.CreateGround(new Vector3(4f, 1f, 4f), new Color(0.20f, 0.22f, 0.24f));
            SceneKit.CreateDirectionalLight();
            SceneKit.CreateHintText("Two players, two levers | E pull | open the door and finish the target together");

            BuildSpawnPoints();

            // Both levers sit close to the spawn points: with -mp-interact-demo every
            // player can pull one without moving, which is how mp-verify proves the
            // server-side "each lever once" rule.
            var leverA = BuildLever("Lever_A", new Vector3(2.6f, 0f, 0f));
            var leverB = BuildLever("Lever_B", new Vector3(-2.6f, 0f, 0f));

            BuildDoor(leverA, leverB);
            BuildTarget();

            SceneKit.SaveScene(scene, spec.ScenePath);

            MPLog.Info("SETUP_TEMPLATE2",
                ("name", spec.Name),
                ("scene", spec.ScenePath),
                ("config", spec.ConfigPath),
                ("gameId", spec.GameId),
                ("maxPlayers", spec.MaxPlayers),
                ("spawnPoints", SpawnPointCount),
                ("levers", LeverCount),
                ("scoreSlot", 0));
        }

        /// <summary>
        /// Four points around the origin, all facing the mission area. Both levers are
        /// within 3.5 m of at least one spawn point, so -mp-interact-demo can pull them
        /// without moving; the target sits behind the door and is reached on foot.
        /// </summary>
        private static void BuildSpawnPoints()
        {
            var lookAt = new Vector3(0f, 0f, 3.2f);
            SceneKit.CreateSpawnPoint("Spawn_A", new Vector3(0.8f, 0f, 0f), lookAt);
            SceneKit.CreateSpawnPoint("Spawn_B", new Vector3(-0.8f, 0f, 0f), lookAt);
            SceneKit.CreateSpawnPoint("Spawn_C", new Vector3(0.8f, 0f, 1.5f), lookAt);
            SceneKit.CreateSpawnPoint("Spawn_D", new Vector3(-0.8f, 0f, 1.5f), lookAt);
        }

        /// <summary>
        /// A networked interaction point with a visible handle. The scene helper already
        /// adds the NetworkObject and the Interactable; CoopDoor subscribes to its
        /// approval event on the server.
        /// </summary>
        private static Interactable BuildLever(string name, Vector3 position)
        {
            var root = SceneKit.CreateLever(name, position, 4f, LeverHandleColor);
            return root.GetComponent<Interactable>();
        }

        /// <summary>
        /// The co-op door: a <see cref="SyncedBool"/> that late joiners replay, plus a
        /// <see cref="CoopDoor"/> that owns the "both levers, each once" rule on the
        /// server and slides the panel locally on every machine.
        /// </summary>
        private static void BuildDoor(Interactable leverA, Interactable leverB)
        {
            var root = new GameObject("Door");
            root.transform.position = new Vector3(0f, 0f, 6f);
            root.AddComponent<NetworkObject>();

            var isOpen = root.AddComponent<SyncedBool>();
            SceneKit.SetSerialized(isOpen, "label", "DoorOpen");

            var panel = SceneKit.CreateBlock("DoorPanel", new Vector3(0f, 1.6f, 6f),
                new Vector3(3.2f, 3.2f, 0.3f), new Color(0.40f, 0.45f, 0.55f));
            panel.transform.SetParent(root.transform, true);

            var door = root.AddComponent<CoopDoor>();
            SceneKit.SetSerialized(door, "isOpen", isOpen);
            SceneKit.SetSerialized(door, "doorPanel", panel.transform);
            SceneKit.SetSerialized(door, "requiredLevers", 2);
            SceneKit.SetSerializedArray(door, "levers", new Object[] { leverA, leverB });
        }

        /// <summary>
        /// The target: server-authoritative health, an interaction that requests a hit,
        /// and <see cref="DamageOnInteract"/> that turns five approvals (20 damage each)
        /// into one point on scoreboard slot 0.
        ///
        /// It stands <em>behind</em> the door (z = 8, door at z = 6), so the co-op mission
        /// reads correctly: pull both levers, walk through the opening door, then finish
        /// the target. Its Interactable range (4 m) keeps it out of reach until a player
        /// has walked past the door.
        /// </summary>
        private static void BuildTarget()
        {
            var root = new GameObject("Target");
            root.transform.position = new Vector3(0f, 0f, 8f);
            root.AddComponent<NetworkObject>();

            var health = root.AddComponent<Health>();
            SceneKit.SetSerialized(health, "label", "target");
            SceneKit.SetSerialized(health, "maxHealth", 100);

            var interactable = root.AddComponent<Interactable>();
            SceneKit.SetSerialized(interactable, "label", "Target");
            SceneKit.SetSerialized(interactable, "maxDistance", 4f);

            // Keep the collider: the body is a solid target, not a decal.
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "TargetBody";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 1f, 0f);
            SceneKit.SetColor(body, new Color(0.72f, 0.25f, 0.28f));

            var damage = root.AddComponent<DamageOnInteract>();
            SceneKit.SetSerialized(damage, "interactable", interactable);
            SceneKit.SetSerialized(damage, "health", health);
            SceneKit.SetSerialized(damage, "damage", 20);
            SceneKit.SetSerialized(damage, "scoreSlot", 0);
            SceneKit.SetSerialized(damage, "visual", body.transform);
        }
    }
}
