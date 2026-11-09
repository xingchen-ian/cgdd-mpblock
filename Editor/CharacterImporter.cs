using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MPBlocks.Core;
using MPBlocks.Gameplay;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MPBlocks.Editor
{
    /// <summary>
    /// Turns a drop-in folder of Mixamo characters into selectable characters.
    ///
    /// Student workflow:
    ///   1. download a character (and its animations) from Mixamo as FBX;
    ///   2. drop them into one folder per character under Assets/Character,
    ///      for example Assets/Character/Rat/;
    ///   3. run MPBlocks > Characters > Import from Assets/Character;
    ///   4. press Play and pick the character on the selection panel.
    ///
    /// For every folder that contains a skinned model the tool builds a visual prefab (model +
    /// Animator + a locomotion controller made from that folder's idle/walking/running clips),
    /// then writes it into the CharacterRoster that CharacterSelect reads.
    ///
    /// Nothing here is required: a hand-built prefab assigned to the roster works exactly the
    /// same. This only removes the repetitive part.
    /// </summary>
    public static class CharacterImporter
    {
        public const string SourceFolder = "Assets/Character";
        public const string GeneratedFolder = SourceFolder + "/_Generated";
        public const string RosterPath = CharacterSelectSetup.RosterPath;

        private static readonly string[] ModelKeywords = { "nonpbr", "model", "character", "mesh", "rig" };

        [MenuItem("MPBlocks/Characters/Import from Assets/Character", false, 33)]
        public static void ImportFromMenu()
        {
            var result = Import(silent: false);

            EditorUtility.DisplayDialog("MPBlocks characters",
                $"Characters found: {result.Found}\n" +
                $"Visual prefabs written: {result.PrefabsWritten}\n" +
                $"Roster entries updated: {result.RosterUpdated}\n\n" +
                (result.Notes.Count > 0 ? string.Join("\n", result.Notes.Take(8)) : "All good."),
                "OK");
        }

        /// <summary>Headless entry point: same work, no dialogs.</summary>
        public static void ImportForCI()
        {
            var result = Import(silent: true);
            Debug.Log($"[MP] INFO CHARACTER_IMPORT_DONE found={result.Found} prefabs={result.PrefabsWritten} roster={result.RosterUpdated}");
            EditorApplication.Exit(result.Found > 0 ? 0 : 1);
        }

        public sealed class Result
        {
            public int Found;
            public int PrefabsWritten;
            public int RosterUpdated;
            public readonly List<string> Notes = new List<string>();
        }

        public static Result Import(bool silent)
        {
            var result = new Result();

            if (!AssetDatabase.IsValidFolder(SourceFolder))
            {
                result.Notes.Add($"No {SourceFolder} folder — create one folder per character, e.g. {SourceFolder}/Rat.");
                Debug.LogWarning("[MP] WARN CHARACTER_IMPORT_NO_FOLDER path=" + SourceFolder);
                return result;
            }

            var roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>(RosterPath);
            if (roster == null)
            {
                result.Notes.Add("No CharacterRoster.asset yet — run MPBlocks > Add Character Selection to Scene first.");
                Debug.LogWarning("[MP] WARN CHARACTER_IMPORT_NO_ROSTER path=" + RosterPath);
                return result;
            }

            EnsureFolder(GeneratedFolder);

            var characters = DiscoverCharacters(result);
            result.Found = characters.Count;

            if (characters.Count == 0)
            {
                result.Notes.Add($"No skinned model found under {SourceFolder}.");
                Debug.LogWarning("[MP] WARN CHARACTER_IMPORT_NO_MODEL path=" + SourceFolder);
                return result;
            }

            var entries = new List<CharacterEntry>();

            foreach (var character in characters)
            {
                var prefab = BuildVisualPrefab(character, result);
                if (prefab == null) continue;

                result.PrefabsWritten++;
                entries.Add(new CharacterEntry(character.Name, prefab));
            }

            result.RosterUpdated = WriteRoster(roster, entries, result);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[MP] INFO CHARACTER_IMPORTED found={result.Found} prefabs={result.PrefabsWritten} roster={result.RosterUpdated}");
            return result;
        }

        // ---------------------------------------------------------------- discovery

        private sealed class CharacterSource
        {
            public string Name;
            public string Folder;
            public string ModelPath;
            public List<string> AnimationFiles = new List<string>();
        }

        private sealed class CharacterEntry
        {
            public readonly string Name;
            public readonly GameObject Prefab;

            public CharacterEntry(string name, GameObject prefab)
            {
                Name = name;
                Prefab = prefab;
            }
        }

        private static List<CharacterSource> DiscoverCharacters(Result result)
        {
            var found = new List<CharacterSource>();

            var modelGuids = AssetDatabase.FindAssets("t:Model", new[] { SourceFolder });
            var byFolder = new Dictionary<string, CharacterSource>(StringComparer.OrdinalIgnoreCase);

            foreach (var guid in modelGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith(SourceFolder + "/", StringComparison.OrdinalIgnoreCase)) continue;
                if (path.Contains("/_Generated/")) continue;

                var folder = Path.GetDirectoryName(path)?.Replace('\\', '/');
                if (string.IsNullOrEmpty(folder)) continue;

                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                bool isCharacter = model != null && model.GetComponentInChildren<SkinnedMeshRenderer>(true) != null;

                if (!byFolder.TryGetValue(folder, out var source))
                {
                    source = new CharacterSource
                    {
                        Folder = folder,
                        Name = Path.GetFileName(folder),
                    };
                    byFolder[folder] = source;
                    found.Add(source);
                }

                if (isCharacter)
                {
                    // Several skinned models in one folder: the first one wins.
                    if (source.ModelPath == null) source.ModelPath = path;
                }
                else
                {
                    source.AnimationFiles.Add(path);
                }
            }

            // Keep only folders that actually contain a character model.
            found.RemoveAll(source =>
            {
                if (source.ModelPath != null) return false;
                result.Notes.Add($"{source.Name}: no skinned model in this folder, skipped.");
                return true;
            });

            return found;
        }

        // ---------------------------------------------------------------- prefab

        private static GameObject BuildVisualPrefab(CharacterSource character, Result result)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(character.ModelPath);
            if (model == null) return null;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            if (instance == null) return null;

            instance.name = character.Name + "_Visual";
            instance.transform.position = Vector3.zero;
            instance.transform.rotation = Quaternion.identity;

            StripGameplay(instance);

            var animator = instance.GetComponent<Animator>();
            if (animator == null) animator = instance.AddComponent<Animator>();

            var controller = BuildController(character, result);
            if (controller != null) animator.runtimeAnimatorController = controller;

            // Movement is driven by the toolkit; root motion would fight the network transform.
            animator.applyRootMotion = false;

            var prefabPath = $"{GeneratedFolder}/{character.Name}_Visual.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
            UnityEngine.Object.DestroyImmediate(instance);

            if (prefab == null)
            {
                result.Notes.Add($"{character.Name}: could not save {prefabPath}.");
                return null;
            }

            return prefab;
        }

        /// <summary>A character visual must not carry physics, cameras or lights — those belong to the player object.</summary>
        private static void StripGameplay(GameObject root)
        {
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }

            foreach (var camera in root.GetComponentsInChildren<Camera>(true))
            {
                UnityEngine.Object.DestroyImmediate(camera);
            }

            foreach (var light in root.GetComponentsInChildren<Light>(true))
            {
                UnityEngine.Object.DestroyImmediate(light);
            }

            foreach (var body in root.GetComponentsInChildren<Rigidbody>(true))
            {
                UnityEngine.Object.DestroyImmediate(body);
            }
        }

        // ---------------------------------------------------------------- controller

        private static AnimatorController BuildController(CharacterSource character, Result result)
        {
            var clips = new Dictionary<string, AnimationClip>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in character.AnimationFiles)
            {
                var clip = LoadFirstClip(file);
                if (clip == null) continue;

                var key = ClassifyAnimation(Path.GetFileNameWithoutExtension(file));
                if (key == null || clips.ContainsKey(key)) continue;

                clips[key] = clip;
                SetLoop(file, key);
            }

            if (!clips.ContainsKey("idle") && !clips.ContainsKey("walk"))
            {
                result.Notes.Add($"{character.Name}: no idle/walking animation found, the character will be static.");
                Debug.LogWarning($"[MP] WARN CHARACTER_NO_LOCOMOTION name={character.Name} folder={character.Folder}");
                return null;
            }

            var controllerPath = $"{GeneratedFolder}/{character.Name}.controller";
            AssetDatabase.DeleteAsset(controllerPath);

            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);

            var stateMachine = controller.layers[0].stateMachine;

            var idle = stateMachine.AddState("Idle");
            idle.motion = clips.ContainsKey("idle") ? clips["idle"] : null;
            stateMachine.defaultState = idle;

            AnimatorState walk = null;
            if (clips.ContainsKey("walk"))
            {
                walk = stateMachine.AddState("Walk");
                walk.motion = clips["walk"];
                AddTransition(idle, walk, 0.1f);
                AddTransition(walk, idle, -0.1f);
            }

            if (clips.ContainsKey("run"))
            {
                var run = stateMachine.AddState("Run");
                run.motion = clips["run"];
                if (walk != null)
                {
                    AddTransition(walk, run, 0.6f);
                    AddTransition(run, walk, -0.6f);
                }
            }

            if (clips.ContainsKey("jump"))
            {
                var jump = stateMachine.AddState("Jump");
                jump.motion = clips["jump"];

                var enter = stateMachine.AddAnyStateTransition(jump);
                enter.hasExitTime = false;
                enter.duration = 0.08f;
                enter.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");

                var exit = jump.AddTransition(idle);
                exit.hasExitTime = false;
                exit.duration = 0.1f;
                exit.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
            }

            Debug.Log($"[MP] INFO CHARACTER_CONTROLLER name={character.Name} clips={string.Join("/", clips.Keys)}");
            return controller;
        }

        private static void AddTransition(AnimatorState from, AnimatorState to, float speedThreshold)
        {
            var transition = from.AddTransition(to);
            transition.hasExitTime = false;
            transition.duration = 0.12f;
            transition.AddCondition(
                speedThreshold > 0f ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less,
                Mathf.Abs(speedThreshold),
                "Speed");
        }

        private static AnimationClip LoadFirstClip(string fbxPath)
        {
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            {
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__", StringComparison.Ordinal))
                {
                    return clip;
                }
            }

            return null;
        }

        private static string ClassifyAnimation(string fileName)
        {
            var name = fileName.ToLowerInvariant();

            if (name.Contains("idle") || name.Contains("stand")) return "idle";
            if (name.Contains("walk")) return "walk";
            if (name.Contains("run") || name.Contains("jog")) return "run";
            if (name.Contains("jump")) return "jump";

            return null;
        }

        /// <summary>Locomotion clips must loop or the character freezes at the last frame.</summary>
        private static void SetLoop(string fbxPath, string key)
        {
            if (key != "idle" && key != "walk" && key != "run") return;

            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null) return;

            var clips = importer.defaultClipAnimations;
            if (clips == null || clips.Length == 0) return;

            bool changed = false;
            foreach (var clip in clips)
            {
                if (clip.loopTime) continue;
                clip.loopTime = true;
                changed = true;
            }

            if (!changed) return;

            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        // ---------------------------------------------------------------- roster

        private static int WriteRoster(CharacterRoster roster, List<CharacterEntry> entries, Result result)
        {
            var so = new SerializedObject(roster);
            var array = so.FindProperty("characters");
            if (array == null)
            {
                result.Notes.Add("CharacterRoster has no 'characters' array.");
                return 0;
            }

            int updated = 0;
            var used = new HashSet<int>();

            foreach (var entry in entries)
            {
                int index = FindEntry(array, entry.Name, used);
                if (index < 0) index = FindPlaceholder(array, used);

                if (index < 0)
                {
                    index = array.arraySize;
                    array.arraySize = index + 1;
                }

                used.Add(index);

                var element = array.GetArrayElementAtIndex(index);
                var displayName = element.FindPropertyRelative("displayName");
                if (displayName != null) displayName.stringValue = entry.Name;

                var visualPrefab = element.FindPropertyRelative("visualPrefab");
                if (visualPrefab != null) visualPrefab.objectReferenceValue = entry.Prefab;

                updated++;
            }

            // Trim empty leftover slots so the panel does not show blanks.
            for (int i = array.arraySize - 1; i >= 0; i--)
            {
                var element = array.GetArrayElementAtIndex(i);
                var name = element.FindPropertyRelative("displayName");
                var prefab = element.FindPropertyRelative("visualPrefab");

                if (prefab.objectReferenceValue != null) continue;
                if (!string.IsNullOrEmpty(name.stringValue)) continue;

                array.DeleteArrayElementAtIndex(i);
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(roster);
            return updated;
        }

        /// <summary>
        /// An entry with no visual prefab is a colour-only placeholder written by
        /// CharacterSelectSetup. Reuse those slots before appending, so importing three
        /// characters does not leave four "Red/Blue/Green/Yellow" shells in the panel.
        /// </summary>
        private static int FindPlaceholder(SerializedProperty array, HashSet<int> used)
        {
            for (int i = 0; i < array.arraySize; i++)
            {
                if (used.Contains(i)) continue;

                var prefab = array.GetArrayElementAtIndex(i).FindPropertyRelative("visualPrefab");
                if (prefab != null && prefab.objectReferenceValue == null) return i;
            }

            return -1;
        }

        private static int FindEntry(SerializedProperty array, string name, HashSet<int> used)
        {
            for (int i = 0; i < array.arraySize; i++)
            {
                if (used.Contains(i)) continue;

                var displayName = array.GetArrayElementAtIndex(i).FindPropertyRelative("displayName");
                if (displayName != null && string.Equals(displayName.stringValue, name, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent)) return;

            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
