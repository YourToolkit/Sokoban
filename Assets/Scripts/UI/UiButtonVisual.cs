using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sokoban.Runtime
{
    [RequireComponent(typeof(Button))]
    public sealed class UiButtonVisual : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;
        [SerializeField] private Image icon;
        [SerializeField] private Color selectedBackground = new Color32(85, 84, 66, 255);
        [SerializeField] private Color selectedIcon = new Color32(242, 211, 107, 255);
        [SerializeField] private Color disabledIcon = new Color32(110, 120, 140, 128);
        private Button button;
        private ColorBlock normal;
        private Color normalIcon;
        public TMP_Text Label => label;
        public Image Icon => icon;
        private void Awake() { button = GetComponent<Button>(); normal = button.colors; normalIcon = icon != null ? icon.color : Color.white; }
        public void Select(bool selected)
        {
            if (button == null) Awake();
            var colors = normal;
            if (selected) colors.normalColor = colors.highlightedColor = colors.selectedColor = selectedBackground;
            button.colors = colors;
            if (icon != null) icon.color = !button.interactable ? disabledIcon : selected ? selectedIcon : normalIcon;
        }
#if UNITY_EDITOR
        public void Configure(TMP_Text text, Image image = null) { label = text; icon = image; }
#endif
    }
}
