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
            // An explicit menu click means "regenerate", so hand edits in _Generated are replaced.
            var result = Import(silent: false, force: true);

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
            var result = Import(silent: true, force: true);
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

        public static Result Import(bool silent, bool force = false)
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
                AlignRig(character, result);
                var prefab = EnsureVisualPrefab(character, force, result);
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
                    // Several skinned models in one folder: prefer the one named after the folder
                    // ("Man.fbx" in Assets/Character/Man), otherwise the first one.
                    var stem = Path.GetFileNameWithoutExtension(path);
                    bool namedAfterFolder = string.Equals(stem, source.Name, StringComparison.OrdinalIgnoreCase);

                    if (source.ModelPath == null || (namedAfterFolder && !string.Equals(
                            Path.GetFileNameWithoutExtension(source.ModelPath), source.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        source.ModelPath = path;
                    }
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

        private static GameObject EnsureVisualPrefab(CharacterSource character, bool force, Result result)
        {
            var prefabPath = $"{GeneratedFolder}/{character.Name}_Visual.prefab";
            var controllerPath = $"{GeneratedFolder}/{character.Name}.controller";

            if (!force
                && AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null
                && AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath) != null)
            {
                return AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            }

            return BuildVisualPrefab(character, force, result);
        }

        private static GameObject BuildVisualPrefab(CharacterSource character, bool force, Result result)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(character.ModelPath);
            if (model == null) return null;

            // A plain Instantiate, not InstantiatePrefab: a nested FBX instance keeps the skeleton
            // one level below the Animator, and generic animation clips (bone paths are relative
            // to the Animator) then bind to nothing. A real copy keeps the model root = Animator.
            var instance = UnityEngine.Object.Instantiate(model);
            instance.name = character.Name + "_Visual";
            instance.transform.position = Vector3.zero;
            instance.transform.rotation = Quaternion.identity;

            StripGameplay(instance);
            FixMaterials(instance, character.Name, result);

            var animator = instance.GetComponent<Animator>();
            if (animator == null) animator = instance.GetComponentInChildren<Animator>(true);
            if (animator == null) animator = instance.AddComponent<Animator>();

            var controller = BuildController(character, force, result);
            if (controller != null) animator.runtimeAnimatorController = controller;

            // Movement is driven by the toolkit; root motion would fight the network transform.
            animator.applyRootMotion = false;

            var prefabPath = $"{GeneratedFolder}/{character.Name}_Visual.prefab";
            AssetDatabase.DeleteAsset(prefabPath);

            // Read what the log needs before the instance is destroyed.
            var animatorName = animator.gameObject.name;
            var controllerName = controller != null ? controller.name : "none";

            var prefab = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
            UnityEngine.Object.DestroyImmediate(instance);

            if (prefab == null)
            {
                result.Notes.Add($"{character.Name}: could not save {prefabPath}.");
                return null;
            }

            Debug.Log($"[MP] INFO CHARACTER_VISUAL name={character.Name} animator={animatorName} controller={controllerName}");
            return prefab;
        }

        /// <summary>
        /// Mixamo materials use the built-in "Standard" shader. In a URP project that renders as
        /// magenta, which reads as "material lost". Replace unsupported or missing materials with
        /// a lit material of the active pipeline, keeping the original colour when there is one.
        /// </summary>
        private static void FixMaterials(GameObject root, string characterName, Result result)
        {
            // GraphicsSettings.currentRenderPipeline is null during batch imports, which made
            // an earlier version think a URP project was built-in and skip the replacement.
            // Ask the shader catalogue instead.
            var urpLit = Shader.Find("Universal Render Pipeline/Lit");
            var hdrpLit = Shader.Find("HDRP/Lit");
            bool srp = urpLit != null || hdrpLit != null
                       || UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;

            var shader = urpLit != null ? urpLit : hdrpLit != null ? hdrpLit : Shader.Find("Standard");
            if (shader == null)
            {
                result.Notes.Add($"{characterName}: no usable shader found for materials.");
                return;
            }

            int replaced = 0;
            int slot = 0;

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                bool changed = false;

                for (int i = 0; i < materials.Length; i++)
                {
                    var material = materials[i];
                    if (material != null && material.shader != null
                        && material.shader.isSupported
                        && !(srp && IsBuiltInOnlyShader(material.shader)))
                    {
                        continue;
                    }

                    materials[i] = CreateMaterial(shader, material, characterName, slot++, srp);
                    changed = true;
                    replaced++;
                }

                if (changed) renderer.sharedMaterials = materials;
            }

            if (replaced > 0)
            {
                var first = root.GetComponentInChildren<Renderer>(true);
                var tint = first != null && first.sharedMaterial != null && first.sharedMaterial.HasProperty("_BaseColor")
                    ? first.sharedMaterial.GetColor("_BaseColor")
                    : Color.white;

                result.Notes.Add($"{characterName}: replaced {replaced} unsupported material(s) for {shader.name}.");
                Debug.Log($"[MP] INFO CHARACTER_MATERIAL name={characterName} replaced={replaced} " +
                          $"shader={shader.name} color=#{ColorUtility.ToHtmlStringRGB(tint)}");
            }
        }

        /// <summary>Shaders that render as magenta under URP/HDRP even when isSupported says true.</summary>
        private static bool IsBuiltInOnlyShader(Shader shader)
        {
            var name = shader.name;
            return name == "Standard" || name.StartsWith("Standard ", StringComparison.Ordinal)
                   || name.StartsWith("Legacy Shaders/", StringComparison.Ordinal)
                   || name == "Diffuse" || name == "Specular" || name == "VertexLit";
        }

        private static Material CreateMaterial(Shader shader, Material original, string characterName, int slot, bool srp)
        {
            var material = new Material(shader) { name = $"{characterName}_Mat{slot}" };

            var color = Color.white;
            if (original != null)
            {
                if (original.HasProperty("_BaseColor")) color = original.GetColor("_BaseColor");
                else if (original.HasProperty("_Color")) color = original.GetColor("_Color");
            }

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);

            if (srp && material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.2f);

            EnsureFolder(GeneratedFolder + "/Materials");
            var path = $"{GeneratedFolder}/Materials/{characterName}_Mat{slot}.mat";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(material, path);
            return material;
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

        private static AnimatorController BuildController(CharacterSource character, bool force, Result result)
        {
            var clips = new Dictionary<string, AnimationClip>(StringComparer.OrdinalIgnoreCase);

            // Several files can match the same slot ("walking.fbx" and "left strafe walking.fbx").
            // Keep the plainest one: strafes and turns are variants, not the locomotion clip.
            var best = new Dictionary<string, KeyValuePair<int, string>>();

            // A Mixamo download with skin is both the model and one clip: "Breathing Idle.fbx"
            // may be the character mesh *and* the only idle animation. Classify it too, otherwise
            // the Idle state ends up with no motion and every action returns to an empty pose.
            var clipCandidates = new List<string>(character.AnimationFiles);
            if (!string.IsNullOrEmpty(character.ModelPath)) clipCandidates.Add(character.ModelPath);

            foreach (var file in clipCandidates)
            {
                var match = ClassifyAnimation(Path.GetFileNameWithoutExtension(file));
                if (match.key == null) continue;

                if (!best.TryGetValue(match.key, out var existing) || match.priority < existing.Key)
                {
                    best[match.key] = new KeyValuePair<int, string>(match.priority, file);
                }
            }

            foreach (var pair in best)
            {
                var key = pair.Key;
                var file = pair.Value.Value;

                // Configure the importer before loading the clip: SaveAndReimport invalidates
                // clip objects that were loaded earlier.
                ConfigureClip(file, key);

                var clip = LoadFirstClip(file);
                if (clip == null)
                {
                    Debug.LogWarning($"[MP] WARN CHARACTER_CLIP_EMPTY key={key} path={Path.GetFileName(file)}");
                    continue;
                }

                clips[key] = clip;
                bool fromModel = file == character.ModelPath;
                Debug.Log($"[MP] INFO CHARACTER_CLIP_USED key={key} path={Path.GetFileName(file)} model={fromModel}");
            }

            if (!clips.ContainsKey("idle") && !clips.ContainsKey("walk"))
            {
                result.Notes.Add($"{character.Name}: no idle/walking animation found, the character will be static.");
                Debug.LogWarning($"[MP] WARN CHARACTER_NO_LOCOMOTION name={character.Name} folder={character.Folder}");
                return null;
            }

            var controllerPath = $"{GeneratedFolder}/{character.Name}.controller";
            if (!force && AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath) != null)
            {
                return AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            }

            AssetDatabase.DeleteAsset(controllerPath);

            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            controller.AddParameter("MoveZ", AnimatorControllerParameterType.Float);
            controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);

            var stateMachine = controller.layers[0].stateMachine;

            var idle = stateMachine.AddState("Idle");
            idle.motion = clips.ContainsKey("idle") ? clips["idle"] : null;
            stateMachine.defaultState = idle;

            AnimatorState walk = null;
            if (clips.ContainsKey("walk"))
            {
                walk = stateMachine.AddState("Walk");
                walk.motion = BuildDirectionalBlend(clips, controller, controllerPath);

                AddSpeedTransition(idle, walk, greater: true, threshold: 0.1f);
                AddSpeedTransition(walk, idle, greater: false, threshold: 0.1f);
            }

            if (clips.ContainsKey("run"))
            {
                var run = stateMachine.AddState("Run");
                run.motion = clips["run"];

                if (walk != null)
                {
                    // Real speed in m/s: a player moving at SimplePlayerMover.moveSpeed = 4.5
                    // runs, at about 1.5 walks.
                    AddSpeedTransition(walk, run, greater: true, threshold: 2.5f);
                    AddSpeedTransition(run, walk, greater: false, threshold: 2.5f);
                }
            }

            // One-shot actions: any other clip whose name says what it does becomes a Trigger
            // state, so "drop attack.fbx in the folder and press F" works without scripting.
            var actionMapping = new List<string>();

            var locomotionFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in best) locomotionFiles.Add(pair.Value.Value);

            foreach (var action in FindActions(character, locomotionFiles))
            {
                var clip = LoadFirstClip(action.Value);
                if (clip == null) continue;

                if (controller.parameters.All(existing => existing.name != action.Key))
                {
                    controller.AddParameter(action.Key, AnimatorControllerParameterType.Trigger);
                }

                var state = stateMachine.AddState(action.Key);
                state.motion = clip;

                var enter = stateMachine.AddAnyStateTransition(state);
                enter.hasExitTime = false;
                enter.duration = 0.05f;
                enter.canTransitionToSelf = false;
                enter.AddCondition(AnimatorConditionMode.If, 0f, action.Key);

                var exit = state.AddTransition(idle);
                exit.hasExitTime = true;
                exit.exitTime = 0.9f;
                exit.duration = 0.1f;

                actionMapping.Add($"{action.Key}={Path.GetFileNameWithoutExtension(action.Value)}");
                Debug.Log($"[MP] INFO CHARACTER_ACTION name={character.Name} action={action.Key}");
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

            var mapping = new List<string>();
            foreach (var pair in best) mapping.Add($"{pair.Key}={Path.GetFileNameWithoutExtension(pair.Value.Value)}");
            mapping.AddRange(actionMapping);

            if (!clips.ContainsKey("idle"))
            {
                Debug.LogWarning($"[MP] WARN CHARACTER_CONTROLLER_INCOMPLETE name={character.Name} " +
                                 "no idle clip was readable — re-run Import from Assets/Character once the FBX files have finished importing");
            }

            Debug.Log($"[MP] INFO CHARACTER_CONTROLLER name={character.Name} " +
                      $"clips={string.Join("/", clips.Keys)} mapping=[{string.Join(", ", mapping)}]");
            return controller;
        }

        /// <summary>
        /// 2D blend on (MoveX, MoveZ) so left/right/back movement plays the matching clip instead
        /// of the forward walk. Falls back to the forward clip for any direction without one.
        /// </summary>
        private static BlendTree BuildDirectionalBlend(Dictionary<string, AnimationClip> clips,
            AnimatorController controller, string controllerPath)
        {
            var tree = new BlendTree
            {
                name = "WalkDirectional",
                blendType = BlendTreeType.FreeformDirectional2D,
                blendParameter = "MoveX",
                blendParameterY = "MoveZ",
            };

            AssetDatabase.AddObjectToAsset(tree, controllerPath);

            var walk = clips["walk"];
            var forward = new Vector2(0f, 1f);
            var backward = new Vector2(0f, -1f);
            var left = new Vector2(-1f, 0f);
            var right = new Vector2(1f, 0f);
            var center = Vector2.zero;

            tree.AddChild(walk, center);
            tree.AddChild(walk, forward);
            tree.AddChild(clips.ContainsKey("back") ? clips["back"] : walk, backward);
            tree.AddChild(clips.ContainsKey("left") ? clips["left"] : walk, left);
            tree.AddChild(clips.ContainsKey("right") ? clips["right"] : walk, right);

            AssetDatabase.SaveAssets();
            return tree;
        }

        private static void AddSpeedTransition(AnimatorState from, AnimatorState to, bool greater, float threshold)
        {
            var transition = from.AddTransition(to);
            transition.hasExitTime = false;
            transition.duration = 0.12f;
            transition.AddCondition(
                greater ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less,
                threshold,
                "Speed");
        }

        private static readonly string[] SlotPrefixes =
        {
            "idle", "walk", "run", "jump", "left", "right", "back",
        };

        private static readonly string[] ActionPrefixes = { "act_", "action_", "a_" };

        private static readonly string[] IgnoredWords =
        {
            "turn", "pose", "tpose", "t-pose", "bind", "preview", "mixamo",
        };

        private static bool IsActionPrefix(string lowerName)
        {
            foreach (var prefix in ActionPrefixes)
            {
                if (lowerName.StartsWith(prefix, StringComparison.Ordinal)) return true;
            }

            return false;
        }

        /// <summary>
        /// Every animation clip that is not already a locomotion slot becomes a one-shot action.
        /// The old behaviour used a keyword allowlist, so a perfectly ordinary clip called
        /// "Shoot" or "Taunt" was silently dropped; a denylist of pose/turn clips is the safer
        /// default because naming an action is the student's decision, not ours.
        /// </summary>
        private static List<KeyValuePair<string, string>> FindActions(
            CharacterSource character, ICollection<string> locomotionFiles)
        {
            var actions = new List<KeyValuePair<string, string>>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in character.AnimationFiles)
            {
                if (locomotionFiles.Contains(file)) continue;

                var stem = Path.GetFileNameWithoutExtension(file);
                var lower = stem.ToLowerInvariant();

                if (IsIgnored(lower)) continue;

                // A second copy of a locomotion clip (strafe vs strafe-walking) is a variant, not
                // an action; firing it from a key would look like nothing happened.
                if (ClassifyAnimation(stem).key != null) continue;

                var name = ActionNameFor(stem);
                if (string.IsNullOrEmpty(name) || !used.Add(name)) continue;

                actions.Add(new KeyValuePair<string, string>(name, file));
            }

            return actions;
        }

        /// <summary>True when the name is exactly this word (ignoring spaces), i.e. a plain slot clip.</summary>
        private static bool IsPlain(string lowerName, string word)
        {
            return lowerName.Replace(" ", string.Empty) == word;
        }

        private static bool IsIgnored(string lowerName)
        {
            foreach (var word in IgnoredWords)
            {
                if (lowerName.Contains(word, StringComparison.Ordinal)) return true;
            }

            return false;
        }

        private static string ActionNameFor(string stem)
        {
            var lower = stem.ToLowerInvariant();

            foreach (var prefix in ActionPrefixes)
            {
                if (lower.StartsWith(prefix, StringComparison.Ordinal)) return ToPascalCase(stem.Substring(prefix.Length));
            }

            return ToPascalCase(stem);
        }

        private static string ToPascalCase(string value)
        {
            var builder = new System.Text.StringBuilder(value.Length);
            bool upper = true;

            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(upper ? char.ToUpperInvariant(c) : c);
                    upper = false;
                }
                else
                {
                    upper = true;
                }
            }

            return builder.ToString();
        }

        private static AnimationClip LoadFirstClip(string fbxPath)
        {
            var clip = FindFirstClip(fbxPath);
            if (clip != null) return clip;

            // The FBX may still be streaming in (a 28 MB Mixamo model takes a moment). Force a
            // synchronous import rather than dropping the slot: a half-imported folder used to
            // produce a controller whose Idle state had no motion.
            AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceSynchronousImport);
            return FindFirstClip(fbxPath);
        }

        private static AnimationClip FindFirstClip(string fbxPath)
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

        /// <summary>
        /// Maps a file name to a controller slot. Priority 0 is a plain clip, higher numbers are
        /// variants that are only used when nothing plainer exists. Direction is checked before
        /// "walk" so "left strafe walking.fbx" becomes the left clip, not the forward walk.
        /// </summary>
        private static (string key, int priority) ClassifyAnimation(string fileName)
        {
            var name = fileName.ToLowerInvariant().Trim();

            // Explicit prefixes always win: "walk_shoot" is a shoot clip, "idle_sit" is idle.
            foreach (var slot in SlotPrefixes)
            {
                if (name.StartsWith(slot + "_") || name.StartsWith(slot + "-"))
                {
                    return (slot, 0);
                }
            }

            // "act_", "a_" or "action_" force a one-shot action, even if the name also contains a
            // locomotion word.
            if (IsActionPrefix(name)) return (null, int.MaxValue);
            bool strafe = name.Contains("strafe");
            bool turn = name.Contains("turn");

            if (turn) return (null, int.MaxValue);

            if (name.Contains("backward") || name.Contains("backwards") || name.Contains("back"))
            {
                return ("back", strafe ? 2 : 0);
            }

            if (strafe && name.Contains("left")) return ("left", name.Contains("walking") ? 1 : 0);
            if (strafe && name.Contains("right")) return ("right", name.Contains("walking") ? 1 : 0);

            // "Walking Push" contains "walk" but is an action: a clip only fills a locomotion
            // slot when nothing plainer exists. Plain names ("walking", "walk") win outright.
            if (!IsPlain(name, "idle") && name.Contains("idle")) return ("idle", 2);
            if (name.Contains("idle")) return ("idle", 0);
            if (!IsPlain(name, "walk") && !IsPlain(name, "walking") && name.Contains("walk")) return ("walk", 2);
            if (name.Contains("walk")) return ("walk", 0);
            if (!IsPlain(name, "run") && !IsPlain(name, "running") && (name.Contains("run") || name.Contains("jog")))
            {
                return ("run", 2);
            }

            if (name.Contains("run") || name.Contains("jog")) return ("run", 0);
            if (!IsPlain(name, "jump") && !IsPlain(name, "jumping") && name.Contains("jump")) return ("jump", 2);
            if (name.Contains("jump")) return ("jump", 0);

            return (null, int.MaxValue);
        }

        /// <summary>
        /// Animation clips must use the same rig type as the model. A model switched to Humanoid
        /// in the Rig tab keeps its animation FBX on Generic, and then nothing binds — the
        /// character stands still even though the controller is fine. This copies the model's
        /// avatar onto every animation FBX so the clips retarget.
        /// </summary>
        private static void AlignRig(CharacterSource character, Result result)
        {
            var modelImporter = AssetImporter.GetAtPath(character.ModelPath) as ModelImporter;
            if (modelImporter == null) return;

            // 3 == Humanoid in the serialized importer settings (verified in .fbx.meta).
            const ModelImporterAnimationType humanoid = (ModelImporterAnimationType)3;

            if ((int)modelImporter.animationType != (int)humanoid) return;

            Avatar avatar = null;
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(character.ModelPath))
            {
                if (asset is Avatar found)
                {
                    avatar = found;
                    break;
                }
            }

            if (avatar == null)
            {
                result.Notes.Add($"{character.Name}: model is Humanoid but has no Avatar yet — open its Rig tab and press Apply.");
                Debug.LogWarning($"[MP] WARN CHARACTER_NO_AVATAR name={character.Name} model={character.ModelPath}");
                return;
            }

            int aligned = 0;

            foreach (var file in character.AnimationFiles)
            {
                var importer = AssetImporter.GetAtPath(file) as ModelImporter;
                if (importer == null) continue;

                if ((int)importer.animationType == (int)humanoid && importer.sourceAvatar == avatar) continue;

                importer.animationType = humanoid;
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar = avatar;
                importer.SaveAndReimport();
                aligned++;
            }

            if (aligned == 0) return;

            Debug.Log($"[MP] INFO CHARACTER_RIG_ALIGNED name={character.Name} type=Humanoid files={aligned}");
        }

        /// <summary>
        /// Import settings the generated controller depends on:
        /// - locomotion clips loop, otherwise the character freezes on the last frame
        ///   (tick "Loop Pose" by hand if the cycle visibly pops once per loop — Unity does not
        ///   expose that flag to scripts);
        /// - root motion is baked into pose, otherwise Mixamo's forward motion stays on the Hips
        ///   bone and the model slides away from the player object (and its camera) while walking.
        /// </summary>
        private static void ConfigureClip(string fbxPath, string key)
        {
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null) return;

            var clips = importer.defaultClipAnimations;
            if (clips == null || clips.Length == 0) return;

            bool loops = key == "idle" || key == "walk" || key == "run"
                         || key == "left" || key == "right" || key == "back";
            bool changed = false;

            foreach (var clip in clips)
            {
                if (loops && !clip.loopTime)
                {
                    clip.loopTime = true;
                    changed = true;
                }

                if (!clip.lockRootPositionXZ)
                {
                    clip.lockRootPositionXZ = true;
                    changed = true;
                }

                if (!clip.lockRootRotation)
                {
                    clip.lockRootRotation = true;
                    changed = true;
                }

                if (loops && !clip.lockRootHeightY)
                {
                    clip.lockRootHeightY = true;
                    changed = true;
                }
            }

            if (!changed) return;

            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            Debug.Log($"[MP] INFO CHARACTER_CLIP_BAKED path={Path.GetFileName(fbxPath)} key={key}");
        }

        // ---------------------------------------------------------------- roster

        private static int WriteRoster(CharacterRoster roster, List<CharacterEntry> entries, Result result)
        {
            if (entries.Count == 0) return 0;

            var so = new SerializedObject(roster);
            var array = so.FindProperty("characters");
            if (array == null)
            {
                result.Notes.Add("CharacterRoster has no 'characters' array.");
                return 0;
            }

            // Rebuild the list instead of patching it: imported characters first, then any
            // hand-authored entry that carries its own visual prefab. Colour-only placeholder
            // capsules are dropped once real characters exist, otherwise the panel still shows
            // a stray "Yellow" capsule next to the imported models.
            var names = new List<string>();
            var prefabs = new List<UnityEngine.Object>();

            foreach (var entry in entries)
            {
                names.Add(entry.Name);
                prefabs.Add(entry.Prefab);
            }

            for (int i = 0; i < array.arraySize; i++)
            {
                var element = array.GetArrayElementAtIndex(i);
                var displayName = element.FindPropertyRelative("displayName");
                var visualPrefab = element.FindPropertyRelative("visualPrefab");
                if (displayName == null || visualPrefab == null) continue;

                var name = displayName.stringValue;
                var prefab = visualPrefab.objectReferenceValue;
                if (prefab == null) continue;
                if (names.Exists(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase))) continue;

                names.Add(name);
                prefabs.Add(prefab);
            }

            array.arraySize = names.Count;

            for (int i = 0; i < names.Count; i++)
            {
                var element = array.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("displayName").stringValue = names[i];
                element.FindPropertyRelative("visualPrefab").objectReferenceValue = prefabs[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(roster);
            return entries.Count;
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
