using System.IO;
using UnityEditor;
using UnityEngine;

namespace MPBlocks.Editor
{
    /// <summary>
    /// Generates the placeholder sound library, so a scene has audible feedback without
    /// shipping binary audio files or downloading anything.
    ///
    /// Each clip is a short synthesised tone/noise envelope — obviously a placeholder, but
    /// distinct enough that students can tell "I got hit" from "I scored" while building.
    /// They are ordinary AudioClips: replace any of them by dragging your own clip into the
    /// component that uses it. Nothing looks the assets up by name at runtime.
    ///
    /// Menu: MPBlocks > Audio > Generate Placeholder Sounds
    /// </summary>
    public static class InteractAudio
    {
        public const string Folder = "Assets/Audio";

        // interaction feedback (InteractSound)
        public const string ApprovedPath = Folder + "/Interact_Approved.asset";
        public const string DeniedPath = Folder + "/Interact_Denied.asset";

        // combat and gameplay feedback (AudioOneShot, or any AudioSource)
        public const string ShotPath = Folder + "/Shot_Fire.asset";
        public const string HitPath = Folder + "/Hit_Damage.asset";
        public const string DeathPath = Folder + "/Player_Death.asset";
        public const string RespawnPath = Folder + "/Player_Respawn.asset";
        public const string PickupPath = Folder + "/Pickup.asset";
        public const string DoorPath = Folder + "/Door_Open.asset";
        public const string ScorePath = Folder + "/Score_Point.asset";

        [InitializeOnLoadMethod]
        private static void EnsureClipsOnLoad()
        {
            // Scene generation also creates these, but a scene built before the feature existed
            // needs the assets on disk so they can be dragged in by hand.
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
                GenerateAll(silent: true);
            };
        }

        [MenuItem("MPBlocks/Audio/Generate Placeholder Sounds", false, 50)]
        public static void GenerateFromMenu()
        {
            GenerateAll(silent: false);
            EditorUtility.DisplayDialog("MPBlocks",
                "Placeholder sounds are in " + Folder + ".\n\n" +
                "Drag them into InteractSound, AudioOneShot, or any AudioSource.", "OK");
        }

        public static void GenerateAll(bool silent)
        {
            int before = ExistingCount();

            Approved();
            Denied();
            Shot();
            Hit();
            Death();
            Respawn();
            Pickup();
            Door();
            Score();

            int created = ExistingCount() - before;
            if (created > 0)
            {
                Debug.Log($"[MP] INFO AUDIO_LIBRARY_CREATED count={created} folder={Folder}");
            }
        }

        // ---- clip accessors (used by scene builders and by hand) ----

        public static AudioClip Approved() => Ensure(ApprovedPath, "Interact_Approved", 0.09f, 900f, 1500f, 0.55f, Shape.Sine, 0f);
        public static AudioClip Denied() => Ensure(DeniedPath, "Interact_Denied", 0.17f, 240f, 150f, 0.8f, Shape.Square, 0f);
        public static AudioClip Shot() => Ensure(ShotPath, "Shot_Fire", 0.12f, 1200f, 220f, 0.9f, Shape.Noise, 0.55f);
        public static AudioClip Hit() => Ensure(HitPath, "Hit_Damage", 0.13f, 220f, 90f, 0.9f, Shape.Noise, 0.25f);
        public static AudioClip Death() => Ensure(DeathPath, "Player_Death", 0.55f, 420f, 70f, 0.85f, Shape.Sine, 0.1f);
        public static AudioClip Respawn() => Ensure(RespawnPath, "Player_Respawn", 0.4f, 480f, 1250f, 0.6f, Shape.Sine, 0f);
        public static AudioClip Pickup() => Ensure(PickupPath, "Pickup", 0.18f, 700f, 1500f, 0.6f, Shape.Sine, 0f);
        public static AudioClip Door() => Ensure(DoorPath, "Door_Open", 0.45f, 130f, 85f, 0.75f, Shape.Saw, 0.15f);
        public static AudioClip Score() => Ensure(ScorePath, "Score_Point", 0.22f, 1000f, 1600f, 0.6f, Shape.Sine, 0f);

        // ---- synthesis ----

        private enum Shape
        {
            Sine,
            Square,
            Saw,
            Noise,
        }

        private static AudioClip Ensure(string path, string name, float seconds, float startHz, float endHz,
            float gain, Shape shape, float noiseMix)
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

            // Deterministic noise: regenerating the asset gives the identical clip.
            uint seed = 12345u;
            float phase = 0f;

            for (int i = 0; i < count; i++)
            {
                float progress = i / (float)count;
                float frequency = Mathf.Lerp(startHz, endHz, progress);

                phase += 2f * Mathf.PI * frequency / rate;

                float tone;
                switch (shape)
                {
                    case Shape.Square:
                        tone = Mathf.Sign(Mathf.Sin(phase)) * 0.6f;
                        break;
                    case Shape.Saw:
                        tone = Mathf.Repeat(phase / (2f * Mathf.PI), 1f) * 2f - 1f;
                        break;
                    default:
                        tone = Mathf.Sin(phase);
                        break;
                }

                if (noiseMix > 0f)
                {
                    seed = seed * 1664525u + 1013904223u;
                    float noise = ((seed >> 8) & 0xFFFF) / 32768f - 1f;
                    tone = tone * (1f - noiseMix) + noise * noiseMix;
                }

                // Exponential decay is what makes a tone read as a click/thud instead of a beep.
                float envelope = Mathf.Exp(-6f * progress);

                data[i] = Mathf.Clamp(tone * envelope * gain, -1f, 1f);
            }

            var clip = AudioClip.Create(name, count, 1, rate, false);
            clip.SetData(data, 0);

            AssetDatabase.CreateAsset(clip, path);
            AssetDatabase.SaveAssets();

            Debug.Log("[MP] INFO AUDIO_CLIP_CREATED path=" + path);
            return clip;
        }

        private static int ExistingCount()
        {
            var guids = AssetDatabase.FindAssets("t:AudioClip", new[] { Folder });
            return guids != null ? guids.Length : 0;
        }

        /// <summary>Used by the scene check: is the library wired into the scene already?</summary>
        public static string ApprovedGuid() => AssetDatabase.AssetPathToGUID(ApprovedPath);

        public static bool Exists() => File.Exists(ApprovedPath);
    }
}
