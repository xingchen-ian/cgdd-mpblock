using MPBlocks.Core;
using MPBlocks.Diagnostics;
using MPBlocks.Gameplay;
using MPBlocks.Sync;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MPBlocks.Editor
{
    /// <summary>
    /// Template 3 — the arena deathmatch: four fighters, a server-simulated projectile,
    /// server-side damage, death, respawn and a four-slot kill scoreboard.
    ///
    /// The scene, the three prefabs and the NetConfig are all generated from this file;
    /// no scene or prefab YAML is ever edited by hand. The combat prefabs live at fixed
    /// shared paths, so a game scene created later from "New Game Scene..." reuses them
    /// instead of copying them — only the scene and its NetConfig are per game.
    /// </summary>
    public static class Template3Builder
    {
        private const string PlayerCombatPrefabPath = "Assets/Prefabs/PlayerCombat.prefab";
        private const string ProjectilePrefabPath = "Assets/Prefabs/Projectile.prefab";
        private const string Scoreboard4PrefabPath = "Assets/Prefabs/Scoreboard_4P.prefab";

        private const float SpawnCorner = 12f;
        private const float CoverOffset = 8f;

        /// <summary>
        /// Generates the complete template 3 content for one scene spec: shared combat
        /// prefabs (reused if they already exist), the spec's NetConfig, and the arena
        /// scene at <c>spec.ScenePath</c>.
        /// </summary>
        public static void Build(GameSpec spec)
        {
            SceneKit.EnsureProjectScaffolding();

            GameObject projectilePrefab = EnsureProjectilePrefab();
            GameObject playerCombatPrefab = EnsurePlayerCombatPrefab(projectilePrefab);
            GameObject scoreboardPrefab = EnsureScoreboard4Prefab();

            Scene scene = SceneKit.NewEmptyScene();

            NetConfig config = SceneKit.CreateNetConfig(spec, playerCombatPrefab, scoreboardPrefab, projectilePrefab);
            SceneKit.CreateInfrastructure(config);

            BuildArena();

            // Without this a fresh scene has no character choice at all.
            CharacterSelectSetup.Apply(config);

            SceneKit.SaveScene(scene, spec.ScenePath);

            MPLog.Info("SETUP_TEMPLATE3",
                ("scene", spec.ScenePath),
                ("config", spec.ConfigPath),
                ("gameId", spec.GameId),
                ("players", spec.MaxPlayers));
        }

        // ---------------------------------------------------------------- scene content

        private static void BuildArena()
        {
            SceneKit.CreateGround(new Vector3(6f, 1f, 6f), new Color(0.13f, 0.14f, 0.16f));
            SceneKit.CreateDirectionalLight();

            SceneKit.CreateBlock("Cover_North", new Vector3(0f, 0.75f, CoverOffset),
                new Vector3(10f, 1.5f, 1f), new Color(0.33f, 0.35f, 0.40f));
            SceneKit.CreateBlock("Cover_South", new Vector3(0f, 0.75f, -CoverOffset),
                new Vector3(10f, 1.5f, 1f), new Color(0.33f, 0.35f, 0.40f));
            SceneKit.CreateBlock("Cover_East", new Vector3(CoverOffset, 0.75f, 0f),
                new Vector3(1f, 1.5f, 10f), new Color(0.38f, 0.31f, 0.28f));
            SceneKit.CreateBlock("Cover_West", new Vector3(-CoverOffset, 0.75f, 0f),
                new Vector3(1f, 1.5f, 10f), new Color(0.38f, 0.31f, 0.28f));

            // Four corners, facing the middle. The server assigns them round-robin
            // (sorted by name) in NetBootstrap's connection approval.
            SceneKit.CreateSpawnPoint("Spawn_A", new Vector3(-SpawnCorner, 0f, -SpawnCorner), Vector3.zero);
            SceneKit.CreateSpawnPoint("Spawn_B", new Vector3(SpawnCorner, 0f, -SpawnCorner), Vector3.zero);
            SceneKit.CreateSpawnPoint("Spawn_C", new Vector3(-SpawnCorner, 0f, SpawnCorner), Vector3.zero);
            SceneKit.CreateSpawnPoint("Spawn_D", new Vector3(SpawnCorner, 0f, SpawnCorner), Vector3.zero);

            // The runtime font has no CJK glyphs: keep generated hints in English.
            SceneKit.CreateHintText("WASD move | RMB look | LMB shoot");
        }

        // ---------------------------------------------------------------- prefabs

        /// <summary>
        /// The bullet: a small collider-less sphere moved only by the server.
        /// Server authority on <c>SyncedTransform</c> is what makes the flight identical
        /// on every machine.
        /// </summary>
        private static GameObject EnsureProjectilePrefab()
        {
            GameObject existing = SceneKit.LoadPrefab(ProjectilePrefabPath);
            if (IsProjectilePrefabValid(existing)) return existing;

            GameObject root = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            root.name = "Projectile";
            root.transform.localScale = new Vector3(0.25f, 0.25f, 0.25f);

            Collider collider = root.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);

            root.AddComponent<NetworkObject>();

            SyncedTransform synced = root.AddComponent<SyncedTransform>();
            SetAuthority(synced, MPAuthority.Server);
            SceneKit.SetSerialized(synced, "label", "projectile");

            Projectile projectile = root.AddComponent<Projectile>();
            SceneKit.AddAudioOneShot(root, InteractAudio.Shot(), AudioOneShot.Trigger.OnEnable, "shot");
            SceneKit.SetSerialized(projectile, "label", "projectile");
            SceneKit.SetSerialized(projectile, "hitRadius", 0.8f);
            SceneKit.SetSerialized(projectile, "bodyCenterHeight", 1f);

            return SceneKit.SavePrefab(root, ProjectilePrefabPath);
        }

        /// <summary>
        /// The arena fighter: classic player blocks plus the two combat blocks. The body
        /// capsule is visual only; hits are tested by distance, not by a collider.
        /// </summary>
        private static GameObject EnsurePlayerCombatPrefab(GameObject projectilePrefab)
        {
            GameObject existing = SceneKit.LoadPrefab(PlayerCombatPrefabPath);
            if (IsPlayerCombatPrefabValid(existing)) return existing;

            GameObject root = new GameObject("PlayerCombat");
            root.AddComponent<NetworkObject>();

            SyncedTransform synced = root.AddComponent<SyncedTransform>();
            SetAuthority(synced, MPAuthority.Owner);
            SceneKit.SetSerialized(synced, "label", "player");

            root.AddComponent<StateHash>();

            Health health = root.AddComponent<Health>();
            SceneKit.SetSerialized(health, "label", "player");
            SceneKit.SetSerialized(health, "maxHealth", 100);

            root.AddComponent<HealthHUD>();

            SimplePlayerMover mover = root.AddComponent<SimplePlayerMover>();

            PlayerShooter shooter = root.AddComponent<PlayerShooter>();
            SceneKit.SetSerialized(shooter, "label", "player");
            SceneKit.SetSerialized(shooter, "projectilePrefab", projectilePrefab);
            SceneKit.SetSerialized(shooter, "fireInterval", 0.35f);
            SceneKit.SetSerialized(shooter, "damage", 25);
            SceneKit.SetSerialized(shooter, "muzzleHeight", 1.2f);

            KillScore killScore = root.AddComponent<KillScore>();
            SceneKit.SetSerialized(killScore, "label", "player");
            SceneKit.SetSerialized(killScore, "health", health);

            PlayerRespawn respawn = root.AddComponent<PlayerRespawn>();
            SceneKit.SetSerialized(respawn, "health", health);
            SceneKit.SetSerialized(respawn, "respawnSeconds", 3f);

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            Collider bodyCollider = body.GetComponent<Collider>();
            if (bodyCollider != null) Object.DestroyImmediate(bodyCollider);
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 1f, 0f);
            SceneKit.SetSerialized(respawn, "visual", body.transform);

            GameObject cameraGo = new GameObject("PlayerCamera");
            cameraGo.transform.SetParent(root.transform, false);
            cameraGo.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            Camera playerCamera = cameraGo.AddComponent<Camera>();
            cameraGo.AddComponent<AudioListener>();
            playerCamera.enabled = false;
            SceneKit.SetSerialized(mover, "playerCamera", playerCamera);

            return SceneKit.SavePrefab(root, PlayerCombatPrefabPath);
        }

        /// <summary>
        /// A four-slot scoreboard. It is spawned by the server from NetConfig, not placed
        /// in the scene, so a late joiner receives the current scores with the spawn.
        /// Slot index equals client id: host 0, clients 1..3.
        /// </summary>
        private static GameObject EnsureScoreboard4Prefab()
        {
            GameObject existing = SceneKit.LoadPrefab(Scoreboard4PrefabPath);
            Scoreboard existingBoard = existing != null ? existing.GetComponent<Scoreboard>() : null;
            if (existingBoard != null && ReadInt(existingBoard, "slotCount") == 4) return existing;

            GameObject root = new GameObject("Scoreboard_4P");
            root.AddComponent<NetworkObject>();

            Scoreboard scoreboard = root.AddComponent<Scoreboard>();
            SceneKit.SetSerialized(scoreboard, "label", "scoreboard");
            SceneKit.SetSerialized(scoreboard, "slotCount", 4);
            SetStringArray(scoreboard, "slotNames", new string[] { "P1", "P2", "P3", "P4" });

            return SceneKit.SavePrefab(root, Scoreboard4PrefabPath);
        }

        // ---------------------------------------------------------------- validation

        private static bool IsProjectilePrefabValid(GameObject prefab)
        {
            if (prefab == null) return false;

            SyncedTransform synced = prefab.GetComponent<SyncedTransform>();
            return prefab.GetComponent<NetworkObject>() != null
                   && synced != null
                   && synced.Authority == MPAuthority.Server
                   && prefab.GetComponent<Projectile>() != null
                   && prefab.GetComponent<Collider>() == null;
        }

        private static bool IsPlayerCombatPrefabValid(GameObject prefab)
        {
            if (prefab == null) return false;

            SyncedTransform synced = prefab.GetComponent<SyncedTransform>();
            SimplePlayerMover mover = prefab.GetComponent<SimplePlayerMover>();
            PlayerShooter shooter = prefab.GetComponent<PlayerShooter>();
            PlayerRespawn respawn = prefab.GetComponent<PlayerRespawn>();

            return prefab.GetComponent<NetworkObject>() != null
                   && synced != null && synced.Authority == MPAuthority.Owner
                   && prefab.GetComponent<StateHash>() != null
                   && prefab.GetComponent<Health>() != null
                   && prefab.GetComponent<HealthHUD>() != null
                   && mover != null && HasObjectReference(mover, "playerCamera")
                   && shooter != null && HasObjectReference(shooter, "projectilePrefab")
                   && respawn != null && HasObjectReference(respawn, "health") && HasObjectReference(respawn, "visual")
                   && prefab.transform.Find("Body") != null
                   && prefab.transform.Find("PlayerCamera") != null;
        }

        // ---------------------------------------------------------------- serialized helpers

        /// <summary>
        /// <c>MPAuthority.Server</c> is enum index 0, so it cannot be set through
        /// <see cref="SceneKit.SetSerialized"/> (no enum case); set it explicitly.
        /// </summary>
        private static void SetAuthority(SyncedTransform synced, MPAuthority authority)
        {
            SerializedObject serialized = new SerializedObject(synced);
            SerializedProperty property = serialized.FindProperty("authority");
            if (property == null)
            {
                MPLog.Warn("SETUP_FIELD_MISSING", ("target", "SyncedTransform"), ("field", "authority"));
                return;
            }

            property.enumValueIndex = (int)authority;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary><c>string[]</c> fields are not UnityEngine.Object[], so SceneKit's array helper cannot set them.</summary>
        private static void SetStringArray(Object target, string field, string[] values)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null)
            {
                MPLog.Warn("SETUP_FIELD_MISSING", ("target", target.name), ("field", field));
                return;
            }

            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).stringValue = values[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static bool HasObjectReference(Object target, string field)
        {
            if (target == null) return false;

            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            return property != null && property.objectReferenceValue != null;
        }

        private static int ReadInt(Object target, string field)
        {
            if (target == null) return 0;

            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            return property != null ? property.intValue : 0;
        }
    }
}
