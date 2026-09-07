using System;
using UnityEngine;

namespace Sokoban.Runtime
{
    public sealed class GameScreens : MonoBehaviour
    {
        [SerializeField] private UiView play, selection, pause, complete, error;
        [SerializeField] private Color infoColor = new Color32(242, 211, 107, 255);
        [SerializeField] private Color normalColor = new Color32(167, 177, 192, 255);
        public UiView Play => play;
        public UiView Selection => selection;
        public UiView Pause => pause;
        public UiView Complete => complete;
        public UiView Error => error;
        public UiView Current { get; private set; }
        public Color InfoColor => infoColor;
        public Color NormalColor => normalColor;
        public void Validate()
        { if (!play || !selection || !pause || !complete || !error) throw new InvalidOperationException("请补齐 GameScreens 的页面引用。"); }
        public UiView Show(UiView page)
        { HideAll(); Current = page; page.gameObject.SetActive(true); return page; }
        public void HideAll()
        { foreach (var view in new[] { play, selection, pause, complete, error }) if (view != null) view.gameObject.SetActive(false); Current = null; }
        public UiView ShowModal(UiView view) { HideModals(); view.gameObject.SetActive(true); view.transform.SetAsLastSibling(); return view; }
        public void HideModals() { pause.gameObject.SetActive(false); complete.gameObject.SetActive(false); }
#if UNITY_EDITOR
        public void Configure(UiView p, UiView s, UiView a, UiView c, UiView e) { play = p; selection = s; pause = a; complete = c; error = e; }
#endif
    }
}
