using MPBlocks.Core;
using MPBlocks.Gameplay;
using MPBlocks.Sync;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace MPBlocks.Editor
{
    /// <summary>
    /// Builds the co-op demo content that turns the bootstrap scene into a playable
    /// template: ground and light, two levers, a door that needs both, and an enemy that
    /// awards a point when killed.
    ///
    /// Everything is generated so the scene stays reproducible instead of being a binary
    /// artefact nobody can review.
    /// </summary>
    public static class DemoSceneBuilder
    {
        public static void Build(NetConfig config)
        {
            BuildGround();
            BuildLight();

            // Both levers sit within reach of the spawn point so a scripted run
            // (-mp-interact-demo) can pull them without any player movement.
            var leverA = BuildLever("Lever_A", new Vector3(2.6f, 0f, 0f));
            var leverB = BuildLever("Lever_B", new Vector3(-2.6f, 0f, 0f));

            BuildDoor(leverA, leverB);
            BuildEnemy();

            var crate = EnsureCratePrefab(config);
            BuildSpawner(crate);
            BuildPhone();
            BuildRoleManager();
            BuildSpawnPoints();
            BuildTeamManager();
            BuildSeerOnlyProp();
            CharacterSelectSetup.Apply(config);

            Debug.Log("[MP] INFO SETUP_DEMO_BUILT");
        }

        private static void BuildGround()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(4f, 1f, 4f);

            var renderer = ground.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial.color = new Color(0.22f, 0.24f, 0.26f);
            }
        }

        private static void BuildLight()
        {
            var go = new GameObject("Directional Light");
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.shadows = LightShadows.Soft;
        }

        private static Interactable BuildLever(string name, Vector3 position)
        {
            var root = SceneKit.CreateLever(name, position, 4f, new Color(0.95f, 0.72f, 0.20f));
            return root.GetComponent<Interactable>();
        }

        private static void BuildDoor(Interactable leverA, Interactable leverB)
        {
            var root = new GameObject("Door");
            root.transform.position = new Vector3(0f, 0f, 6f);
            root.AddComponent<NetworkObject>();

            var isOpen = root.AddComponent<SyncedBool>();
            Set(isOpen, "label", "DoorOpen");

            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "Panel";
            panel.transform.SetParent(root.transform, false);
            panel.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            panel.transform.localScale = new Vector3(3.2f, 3.2f, 0.3f);

            var renderer = panel.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial.color = new Color(0.40f, 0.45f, 0.55f);
            }

            var door = root.AddComponent<CoopDoor>();
            Set(door, "isOpen", isOpen);
            Set(door, "doorPanel", panel.transform);
            Set(door, "requiredLevers", 2);
            SetArray(door, "levers", new Object[] { leverA, leverB });
        }

        private static void BuildEnemy()
        {
            var root = new GameObject("Enemy");
            root.transform.position = new Vector3(0f, 0f, 3f);
            root.AddComponent<NetworkObject>();

            var health = root.AddComponent<Health>();
            Set(health, "label", "enemy");
            Set(health, "maxHealth", 100);

            var interactable = root.AddComponent<Interactable>();

            SceneKit.AddInteractSound(root, root.name);
            Set(interactable, "label", "Enemy");
            Set(interactable, "maxDistance", 4f);

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 1f, 0f);

            var renderer = body.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial.color = new Color(0.72f, 0.25f, 0.28f);
            }

            var damage = root.AddComponent<DamageOnInteract>();
            Set(damage, "interactable", interactable);
            Set(damage, "health", health);
            Set(damage, "damage", 20);
            Set(damage, "scoreSlot", 0);
            Set(damage, "visual", body.transform);
        }

        private const string CratePrefabPath = "Assets/Prefabs/Crate.prefab";

        /// <summary>
        /// The thing the Spawner produces: a plain networked cube. Groups replace the prefab
        /// with whatever their game spawns.
        /// </summary>
        /// <summary>
        /// The pickup demo: one "phone" that changes owner. Ownership transfer is the
        /// capability the other blocks cannot express.
        /// </summary>
        /// <summary>Hands out private roles to players; rotates them when asked.</summary>
        /// <summary>Splits players into public teams.</summary>
        /// <summary>
        /// An asymmetric-prop demo: a glowing marker that only the player with role 1 can see.
        /// It is a plain child visual, never a NetworkObject.
        /// </summary>
        private static void BuildSeerOnlyProp()
        {
            var root = new GameObject("SeerMarker");
            root.transform.position = new Vector3(0f, 1.2f, -2.2f);

            var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(0.45f, 0.45f, 0.45f);

            var collider = body.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);

            var renderer = body.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial.color = new Color(0.95f, 0.35f, 0.85f);
            }

            var show = root.AddComponent<ShowForRole>();
            Set(show, "label", "seer-marker");
            var so = new SerializedObject(show);
            var prop = so.FindProperty("visibleForRoles");
            if (prop != null)
            {
                prop.arraySize = 1;
                prop.GetArrayElementAtIndex(0).intValue = 1;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <summary>
        /// Four spawn points in a ring. Without them every player spawns at the world origin,
        /// on top of everyone else — which looks like "the other player is missing".
        /// </summary>
        private static void BuildSpawnPoints()
        {
            const int count = 4;
            const float radius = 3f;

            for (int i = 0; i < count; i++)
            {
                float angle = i * Mathf.PI * 2f / count;
                var point = new GameObject("SpawnPoint_" + (i + 1));
                point.transform.position = new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius);
                point.transform.rotation = Quaternion.LookRotation(-point.transform.position);
                point.AddComponent<PlayerSpawnPoint>();
            }
        }

        private static void BuildTeamManager()
        {
            var root = new GameObject("TeamManager");
            root.transform.position = new Vector3(0f, 0f, -9f);
            root.AddComponent<NetworkObject>();
            root.AddComponent<TeamManager>();
        }

        private static void BuildRoleManager()
        {
            var root = new GameObject("RoleManager");
            root.transform.position = new Vector3(0f, 0f, -6f);
            root.AddComponent<NetworkObject>();
            root.AddComponent<RoleManager>();
        }

        private static void BuildPhone()
        {
            var root = new GameObject("Phone");
            root.transform.position = new Vector3(0f, 0.2f, 1.4f);
            root.AddComponent<NetworkObject>();

            var synced = root.AddComponent<SyncedTransform>();
            Set(synced, "label", "phone");

            var interactable = root.AddComponent<Interactable>();

            SceneKit.AddInteractSound(root, root.name);
            Set(interactable, "label", "Phone");
            Set(interactable, "maxDistance", 4f);

            var pickable = root.AddComponent<Pickable>();
            Set(pickable, "label", "phone");
            Set(pickable, "requestInteractable", interactable);

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(0.35f, 0.12f, 0.65f);

            var renderer = body.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial.color = new Color(0.25f, 0.45f, 0.85f);
            }
        }

        private static GameObject EnsureCratePrefab(NetConfig config)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CratePrefabPath);
            if (prefab == null)
            {
                var root = GameObject.CreatePrimitive(PrimitiveType.Cube);
                root.name = "Crate";
                root.transform.localScale = new Vector3(0.9f, 0.9f, 0.9f);
                root.AddComponent<NetworkObject>();

                var renderer = root.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.sharedMaterial.color = new Color(0.35f, 0.65f, 0.40f);
                }

                prefab = PrefabUtility.SaveAsPrefabAsset(root, CratePrefabPath);
                Object.DestroyImmediate(root);
                Debug.Log("[MP] INFO SETUP_CRATE_PREFAB path=" + CratePrefabPath);
            }

            // Register it so NGO will accept runtime spawns of this prefab.
            var so = new SerializedObject(config);
            var list = so.FindProperty("spawnablePrefabs");
            if (list != null)
            {
                bool present = false;
                for (int i = 0; i < list.arraySize; i++)
                {
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue == prefab) present = true;
                }

                if (!present)
                {
                    list.arraySize++;
                    list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = prefab;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(config);
                    Debug.Log("[MP] INFO SETUP_SPAWNABLE_REGISTERED prefab=" + prefab.name);
                }
            }

            return prefab;
        }

        private static void BuildSpawner(GameObject crate)
        {
            var root = new GameObject("Spawner");
            root.transform.position = new Vector3(0f, 0f, 12f);
            root.AddComponent<NetworkObject>();

            var spawner = root.AddComponent<Spawner>();
            Set(spawner, "prefab", crate);
            Set(spawner, "interval", 3f);
            Set(spawner, "maxAlive", 6);
            Set(spawner, "radius", 6f);
        }

        private static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogWarning($"[MP] WARN SETUP_FIELD_MISSING target={target.GetType().Name} field={field}");
                return;
            }

            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Set(Object target, string field, int value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogWarning($"[MP] WARN SETUP_FIELD_MISSING target={target.GetType().Name} field={field}");
                return;
            }

            prop.intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Set(Object target, string field, float value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogWarning($"[MP] WARN SETUP_FIELD_MISSING target={target.GetType().Name} field={field}");
                return;
            }

            prop.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Set(Object target, string field, string value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogWarning($"[MP] WARN SETUP_FIELD_MISSING target={target.GetType().Name} field={field}");
                return;
            }

            prop.stringValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetArray(Object target, string field, Object[] values)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogWarning($"[MP] WARN SETUP_FIELD_MISSING target={target.GetType().Name} field={field}");
                return;
            }

            prop.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
