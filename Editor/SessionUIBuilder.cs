using MPBlocks.Core;
using MPBlocks.Gameplay;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MPBlocks.Editor
{
    /// <summary>
    /// Builds the session UI (host / join / leave / player list / refusal reason) with
    /// uGUI, from code. Generated rather than hand-authored so the scene stays
    /// reproducible and agents never have to edit scene YAML.
    /// </summary>
    public static class SessionUIBuilder
    {
        private static Font _font;

        public static void Build(NetBootstrap bootstrap)
        {
            EnsureEventSystem();

            var canvasGo = new GameObject("SessionCanvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var panel = MakePanel(canvasGo.transform);

            var title = MakeText(panel, "Title", "MULTIPLAYER", 22, FontStyle.Bold, Color.white);
            title.gameObject.name = "Title";

            var status = MakeText(panel, "Status", "Not connected", 18, FontStyle.Normal, Color.white);
            var players = MakeText(panel, "Players", string.Empty, 16, FontStyle.Normal, new Color(0.85f, 0.85f, 0.85f));

            var nameInput = MakeInputField(panel, "NameInput", "Your name");

            var joinRow = new GameObject("JoinRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            joinRow.transform.SetParent(panel, false);
            var rowLayout = joinRow.GetComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 8f;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;

            var address = MakeInputField(joinRow.transform, "Address", "127.0.0.1");
            var joinButton = MakeButton(joinRow.transform, "JoinButton", "Join");

            var hostButton = MakeButton(panel, "HostButton", "Host");
            var leaveButton = MakeButton(panel, "LeaveButton", "Leave");

            var reason = MakeText(panel, "Reason", string.Empty, 16, FontStyle.Bold, new Color(1f, 0.45f, 0.45f));

            var ui = canvasGo.AddComponent<NetSessionUI>();

            var so = new SerializedObject(ui);
            Assign(so, "bootstrap", bootstrap);
            Assign(so, "statusText", status);
            Assign(so, "playerListText", players);
            Assign(so, "reasonText", reason);
            Assign(so, "nameInput", nameInput);
            Assign(so, "addressInput", address);
            Assign(so, "hostButton", hostButton);
            Assign(so, "joinButton", joinButton);
            Assign(so, "leaveButton", leaveButton);
            so.ApplyModifiedPropertiesWithoutUndo();

            reason.gameObject.name = "Reason";

            BuildScorePanel(canvasGo.transform);
            Debug.Log("[MP] INFO SETUP_SESSION_UI_CREATED");
        }

        /// <summary>
        /// A read-only score panel in the top-right corner, driven by the runtime-spawned
        /// Scoreboard. Groups that want their own presentation can delete this.
        /// </summary>
        private static void BuildScorePanel(Transform canvas)
        {
            var go = new GameObject("ScorePanel", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(canvas, false);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-24f, -24f);
            rt.sizeDelta = new Vector2(300f, 130f);

            var image = go.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.65f);

            var text = MakeText(go.transform, "Scores", string.Empty, 20, FontStyle.Bold, Color.white);
            var textRt = (RectTransform)text.transform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(16f, 12f);
            textRt.offsetMax = new Vector2(-16f, -12f);
            text.alignment = TextAnchor.UpperLeft;

            var element = text.GetComponent<LayoutElement>();
            if (element != null) Object.DestroyImmediate(element);

            var hud = go.AddComponent<ScoreboardHUD>();
            var so = new SerializedObject(hud);
            Assign(so, "target", text);
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log("[MP] INFO SETUP_SCORE_PANEL_CREATED");
        }

        private static void Assign(SerializedObject so, string field, Object value)
        {
            var prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogWarning("[MP] WARN SETUP_UI_FIELD_MISSING field=" + field);
                return;
            }

            prop.objectReferenceValue = value;
        }

        private static void EnsureEventSystem()
        {
            if (Object.FindObjectOfType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        private static Font BuiltinFont()
        {
            if (_font != null) return _font;

            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return _font;
        }

        private static RectTransform MakePanel(Transform parent)
        {
            var go = new GameObject("SessionPanel", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(24f, -24f);
            rt.sizeDelta = new Vector2(480f, 100f);

            var image = go.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.75f);

            var layout = go.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 16, 16);
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = go.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            return rt;
        }

        private static Text MakeText(Transform parent, string name, string content, int size, FontStyle style, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);

            var text = go.GetComponent<Text>();
            text.font = BuiltinFont();
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.text = content;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = false;

            var element = go.AddComponent<LayoutElement>();
            element.minHeight = size + 6f;

            return text;
        }

        private static Button MakeButton(Transform parent, string name, string label)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var image = go.GetComponent<Image>();
            image.color = new Color(0.20f, 0.42f, 0.72f, 1f);

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;

            var element = go.AddComponent<LayoutElement>();
            element.minHeight = 40f;
            element.preferredWidth = 120f;

            var text = MakeText(go.transform, "Label", label, 18, FontStyle.Bold, Color.white);
            text.alignment = TextAnchor.MiddleCenter;
            var textRt = (RectTransform)text.transform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            var textElement = text.GetComponent<LayoutElement>();
            if (textElement != null) Object.DestroyImmediate(textElement);

            return button;
        }

        /// <summary>Public wrapper so the upgrader can add a field to an existing scene.</summary>
        public static InputField CreateInputField(Transform parent, string name, string placeholderText)
            => MakeInputField(parent, name, placeholderText);

        private static InputField MakeInputField(Transform parent, string name, string placeholderText)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(InputField));
            go.transform.SetParent(parent, false);

            var image = go.GetComponent<Image>();
            image.color = new Color(0.12f, 0.12f, 0.12f, 1f);

            var field = go.GetComponent<InputField>();

            var text = MakeText(go.transform, "Text", string.Empty, 16, FontStyle.Normal, Color.white);
            var textRt = (RectTransform)text.transform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(8f, 2f);
            textRt.offsetMax = new Vector2(-8f, -2f);
            text.alignment = TextAnchor.MiddleLeft;
            var textElement = text.GetComponent<LayoutElement>();
            if (textElement != null) Object.DestroyImmediate(textElement);

            var placeholder = MakeText(go.transform, "Placeholder", placeholderText, 16, FontStyle.Italic,
                new Color(0.6f, 0.6f, 0.6f));
            var placeholderRt = (RectTransform)placeholder.transform;
            placeholderRt.anchorMin = Vector2.zero;
            placeholderRt.anchorMax = Vector2.one;
            placeholderRt.offsetMin = new Vector2(8f, 2f);
            placeholderRt.offsetMax = new Vector2(-8f, -2f);
            placeholder.alignment = TextAnchor.MiddleLeft;
            var placeholderElement = placeholder.GetComponent<LayoutElement>();
            if (placeholderElement != null) Object.DestroyImmediate(placeholderElement);

            field.textComponent = text;
            field.placeholder = placeholder;
            field.text = placeholderText;
            field.lineType = InputField.LineType.SingleLine;

            var element = go.AddComponent<LayoutElement>();
            element.minHeight = 40f;
            element.minWidth = 240f;
            element.flexibleWidth = 1f;

            return field;
        }
    }
}
