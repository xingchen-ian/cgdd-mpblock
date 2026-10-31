using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace MPBlocks.Gameplay
{
    /// <summary>
    /// Renders a <see cref="Scoreboard"/> into a uGUI text block.
    ///
    /// This is the optional "make it visible" half of the scoreboard: keep the data block
    /// if you want your own presentation, or drop this prefab/component in to get a
    /// readable board for free.
    /// </summary>
    public sealed class ScoreboardHUD : MonoBehaviour
    {
        [Header("MPBlocks")]
        [Tooltip("uGUI Text that draws the score lines. If empty, Awake first looks for a Text on this object and then in its children; with none found nothing is drawn.")]
        [SerializeField] private Text target;

        [Tooltip("Scoreboard to display; optional. If empty, it searches the scene for the first Scoreboard every searchInterval seconds, because the scoreboard is usually spawned at runtime by the server.")]
        [SerializeField] private Scoreboard scoreboard;

        [Tooltip("Seconds between re-searches while scoreboard is empty. It only affects the lookup frequency, not live refreshes after subscribing; a large value makes the HUD appear late.")]
        [SerializeField] private float searchInterval = 1f;

        private readonly StringBuilder _builder = new StringBuilder(128);
        private float _searchTimer;

        private void Awake()
        {
            if (target == null) target = GetComponent<Text>();
            if (target == null) target = GetComponentInChildren<Text>();
        }

        private void Update()
        {
            if (scoreboard == null)
            {
                _searchTimer += Time.deltaTime;
                if (_searchTimer < searchInterval) return;
                _searchTimer = 0f;

                scoreboard = FindObjectOfType<Scoreboard>();
                if (scoreboard != null) Subscribe();
            }

            Render();
        }

        private void Subscribe()
        {
            if (scoreboard == null) return;
            scoreboard.Changed -= Render;
            scoreboard.Changed += Render;
        }

        private void Render()
        {
            if (target == null) return;

            if (scoreboard == null || scoreboard.SlotCount == 0)
            {
                target.text = string.Empty;
                return;
            }

            _builder.Clear();
            for (int i = 0; i < scoreboard.SlotCount; i++)
            {
                _builder.Append(scoreboard.SlotName(i)).Append("   ").Append(scoreboard.GetScore(i));
                if (i < scoreboard.SlotCount - 1) _builder.Append('\n');
            }

            target.text = _builder.ToString();
        }
    }
}
