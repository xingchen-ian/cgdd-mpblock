using System.IO;
using MPBlocks.Core;
using MPBlocks.Diagnostics;
using MPBlocks.Gameplay;
using MPBlocks.Sync;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MPBlocks.Editor
{
    /// <summary>
    /// One-time project scaffolding for the block package.
    ///
    /// Creates Assets/Settings/NetConfig.asset and Assets/Scenes/Bootstrap.unity
    /// (containing a real NetworkManager + UnityTransport + NetBootstrap), then
    /// registers the scene in Build Settings.
    ///
    /// Runs automatically when the recorded setup version is behind, so it is a
    /// no-op after the first run. Also available from the MPBlocks menu.
    /// </summary>
    public static class ProjectSetup
    {
        /// <summary>Bump when the generated scene layout changes.</summary>
        public const int SetupVersion = 14;

        public const string SettingsFolder = "Assets/Settings";
        public const string ScenesFolder = "Assets/Scenes";
        public const string ConfigPath = SettingsFolder + "/NetConfig.asset";
        public const string ScenePath = ScenesFolder + "/Bootstrap.unity";
        public const string VersionPath = SettingsFolder + "/.mpblocks-setup";
        public const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";
        public const string RigidbodyPlayerPrefabPath = "Assets/Prefabs/PlayerRigidbody.prefab";
        public const string ScoreboardPrefabPath = "Assets/Prefabs/Scoreboard.prefab";

        [MenuItem("MPBlocks/Setup Bootstrap Scene")]
        public static void SetupFromMenu() => Setup(force: true);

        /// <summary>
        /// Re-runs the scaffolding without rebuilding the Bootstrap scene: regenerates the
        /// shared prefabs and fills in missing fields on every NetConfig asset (for example
        /// the Rigidbody player prefab on game configs created before it existed).
        /// </summary>
        [MenuItem("MPBlocks/Refresh Project Setup")]
        public static void RefreshFromMenu()
        {
            Setup(force: false);
            AssetDatabase.SaveAssets();
            Debug.Log("[MP] INFO SETUP_REFRESHED");
        }

        [InitializeOnLoadMethod]
        private static void AutoSetup()
        {
            // Player settings are safe to touch during initialization and must be
            // correct even in batch builds, where delayCall never runs.
            EnsurePlayerSettings();

            if (Application.isBatchMode) return;

            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;

                if (ReadSetupVersion() >= SetupVersion && File.Exists(ScenePath)) return;
                Setup(force: false);
            };
        }

        public static void Setup(bool force)
        {
            EnsureFolder("Assets", "Settings");
            EnsureFolder("Assets", "Scenes");

            var config = AssetDatabase.LoadAssetAtPath<NetConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<NetConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
                AssetDatabase.SaveAssets();
                Debug.Log("[MP] INFO SETUP_CONFIG_CREATED path=" + ConfigPath);
            }

            EnsurePlayerPrefab(config);
            EnsureRigidbodyPlayerPrefab(config);
            EnsureScoreboardPrefab(config);
            AssignMissingRigidbodyPrefabs();

            var needsScene = force || !File.Exists(ScenePath) || !SceneHasNetworkManager()
                            || !SceneHasSessionUI() || !SceneHasScoreboardHUD() || !SceneHasDemoContent()
                            || !SceneHasSpawner() || !SceneHasLatencySimulator() || !SceneHasNetDiag();
            if (needsScene)
            {
                BuildScene(config);
            }

            AddSceneToBuildSettings();
            WriteSetupVersion(SetupVersion);
            AssetDatabase.SaveAssets();
        }

        private static void BuildScene(NetConfig config)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // A real NetworkManager must exist in the scene before any session starts.
            // Creating one at runtime is too late: NGO registers message handlers during
            // initialization, and a late instance makes clients fail to send
            // ConnectionRequestMessage (KeyNotFoundException at connect time).
            var nmGo = new GameObject("NetworkManager");
            nmGo.AddComponent<NetworkManager>();
            nmGo.AddComponent<UnityTransport>();

            var bootGo = new GameObject("Net");
            var bootstrap = bootGo.AddComponent<NetBootstrap>();
            var so = new SerializedObject(bootstrap);
            var prop = so.FindProperty("config");
            if (prop != null)
            {
                prop.objectReferenceValue = config;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            bootGo.AddComponent<LatencySimulator>();
            bootGo.AddComponent<NetDiag>();

            SessionUIBuilder.Build(bootstrap);
            DemoSceneBuilder.Build(config);

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[MP] INFO SETUP_SCENE_BUILT path=" + ScenePath);
        }

        /// <summary>
        /// Builds Assets/Prefabs/Player.prefab: a capsule body, a camera that only the
        /// owning client enables, and the sync/move blocks. Groups are expected to
        /// replace this with their own avatar.
        /// </summary>
        private static void EnsurePlayerPrefab(NetConfig config)
        {
            EnsureFolder("Assets", "Prefabs");

            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (existing != null)
            {
                bool needsUpgrade = existing.GetComponent<StateHash>() == null
                                    || existing.GetComponent<Health>() == null
                                    || existing.GetComponent<HealthHUD>() == null
                                    || existing.GetComponent<PlayerInteractor>() == null
                                    || existing.GetComponent<PlayerAppearance>() == null;

                if (needsUpgrade)
                {
                    var contents = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
                    if (contents.GetComponent<StateHash>() == null) contents.AddComponent<StateHash>();
                    if (contents.GetComponent<Health>() == null) contents.AddComponent<Health>();
                    if (contents.GetComponent<HealthHUD>() == null) contents.AddComponent<HealthHUD>();
                    if (contents.GetComponent<PlayerInteractor>() == null) contents.AddComponent<PlayerInteractor>();
                    if (contents.GetComponent<PlayerAppearance>() == null) contents.AddComponent<PlayerAppearance>();
                    PrefabUtility.SaveAsPrefabAsset(contents, PlayerPrefabPath);
                    PrefabUtility.UnloadPrefabContents(contents);
                    Debug.Log("[MP] INFO SETUP_PLAYER_PREFAB_UPGRADED path=" + PlayerPrefabPath);
                    existing = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
                }

                AssignPlayerPrefab(config, existing);
                return;
            }

            var root = new GameObject("Player");
            root.AddComponent<NetworkObject>();
            var synced = root.AddComponent<SyncedTransform>();
            synced.Label = "player";
            root.AddComponent<StateHash>();
            root.AddComponent<Health>();
            root.AddComponent<HealthHUD>();
            root.AddComponent<PlayerInteractor>();
            root.AddComponent<PlayerAppearance>();
            var mover = root.AddComponent<SimplePlayerMover>();

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            var collider = body.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 1f, 0f);

            var camGo = new GameObject("PlayerCamera");
            camGo.transform.SetParent(root.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            var cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            cam.enabled = false;

            var so = new SerializedObject(mover);
            var prop = so.FindProperty("playerCamera");
            if (prop != null)
            {
                prop.objectReferenceValue = cam;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            Object.DestroyImmediate(root);

            AssignPlayerPrefab(config, prefab);
            Debug.Log("[MP] INFO SETUP_PLAYER_PREFAB path=" + PlayerPrefabPath);
        }

        /// <summary>
        /// Builds Assets/Prefabs/PlayerRigidbody.prefab: the same player blocks as
        /// Player.prefab, but with a Rigidbody + CapsuleCollider and
        /// <see cref="RigidbodyPlayerMover"/> instead of <see cref="SimplePlayerMover"/>.
        /// Selecting <c>NetConfig.Player Movement = Rigidbody</c> spawns this prefab.
        /// Regenerated only when it is missing or structurally incomplete.
        /// </summary>
        private static void EnsureRigidbodyPlayerPrefab(NetConfig config)
        {
            EnsureFolder("Assets", "Prefabs");

            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(RigidbodyPlayerPrefabPath);
            if (existing != null)
            {
                bool needsUpgrade = existing.GetComponent<RigidbodyPlayerMover>() == null
                                    || existing.GetComponent<Rigidbody>() == null
                                    || existing.GetComponent<CapsuleCollider>() == null
                                    || existing.GetComponent<StateHash>() == null
                                    || existing.GetComponent<Health>() == null
                                    || existing.GetComponent<HealthHUD>() == null
                                    || existing.GetComponent<PlayerInteractor>() == null
                                    || existing.GetComponent<PlayerAppearance>() == null
                                    || existing.GetComponent<Rigidbody>().constraints != RigidbodyConstraints.FreezeRotation;

                if (!needsUpgrade)
                {
                    AssignRigidbodyPlayerPrefab(config, existing);
                    return;
                }

                var contents = PrefabUtility.LoadPrefabContents(RigidbodyPlayerPrefabPath);
                if (contents.GetComponent<StateHash>() == null) contents.AddComponent<StateHash>();
                if (contents.GetComponent<Health>() == null) contents.AddComponent<Health>();
                if (contents.GetComponent<HealthHUD>() == null) contents.AddComponent<HealthHUD>();
                if (contents.GetComponent<PlayerInteractor>() == null) contents.AddComponent<PlayerInteractor>();
                if (contents.GetComponent<PlayerAppearance>() == null) contents.AddComponent<PlayerAppearance>();

                var contentBody = contents.GetComponent<Rigidbody>();
                if (contentBody == null) contentBody = contents.AddComponent<Rigidbody>();
                ConfigureRigidbody(contentBody);

                var contentCapsule = contents.GetComponent<CapsuleCollider>();
                if (contentCapsule == null) contentCapsule = contents.AddComponent<CapsuleCollider>();
                ConfigureCapsule(contentCapsule);

                if (contents.GetComponent<RigidbodyPlayerMover>() == null) contents.AddComponent<RigidbodyPlayerMover>();

                PrefabUtility.SaveAsPrefabAsset(contents, RigidbodyPlayerPrefabPath);
                PrefabUtility.UnloadPrefabContents(contents);
                Debug.Log("[MP] INFO SETUP_RIGIDBODY_PLAYER_PREFAB_UPGRADED path=" + RigidbodyPlayerPrefabPath);
                existing = AssetDatabase.LoadAssetAtPath<GameObject>(RigidbodyPlayerPrefabPath);
                AssignRigidbodyPlayerPrefab(config, existing);
                return;
            }

            var root = new GameObject("PlayerRigidbody");
            root.AddComponent<NetworkObject>();

            var synced = root.AddComponent<SyncedTransform>();
            synced.Label = "player";

            root.AddComponent<StateHash>();
            root.AddComponent<Health>();
            root.AddComponent<HealthHUD>();
            root.AddComponent<PlayerInteractor>();
            root.AddComponent<PlayerAppearance>();

            var body = root.AddComponent<Rigidbody>();
            ConfigureRigidbody(body);

            var capsule = root.AddComponent<CapsuleCollider>();
            ConfigureCapsule(capsule);

            var mover = root.AddComponent<RigidbodyPlayerMover>();

            // Visual body only: no collider here, the CapsuleCollider above is the physics shape.
            var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "Body";
            var visualCollider = visual.GetComponent<Collider>();
            if (visualCollider != null) Object.DestroyImmediate(visualCollider);
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = new Vector3(0f, 1f, 0f);

            var camGo = new GameObject("PlayerCamera");
            camGo.transform.SetParent(root.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            var cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            cam.enabled = false;

            var so = new SerializedObject(mover);
            var prop = so.FindProperty("playerCamera");
            if (prop != null)
            {
                prop.objectReferenceValue = cam;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, RigidbodyPlayerPrefabPath);
            Object.DestroyImmediate(root);

            AssignRigidbodyPlayerPrefab(config, prefab);
            Debug.Log("[MP] INFO SETUP_RIGIDBODY_PLAYER_PREFAB path=" + RigidbodyPlayerPrefabPath);
        }

        /// <summary>
        /// Builds Assets/Prefabs/Scoreboard.prefab: a bare NetworkObject carrying the
        /// Scoreboard block. The server spawns it from NetConfig, so no scene needs to
        /// place one.
        /// </summary>
        private static void EnsureScoreboardPrefab(NetConfig config)
        {
            EnsureFolder("Assets", "Prefabs");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ScoreboardPrefabPath);
            if (prefab == null)
            {
                var root = new GameObject("Scoreboard");
                root.AddComponent<NetworkObject>();
                root.AddComponent<Scoreboard>();

                prefab = PrefabUtility.SaveAsPrefabAsset(root, ScoreboardPrefabPath);
                Object.DestroyImmediate(root);
                Debug.Log("[MP] INFO SETUP_SCOREBOARD_PREFAB path=" + ScoreboardPrefabPath);
            }

            var so = new SerializedObject(config);
            var prop = so.FindProperty("scoreboardPrefab");
            if (prop != null)
            {
                prop.objectReferenceValue = prefab;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(config);
            }
        }

        private static void AssignPlayerPrefab(NetConfig config, GameObject prefab)
        {
            if (prefab == null) return;

            var so = new SerializedObject(config);
            var prop = so.FindProperty("playerPrefab");
            if (prop == null) return;

            prop.objectReferenceValue = prefab;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
        }

        private static void AssignRigidbodyPlayerPrefab(NetConfig config, GameObject prefab)
        {
            if (prefab == null) return;

            var so = new SerializedObject(config);
            var prop = so.FindProperty("rigidbodyPlayerPrefab");
            if (prop == null) return;

            prop.objectReferenceValue = prefab;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
        }

        /// <summary>
        /// Game configs created before the Rigidbody player existed have an empty
        /// rigidbodyPlayerPrefab, so selecting Player Movement = Rigidbody would silently
        /// fall back to the Transform prefab. Fill the field on every NetConfig asset that
        /// does not point at a prefab yet; configs that deliberately use another prefab are
        /// left alone.
        /// </summary>
        private static void AssignMissingRigidbodyPrefabs()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigidbodyPlayerPrefabPath);
            if (prefab == null) return;

            foreach (var guid in AssetDatabase.FindAssets("t:NetConfig"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var config = AssetDatabase.LoadAssetAtPath<NetConfig>(path);
                if (config == null || config.RigidbodyPlayerPrefab != null) continue;

                var so = new SerializedObject(config);
                var prop = so.FindProperty("rigidbodyPlayerPrefab");
                if (prop == null) continue;

                prop.objectReferenceValue = prefab;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(config);
                Debug.Log("[MP] INFO CONFIG_RIGIDBODY_PREFAB path=" + path);
            }
        }

        /// <summary>Physics settings the sample Rigidbody player expects; see recipes/rigidbody-sync.md.</summary>
        private static void ConfigureRigidbody(Rigidbody body)
        {
            body.mass = 1f;
            body.drag = 0f;
            body.angularDrag = 0.05f;
            body.useGravity = true;
            body.isKinematic = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.Continuous;

            // All rotation is script-driven (yaw). Leaving Y free lets collisions inject
            // angular velocity into the capsule, which shakes the child camera.
            body.constraints = RigidbodyConstraints.FreezeRotation;
        }

        /// <summary>One capsule per body; the visual capsule child has no collider.</summary>
        private static void ConfigureCapsule(CapsuleCollider capsule)
        {
            capsule.height = 2f;
            capsule.radius = 0.5f;
            capsule.center = new Vector3(0f, 1f, 0f);
        }

        private static bool SceneHasSessionUI()
        {
            if (!File.Exists(ScenePath)) return false;

            var guid = AssetDatabase.AssetPathToGUID("Packages/com.cgdd.mpblocks/Runtime/Core/NetSessionUI.cs");
            if (string.IsNullOrEmpty(guid)) return false;

            return File.ReadAllText(ScenePath).Contains("guid: " + guid);
        }

        private static bool SceneHasNetDiag()
        {
            if (!File.Exists(ScenePath)) return false;

            var guid = AssetDatabase.AssetPathToGUID("Packages/com.cgdd.mpblocks/Runtime/Diagnostics/NetDiag.cs");
            if (string.IsNullOrEmpty(guid)) return false;

            return File.ReadAllText(ScenePath).Contains("guid: " + guid);
        }

        private static bool SceneHasSpawner()
        {
            if (!File.Exists(ScenePath)) return false;
            return File.ReadAllText(ScenePath).Contains("Spawner");
        }

        private static bool SceneHasLatencySimulator()
        {
            if (!File.Exists(ScenePath)) return false;

            var guid = AssetDatabase.AssetPathToGUID("Packages/com.cgdd.mpblocks/Runtime/Diagnostics/LatencySimulator.cs");
            if (string.IsNullOrEmpty(guid)) return false;

            return File.ReadAllText(ScenePath).Contains("guid: " + guid);
        }

        private static bool SceneHasDemoContent()
        {
            if (!File.Exists(ScenePath)) return false;
            return File.ReadAllText(ScenePath).Contains("Lever_A");
        }

        private static bool SceneHasScoreboardHUD()
        {
            if (!File.Exists(ScenePath)) return false;

            var guid = AssetDatabase.AssetPathToGUID("Packages/com.cgdd.mpblocks/Runtime/Gameplay/ScoreboardHUD.cs");
            if (string.IsNullOrEmpty(guid)) return false;

            return File.ReadAllText(ScenePath).Contains("guid: " + guid);
        }

        private static bool SceneHasNetworkManager()
        {
            if (!File.Exists(ScenePath)) return false;

            var scriptPath = "Packages/com.unity.netcode.gameobjects/Runtime/Core/NetworkManager.cs";
            var guid = AssetDatabase.AssetPathToGUID(scriptPath);
            if (string.IsNullOrEmpty(guid)) return false;

            var contents = File.ReadAllText(ScenePath);
            return contents.Contains("guid: " + guid);
        }

        /// <summary>
        /// A project scaffolded from disk gets its product name from the folder name,
        /// which makes the built executable confusingly called "unity". Fix that once,
        /// and leave a name the group may have chosen alone.
        /// </summary>
        public static void EnsurePlayerSettings()
        {
            var projectFolder = new DirectoryInfo(Application.dataPath).Parent?.Name ?? "unity";
            var bad = string.IsNullOrWhiteSpace(PlayerSettings.productName)
                      || PlayerSettings.productName == projectFolder
                      || PlayerSettings.productName == "New Unity Project";

            if (bad)
            {
                PlayerSettings.productName = "MPGame";
                Debug.Log("[MP] INFO SETUP_PRODUCT_NAME productName=MPGame");
            }

            if (string.IsNullOrWhiteSpace(PlayerSettings.companyName)
                || PlayerSettings.companyName == "DefaultCompany")
            {
                PlayerSettings.companyName = "CGDD";
            }
        }

        private static void AddSceneToBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes;
            foreach (var s in scenes)
            {
                if (s.path == ScenePath) return;
            }

            var updated = new EditorBuildSettingsScene[scenes.Length + 1];
            updated[0] = new EditorBuildSettingsScene(ScenePath, true);
            for (int i = 0; i < scenes.Length; i++)
            {
                updated[i + 1] = scenes[i];
            }

            EditorBuildSettings.scenes = updated;
            Debug.Log("[MP] INFO SETUP_SCENE_REGISTERED path=" + ScenePath);
        }

        private static int ReadSetupVersion()
        {
            try
            {
                if (!File.Exists(VersionPath)) return 0;
                return int.TryParse(File.ReadAllText(VersionPath).Trim(), out var v) ? v : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static void WriteSetupVersion(int version)
        {
            try
            {
                File.WriteAllText(VersionPath, version.ToString());
                AssetDatabase.ImportAsset(VersionPath);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[MP] WARN SETUP_VERSION_WRITE_FAILED " + e.Message);
            }
        }

        private static void EnsureFolder(string parent, string child)
        {
            var full = parent + "/" + child;
            if (AssetDatabase.IsValidFolder(full)) return;
            AssetDatabase.CreateFolder(parent, child);
        }
    }
}
