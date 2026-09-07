using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Sokoban.Runtime
{
    /// <summary>Small, shared visual vocabulary for the runtime screens.</summary>
    public static class UiFactory
    {
        public const float ReferenceWidth = 1600;
        public const float ReferenceHeight = 900;
        public static readonly Color Ink = Hex(0xECEAE4);
        public static readonly Color Muted = Hex(0xA7B1C0);
        public static readonly Color Paper = Hex(0x232735);
        public static readonly Color White = Hex(0x343B4C);
        public static readonly Color Teal = Hex(0xF2D36B);
        public static readonly Color PaleTeal = Hex(0x555442);
        public static readonly Color Gold = Hex(0xDFA164);
        private static TMP_FontAsset font;

        public static Color Hex(uint value) => new Color(((value >> 16) & 255) / 255f, ((value >> 8) & 255) / 255f, (value & 255) / 255f, 1);

#if UNITY_EDITOR
        public static Canvas Canvas(Transform parent)
        {
            var go = new GameObject("Game interface", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(parent, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            return canvas;
        }

        public static RectTransform Root(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            // Layout coordinates always address a centered reference canvas.
            // Expand keeps that complete rectangle visible in any window aspect.
            rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f);
            rt.pivot = new Vector2(.5f, .5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(ReferenceWidth, ReferenceHeight);
            return rt;
        }

        // Positions use the top-left corner of the 1600 x 900 reference canvas.
        public static RectTransform Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        public static Image Panel(Transform parent, string name, float x, float y, float width, float height, Color color, bool framed = true)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            Place(image.rectTransform, x, y, width, height);
            image.color = color;
            image.raycastTarget = false;
            if (framed)
            {
                var border = go.AddComponent<Outline>();
                border.effectColor = Hex(0x687489);
                border.effectDistance = new Vector2(2, -2);
                border.useGraphicAlpha = false;
            }
            return image;
        }

        public static TMP_Text Text(Transform parent, string name, string value, float x, float y, float width, float height,
            float size = 24, Color? color = null, FontStyles style = FontStyles.Normal, TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            Place(text.rectTransform, x, y, width, height);
            if (font == null)
            {
                font = Resources.Load<TMP_FontAsset>("Fonts/SokobanChinese");
                if (font == null) font = TMP_Settings.defaultFontAsset;
                if (font == null) font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            }
            if (font != null) text.font = font;
            text.text = value;
            text.fontSize = size;
            text.color = color ?? Ink;
            text.fontStyle = style;
            // Names and descriptions can come from authored content; show them literally.
            text.richText = false;
            text.alignment = alignment;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            return text;
        }

        public static Button Button(Transform parent, string name, string label, float x, float y, float width, float height,
            UnityAction action, bool primary = false)
        {
            var image = Panel(parent, name, x, y, width, height, primary ? Teal : White);
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            image.color = Color.white;
            var colors = button.colors;
            colors.normalColor = primary ? Teal : White;
            colors.highlightedColor = primary ? Hex(0xFFE58C) : Hex(0x55637B);
            colors.pressedColor = primary ? Gold : Hex(0x64748A);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = Hex(0x272D3B);
            colors.fadeDuration = .1f;
            button.colors = colors;
            // Keyboard belongs to the puzzle, while mouse/touch operate the UI.
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(action);
            Text(image.transform, "Label", label, 12, 0, width - 24, height, 24, primary ? Paper : Ink,
                FontStyles.Bold, TextAlignmentOptions.Center);
            return button;
        }

        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        public static TMP_InputField InputField(Transform parent, string name, string initial, float x, float y, float width, float height,
            UnityAction<string> onChanged, bool multiline = false)
        {
            var panel = Panel(parent, name, x, y, width, height, Paper);
            panel.raycastTarget = true;
            var input = panel.gameObject.AddComponent<TMP_InputField>();
            var area = new GameObject("Text area", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
            area.SetParent(panel.transform, false);
            Stretch(area);
            area.offsetMin = new Vector2(12, 8);
            area.offsetMax = new Vector2(-12, -8);
            var label = Text(area, "Text", initial ?? "", 0, 0, width - 24, height - 16, 24, Ink,
                FontStyles.Normal, multiline ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft);
            Stretch(label.rectTransform);
            label.overflowMode = TextOverflowModes.Overflow;
            label.enableWordWrapping = multiline;
            label.richText = false;
            input.textViewport = area;
            input.textComponent = label;
            input.targetGraphic = panel;
            panel.color = Color.white;
            var inputColors = input.colors;
            inputColors.normalColor = Paper;
            inputColors.highlightedColor = White;
            inputColors.selectedColor = Hex(0x414B60);
            inputColors.pressedColor = White;
            input.colors = inputColors;
            input.lineType = multiline ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
            input.richText = false;
            input.caretColor = Teal;
            input.customCaretColor = true;
            input.selectionColor = new Color(.65f, .66f, .35f, .6f);
            input.characterLimit = multiline ? 300 : 64;
            input.text = initial ?? "";
            if (onChanged != null) input.onValueChanged.AddListener(onChanged);
            return input;
        }

#endif
        public static bool IsTextInputFocused()
        {
            if (!string.IsNullOrEmpty(Input.compositionString)) return true;
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (selected == null) return false;
            var tmp = selected.GetComponentInParent<TMP_InputField>();
            if (tmp != null && tmp.isFocused) return true;
            var legacy = selected.GetComponentInParent<UnityEngine.UI.InputField>();
            return legacy != null && legacy.isFocused;
        }
    }
}
