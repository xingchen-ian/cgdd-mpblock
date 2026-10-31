using Unity.Netcode;
using UnityEngine;

namespace MPBlocks.Gameplay
{
    /// <summary>
    /// Minimal on-screen health readout for the local player, drawn with IMGUI so it needs
    /// no canvas, no prefab wiring and no setup.
    ///
    /// This is a placeholder for prototyping. Groups that want a real HUD should replace
    /// it — the data they need is on <see cref="Health.Changed"/>.
    /// </summary>
    public sealed class HealthHUD : MonoBehaviour
    {
        [Tooltip("Pixel offset from the top of the screen: x to the right, y down; the default is horizontally centred with a 16 px top margin. It is drawn only when a local player exists, and changing it only moves the HUD.")]
        [SerializeField] private Vector2 offset = new Vector2(0f, 16f);

        [Tooltip("HUD font size in pixels; it also sets the text box height (font size + 10). This is a placeholder IMGUI implementation, so a large value can overlap the screen top or other HUDs; replace it with a Canvas for real UI.")]
        [SerializeField] private int fontSize = 20;

        private GUIStyle _style;
        private Health _health;

        private void Update()
        {
            if (_health != null) return;

            var manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening) return;
            if (manager.LocalClient == null || manager.LocalClient.PlayerObject == null) return;

            _health = manager.LocalClient.PlayerObject.GetComponent<Health>();
        }

        private void OnGUI()
        {
            var health = _health;
            if (health == null) return;

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = fontSize,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                };
            }

            var alive = health.Current > 0;
            _style.normal.textColor = alive ? Color.white : new Color(1f, 0.4f, 0.4f);

            var text = $"HP {health.Current} / {health.Max}";
            var rect = new Rect(
                Screen.width * 0.5f - 120f + offset.x,
                offset.y,
                240f,
                fontSize + 10f);

            GUI.Label(rect, text, _style);
        }
    }
}
