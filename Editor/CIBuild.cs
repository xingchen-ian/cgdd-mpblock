using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MPBlocks.Editor
{
    /// <summary>
    /// Build entry points invoked by tools/build.sh through the Unity CLI's
    /// --execute-method. Doing the scaffolding here (instead of in
    /// [InitializeOnLoadMethod]) is deliberate: in batch mode Unity never reaches
    /// EditorApplication.delayCall, so delayed setup silently never runs.
    ///
    /// Which scene ships follows Build Settings (first enabled scene), not a hard-coded
    /// path: a group can create its own scene from the New Game Scene window and build
    /// without touching C#. Two overrides exist for tooling:
    ///
    /// - `-buildScene &lt;path&gt;` / `MP_BUILD_SCENE` builds exactly that scene (used by
    ///   the per-template acceptance runs).
    /// - `MP_TOOL_BUILD=1` (tools/build.sh TOOL_BUILD=1) builds the teaching tool only:
    ///   Bootstrap plus every scene under Assets/Scenes/Templates, ignoring a group's own
    ///   level even when Build Settings has it enabled first.
    /// </summary>
    public static class CIBuild
    {
        public static void BuildMac() => Build("StandaloneOSX");
        public static void BuildWindows() => Build("StandaloneWindows64");

        private static void Build(string targetName)
        {
            ProjectSetup.Setup(force: false);

            var target = (BuildTarget)Enum.Parse(typeof(BuildTarget), targetName);
            var output = ArgumentValue("-buildOutput") ?? FallbackOutput(target);
            var scenes = BuildSceneList();

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = target,
                options = BuildOptions.None,
            };

            Debug.Log("[MP] INFO BUILD_SCENES " + string.Join(" ", scenes));

            var report = BuildPipeline.BuildPlayer(options);
            var result = report.summary.result;

            if (result != BuildResult.Succeeded)
            {
                Debug.LogError($"[MP] ERROR BUILD_FAILED target={targetName} result={result}");
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log($"[MP] INFO BUILD_OK target={targetName} path={output} size={report.summary.totalSize}");
            EditorApplication.Exit(0);
        }

        /// <summary>
        /// Precedence: explicit scene (`-buildScene` / `MP_BUILD_SCENE`) &gt; tool build
        /// (`MP_TOOL_BUILD=1`) &gt; first enabled Build Settings scene &gt; Bootstrap.
        /// The first enabled Build Settings scene is the same scene validate.sh checks and
        /// a group sees when it presses Build. Bootstrap is only a fallback for a project
        /// whose Build Settings were emptied.
        /// </summary>
        private static string[] BuildSceneList()
        {
            var explicitScene = ArgumentValue("-buildScene");
            if (string.IsNullOrEmpty(explicitScene))
            {
                explicitScene = Environment.GetEnvironmentVariable("MP_BUILD_SCENE");
            }

            if (!string.IsNullOrEmpty(explicitScene))
            {
                var resolved = ResolveAssetPath(explicitScene);
                if (resolved != null) return new[] { resolved };

                Debug.LogWarning("[MP] WARN BUILD_SCENE_MISSING path=" + explicitScene);
            }

            if (Environment.GetEnvironmentVariable("MP_TOOL_BUILD") == "1")
            {
                return ToolScenes();
            }

            var scenes = new List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (!scene.enabled || !File.Exists(scene.path)) continue;
                scenes.Add(scene.path);
            }

            if (scenes.Count > 0) return scenes.ToArray();

            Debug.LogWarning("[MP] WARN BUILD_SETTINGS_EMPTY fallback=" + ProjectSetup.ScenePath);
            return new[] { ProjectSetup.ScenePath };
        }

        /// <summary>
        /// The teaching tool content: the Bootstrap demo first, then every template scene
        /// found on disk. Deliberately ignores Build Settings, so a group's level cannot
        /// become the shipped demo just because it is enabled first there.
        /// </summary>
        private static string[] ToolScenes()
        {
            var scenes = new List<string>();

            if (File.Exists(ProjectSetup.ScenePath)) scenes.Add(ProjectSetup.ScenePath);

            var templatesFolder = Path.Combine(Application.dataPath, "Scenes", "Templates");
            if (Directory.Exists(templatesFolder))
            {
                var files = Directory.GetFiles(templatesFolder, "*.unity");
                Array.Sort(files, StringComparer.Ordinal);
                foreach (var file in files)
                {
                    if (!File.Exists(file)) continue;

                    var assetPath = "Assets/Scenes/Templates/" + Path.GetFileName(file);
                    scenes.Add(assetPath);
                }
            }

            if (scenes.Count == 0)
            {
                Debug.LogWarning("[MP] WARN TOOL_SCENES_EMPTY fallback=" + ProjectSetup.ScenePath);
                return new[] { ProjectSetup.ScenePath };
            }

            Debug.Log("[MP] INFO BUILD_MODE tool scenes=" + scenes.Count);
            return scenes.ToArray();
        }

        /// <summary>Build Settings paths are project-relative; accept both forms.</summary>
        private static string ResolveAssetPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (File.Exists(path)) return path;

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (!string.IsNullOrEmpty(projectRoot))
            {
                var full = Path.Combine(projectRoot, path);
                if (File.Exists(full)) return path;
            }

            return null;
        }

        private static string FallbackOutput(BuildTarget target)
        {
            var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                Application.dataPath, "..", "..", "out", "builds"));

            return target switch
            {
                BuildTarget.StandaloneOSX => System.IO.Path.Combine(root, "mac", "MPGame.app"),
                BuildTarget.StandaloneWindows64 => System.IO.Path.Combine(root, "win", "MPGame.exe"),
                _ => System.IO.Path.Combine(root, "player"),
            };
        }

        private static string ArgumentValue(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name) return args[i + 1];
            }
            return null;
        }
    }
}
