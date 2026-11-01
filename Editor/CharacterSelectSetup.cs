using MPBlocks.Core;
using MPBlocks.Gameplay;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MPBlocks.Editor
{
    /// <summary>
    /// One-click setup for the fixed character-selection feature:
    ///
    /// 1. creates Assets/Settings/CharacterRoster.asset with four example characters
    ///    (a group replaces the names/colours or adds visual prefabs),
    /// 2. assigns the roster to the scene's NetConfig,
    /// 3. adds a CharacterSelect object (NetworkObject + CharacterSelect) to the scene,
    /// 4. ensures the configured player prefabs carry PlayerAppearance.
    ///
    /// After this, players see "Choose your character" when the session starts. The server
    /// assigns unique characters by default; the panel lets each player change theirs.
    /// </summary>
    public static class CharacterSelectSetup
    {
        public const string RosterPath = "Assets/Settings/CharacterRoster.asset";

        [MenuItem("MPBlocks/Add Character Selection to Scene")]
        public static void AddToScene()
        {
            var bootstrap = Object.FindObjectOfType<NetBootstrap>(true);
            if (bootstrap == null || bootstrap.Config == null)
            {
                EditorUtility.DisplayDialog("No Net session in this scene",
                    "Add the MPBlocks infrastructure first (MPBlocks > New Game Scene...), " +
                    "or assign a NetConfig to the scene's NetBootstrap.", "OK");
                return;
            }

            var config = bootstrap.Config;
            var roster = EnsureRoster(config);
            AssignConfigRoster(config, roster);

            var selector = EnsureSelector(roster);

            EnsureAppearance(config.PlayerPrefab);
            EnsureAppearance(config.TransformPlayerPrefab);
            EnsureAppearance(config.RigidbodyPlayerPrefab);

            EditorSceneManager.MarkSceneDirty(selector.gameObject.scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = selector.gameObject;

            Debug.Log("[MP] INFO CHARACTER_SELECT_ADDED roster=" + RosterPath + " scene=" + selector.gameObject.scene.name);
        }

        private static CharacterRoster EnsureRoster(NetConfig config)
        {
            var roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>(RosterPath);
            if (roster != null) return roster;

            if (!AssetDatabase.IsValidFolder("Assets/Settings"))
            {
                AssetDatabase.CreateFolder("Assets", "Settings");
            }

            roster = ScriptableObject.CreateInstance<CharacterRoster>();
            AssetDatabase.CreateAsset(roster, RosterPath);

            var so = new SerializedObject(roster);
            var list = so.FindProperty("characters");
            if (list != null)
            {
                // One character per player slot: New Game Scene already chose the maximum
                // player count, so the roster starts with exactly that many entries.
                int count = Mathf.Clamp(config != null ? config.MaxPlayers : 4, 2, 8);

                string[] names = { "Red", "Blue", "Green", "Yellow", "Purple", "Cyan", "Orange", "Pink" };
                Color[] colors =
                {
                    new Color(0.85f, 0.25f, 0.25f),
                    new Color(0.25f, 0.45f, 0.95f),
                    new Color(0.25f, 0.80f, 0.35f),
                    new Color(0.95f, 0.80f, 0.20f),
                    new Color(0.60f, 0.30f, 0.85f),
                    new Color(0.25f, 0.80f, 0.85f),
                    new Color(0.95f, 0.55f, 0.20f),
                    new Color(0.95f, 0.45f, 0.75f),
                };

                list.arraySize = count;
                for (int i = 0; i < count; i++)
                {
                    var element = list.GetArrayElementAtIndex(i);
                    var displayName = element.FindPropertyRelative("displayName");
                    var color = element.FindPropertyRelative("color");
                    if (displayName != null) displayName.stringValue = names[i];
                    if (color != null) color.colorValue = colors[i];
                }

                so.ApplyModifiedPropertiesWithoutUndo();
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[MP] INFO CHARACTER_ROSTER_CREATED path=" + RosterPath);
            return roster;
        }

        private static void AssignConfigRoster(NetConfig config, CharacterRoster roster)
        {
            var so = new SerializedObject(config);
            var prop = so.FindProperty("characterRoster");
            if (prop == null)
            {
                Debug.LogWarning("[MP] WARN SETUP_FIELD_MISSING target=NetConfig field=characterRoster");
                return;
            }

            prop.objectReferenceValue = roster;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
        }

        private static CharacterSelect EnsureSelector(CharacterRoster roster)
        {
            var existing = Object.FindObjectOfType<CharacterSelect>(true);
            if (existing != null) return existing;

            var go = new GameObject("CharacterSelect");
            go.AddComponent<NetworkObject>();
            var selector = go.AddComponent<CharacterSelect>();

            var so = new SerializedObject(selector);
            var rosterProp = so.FindProperty("roster");
            if (rosterProp != null) rosterProp.objectReferenceValue = roster;
            so.ApplyModifiedPropertiesWithoutUndo();

            return selector;
        }

        private static void EnsureAppearance(GameObject prefab)
        {
            if (prefab == null) return;

            var assetPath = AssetDatabase.GetAssetPath(prefab);
            if (string.IsNullOrEmpty(assetPath) || prefab.GetComponent<PlayerAppearance>() != null) return;

            var contents = PrefabUtility.LoadPrefabContents(assetPath);
            if (contents.GetComponent<PlayerAppearance>() == null)
            {
                contents.AddComponent<PlayerAppearance>();
            }

            PrefabUtility.SaveAsPrefabAsset(contents, assetPath);
            PrefabUtility.UnloadPrefabContents(contents);
            Debug.Log("[MP] INFO PLAYER_APPEARANCE_ADDED prefab=" + assetPath);
        }
    }
}
