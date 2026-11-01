using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MPBlocks.Editor
{
    /// <summary>
    /// Isolated build entry point that compiles **only** the generated Bootstrap template,
    /// ignoring Build Settings.
    ///
    /// Why this exists: the project's real build follows Build Settings, which by now point
    /// at whatever game is being worked on. Verifying the toolkit itself needs a build that
    /// is guaranteed to contain the template, and it must not disturb the real build output.
    ///
    /// Usage:
    ///   unity build &lt;project&gt; --target StandaloneOSX \
    ///     --execute-method MPBlocks.Editor.BootstrapPreview.BuildMac \
    ///     -o out/preview/MPGame.app
    /// </summary>
    public static class BootstrapPreview
    {
        public static void BuildMac() => Build("StandaloneOSX");
        public static void BuildWindows() => Build("StandaloneWindows64");

        private static void Build(string targetName)
        {
            // Scaffolding must run inside the build: batch mode never reaches
            // EditorApplication.delayCall, so nothing else regenerates the template.
            ProjectSetup.Setup(force: false);

            var scene = ProjectSetup.ScenePath;
            if (!File.Exists(scene))
            {
                Debug.LogError("[MP] ERROR PREVIEW_NO_SCENE path=" + scene);
                EditorApplication.Exit(1);
                return;
            }

            var target = (BuildTarget)System.Enum.Parse(typeof(BuildTarget), targetName);
            var output = ArgumentValue("-buildOutput") ?? Fallback(target);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { scene },
                locationPathName = output,
                target = target,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[MP] ERROR PREVIEW_BUILD_FAILED target={targetName} result={report.summary.result}");
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log($"[MP] INFO PREVIEW_BUILD_OK target={targetName} path={output}");
            EditorApplication.Exit(0);
        }

        private static string Fallback(BuildTarget target)
        {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "out", "preview"));
            return target == BuildTarget.StandaloneWindows64
                ? Path.Combine(root, "MPGame.exe")
                : Path.Combine(root, "MPGame.app");
        }

        private static string ArgumentValue(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name) return args[i + 1];
            }

            return null;
        }
    }
}
