using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using MPBlocks.Core;
using Unity.Netcode;
using UnityEngine;

namespace MPBlocks.Diagnostics
{
    /// <summary>
    /// Press F2 for the diagnostic overlay.
    ///
    /// On a LAN the hardest question is always "why can this machine not reach that one",
    /// and the answer is almost never visible in the game. This panel makes it visible:
    /// every local IPv4 address (so you know what to type on the other machine), the port
    /// in use, the session id, the connection role, and a rolling log of connection
    /// events.
    /// </summary>
    public sealed class NetDiag : MonoBehaviour
    {
        [Header("MPBlocks")]
        [Tooltip("NetBootstrap used to display port and gameId; when empty the one in the scene is found automatically, so dragging it in is usually unnecessary.")]
        [SerializeField] private NetBootstrap bootstrap;

        [Tooltip("Key that shows and hides the diagnostic overlay; F2 by default. Keep it different from LatencySimulator's F3.")]
        [SerializeField] private KeyCode toggleKey = KeyCode.F2;

        [Tooltip("Show the diagnostic overlay without pressing the toggle key; useful when debugging why a machine cannot connect.")]
        [SerializeField] private bool visibleOnStart;

        [Tooltip("How many recent connection events (connect, disconnect, server started) the overlay keeps. 10 is plenty; a much larger number overflows the panel.")]
        [SerializeField] private int logLines = 10;

        private readonly List<string> _events = new List<string>();
        private readonly StringBuilder _builder = new StringBuilder(512);

        private bool _visible;
        private bool _subscribed;
        private NetworkManager _manager;
        private GUIStyle _style;
        private GUIStyle _header;

        private void Awake()
        {
            if (bootstrap == null) bootstrap = FindObjectOfType<NetBootstrap>();
            _visible = visibleOnStart;
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey)) _visible = !_visible;

            if (_subscribed) return;

            _manager = NetworkManager.Singleton;
            if (_manager == null) return;

            _manager.OnClientConnectedCallback += OnConnected;
            _manager.OnClientDisconnectCallback += OnDisconnected;
            _manager.OnServerStarted += OnServerStarted;
            _subscribed = true;
        }

        private void OnDestroy()
        {
            if (!_subscribed || _manager == null) return;

            _manager.OnClientConnectedCallback -= OnConnected;
            _manager.OnClientDisconnectCallback -= OnDisconnected;
            _manager.OnServerStarted -= OnServerStarted;
        }

        private void OnConnected(ulong clientId) => Push($"connected clientId={clientId}");
        private void OnDisconnected(ulong clientId) => Push($"disconnected clientId={clientId}");
        private void OnServerStarted() => Push("server started");

        private void Push(string line)
        {
            _events.Add(line);
            while (_events.Count > Mathf.Max(1, logLines)) _events.RemoveAt(0);
        }

        private void OnGUI()
        {
            if (!_visible) return;

            EnsureStyles();

            var manager = NetworkManager.Singleton;
            var config = bootstrap != null ? bootstrap.Config : null;

            _builder.Clear();
            _builder.Append("NETDIAG (F2)\n");

            if (manager == null)
            {
                _builder.Append("no NetworkManager\n");
            }
            else
            {
                var role = manager.IsServer ? (manager.IsClient ? "host" : "server") : (manager.IsClient ? "client" : "offline");
                _builder.Append("role     ").Append(role).Append('\n');
                _builder.Append("listening ").Append(manager.IsListening).Append('\n');
                _builder.Append("clientId ").Append(manager.LocalClientId).Append('\n');
                _builder.Append("players  ").Append(manager.ConnectedClientsIds.Count).Append('\n');

                var reason = SafeReason(manager);
                if (!string.IsNullOrEmpty(reason)) _builder.Append("refused  ").Append(reason).Append('\n');
            }

            if (config != null)
            {
                _builder.Append("port     ").Append(config.Port).Append('\n');
                _builder.Append("gameId   ").Append(config.GameId).Append('\n');
            }

            _builder.Append("\nlocal addresses\n");
            foreach (var address in LocalAddresses())
            {
                _builder.Append("  ").Append(address).Append('\n');
            }

            _builder.Append("\nevents\n");
            if (_events.Count == 0)
            {
                _builder.Append("  (none)\n");
            }
            else
            {
                foreach (var entry in _events)
                {
                    _builder.Append("  ").Append(entry).Append('\n');
                }
            }

            var height = 250f;
            var area = new Rect(Screen.width - 380f - 24f, Screen.height - height - 24f, 380f, height);
            GUI.Box(area, string.Empty);
            GUI.Label(new Rect(area.x + 14f, area.y + 10f, area.width - 28f, area.height - 20f), _builder.ToString(), _style);
        }

        private void EnsureStyles()
        {
            if (_style != null) return;

            _style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                alignment = TextAnchor.UpperLeft,
                richText = false,
            };
            _header = new GUIStyle(_style) { fontStyle = FontStyle.Bold };
        }

        private static string SafeReason(NetworkManager manager)
        {
            try
            {
                return manager.DisconnectReason;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Every non-loopback IPv4 address on this machine, so students know what to type.</summary>
        public static List<string> LocalAddresses()
        {
            var result = new List<string>();

            try
            {
                foreach (var address in Dns.GetHostAddresses(Dns.GetHostName()))
                {
                    if (address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (IPAddress.IsLoopback(address)) continue;
                    result.Add(address.ToString());
                }
            }
            catch
            {
                // ignored
            }

            if (result.Count == 0) result.Add("127.0.0.1");

            return result;
        }
    }
}
