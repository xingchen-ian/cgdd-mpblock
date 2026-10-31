using UnityEngine;

namespace MPBlocks.Core
{
    /// <summary>What the session starts as when the build launches.</summary>
    public enum MPNetMode
    {
        /// <summary>One machine plays and also acts as the authority. Uses all N laptops as players.</summary>
        HostAndPlay = 0,

        /// <summary>Authority only, no player. Used for the exhibition server laptop.</summary>
        DedicatedServer = 1,

        /// <summary>Plain client.</summary>
        Client = 2,
    }

    /// <summary>
    /// Which player prefab is spawned for every client.
    ///
    /// - <see cref="Transform"/>: the classic sample, <c>SimplePlayerMover</c> writes the
    ///   transform. No Rigidbody, no gravity, cheapest and most predictable.
    /// - <see cref="Rigidbody"/>: the physics sample, <c>RigidbodyPlayerMover</c> drives a
    ///   Rigidbody (gravity, jumps, collisions). The owner simulates; remote copies follow
    ///   <c>SyncedTransform</c>.
    ///
    /// Both prefabs live on this asset, so a game switches by changing this enum instead
    /// of editing the scene or rebuilding prefabs.
    /// </summary>
    public enum MPPlayerMovement
    {
        Transform = 0,
        Rigidbody = 1,
    }

    /// <summary>
    /// All session configuration for a game. Kept in a ScriptableObject (not in scene fields)
    /// so it is plain text, diffable, and generatable by agents.
    ///
    /// Create via: Assets > Create > MPBlocks > Net Config
    /// </summary>
    [CreateAssetMenu(menuName = "MPBlocks/Net Config", fileName = "NetConfig")]
    public sealed class NetConfig : ScriptableObject
    {
        [Header("Session")]
        [Tooltip("UDP port the host/server listens on; 7777 is the Unity default. Every machine in the session must use the same port, otherwise clients only see a connection timeout.")]
        [SerializeField] private ushort port = 7777;

        [Tooltip("Maximum number of players connected at once, including the host when it is also playing. Set the real headcount; extra connections are refused and reported as server full.")]
        [SerializeField] private int maxPlayers = 4;

        [Header("Session identity")]
        [Tooltip("Session identifier; client and server must use exactly the same value or the connection is refused and [MP] logs SESSION_ID_MISMATCH. Give every game its own id (e.g. g03-flagrush) instead of sharing the default.")]
        [SerializeField] private string gameId = "default-game";

        [Header("Startup")]
        [Tooltip("What this build does when it launches: HostAndPlay plays and acts as the server, DedicatedServer is server-only without a player, Client only joins. Usually only the exhibition server needs a different value.")]
        [SerializeField] private MPNetMode startMode = MPNetMode.HostAndPlay;

        [Tooltip("Prefab spawned for every connected client when Player Movement = Transform; it must contain a NetworkObject or the player object never appears and cannot move. Leave empty only for games without a player avatar.")]
        [SerializeField] private GameObject playerPrefab;

        [Tooltip("Prefab spawned for every connected client when Player Movement = Rigidbody. It must contain a NetworkObject, a Rigidbody and a RigidbodyPlayerMover; the project generates Assets/Prefabs/PlayerRigidbody.prefab and fills this in automatically.")]
        [SerializeField] private GameObject rigidbodyPlayerPrefab;

        [Tooltip("Which player prefab is used: Transform = SimplePlayerMover (no gravity/physics), Rigidbody = RigidbodyPlayerMover (gravity, jump, collisions). Switching only changes what is spawned; the scene itself does not change. If Rigidbody is selected but its prefab is empty, the Transform prefab is used instead.")]
        [SerializeField] private MPPlayerMovement playerMovement = MPPlayerMovement.Transform;

        [Tooltip("Scoreboard prefab the server spawns when the session starts; leave empty for games without a scoreboard. If set, it must contain a NetworkObject or [MP] logs ConfigError.")]
        [SerializeField] private GameObject scoreboardPrefab;

        [Tooltip("Prefabs that may be instantiated and spawned at runtime, such as bullets or pickups. Anything spawned after the session starts must be registered here and contain a NetworkObject; a missing entry makes Spawn fail.")]
        [SerializeField] private GameObject[] spawnablePrefabs = System.Array.Empty<GameObject>();

        [Tooltip("Address a client connects to when -mp-client is not passed. Use 127.0.0.1 for local testing and the server machine's LAN IP when joining from another computer.")]
        [SerializeField] private string defaultAddress = "127.0.0.1";

        [Tooltip("Seconds to wait before auto-connecting, so the scene can finish initializing first. The 0.5 s default is fine; too small may fail to connect, too large slows automation down.")]
        [SerializeField] private float autoConnectDelay = 0.5f;

        public ushort Port => port;
        public int MaxPlayers => maxPlayers;
        public MPNetMode StartMode => startMode;
        public string DefaultAddress => defaultAddress;

        /// <summary>The prefab that will actually be spawned: Rigidbody when selected and assigned, otherwise Transform.</summary>
        public GameObject PlayerPrefab =>
            playerMovement == MPPlayerMovement.Rigidbody && rigidbodyPlayerPrefab != null
                ? rigidbodyPlayerPrefab
                : playerPrefab;

        /// <summary>The Transform-based prefab (SimplePlayerMover), regardless of the selected mode.</summary>
        public GameObject TransformPlayerPrefab => playerPrefab;

        /// <summary>The Rigidbody-based prefab (RigidbodyPlayerMover), regardless of the selected mode.</summary>
        public GameObject RigidbodyPlayerPrefab => rigidbodyPlayerPrefab;

        /// <summary>Which movement sample the game uses.</summary>
        public MPPlayerMovement PlayerMovement => playerMovement;

        public string GameId => gameId;
        public GameObject ScoreboardPrefab => scoreboardPrefab;
        public GameObject[] SpawnablePrefabs => spawnablePrefabs;
        public float AutoConnectDelay => autoConnectDelay;

        /// <summary>Runtime overrides for tooling and CLI launches.</summary>
        public void SetPort(ushort value) => port = value;
        public void SetMaxPlayers(int value) => maxPlayers = Mathf.Max(1, value);
        public void SetStartMode(MPNetMode value) => startMode = value;
        public void SetDefaultAddress(string value) => defaultAddress = value;

        /// <summary>Sets the Transform-based player prefab (the classic sample).</summary>
        public void SetPlayerPrefab(GameObject value) => playerPrefab = value;

        /// <summary>Sets the Rigidbody-based player prefab (the physics sample).</summary>
        public void SetRigidbodyPlayerPrefab(GameObject value) => rigidbodyPlayerPrefab = value;

        /// <summary>Selects which player prefab is spawned. Safe to call before or after the prefabs are assigned.</summary>
        public void SetPlayerMovement(MPPlayerMovement value) => playerMovement = value;
        public void SetGameId(string value) => gameId = value;
        public void SetScoreboardPrefab(GameObject value) => scoreboardPrefab = value;
        public void SetSpawnablePrefabs(params GameObject[] value) => spawnablePrefabs = value ?? System.Array.Empty<GameObject>();

        private void OnValidate()
        {
            maxPlayers = Mathf.Clamp(maxPlayers, 1, 32);
            if (port == 0) port = 7777;
            if (string.IsNullOrWhiteSpace(defaultAddress)) defaultAddress = "127.0.0.1";
            if (string.IsNullOrWhiteSpace(gameId)) gameId = "default-game";
        }
    }
}
