using UnityEngine;
using UnityEngine.UI;

namespace Sokoban.Runtime
{
    /// <summary>The authored area determines available space; its child displays whole physical pixels.</summary>
    public sealed class BoardViewport : MonoBehaviour
    {
        [SerializeField] private RawImage surface;
        public RawImage Surface => surface;
        public Rect Area => ScreenBounds((RectTransform)transform);
        public Rect Display => ScreenBounds(surface.rectTransform);
        public void Present(RenderTexture texture, int integerScale)
        {
            surface.texture = texture;
            float scale = GetComponentInParent<Canvas>().scaleFactor;
            surface.rectTransform.sizeDelta = new Vector2(texture.width, texture.height) * integerScale / Mathf.Max(.001f, scale);
            surface.rectTransform.anchoredPosition = Vector2.zero;
            var bounds = Display;
            // The parent remains editable; only the generated surface snaps to the physical pixel grid.
            surface.rectTransform.anchoredPosition = new Vector2(Mathf.Round(bounds.xMin)-bounds.xMin,
                Mathf.Round(bounds.yMin)-bounds.yMin) / Mathf.Max(.001f, scale);
            surface.gameObject.SetActive(true);
        }
        private static Rect ScreenBounds(RectTransform rect)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            var canvas = rect.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            var a = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            var b = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            return Rect.MinMaxRect(a.x, a.y, b.x, b.y);
        }
#if UNITY_EDITOR
        public void Configure(RawImage image) { surface = image; }
#endif
    }
}
