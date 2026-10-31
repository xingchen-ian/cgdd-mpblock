using System;
using System.Collections;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace MPBlocks.Core
{
    /// <summary>
    /// Starts a network session from a <see cref="NetConfig"/> plus command-line arguments.
    ///
    /// Command line (see docs/AI_API.md):
    /// Flags are prefixed with "-mp-" because Unity reserves several bare names
    /// (-server, -client, -screen-*, ...). Colliding with them aborts the player
    /// before our code runs.
    ///
    ///   -mp-server                 host/server on NetConfig.Port
    ///   -mp-client &lt;ip&gt;           connect to ip
    ///   -mp-players &lt;n&gt;           override max players
    ///   -mp-quit-after &lt;seconds&gt;   exit after n seconds (automation)
    ///
    /// If no NetworkManager exists in the scene, one is created at runtime, so a scene
    /// only needs this component to be playable.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetBootstrap : MonoBehaviour
    {
        [Tooltip("NetConfig asset used for this session. If empty, a default config is created at runtime and a warning is logged, so real scenes should always assign one.")]
        [SerializeField] private NetConfig config;

        [Tooltip("Create a NetworkManager at runtime when the scene has none. Keep it on as a fallback, but real scenes should place a NetworkManager in the scene: creating one at runtime is too late for NGO's message handlers.")]
        [SerializeField] private bool createNetworkManagerIfMissing = true;

        [Tooltip("Start the session automatically from the command line (-mp-server to host, -mp-client to join). Disable it when a UI button or a script should control hosting and joining.")]
        [SerializeField] private bool autoStart = true;

        [Tooltip("Host a session when Play is pressed in the Editor without command-line flags, so the edit-play loop stays usable. This is ignored when the scene contains a NetSessionUI, which owns the controls.")]
        [SerializeField] private bool hostOnPlayInEditor = true;

        public NetConfig Config
        {
            get => config;
            set => config = value;
        }

        public NetworkManager Manager { get; private set; }
        public bool IsServer => Manager != null && Manager.IsServer;
        public bool IsClient => Manager != null && Manager.IsClient;

        private bool _started;
        private GameObject _scoreboardInstance;
        private string _lastClientAddress;
        private bool _lastWasServer;
        private bool _hasPendingStart;
        private bool _pendingServer;
        private string _pendingClient;

        private PlayerSpawnPoint[] _spawnPoints;
        private int _spawnCursor;

        private void Awake()
        {
            Manager = NetworkManager.Singleton;

            if (Manager == null)
            {
                if (!createNetworkManagerIfMissing)
                {
                    MPLog.Error(MPEvent.NoNetworkManager, ("hint", "add a NetworkManager or enable createNetworkManagerIfMissing"));
                    return;
                }

                Manager = CreateNetworkManager();
            }

            ApplyConfig();
            ApplyLatencyFromCommandLine();

            var args = ReadArguments(out var serverMode, out var clientAddress, out var playerOverride,
                out var quitAfter, out var gameIdOverride);

            if (!string.IsNullOrEmpty(gameIdOverride))
            {
                config.SetGameId(gameIdOverride);
            }

            if (playerOverride > 0)
            {
                config.SetMaxPlayers(playerOverride);
            }

            if (quitAfter > 0f)
            {
                Invoke(nameof(QuitNow), quitAfter);
            }

            bool uiPresent = FindObjectOfType<NetSessionUI>() != null;

            if (!args && Application.isEditor && hostOnPlayInEditor && !uiPresent)
            {
                serverMode = true;
                args = true;
                MPLog.Info("EDITOR_AUTOHOST", ("hint", "pass -mp-client <ip> to join instead"));
            }

            _hasPendingStart = autoStart && args;
            _pendingServer = serverMode;
            _pendingClient = clientAddress;
        }

        private void Start()
        {
            if (_hasPendingStart)
            {
                StartCoroutine(StartAfterInit());
            }
        }

        /// <summary>
        /// NGO registers its message handlers during initialization. Starting a session
        /// in Awake races that and produces "ConnectionRequestMessage not present in the
        /// dictionary" on clients, so the session starts one frame later.
        /// </summary>
        private IEnumerator StartAfterInit()
        {
            yield return null;

            if (_pendingServer)
            {
                StartServer();
            }
            else if (_pendingClient != null)
            {
                StartClient(_pendingClient);
            }
        }

        private NetworkManager CreateNetworkManager()
        {
            var go = new GameObject("NetworkManager");
            var nm = go.AddComponent<NetworkManager>();
            var transport = go.AddComponent<UnityTransport>();
            nm.NetworkConfig ??= new NetworkConfig();
            nm.NetworkConfig.NetworkTransport = transport;
            MPLog.Info("NETWORKMANAGER_CREATED");
            return nm;
        }

        private void ApplyConfig()
        {
            if (config == null)
            {
                MPLog.Warn(MPEvent.ConfigError, ("reason", "no NetConfig assigned; using defaults"));
                config = ScriptableObject.CreateInstance<NetConfig>();
            }

            var transport = Manager.GetComponent<UnityTransport>();
            if (transport == null)
            {
                transport = Manager.gameObject.AddComponent<UnityTransport>();
            }

            Manager.NetworkConfig ??= new NetworkConfig();
            Manager.NetworkConfig.NetworkTransport = transport;
            Manager.NetworkConfig.ConnectionApproval = true;

            if (config.PlayerMovement == MPPlayerMovement.Rigidbody && config.RigidbodyPlayerPrefab == null)
            {
                MPLog.Warn("PLAYER_PREFAB_FALLBACK",
                    ("reason", "Player Movement = Rigidbody but no rigidbody player prefab is assigned; using the Transform prefab"),
                    ("gameId", config.GameId));
            }

            if (config.PlayerPrefab != null)
            {
                Manager.NetworkConfig.PlayerPrefab = config.PlayerPrefab;
            }

            Manager.ConnectionApprovalCallback = (request, response) =>
            {
                // Session identity: a client must belong to this game. Without this,
                // a mistyped IP silently joins someone else's server and looks like a
                // sync bug instead of a configuration mistake.
                var requestedGameId = request.Payload != null && request.Payload.Length > 0
                    ? Encoding.UTF8.GetString(request.Payload)
                    : string.Empty;

                if (!string.Equals(requestedGameId, config.GameId, StringComparison.Ordinal))
                {
                    response.Approved = false;
                    response.Reason = "wrong game";
                    MPLog.Warn(MPEvent.SessionIdMismatch, ("expected", config.GameId), ("got", requestedGameId));
                    return;
                }

                var current = Manager.ConnectedClientsIds.Count;
                var allowed = current < config.MaxPlayers;
                response.Approved = allowed;
                response.CreatePlayerObject = config.PlayerPrefab != null;

                if (!allowed)
                {
                    response.Reason = "server full";
                }
                else
                {
                    MPLog.Info(MPEvent.SessionIdOk, ("gameId", config.GameId), ("clients", current));

                    // Player objects are owner-authoritative, so the server cannot move a
                    // client's body after it spawns: the owner would immediately overwrite
                    // it. The approval response is the only correct moment to place it.
                    var spawnPoint = NextSpawnPoint();
                    if (response.CreatePlayerObject && spawnPoint != null)
                    {
                        response.Position = spawnPoint.Position;
                        response.Rotation = spawnPoint.Rotation;
                        MPLog.Info("SPAWN_POINT_ASSIGNED", ("clientId", request.ClientNetworkId), ("point", spawnPoint.name));
                    }
                }
            };

            if (config.ScoreboardPrefab != null)
            {
                EnsurePrefabRegistered(config.ScoreboardPrefab);
            }

            if (config.SpawnablePrefabs != null)
            {
                foreach (var spawnable in config.SpawnablePrefabs)
                {
                    if (spawnable != null) EnsurePrefabRegistered(spawnable);
                }
            }

            Manager.OnClientConnectedCallback += OnClientConnected;
            Manager.OnClientDisconnectCallback += OnClientDisconnected;
            Manager.OnServerStarted += OnServerStarted;
        }

        private void ApplyLatencyFromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            int delay = 0, jitter = 0, drop = 0;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-mp-delay":
                        if (i + 1 < args.Length) int.TryParse(args[i + 1], out delay);
                        break;
                    case "-mp-jitter":
                        if (i + 1 < args.Length) int.TryParse(args[i + 1], out jitter);
                        break;
                    case "-mp-drop":
                        if (i + 1 < args.Length) int.TryParse(args[i + 1], out drop);
                        break;
                }
            }

            if (delay <= 0 && jitter <= 0 && drop <= 0) return;

            ApplySimulator(delay, jitter, drop);
        }

        private void ApplySimulator(int delayMs, int jitterMs, int dropPercent)
        {
            var transport = Manager != null ? Manager.GetComponent<UnityTransport>() : null;
            if (transport == null) return;

            transport.SetDebugSimulatorParameters(Mathf.Max(0, delayMs), Mathf.Max(0, jitterMs), Mathf.Clamp(dropPercent, 0, 100));
            MPLog.Info("LATENCY_APPLIED", ("delay", delayMs), ("jitter", jitterMs), ("drop", dropPercent));
        }

        /// <summary>
        /// Restarts the session with new network conditions. Unity Transport only accepts
        /// simulator parameters before a connection exists, so "apply" means "reconnect".
        /// </summary>
        public void RestartWithSimulator(int delayMs, int jitterMs, int dropPercent)
        {
            StartCoroutine(RestartRoutine(delayMs, jitterMs, dropPercent));
        }

        private IEnumerator RestartRoutine(int delayMs, int jitterMs, int dropPercent)
        {
            MPLog.Info("LATENCY_RESTART", ("delay", delayMs), ("jitter", jitterMs), ("drop", dropPercent));

            bool wasServer = _lastWasServer || (Manager != null && Manager.IsServer);
            string address = _lastClientAddress;

            if (Manager != null && Manager.IsListening) Manager.Shutdown();
            yield return null;
            yield return null;

            ApplySimulator(delayMs, jitterMs, dropPercent);

            _started = false;
            if (Manager != null && Manager.IsListening) yield break;

            if (wasServer) StartServer();
            else if (!string.IsNullOrEmpty(address)) StartClient(address);
        }

        private void OnServerStarted()
        {
            MPLog.Info(MPEvent.ServerReady, ("port", config.Port), ("maxPlayers", config.MaxPlayers));

            if (config.ScoreboardPrefab != null && _scoreboardInstance == null)
            {
                var instance = Instantiate(config.ScoreboardPrefab);
                instance.name = "Scoreboard";

                var networkObject = instance.GetComponent<NetworkObject>();
                if (networkObject == null)
                {
                    MPLog.Error(MPEvent.ConfigError, ("reason", "scoreboard prefab has no NetworkObject"));
                    Destroy(instance);
                }
                else
                {
                    networkObject.Spawn();
                    _scoreboardInstance = instance;
                    MPLog.Info("SCOREBOARD_SPAWNED");
                }
            }
        }

        private void EnsurePrefabRegistered(GameObject prefab)
        {
            if (prefab == null) return;

            var prefabs = Manager.NetworkConfig.Prefabs;
            if (prefabs.Contains(prefab)) return;

            if (prefab.GetComponent<NetworkObject>() == null)
            {
                MPLog.Error(MPEvent.ConfigError, ("reason", "scoreboard prefab has no NetworkObject"), ("prefab", prefab.name));
                return;
            }

            prefabs.Add(new NetworkPrefab { Prefab = prefab });
            MPLog.Info("PREFAB_REGISTERED", ("prefab", prefab.name));
        }

        private void OnClientConnected(ulong clientId)
        {
            if (Manager.IsServer)
            {
                MPLog.Info(MPEvent.ClientJoined, ("clientId", clientId), ("players", Manager.ConnectedClientsIds.Count));
            }
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (Manager.IsServer)
            {
                MPLog.Info(MPEvent.ClientLeft, ("clientId", clientId));
            }
        }

        /// <summary>
        /// Sorted by name so "host gets point A, first client point B" is reproducible
        /// instead of depending on Unity's object enumeration order.
        /// </summary>
        private void CollectSpawnPoints()
        {
            _spawnPoints = FindObjectsOfType<PlayerSpawnPoint>(true);
            _spawnCursor = 0;

            if (_spawnPoints == null || _spawnPoints.Length == 0)
            {
                _spawnPoints = null;
                return;
            }

            Array.Sort(_spawnPoints, (a, b) => string.CompareOrdinal(a.name, b.name));
            MPLog.Info("SPAWN_POINTS_READY", ("count", _spawnPoints.Length));
        }

        private PlayerSpawnPoint NextSpawnPoint()
        {
            if (_spawnPoints == null || _spawnPoints.Length == 0) return null;

            var point = _spawnPoints[_spawnCursor % _spawnPoints.Length];
            _spawnCursor++;
            return point;
        }

        /// <summary>Start as server + player (or dedicated server when mode says so).</summary>
        public void StartServer()
        {
            if (Manager != null && Manager.IsListening) return;
            _started = true;

            CollectSpawnPoints();

            var transport = Manager.GetComponent<UnityTransport>();
            transport.SetConnectionData("0.0.0.0", config.Port, "0.0.0.0");

            // The host is also a client; it must present the same session id so the
            // approval callback identifies it correctly instead of seeing an empty payload.
            Manager.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(config.GameId);

            bool ok = config.StartMode == MPNetMode.DedicatedServer
                ? Manager.StartServer()
                : Manager.StartHost();

            if (!ok)
            {
                MPLog.Error("START_FAILED", ("role", config.StartMode.ToString()));
                return;
            }

            _lastWasServer = true;
            MPLog.Info("STARTED", ("role", config.StartMode.ToString()), ("port", config.Port));
        }

        /// <summary>Start as client and connect to <paramref name="address"/>.</summary>
        public void StartClient(string address)
        {
            if (Manager != null && Manager.IsListening) return;
            _started = true;

            var transport = Manager.GetComponent<UnityTransport>();
            transport.SetConnectionData(address, config.Port);

            Manager.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(config.GameId);

            _lastWasServer = false;
            _lastClientAddress = address;

            MPLog.Info(MPEvent.ClientConnecting, ("address", address), ("port", config.Port), ("gameId", config.GameId));

            if (!Manager.StartClient())
            {
                MPLog.Error("START_FAILED", ("role", "Client"), ("address", address));
            }
        }

        private static bool ReadArguments(out bool serverMode, out string clientAddress,
            out int playerOverride, out float quitAfter, out string gameIdOverride)
        {
            serverMode = false;
            clientAddress = null;
            playerOverride = 0;
            quitAfter = 0f;
            gameIdOverride = null;

            string[] args;
            try
            {
                args = Environment.GetCommandLineArgs();
            }
            catch
            {
                return false;
            }

            bool any = false;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-mp-server":
                        serverMode = true;
                        any = true;
                        break;

                    case "-mp-client":
                        if (i + 1 < args.Length)
                        {
                            clientAddress = args[i + 1];
                            any = true;
                            i++;
                        }
                        break;

                    case "-mp-game":
                        if (i + 1 < args.Length)
                        {
                            gameIdOverride = args[i + 1];
                            any = true;
                            i++;
                        }
                        break;

                    case "-mp-players":
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out var p))
                        {
                            playerOverride = p;
                            i++;
                        }
                        break;

                    case "-mp-quit-after":
                        if (i + 1 < args.Length && float.TryParse(args[i + 1],
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var q))
                        {
                            quitAfter = q;
                            i++;
                        }
                        break;
                }
            }

            return any;
        }

        private void QuitNow()
        {
            MPLog.Info("QUIT_REQUESTED");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
