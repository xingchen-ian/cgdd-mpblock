using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MPBlocks.Core;
using MPBlocks.Diagnostics;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MPBlocks.Editor
{
    /// <summary>
    /// Everything a generated scene needs, and nothing a generated scene may decide on its
    /// own. Template builders (Template1Builder, Template2Builder, Template3Builder) and the
    /// "New Game Scene" window all go through this class so the four infrastructure objects,
    /// the per-game <see cref="NetConfig"/> and the Build Settings bookkeeping exist in one
    /// place instead of being copy-pasted.
    ///
    /// Scenes are always generated from C#. Unity scene/prefab YAML is never edited by hand.
    /// </summary>
    public static class SceneKit
    {
        public const string ScenesFolder = "Assets/Scenes";
        public const string TemplatesFolder = "Assets/Scenes/Templates";
        public const string SettingsFolder = "Assets/Settings";
        public const string PrefabsFolder = "Assets/Prefabs";

        public const string BootstrapScenePath = ScenesFolder + "/Bootstrap.unity";
        public const string PlayerPrefabPath = PrefabsFolder + "/Player.prefab";
        public const string ScoreboardPrefabPath = PrefabsFolder + "/Scoreboard.prefab";

        private static bool _scaffolded;

        // ---------------------------------------------------------------- folders

        public static void EnsureFolders()
        {
            EnsureFolder(ScenesFolder);
            EnsureFolder(SettingsFolder);
            EnsureFolder(PrefabsFolder);
            EnsureFolder(TemplatesFolder);
        }

        public static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            path = path.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(path)) return;

            var parent = Path.GetDirectoryName(path);
            var leaf = Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) return;

            EnsureFolder(parent);
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, leaf);
            }
        }

        // ---------------------------------------------------------------- names

        /// <summary>File-system safe name for scenes/prefabs. Keeps letters, digits, '_' and '-'.</summary>
        public static string SafeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Game";

            var sb = new StringBuilder();
            foreach (var c in name.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-') sb.Append(c);
                else if (char.IsWhiteSpace(c)) sb.Append('_');
            }

            var result = sb.ToString().Trim('_', '-');
            return string.IsNullOrEmpty(result) ? "Game" : result;
        }

        /// <summary>Session id fragment: lower-case, ascii-ish, no spaces.</summary>
        public static string Slug(string name)
        {
            var safe = SafeFileName(name).ToLowerInvariant();
            var sb = new StringBuilder();
            foreach (var c in safe)
            {
                sb.Append(char.IsLetterOrDigit(c) ? c : '-');
            }

            var slug = sb.ToString().Trim('-');
            while (slug.Contains("--")) slug = slug.Replace("--", "-");
            return string.IsNullOrEmpty(slug) ? "game" : slug;
        }

        /// <summary>
        /// A game id that does not collide with another game started from this window on
        /// the same machine. Two different games on one LAN must not share it; two tables
        /// on separate routers do not care.
        /// </summary>
        public static string NewGameId(string name)
        {
            return Slug(name) + "-" + Guid.NewGuid().ToString("N").Substring(0, 4);
        }

        public static string GameScenePath(string name) => ScenesFolder + "/" + SafeFileName(name) + ".unity";

        public static string GameConfigPath(string name) => SettingsFolder + "/NetConfig_" + SafeFileName(name) + ".asset";

        public static string TemplateScenePath(string fileName) => TemplatesFolder + "/" + fileName + ".unity";

        public static string TemplateConfigPath(string fileName) => SettingsFolder + "/NetConfig_" + fileName + ".asset";

        // ---------------------------------------------------------------- project assets

        /// <summary>
        /// Makes sure Assets/Settings/NetConfig.asset, Assets/Prefabs/Player.prefab and
        /// Assets/Prefabs/Scoreboard.prefab exist. Templates reuse those shared prefabs;
        /// only template 3 needs its own combat player prefab.
        /// </summary>
        public static void EnsureProjectScaffolding()
        {
            if (_scaffolded) return;

            EnsureFolders();
            ProjectSetup.Setup(force: false);
            AssetDatabase.SaveAssets();
            _scaffolded = true;
        }

        public static GameObject PlayerPrefab()
        {
            EnsureProjectScaffolding();
            return AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        }

        /// <summary>Rigidbody-based sample player (RigidbodyPlayerMover).</summary>
        public static GameObject RigidbodyPlayerPrefab()
        {
            EnsureProjectScaffolding();
            return AssetDatabase.LoadAssetAtPath<GameObject>(ProjectSetup.RigidbodyPlayerPrefabPath);
        }

        public static GameObject ScoreboardPrefab()
        {
            EnsureProjectScaffolding();
            return AssetDatabase.LoadAssetAtPath<GameObject>(ScoreboardPrefabPath);
        }

        // ---------------------------------------------------------------- scene

        public static Scene NewEmptyScene()
        {
            return EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        public static void SaveScene(Scene scene, string path)
        {
            var folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) EnsureFolder(folder);

            EditorSceneManager.SaveScene(scene, path);
            AssetDatabase.SaveAssets();
            Debug.Log("[MP] INFO SCENE_SAVED path=" + path);
        }

        public static void OpenScene(string path)
        {
            if (!File.Exists(path))
            {
                Debug.LogWarning("[MP] WARN SCENE_MISSING path=" + path);
                return;
            }

            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var asset = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
            if (asset != null) Selection.activeObject = asset;
        }

        /// <summary>
        /// The four infrastructure objects every playable multiplayer scene needs:
        /// a real NetworkManager (never created at runtime), the Net object that reads
        /// NetConfig and the command line, the session UI, and the diagnostic overlays.
        /// </summary>
        public static NetBootstrap CreateInfrastructure(NetConfig config)
        {
            var managerGo = new GameObject("NetworkManager");
            managerGo.AddComponent<NetworkManager>();
            managerGo.AddComponent<UnityTransport>();

            var netGo = new GameObject("Net");
            var bootstrap = netGo.AddComponent<NetBootstrap>();
            var so = new SerializedObject(bootstrap);
            var prop = so.FindProperty("config");
            if (prop != null)
            {
                prop.objectReferenceValue = config;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            netGo.AddComponent<LatencySimulator>();
            netGo.AddComponent<NetDiag>();

            SessionUIBuilder.Build(bootstrap);

            MPLog.Info("SCENE_INFRASTRUCTURE", ("config", config != null ? config.name : "none"));
            return bootstrap;
        }

        // ---------------------------------------------------------------- world

        public static GameObject CreateGround(Vector3 scale, Color color)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = scale;
            SetColor(ground, color);
            return ground;
        }

        public static GameObject CreateDirectionalLight()
        {
            var go = new GameObject("Directional Light");
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.shadows = LightShadows.Soft;
            return go;
        }

        /// <summary>Visible, collider-less cube used for walls, cover, levers, pads.</summary>
        public static GameObject CreateBlock(string name, Vector3 position, Vector3 size, Color color, bool keepCollider = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = size;

            if (!keepCollider)
            {
                var collider = go.GetComponent<Collider>();
                if (collider != null) Object.DestroyImmediate(collider);
            }

            SetColor(go, color);
            return go;
        }

        /// <summary>Replaces the shared default material so one object's colour cannot leak.</summary>
        public static void SetColor(GameObject go, Color color)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return;

            var source = renderer.sharedMaterial;
            var material = source != null ? new Material(source) : new Material(Shader.Find("Standard"));
            material.name = "MP_" + go.name;
            material.color = color;
            renderer.sharedMaterial = material;
        }

        public static PlayerSpawnPoint CreateSpawnPoint(string name, Vector3 position, Vector3 lookAt)
        {
            var go = new GameObject(name);
            go.transform.position = position;

            var direction = new Vector3(lookAt.x - position.x, 0f, lookAt.z - position.z);
            if (direction.sqrMagnitude > 0.001f)
            {
                go.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            }

            var point = go.AddComponent<PlayerSpawnPoint>();
            return point;
        }

        /// <summary>A bottom-centre hint in English: the built-in runtime font has no CJK glyphs.</summary>
        public static Text CreateHintText(string text)
        {
            var canvasGo = new GameObject("HintCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var go = new GameObject("Hint", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(canvasGo.transform, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 28f);
            rect.sizeDelta = new Vector2(1400f, 40f);

            var label = go.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (label.font == null) label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            label.fontSize = 20;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color(1f, 1f, 1f, 0.85f);
            label.text = text;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;

            return label;
        }

        // ---------------------------------------------------------------- configuration

        public static NetConfig CreateNetConfig(GameSpec spec, GameObject playerPrefab, GameObject scoreboardPrefab,
            params GameObject[] spawnablePrefabs)
        {
            EnsureFolders();

            var config = AssetDatabase.LoadAssetAtPath<NetConfig>(spec.ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<NetConfig>();
                AssetDatabase.CreateAsset(config, spec.ConfigPath);
                Debug.Log("[MP] INFO CONFIG_CREATED path=" + spec.ConfigPath);
            }

            config.SetGameId(spec.GameId);
            config.SetPort(spec.Port);
            config.SetMaxPlayers(spec.MaxPlayers);
            config.SetStartMode(MPNetMode.HostAndPlay);
            config.SetDefaultAddress("127.0.0.1");
            config.SetPlayerPrefab(playerPrefab);
            config.SetRigidbodyPlayerPrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>(ProjectSetup.RigidbodyPlayerPrefabPath));
            config.SetScoreboardPrefab(scoreboardPrefab);
            config.SetSpawnablePrefabs(spawnablePrefabs);

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            Debug.Log("[MP] INFO CONFIG_WRITTEN path=" + spec.ConfigPath + " gameId=" + spec.GameId);
            return config;
        }

        // ---------------------------------------------------------------- prefabs

        public static GameObject SavePrefab(GameObject root, string prefabPath)
        {
            var folder = Path.GetDirectoryName(prefabPath);
            if (!string.IsNullOrEmpty(folder)) EnsureFolder(folder);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);

            if (prefab == null) Debug.LogError("[MP] ERROR PREFAB_SAVE_FAILED path=" + prefabPath);
            else Debug.Log("[MP] INFO PREFAB_SAVED path=" + prefabPath);
            return prefab;
        }

        public static GameObject LoadPrefab(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path);

        // ---------------------------------------------------------------- build settings

        /// <summary>Adds or updates one Build Settings entry, preserving the others.</summary>
        public static void RegisterScene(string scenePath, bool enabled)
        {
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            var index = list.FindIndex(s => s.path == scenePath);
            var entry = new EditorBuildSettingsScene(scenePath, enabled);

            if (index >= 0) list[index] = entry;
            else list.Add(entry);

            EditorBuildSettings.scenes = list.ToArray();
            Debug.Log("[MP] INFO BUILD_SCENE_REGISTERED path=" + scenePath + " enabled=" + enabled);
        }

        /// <summary>
        /// Makes the given scene the one that ships: first entry, enabled. The template
        /// Bootstrap scene keeps its entry but is disabled — removing the entry would make
        /// ProjectSetup insert it again at position 0 on the next build.
        /// </summary>
        public static void MakeGameSceneFirst(string scenePath)
        {
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            list.RemoveAll(s => s.path == scenePath);

            var bootstrap = list.FindIndex(s => s.path == BootstrapScenePath);
            if (bootstrap >= 0) list[bootstrap] = new EditorBuildSettingsScene(BootstrapScenePath, false);
            else list.Insert(0, new EditorBuildSettingsScene(BootstrapScenePath, false));

            list.Insert(0, new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = list.ToArray();

            Debug.Log("[MP] INFO BUILD_SCENE_ACTIVE path=" + scenePath);
        }

        public static GameObject CreateLever(string name, Vector3 position, float maxDistance, Color handleColor)
        {
            var root = new GameObject(name);
            root.transform.position = position;
            root.AddComponent<NetworkObject>();

            var interactable = root.AddComponent<MPBlocks.Gameplay.Interactable>();
            var so = new SerializedObject(interactable);
            var label = so.FindProperty("label");
            if (label != null) label.stringValue = name;
            var distance = so.FindProperty("maxDistance");
            if (distance != null) distance.floatValue = maxDistance;
            so.ApplyModifiedPropertiesWithoutUndo();

            var handle = CreateBlock("Handle", position + new Vector3(0f, 0.6f, 0f),
                new Vector3(0.35f, 1.2f, 0.35f), handleColor);
            handle.transform.SetParent(root.transform, true);

            var state = root.AddComponent<MPBlocks.Sync.SyncedBool>();
            SetSerialized(state, "label", name + "On");

            var toggle = root.AddComponent<MPBlocks.Gameplay.InteractToggle>();
            SetSerialized(toggle, "interactable", interactable);
            SetSerialized(toggle, "state", state);
            SetSerialized(toggle, "targetRenderer", handle.GetComponent<Renderer>());
            SetSerialized(toggle, "offColor", new Color(handleColor.r * 0.35f, handleColor.g * 0.35f, handleColor.b * 0.35f, 1f));
            SetSerialized(toggle, "onColor", Color.Lerp(handleColor, Color.white, 0.35f));

            AddInteractSound(root, name);

            return root;
        }

        /// <summary>
        /// Adds the click/thud feedback to any object that has an Interactable. Kept next to
        /// the interactable it belongs to, so a lever can sound different from a phone.
        /// </summary>
        /// <summary>
        /// Adds a one-shot sound to any object. Used for deaths, hits and shots, where there is
        /// no Interactable to hang an InteractSound on.
        /// </summary>
        public static MPBlocks.Gameplay.AudioOneShot AddAudioOneShot(GameObject root, AudioClip clip,
            MPBlocks.Gameplay.AudioOneShot.Trigger trigger, string label)
        {
            if (root == null || clip == null) return null;

            // An object can need several one-shots (hit and death, for example), so match on
            // the label instead of reusing whatever component happens to be first.
            MPBlocks.Gameplay.AudioOneShot audio = null;
            foreach (var candidate in root.GetComponents<MPBlocks.Gameplay.AudioOneShot>())
            {
                if (candidate == null) continue;

                var candidateSo = new SerializedObject(candidate);
                var candidateLabel = candidateSo.FindProperty("label");
                if (candidateLabel != null && candidateLabel.stringValue == label)
                {
                    audio = candidate;
                    break;
                }
            }

            if (audio == null) audio = root.AddComponent<MPBlocks.Gameplay.AudioOneShot>();

            var so = new SerializedObject(audio);
            var clipProp = so.FindProperty("clip");
            if (clipProp != null) clipProp.objectReferenceValue = clip;
            var triggerProp = so.FindProperty("trigger");
            if (triggerProp != null) triggerProp.intValue = (int)trigger;
            var labelProp = so.FindProperty("label");
            if (labelProp != null) labelProp.stringValue = label;
            so.ApplyModifiedPropertiesWithoutUndo();

            return audio;
        }

        public static MPBlocks.Gameplay.InteractSound AddInteractSound(GameObject root, string label)
        {
            if (root == null) return null;

            var sound = root.GetComponent<MPBlocks.Gameplay.InteractSound>();
            if (sound == null) sound = root.AddComponent<MPBlocks.Gameplay.InteractSound>();

            var so = new SerializedObject(sound);
            var labelProp = so.FindProperty("label");
            if (labelProp != null) labelProp.stringValue = label;
            var approved = so.FindProperty("approvedClip");
            if (approved != null) approved.objectReferenceValue = InteractAudio.Approved();
            var denied = so.FindProperty("deniedClip");
            if (denied != null) denied.objectReferenceValue = InteractAudio.Denied();
            so.ApplyModifiedPropertiesWithoutUndo();

            return sound;
        }

        public static void SetSerialized(Object target, string field, object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogWarning("[MP] WARN SETUP_FIELD_MISSING target=" + target.name + " field=" + field);
                return;
            }

            switch (value)
            {
                case null:
                    prop.objectReferenceValue = null;
                    break;
                case Object unityObject:
                    prop.objectReferenceValue = unityObject;
                    break;
                case string text:
                    prop.stringValue = text;
                    break;
                case bool flag:
                    prop.boolValue = flag;
                    break;
                case int number:
                    prop.intValue = number;
                    break;
                case float number:
                    prop.floatValue = number;
                    break;
                case Vector3 vector:
                    prop.vector3Value = vector;
                    break;
                case Color color:
                    prop.colorValue = color;
                    break;
                default:
                    Debug.LogWarning("[MP] WARN SETUP_FIELD_UNSUPPORTED field=" + field + " type=" + value.GetType().Name);
                    return;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetSerializedArray(Object target, string field, Object[] values)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogWarning("[MP] WARN SETUP_FIELD_MISSING target=" + target.name + " field=" + field);
                return;
            }

            prop.arraySize = values?.Length ?? 0;
            for (int i = 0; i < prop.arraySize; i++)
            {
                prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    /// <summary>
    /// Everything that identifies one generated game scene: its name, its session id, its
    /// scene path and its own NetConfig asset. One spec in, one playable scene out.
    /// </summary>
    public readonly struct GameSpec
    {
        public readonly string Name;
        public readonly string GameId;
        public readonly int MaxPlayers;
        public readonly ushort Port;
        public readonly string ScenePath;
        public readonly string ConfigPath;

        public GameSpec(string name, string gameId, int maxPlayers, ushort port, string scenePath, string configPath)
        {
            Name = name;
            GameId = gameId;
            MaxPlayers = maxPlayers;
            Port = port;
            ScenePath = scenePath;
            ConfigPath = configPath;
        }

        /// <summary>A game scene created from the New Game Scene window.</summary>
        public static GameSpec ForGame(string name, int maxPlayers, ushort port)
        {
            var safe = SceneKit.SafeFileName(name);
            return new GameSpec(
                safe,
                SceneKit.NewGameId(safe),
                maxPlayers,
                port,
                SceneKit.GameScenePath(safe),
                SceneKit.GameConfigPath(safe));
        }

        /// <summary>A teaching template scene generated by the template builders.</summary>
        public static GameSpec ForTemplate(string fileName, string gameId, int maxPlayers, ushort port)
        {
            return new GameSpec(
                fileName,
                gameId,
                maxPlayers,
                port,
                SceneKit.TemplateScenePath(fileName),
                SceneKit.TemplateConfigPath(fileName));
        }
    }
}
