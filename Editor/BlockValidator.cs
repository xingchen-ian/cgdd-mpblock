using System.Collections.Generic;
using System.IO;
using System.Text;
using MPBlocks.Core;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MPBlocks.Editor
{
    /// <summary>
    /// Static checks for the mistakes that cost students the most time: a NetworkBehaviour
    /// without a NetworkObject, a missing transport, a prefab that was never registered,
    /// a scene that is not in Build Settings.
    ///
    /// Every scene registered in Build Settings is opened and checked, enabled or not: the
    /// teaching templates ship disabled, and a broken object in one of them must be
    /// reported long before the class that needs it.
    ///
    /// Output is deliberately machine-readable — one line per problem — so an agent can
    /// read it and fix the cause without opening the Editor:
    ///
    ///   [MP] VALIDATE ERROR scene=Template1_WorldInteraction where=Lever_A message=...
    ///   [MP] VALIDATE SUMMARY errors=1 warnings=2
    ///
    /// Project-wide findings (Build Settings itself, NetConfig assets) use scene=project.
    /// Menu: MPBlocks > Validate Project.  CLI: tools/validate.sh
    /// </summary>
    public static class BlockValidator
    {
        public readonly struct Issue
        {
            public readonly bool IsError;

            /// <summary>Name of the scene the issue belongs to; empty for project-wide issues.</summary>
            public readonly string Scene;

            public readonly string Where;
            public readonly string Message;

            public Issue(bool isError, string where, string message)
                : this(isError, null, where, message)
            {
            }

            public Issue(bool isError, string scene, string where, string message)
            {
                IsError = isError;
                Scene = scene;
                Where = where;
                Message = message;
            }
        }

        [MenuItem("MPBlocks/Validate Project")]
        public static void ValidateFromMenu()
        {
            // Opening scenes is an edit-mode operation.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[MP] WARN VALIDATE_SKIPPED reason=play-mode");
                return;
            }

            // Validation opens every registered scene in turn. Let the user keep unsaved
            // work first, then put the Editor back where it was afterwards.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var previousSetup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                Report(Run());
            }
            finally
            {
                if (previousSetup != null && previousSetup.Length > 0)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
                }
            }
        }

        /// <summary>Entry point for tools/validate.sh: exits non-zero when an error is found.</summary>
        public static void ValidateForCI()
        {
            var issues = Run();
            Report(issues);

            int errors = 0;
            foreach (var issue in issues)
            {
                if (issue.IsError) errors++;
            }

            EditorApplication.Exit(errors == 0 ? 0 : 1);
        }

        /// <summary>
        /// Validates Build Settings, then every scene registered in it — enabled entries
        /// just as much as disabled ones. Batch mode starts with no scene open; the menu
        /// path restores whatever scene the user had. Missing scene files are reported by
        /// <see cref="CheckBuildSettings"/> and skipped here so OpenScene is never asked to
        /// open a path that does not exist.
        /// </summary>
        public static List<Issue> Run()
        {
            var issues = new List<Issue>();

            var scenes = EditorBuildSettings.scenes;
            CheckBuildSettings(scenes, issues);

            var validated = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var scene in scenes)
            {
                if (!File.Exists(scene.path)) continue;

                // A scene registered twice in Build Settings must still be validated once.
                if (!validated.Add(scene.path)) continue;

                ValidateScene(scene.path, issues);
            }

            CheckNetConfigs(issues);
            CheckAudioClips(issues);

            return issues;
        }

        private static void ValidateScene(string scenePath, List<Issue> issues)
        {
            var sceneName = Path.GetFileNameWithoutExtension(scenePath);

            try
            {
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            }
            catch (System.Exception e)
            {
                issues.Add(new Issue(true, sceneName, scenePath, "could not open scene: " + e.Message));
                return;
            }

            // The checks below were written against "the open scene" and append to a list;
            // tag their results with the scene name once instead of threading a scene
            // parameter through every check.
            var sceneIssues = new List<Issue>();
            CheckNetworkManager(sceneIssues);
            CheckSceneBehaviours(sceneIssues);

            foreach (var issue in sceneIssues)
            {
                issues.Add(new Issue(issue.IsError, sceneName, issue.Where, issue.Message));
            }
        }

        private static void CheckNetworkManager(List<Issue> issues)
        {
            var managers = Object.FindObjectsOfType<NetworkManager>(true);

            if (managers.Length == 0)
            {
                issues.Add(new Issue(true, "scene", "no NetworkManager in the open scene"));
                return;
            }

            if (managers.Length > 1)
            {
                issues.Add(new Issue(true, "scene", $"{managers.Length} NetworkManagers in the open scene; exactly one is allowed"));
            }

            var manager = managers[0];
            if (manager.GetComponent<UnityTransport>() == null)
            {
                issues.Add(new Issue(true, manager.name, "NetworkManager has no UnityTransport"));
            }

            if (manager.NetworkConfig == null || manager.NetworkConfig.PlayerPrefab == null)
            {
                // MPBlocks injects the player prefab at runtime from NetConfig, so a null
                // field on the NetworkManager itself is only a problem when the config has
                // none either.
                var boot = Object.FindObjectOfType<NetBootstrap>(true);
                bool providedByConfig = boot != null && boot.Config != null && boot.Config.PlayerPrefab != null;

                if (!providedByConfig)
                {
                    issues.Add(new Issue(false, manager.name, "no Player Prefab; clients will connect but spawn nothing"));
                }
            }
        }

        private static void CheckSceneBehaviours(List<Issue> issues)
        {
            var behaviours = Object.FindObjectsOfType<NetworkBehaviour>(true);
            foreach (var behaviour in behaviours)
            {
                if (behaviour == null) continue;

                if (behaviour.GetComponent<NetworkObject>() == null)
                {
                    issues.Add(new Issue(true, behaviour.name,
                        $"{behaviour.GetType().Name} needs a NetworkObject on the same GameObject"));
                }
            }

            var interactables = Object.FindObjectsOfType<Gameplay.Interactable>(true);
            foreach (var interactable in interactables)
            {
                if (interactable.MaxDistance <= 0f)
                {
                    issues.Add(new Issue(false, interactable.name,
                        "Interactable maxDistance is 0; nothing will ever be in range"));
                }
            }

            var spawners = Object.FindObjectsOfType<Gameplay.Spawner>(true);
            foreach (var spawner in spawners)
            {
                if (spawner.GetComponent<NetworkObject>() == null)
                {
                    issues.Add(new Issue(true, spawner.name, "Spawner needs a NetworkObject"));
                }
            }
        }

        private static void CheckNetConfigs(List<Issue> issues)
        {
            var guids = AssetDatabase.FindAssets("t:NetConfig");
            if (guids.Length == 0)
            {
                issues.Add(new Issue(false, "Assets", "no NetConfig asset found; NetBootstrap will fall back to defaults"));
                return;
            }

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var config = AssetDatabase.LoadAssetAtPath<NetConfig>(path);
                if (config == null) continue;

                if (config.PlayerPrefab == null)
                {
                    issues.Add(new Issue(false, path, "NetConfig has no player prefab"));
                }
                else if (config.PlayerPrefab.GetComponent<NetworkObject>() == null)
                {
                    issues.Add(new Issue(true, path, "NetConfig player prefab has no NetworkObject"));
                }

                if (config.PlayerMovement == MPPlayerMovement.Rigidbody && config.RigidbodyPlayerPrefab == null)
                {
                    issues.Add(new Issue(false, path,
                        "Player Movement = Rigidbody but no rigidbody player prefab is assigned; falling back to the Transform prefab"));
                }

                if (config.RigidbodyPlayerPrefab != null && config.RigidbodyPlayerPrefab.GetComponent<NetworkObject>() == null)
                {
                    issues.Add(new Issue(true, path, "rigidbody player prefab has no NetworkObject"));
                }

                if (config.ScoreboardPrefab != null && config.ScoreboardPrefab.GetComponent<NetworkObject>() == null)
                {
                    issues.Add(new Issue(true, path, "NetConfig scoreboard prefab has no NetworkObject"));
                }

                if (config.SpawnablePrefabs != null)
                {
                    foreach (var spawnable in config.SpawnablePrefabs)
                    {
                        if (spawnable == null) continue;
                        if (spawnable.GetComponent<NetworkObject>() == null)
                        {
                            issues.Add(new Issue(true, path, $"spawnable prefab '{spawnable.name}' has no NetworkObject"));
                        }
                    }
                }

                if (config.MaxPlayers < 1)
                {
                    issues.Add(new Issue(true, path, "maxPlayers is below 1"));
                }
            }
        }

        /// <summary>
        /// Every audio clip in the project must contain samples. A clip written with
        /// AudioClip.Create + CreateAsset keeps only its header (length 0, silence), which
        /// looks fine in the Project window and plays nothing — the exact bug this catches.
        /// </summary>
        private static void CheckAudioClips(List<Issue> issues)
        {
            // Scan files rather than FindAssets("t:AudioClip"): an audio file that fails to
            // import has no AudioClip object, so a type filter would silently miss exactly the
            // broken case this check exists for.
            var extensions = new[] { "*.wav", "*.ogg", "*.mp3", "*.aif", "*.aiff", "*.flac" };
            var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            foreach (var pattern in extensions)
            {
                foreach (var file in Directory.GetFiles("Assets", pattern, SearchOption.AllDirectories))
                {
                    var path = file.Replace('\\', '/');
                    if (!seen.Add(path)) continue;

                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                    if (clip == null)
                    {
                        issues.Add(new Issue(true, Path.GetFileName(path), path,
                            "audio clip failed to import (no samples or corrupt); " +
                            "regenerate with MPBlocks > Audio > Generate Placeholder Sounds"));
                        continue;
                    }

                    if (clip.length > 0.005f && clip.samples > 0) continue;

                    issues.Add(new Issue(true, Path.GetFileName(path), path,
                        $"audio clip is empty (length {clip.length:0.###}s, samples {clip.samples}); " +
                        "regenerate with MPBlocks > Audio > Generate Placeholder Sounds"));
                }
            }

            // Legacy shells: AudioClip saved as .asset by an older toolkit version.
            foreach (var file in Directory.GetFiles("Assets", "*.asset", SearchOption.AllDirectories))
            {
                var path = file.Replace('\\', '/');
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null) continue;

                issues.Add(new Issue(true, Path.GetFileName(path), path,
                    "audio clip saved as .asset has no sample data (length 0); the toolkit now writes .wav files"));
            }
        }

        private static void CheckBuildSettings(EditorBuildSettingsScene[] scenes, List<Issue> issues)
        {
            if (scenes.Length == 0)
            {
                issues.Add(new Issue(false, "Build Settings", "no scenes registered; the player build will fail"));
                return;
            }

            bool anyEnabled = false;
            foreach (var scene in scenes)
            {
                if (scene.enabled) anyEnabled = true;
                if (!File.Exists(scene.path))
                {
                    issues.Add(new Issue(true, "Build Settings", $"scene path does not exist: {scene.path}"));
                }
            }

            if (!anyEnabled)
            {
                issues.Add(new Issue(true, "Build Settings", "all scenes are disabled; the player build will fail"));
            }
        }

        private static void Report(List<Issue> issues)
        {
            var builder = new StringBuilder();

            foreach (var issue in issues)
            {
                builder.Append("[MP] VALIDATE ")
                    .Append(issue.IsError ? "ERROR" : "WARN")
                    .Append(" scene=").Append(string.IsNullOrEmpty(issue.Scene) ? "project" : issue.Scene)
                    .Append(" where=").Append(issue.Where)
                    .Append(" message=").Append(issue.Message)
                    .Append('\n');
            }

            int errors = 0;
            int warnings = 0;
            foreach (var issue in issues)
            {
                if (issue.IsError) errors++;
                else warnings++;
            }

            builder.Append("[MP] VALIDATE SUMMARY errors=").Append(errors).Append(" warnings=").Append(warnings);

            if (errors > 0) Debug.LogError(builder.ToString());
            else if (warnings > 0) Debug.LogWarning(builder.ToString());
            else Debug.Log(builder.ToString());
        }
    }
}
