using System.Collections.Generic;
using System.IO;
using MPBlocks.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace MPBlocks.Editor
{
    /// <summary>
    /// Adds UI fields that were introduced after a scene was generated.
    ///
    /// Scenes are only regenerated when they are missing something the setup script knows
    /// about; a scene made earlier keeps its layout, which is what students want — but it also
    /// means a later addition (like the name field) is missing from it. This tool inserts the
    /// missing pieces into existing scenes without rebuilding them.
    ///
    /// Menu: MPBlocks > Upgrade Session UI
    /// </summary>
    public static class SessionUIUpgrader
    {
        [MenuItem("MPBlocks/Upgrade Session UI (Open Scene)")]
        public static void UpgradeOpenScene()
        {
            var ui = Object.FindObjectOfType<NetSessionUI>();
            if (ui == null)
            {
                Debug.LogWarning("[MP] WARN UPGRADE_NO_UI the open scene has no NetSessionUI");
                return;
            }

            if (!Upgrade(ui))
            {
                Debug.Log("[MP] INFO UPGRADE_NOTHING_TO_DO the session UI is already current");
                return;
            }

            EditorSceneManager.MarkSceneDirty(ui.gameObject.scene);
            Debug.Log("[MP] INFO UPGRADE_SESSION_UI scene=" + ui.gameObject.scene.name);
        }

        /// <summary>
        /// Upgrades the currently open scene only. Safe to call from another menu item:
        /// it never opens or saves any other scene.
        /// </summary>
        public static bool TryUpgradeActiveScene()
        {
            var ui = Object.FindObjectOfType<NetSessionUI>();
            if (ui == null) return false;
            if (!Upgrade(ui)) return false;

            EditorSceneManager.MarkSceneDirty(ui.gameObject.scene);
            Debug.Log("[MP] INFO UPGRADE_SESSION_UI scene=" + ui.gameObject.scene.name);
            return true;
        }

        [MenuItem("MPBlocks/Upgrade Session UI (All Scenes)")]
        public static void UpgradeAllScenes()
        {
            var paths = ScenesWithSessionUI();
            if (paths.Count == 0)
            {
                Debug.Log("[MP] INFO UPGRADE_NO_SCENES no scene contains a SessionCanvas");
                return;
            }

            var current = EditorSceneManager.GetActiveScene().path;
            int changed = 0;

            foreach (var path in paths)
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                var ui = Object.FindObjectOfType<NetSessionUI>();
                if (ui == null) continue;

                if (!Upgrade(ui)) continue;

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                changed++;
                Debug.Log("[MP] INFO UPGRADE_SESSION_UI scene=" + path);
            }

            if (!string.IsNullOrEmpty(current) && File.Exists(current))
            {
                EditorSceneManager.OpenScene(current, OpenSceneMode.Single);
            }

            Debug.Log($"[MP] INFO UPGRADE_DONE changed={changed} scanned={paths.Count}");
        }

        /// <summary>Returns true when something was added.</summary>
        public static bool Upgrade(NetSessionUI ui)
        {
            var so = new SerializedObject(ui);
            var nameProp = so.FindProperty("nameInput");

            if (nameProp == null)
            {
                Debug.LogWarning("[MP] WARN UPGRADE_FIELD_MISSING field=nameInput");
                return false;
            }

            if (nameProp.objectReferenceValue != null) return false;

            var addressProp = so.FindProperty("addressInput");
            var address = addressProp != null ? addressProp.objectReferenceValue as InputField : null;

            // Put the name above the address/join row, inside the same panel.
            Transform panel = null;
            int insertIndex = -1;

            if (address != null)
            {
                var joinRow = address.transform.parent;
                panel = joinRow != null ? joinRow.parent : null;
                insertIndex = joinRow != null ? joinRow.GetSiblingIndex() : -1;
            }

            if (panel == null)
            {
                var named = ui.transform.Find("SessionPanel");
                panel = named != null ? named : ui.transform;
            }

            var field = SessionUIBuilder.CreateInputField(panel, "NameInput", "Your name");

            if (insertIndex >= 0 && insertIndex < panel.childCount - 1)
            {
                field.transform.SetSiblingIndex(insertIndex);
            }

            nameProp.objectReferenceValue = field;
            so.ApplyModifiedPropertiesWithoutUndo();

            return true;
        }

        private static List<string> ScenesWithSessionUI()
        {
            var result = new List<string>();

            foreach (var guid in AssetDatabase.FindAssets("t:Scene"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;

                try
                {
                    if (File.ReadAllText(path).Contains("SessionCanvas")) result.Add(path);
                }
                catch
                {
                    // ignored
                }
            }

            result.Sort();
            return result;
        }
    }
}
