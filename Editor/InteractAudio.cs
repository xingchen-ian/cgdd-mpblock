using System.IO;
using UnityEditor;
using UnityEngine;

namespace MPBlocks.Editor
{
    /// <summary>
    /// Generates the two placeholder interaction sounds, so a generated scene has audible
    /// feedback without shipping binary audio files.
    ///
    /// Replace them with your own clips: select the lever's InteractSound component and drag a
    /// real clip into Approved / Denied. Nothing else changes.
    /// </summary>
    public static class InteractAudio
    {
        public const string Folder = "Assets/Audio";
        public const string ApprovedPath = Folder + "/Interact_Approved.asset";
        public const string DeniedPath = Folder + "/Interact_Denied.asset";

        [UnityEditor.InitializeOnLoadMethod]
        private static void EnsureClipsOnLoad()
        {
            // The clips are also created by scene generation, but a scene built before this
            // feature existed needs the assets on disk to drag in by hand.
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (UnityEditor.EditorApplication.isCompiling
                    || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) return;

                Approved();
                Denied();
            };
        }

        /// <summary>Short bright click for a successful interaction.</summary>
        public static AudioClip Approved() =>
            Ensure(ApprovedPath, "Interact_Approved", 0.09f, 900f, 1500f, 0.55f);

        /// <summary>Lower, longer thud for a refused interaction.</summary>
        public static AudioClip Denied() =>
            Ensure(DeniedPath, "Interact_Denied", 0.17f, 240f, 150f, 0.8f);

        private static AudioClip Ensure(string path, string name, float seconds, float startHz, float endHz, float gain)
        {
            var existing = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (existing != null) return existing;

            if (!AssetDatabase.IsValidFolder(Folder))
            {
                if (!AssetDatabase.IsValidFolder("Assets")) return null;
                AssetDatabase.CreateFolder("Assets", "Audio");
            }

            const int rate = 44100;
            int count = Mathf.Max(1, Mathf.RoundToInt(rate * seconds));
            var data = new float[count];

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)rate;
                float progress = i / (float)count;
                float frequency = Mathf.Lerp(startHz, endHz, progress);

                // A short exponential decay is what makes a tone read as a "click" instead of a beep.
                float envelope = Mathf.Exp(-7f * progress);

                data[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * envelope * gain;
            }

            var clip = AudioClip.Create(name, count, 1, rate, false);
            clip.SetData(data, 0);

            AssetDatabase.CreateAsset(clip, path);
            AssetDatabase.SaveAssets();

            Debug.Log("[MP] INFO INTERACT_SOUND_CREATED path=" + path);
            return clip;
        }

        /// <summary>Used by the scene check: is the placeholder wired into the scene already?</summary>
        public static string ApprovedGuid() => AssetDatabase.AssetPathToGUID(ApprovedPath);

        public static bool Exists() => File.Exists(ApprovedPath);
    }
}
