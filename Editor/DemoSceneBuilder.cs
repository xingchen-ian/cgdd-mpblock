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
            var root = new GameObject(name);
            root.transform.position = position;
            root.AddComponent<NetworkObject>();

            var interactable = root.AddComponent<Interactable>();
            Set(interactable, "label", name);
            Set(interactable, "maxDistance", 4f);

            var handle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            handle.name = "Handle";
            handle.transform.SetParent(root.transform, false);
            handle.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            handle.transform.localScale = new Vector3(0.35f, 1.2f, 0.35f);

            var renderer = handle.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial.color = new Color(0.95f, 0.72f, 0.20f);
            }

            return interactable;
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
