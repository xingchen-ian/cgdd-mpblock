using System;
using UnityEngine;

namespace MPBlocks.Core
{
    /// <summary>
    /// One character a player can pick. A character is either a colour tint on the player's
    /// body or, when <see cref="VisualPrefab"/> is set, a visual model instantiated under the
    /// player. Keep visual prefabs free of colliders, cameras and gameplay scripts: the player
    /// object itself owns movement, health and networking.
    /// </summary>
    [Serializable]
    public sealed class CharacterDefinition
    {
        [Tooltip("Name shown on the character-select panel and in [MP] logs.")]
        [SerializeField] private string displayName = "Character";

        [Tooltip("Colour tinted on the player's body renderer when no Visual Prefab is assigned. URP uses _BaseColor; the built-in pipeline uses _Color; both are set.")]
        [SerializeField] private Color color = Color.white;

        [Tooltip("Optional visual prefab instantiated under the player as this character's model. Leave empty to tint the body renderer instead. The prefab should contain no colliders, camera or scripts that need ownership.")]
        [SerializeField] private GameObject visualPrefab;

        /// <summary>Name shown on the select panel and in [MP] logs.</summary>
        public string DisplayName => displayName;

        /// <summary>Tint used when no visual prefab is assigned.</summary>
        public Color Color => color;

        /// <summary>Optional model instantiated for this character. May be null.</summary>
        public GameObject VisualPrefab => visualPrefab;

        /// <summary>Code-first: set the display name.</summary>
        public void SetDisplayName(string value) => displayName = value ?? "Character";

        /// <summary>Code-first: set the tint used without a visual prefab.</summary>
        public void SetColor(Color value) => color = value;

        /// <summary>Code-first: set the optional visual prefab.</summary>
        public void SetVisualPrefab(GameObject value) => visualPrefab = value;
    }

    /// <summary>
    /// The list of characters offered to players at the start of a session.
    ///
    /// Assign it to <see cref="NetConfig.CharacterRoster"/> and put a
    /// <c>CharacterSelect</c> object in the scene; the panel then
    /// lets every player claim one character (server-authoritative, late joiners included).
    ///
    /// Create with: Assets > Create > MPBlocks > Character Roster.
    /// </summary>
    [CreateAssetMenu(menuName = "MPBlocks/Character Roster", fileName = "CharacterRoster")]
    public sealed class CharacterRoster : ScriptableObject
    {
        [Tooltip("Characters in the order the select panel shows them. Two to eight entries works well; an empty roster disables character selection.")]
        [SerializeField] private CharacterDefinition[] characters = Array.Empty<CharacterDefinition>();

        /// <summary>Number of characters in the roster.</summary>
        public int Count => characters?.Length ?? 0;

        /// <summary>The definitions, in panel order. May be empty.</summary>
        public CharacterDefinition[] Characters => characters;

        /// <summary>Returns the definition at <paramref name="index"/>, or null when out of range.</summary>
        public CharacterDefinition Get(int index)
        {
            if (characters == null || index < 0 || index >= characters.Length) return null;
            return characters[index];
        }

        /// <summary>Code-first: replace the roster contents.</summary>
        public void SetCharacters(CharacterDefinition[] value)
        {
            characters = value ?? Array.Empty<CharacterDefinition>();
        }
    }
}
