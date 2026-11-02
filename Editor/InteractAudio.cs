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
        public const string ApprovedPath = Folder + "/Interact_Approved.wav";
        public const string DeniedPath = Folder + "/Interact_Denied.wav";

        // combat and gameplay feedback (AudioOneShot, or any AudioSource)
        public const string ShotPath = Folder + "/Shot_Fire.wav";
        public const string HitPath = Folder + "/Hit_Damage.wav";
        public const string DeathPath = Folder + "/Player_Death.wav";
        public const string RespawnPath = Folder + "/Player_Respawn.wav";
        public const string PickupPath = Folder + "/Pickup.wav";
        public const string DoorPath = Folder + "/Door_Open.wav";
        public const string ScorePath = Folder + "/Score_Point.wav";

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
            DeleteLegacyAssetClips();
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
            // A generated AudioClip cannot be saved as an .asset: Unity writes only the header,
            // so the clip ends up with length 0 and silence. A real .wav file on disk is
            // imported like any other sound and keeps its samples.
            if (!File.Exists(path)) WriteWav(path, seconds, startHz, endHz, gain, shape, noiseMix);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        private static void WriteWav(string path, float seconds, float startHz, float endHz,
            float gain, Shape shape, float noiseMix)
        {
            if (!AssetDatabase.IsValidFolder(Folder))
            {
                if (!AssetDatabase.IsValidFolder("Assets")) return;
                AssetDatabase.CreateFolder("Assets", "Audio");
            }

            const int rate = 44100;
            int count = Mathf.Max(1, Mathf.RoundToInt(rate * seconds));
            var samples = new float[count];

            // Deterministic noise: regenerating gives the identical clip.
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

                float envelope = Mathf.Exp(-6f * progress);
                samples[i] = Mathf.Clamp(tone * envelope * gain, -1f, 1f);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "Assets/Audio");
            File.WriteAllBytes(path, BuildWavBytes(samples, rate));

            Debug.Log("[MP] INFO AUDIO_CLIP_CREATED path=" + path);
        }

        /// <summary>16-bit mono PCM in a RIFF/WAVE container.</summary>
        private static byte[] BuildWavBytes(float[] samples, int rate)
        {
            using (var stream = new MemoryStream(44 + samples.Length * 2))
            using (var writer = new BinaryWriter(stream))
            {
                int dataBytes = samples.Length * 2;

                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + dataBytes);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16);            // PCM chunk size
                writer.Write((short)1);      // PCM
                writer.Write((short)1);      // mono
                writer.Write(rate);
                writer.Write(rate * 2);      // byte rate
                writer.Write((short)2);      // block align
                writer.Write((short)16);     // bits per sample
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                writer.Write(dataBytes);

                foreach (float sample in samples)
                {
                    writer.Write((short)Mathf.Clamp(Mathf.RoundToInt(sample * 32767f), short.MinValue, short.MaxValue));
                }

                writer.Flush();
                return stream.ToArray();
            }
        }

        /// <summary>
        /// Removes the empty .asset clips that an earlier version of this tool wrote. They have
        /// length 0 and would otherwise stay wired into prefabs forever.
        /// </summary>
        private static void DeleteLegacyAssetClips()
        {
            foreach (var name in new[]
                     {
                         "Interact_Approved", "Interact_Denied", "Shot_Fire", "Hit_Damage",
                         "Player_Death", "Player_Respawn", "Pickup", "Door_Open", "Score_Point",
                     })
            {
                var legacy = Folder + "/" + name + ".asset";
                if (AssetDatabase.LoadAssetAtPath<AudioClip>(legacy) == null) continue;

                AssetDatabase.DeleteAsset(legacy);
                Debug.Log("[MP] INFO AUDIO_LEGACY_REMOVED path=" + legacy);
            }
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
