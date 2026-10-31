using System.Collections;
using MPBlocks.Core;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace MPBlocks.Diagnostics
{
    /// <summary>
    /// Injects artificial delay, jitter and packet loss into this machine's connection, so
    /// students can feel what their game is like on a bad network. On a LAN everything is
    /// instantaneous, which hides every latency problem a multiplayer game has.
    ///
    /// Unity Transport only accepts simulator settings **before** a connection starts, so
    /// changing a slider restarts the session. That constraint is itself worth teaching.
    ///
    /// Press F3 for the panel. Command line: -mp-delay, -mp-jitter, -mp-drop.
    /// </summary>
    public sealed class LatencySimulator : MonoBehaviour
    {
        [Header("MPBlocks")]
        [Tooltip("NetBootstrap to restart; when empty the one in the scene is found automatically, so dragging it in is usually unnecessary. Without one, Apply only logs an [MP] warning.")]
        [SerializeField] private NetBootstrap bootstrap;

        [Tooltip("Key that shows and hides the panel; F3 by default. Keep it different from NetDiag's F2.")]
        [SerializeField] private KeyCode toggleKey = KeyCode.F3;

        [Tooltip("Show the panel without pressing the toggle key, which is handy for demos and screenshots. Leave it off for normal play so it does not cover the view.")]
        [SerializeField] private bool visibleOnStart;

        [Header("Current values (restart to apply)")]
        [Tooltip("Artificial delay added to every packet, in milliseconds. Unity Transport only accepts this before a connection starts, so press Apply and reconnect to restart the session.")]
        [SerializeField] private int delayMs;
        [Tooltip("Random variation added on top of the delay, in milliseconds, to simulate an unstable network. Like the delay it only applies before connecting, so restart the session after changing it.")]
        [SerializeField] private int jitterMs;
        [Tooltip("Percentage of packets to drop, to simulate bad WiFi. It only applies before connecting, so press Apply and reconnect for the change to take effect.")]
        [SerializeField] private int dropPercent;

        private bool _visible;
        private GUIStyle _label;
        private GUIStyle _warning;

        public int DelayMs => delayMs;
        public int JitterMs => jitterMs;
        public int DropPercent => dropPercent;

        private void Awake()
        {
            if (bootstrap == null) bootstrap = FindObjectOfType<NetBootstrap>();
            _visible = visibleOnStart;

            ParseArguments();
        }

        private void ParseArguments()
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-mp-delay":
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out var d)) delayMs = Mathf.Max(0, d);
                        break;
                    case "-mp-jitter":
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out var j)) jitterMs = Mathf.Max(0, j);
                        break;
                    case "-mp-drop":
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out var p)) dropPercent = Mathf.Clamp(p, 0, 100);
                        break;
                }
            }

            if (delayMs > 0 || jitterMs > 0 || dropPercent > 0)
            {
                MPLog.Info("LATENCY_FROM_CLI", ("delay", delayMs), ("jitter", jitterMs), ("drop", dropPercent));
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey)) _visible = !_visible;
        }

        private void OnGUI()
        {
            if (!_visible) return;

            EnsureStyles();

            var area = new Rect(24f, Screen.height - 210f, 420f, 186f);
            GUI.Box(area, string.Empty);

            GUI.Label(new Rect(area.x + 14f, area.y + 8f, 380f, 24f), "Network conditions (F3)", _label);

            delayMs = Mathf.RoundToInt(GUI.HorizontalSlider(
                new Rect(area.x + 150f, area.y + 40f, 240f, 20f), delayMs, 0f, 500f));
            GUI.Label(new Rect(area.x + 14f, area.y + 34f, 130f, 24f), $"Delay {delayMs} ms", _label);

            jitterMs = Mathf.RoundToInt(GUI.HorizontalSlider(
                new Rect(area.x + 150f, area.y + 70f, 240f, 20f), jitterMs, 0f, 200f));
            GUI.Label(new Rect(area.x + 14f, area.y + 64f, 130f, 24f), $"Jitter {jitterMs} ms", _label);

            dropPercent = Mathf.RoundToInt(GUI.HorizontalSlider(
                new Rect(area.x + 150f, area.y + 100f, 240f, 20f), dropPercent, 0f, 50f));
            GUI.Label(new Rect(area.x + 14f, area.y + 94f, 130f, 24f), $"Drop {dropPercent}%", _label);

            GUI.Label(new Rect(area.x + 14f, area.y + 124f, 390f, 20f),
                "Changing these restarts the session.", _warning);

            if (GUI.Button(new Rect(area.x + 14f, area.y + 148f, 180f, 30f), "Apply and reconnect"))
            {
                Apply();
            }

            if (GUI.Button(new Rect(area.x + 204f, area.y + 148f, 120f, 30f), "Reset"))
            {
                delayMs = 0;
                jitterMs = 0;
                dropPercent = 0;
                Apply();
            }
        }

        private void EnsureStyles()
        {
            if (_label != null) return;

            _label = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleLeft };
            _warning = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Italic,
                alignment = TextAnchor.MiddleLeft,
            };
            _warning.normal.textColor = new Color(1f, 0.8f, 0.4f);
        }

        /// <summary>Applies the current values and restarts the session so they take effect.</summary>
        public void Apply()
        {
            if (bootstrap == null)
            {
                MPLog.Warn(MPEvent.ConfigError, ("reason", "LatencySimulator has no NetBootstrap"));
                return;
            }

            bootstrap.RestartWithSimulator(delayMs, jitterMs, dropPercent);
        }

        /// <summary>Applies the configured values without restarting (call before StartClient/StartServer).</summary>
        public void ApplyWithoutRestart()
        {
            var manager = NetworkManager.Singleton;
            if (manager == null) return;

            var transport = manager.GetComponent<UnityTransport>();
            if (transport == null) return;

            transport.SetDebugSimulatorParameters(delayMs, jitterMs, dropPercent);
            MPLog.Info("LATENCY_APPLIED", ("delay", delayMs), ("jitter", jitterMs), ("drop", dropPercent));
        }
    }
}
