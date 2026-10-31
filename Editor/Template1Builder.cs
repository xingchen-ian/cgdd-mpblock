using MPBlocks.Core;
using MPBlocks.Gameplay;
using MPBlocks.Sync;
using Unity.Netcode;
using UnityEngine;

namespace MPBlocks.Editor
{
    /// <summary>
    /// Template 1 — world coexistence and interaction.
    ///
    /// Four players share a small room with four coloured pads. Interacting with a pad
    /// asks the server for approval; the server flips that pad's <see cref="SyncedBool"/>
    /// and every machine repaints it, so the world agrees even when nobody is looking.
    /// The lesson is the three-step model: a client <em>requests</em>, the server
    /// <em>decides</em>, and the result is replicated state.
    ///
    /// Generated from C# (never hand-edited YAML) and callable for both the teaching
    /// template and a per-group game scene: paths and the session id come from
    /// <see cref="GameSpec"/>, never from constants here.
    /// </summary>
    public static class Template1Builder
    {
        private const int SpawnPointCount = 4;
        private const int PadCount = 4;

        /// <summary>
        /// Builds Assets/Scenes/Templates/Template1_WorldInteraction.unity (or the
        /// equivalent per-game scene) plus its own NetConfig asset and saves both.
        /// </summary>
        /// <param name="spec">Scene path, config path, game id, player count and port.</param>
        public static void Build(GameSpec spec)
        {
            SceneKit.EnsureProjectScaffolding();
            var playerPrefab = SceneKit.PlayerPrefab();

            var scene = SceneKit.NewEmptyScene();

            // No scoreboard and no spawnables: this template only teaches interaction.
            var config = SceneKit.CreateNetConfig(spec, playerPrefab, null);
            SceneKit.CreateInfrastructure(config);

            SceneKit.CreateGround(new Vector3(4f, 1f, 4f), new Color(0.16f, 0.17f, 0.19f));
            SceneKit.CreateDirectionalLight();
            SceneKit.CreateHintText("WASD move | RMB look | E toggle a pad");

            BuildSpawnPoints();

            // The pads sit 3.29 m from the nearest spawn point, inside both
            // PlayerInteractor.range (3.5) and the pad's own maxDistance (4), so
            // -mp-interact-demo can flip them without any player movement.
            BuildPad("Pad_Red", "PadRedOpen", new Vector3(3.5f, 0.5f, 3.5f),
                new Color(0.32f, 0.10f, 0.10f), new Color(0.95f, 0.25f, 0.25f));
            BuildPad("Pad_Green", "PadGreenOpen", new Vector3(-3.5f, 0.5f, 3.5f),
                new Color(0.10f, 0.30f, 0.14f), new Color(0.30f, 0.90f, 0.35f));
            BuildPad("Pad_Blue", "PadBlueOpen", new Vector3(-3.5f, 0.5f, -3.5f),
                new Color(0.10f, 0.18f, 0.34f), new Color(0.30f, 0.55f, 1.00f));
            BuildPad("Pad_Yellow", "PadYellowOpen", new Vector3(3.5f, 0.5f, -3.5f),
                new Color(0.34f, 0.30f, 0.10f), new Color(1.00f, 0.90f, 0.25f));

            SceneKit.SaveScene(scene, spec.ScenePath);

            MPLog.Info("SETUP_TEMPLATE1",
                ("name", spec.Name),
                ("scene", spec.ScenePath),
                ("config", spec.ConfigPath),
                ("gameId", spec.GameId),
                ("maxPlayers", spec.MaxPlayers),
                ("spawnPoints", SpawnPointCount),
                ("pads", PadCount));
        }

        /// <summary>
        /// Four points around the centre, each looking inwards. The server assigns them
        /// round-robin at connect time (PlayerSpawnPoint), so no extra code runs here.
        /// </summary>
        private static void BuildSpawnPoints()
        {
            SceneKit.CreateSpawnPoint("Spawn_A", new Vector3(1.2f, 0f, 1.2f), Vector3.zero);
            SceneKit.CreateSpawnPoint("Spawn_B", new Vector3(-1.2f, 0f, 1.2f), Vector3.zero);
            SceneKit.CreateSpawnPoint("Spawn_C", new Vector3(-1.2f, 0f, -1.2f), Vector3.zero);
            SceneKit.CreateSpawnPoint("Spawn_D", new Vector3(1.2f, 0f, -1.2f), Vector3.zero);
        }

        /// <summary>
        /// One pad: networked object + server-owned state + interaction + the toggle that
        /// bridges them, with a 0.8 m cube that changes colour when the state flips.
        /// </summary>
        private static InteractToggle BuildPad(string name, string syncedLabel, Vector3 position,
            Color offColor, Color onColor)
        {
            var root = new GameObject(name);
            root.transform.position = position;
            root.AddComponent<NetworkObject>();

            var state = root.AddComponent<SyncedBool>();
            SceneKit.SetSerialized(state, "label", syncedLabel);

            var interactable = root.AddComponent<Interactable>();
            SceneKit.SetSerialized(interactable, "label", name);
            SceneKit.SetSerialized(interactable, "maxDistance", 4f);

            var visual = SceneKit.CreateBlock(name + "_Visual", position,
                new Vector3(0.8f, 0.8f, 0.8f), offColor, keepCollider: false);
            visual.transform.SetParent(root.transform, true);

            var toggle = root.AddComponent<InteractToggle>();
            SceneKit.SetSerialized(toggle, "label", name);
            SceneKit.SetSerialized(toggle, "interactable", interactable);
            SceneKit.SetSerialized(toggle, "state", state);
            SceneKit.SetSerialized(toggle, "targetRenderer", visual.GetComponent<Renderer>());
            SceneKit.SetSerialized(toggle, "offColor", offColor);
            SceneKit.SetSerialized(toggle, "onColor", onColor);

            return toggle;
        }
    }
}
