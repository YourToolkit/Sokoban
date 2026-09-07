#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Sokoban.Runtime
{
    /// <summary>The Editor supplies assets; the document controller owns no layout construction.</summary>
    public sealed class WorkshopViews
    {
        public static WorkshopViewCatalog Catalog { get; set; }
        public UiView Main { get; }
        private readonly Dictionary<string, UiView> dialogs = new Dictionary<string, UiView>();
        private UiView activeDialog;
        public WorkshopViews(Transform parent)
        {
            if (!Catalog || !Catalog.Main) throw new InvalidOperationException("请检查编辑器界面资源配置。");
            Main = UnityEngine.Object.Instantiate(Catalog.Main, parent, false);
            Main.name = "Runtime workshop";
        }
        public UiView OpenDialog(string key)
        {
            CloseDialog();
            if (!dialogs.TryGetValue(key, out var view))
            {
                var template = Array.Find(Catalog.Dialogs, entry => entry.Key == key).Prefab;
                if (!template) throw new InvalidOperationException("缺少编辑弹窗：" + key);
                view = UnityEngine.Object.Instantiate(template, Main.transform, false); view.name = key;
                dialogs.Add(key, view);
            }
            activeDialog = view; view.gameObject.SetActive(true); view.transform.SetAsLastSibling(); return view;
        }
        public void CloseDialog() { if (activeDialog != null) { activeDialog.ReleaseBindings(); activeDialog.gameObject.SetActive(false); } activeDialog = null; }
        public void Hide() { Main.gameObject.SetActive(false); }
        public static void AttachEntry(Transform parent, UnityAction action, string label = "关卡编辑器")
        {
            if (!Catalog || !Catalog.Entry) return;
            var entry = parent.GetComponentInChildren<WorkshopEntry>(true);
            if (!entry)
            {
                var view = UnityEngine.Object.Instantiate(Catalog.Entry, parent, false);
                view.name = "Editor controls"; entry = view.gameObject.AddComponent<WorkshopEntry>(); entry.View = view;
            }
            entry.gameObject.SetActive(true); entry.View.Button("Open editor", action, label);
        }
    }
    public sealed class WorkshopEntry : MonoBehaviour { public UiView View; }
}
#endif
