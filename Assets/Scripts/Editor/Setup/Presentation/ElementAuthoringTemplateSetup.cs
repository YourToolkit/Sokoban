using System;
using System.IO;
using Sokoban.Content;
using Sokoban.Runtime;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Sokoban.EditorTools
{
    public static partial class PresentationSetup
    {
        /// <summary>Explicit one-time structural upgrade. Existing styles and later Inspector edits remain authoritative.</summary>
        [MenuItem("推箱子/升级元素编辑界面", priority = 54)]
        public static void UpgradeElementAuthoringTemplates()
        {
            resources = AssetDatabase.LoadAssetAtPath<GameResources>(ProjectSetup.ResourcesPath);
            if (resources == null) throw new InvalidOperationException("缺少游戏资源配置。");
            Directory.CreateDirectory(Workshop); AssetDatabase.Refresh();
            var catalog = AssetDatabase.LoadAssetAtPath<WorkshopViewCatalog>(WorkshopViewRegistration.Path);
            if (catalog == null) { BuildWorkshop(); catalog = AssetDatabase.LoadAssetAtPath<WorkshopViewCatalog>(WorkshopViewRegistration.Path); }
            var element = Page("Element item", v =>
            {
                var button = B(v.transform, "Element button", "元素", 0, 0, 124, 78);
                var icon = UiFactory.Panel(button.transform, "Element icon", 10, 15, 42, 42, Color.white, false);
                icon.preserveAspect = true;
                var label = button.GetComponentInChildren<TMP_Text>();
                UiFactory.Place(label.rectTransform, 54, 6, 66, 66); label.fontSize = 20; label.richText = false;
                button.GetComponent<UiButtonVisual>().Configure(label, icon);
                button.gameObject.AddComponent<WorkshopPointerHint>();
            }, Workshop, 124, 78);
            var property = Page("Object property item", v =>
            {
                T(v.transform, "Property name", "属性", 0, 0, 240, 44, 22);
                T(v.transform, "Property source", "继承默认", 246, 0, 148, 44, 18, UiFactory.Muted, TextAlignmentOptions.Right);
                UiFactory.InputField(v.transform, "Property input", "", 0, 50, 244, 48, null);
                B(v.transform, "Property action", "选择", 0, 50, 244, 48);
                B(v.transform, "Reset property", "恢复默认", 254, 50, 140, 48);
            }, Workshop, 394, 112);
            if (catalog.ElementAuthoringLayoutVersion < 2)
            {
                // A named migration runs once; subsequent Inspector geometry and styles are never reset.
                string propertyPath = AssetDatabase.GetAssetPath(property);
                var propertyContents = PrefabUtility.LoadPrefabContents(propertyPath);
                try
                {
                    var view = propertyContents.GetComponent<UiView>();
                    view.Rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 112);
                    var heading = view.Get<RectTransform>("Property name");
                    heading.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 44);
                    var inheritance = view.Get<RectTransform>("Property source");
                    inheritance.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 44);
                    inheritance.anchoredPosition = new Vector2(inheritance.anchoredPosition.x, 0);
                    foreach (string key in new[] { "Property input", "Property action", "Reset property" })
                    {
                        var rect = view.Get<RectTransform>(key);
                        rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, -50);
                    }
                    PrefabUtility.SaveAsPrefabAsset(propertyContents, propertyPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(propertyContents); }
                catalog.ElementAuthoringLayoutVersion = 2; EditorUtility.SetDirty(catalog);
            }
            var properties = Page("Object properties", v =>
            {
                // An on-demand panel occupies only its own rectangle; the rest of the board remains interactive.
                var panel = UiFactory.Panel(v.transform, "Properties panel", 40, 162, 454, 568, UiFactory.White);
                panel.raycastTarget = true;
                var r = panel.transform;
                T(r, "Object heading", "对象属性", 26, 20, 326, 44, 27);
                B(r, "Close properties", "关闭", 348, 20, 84, 42);
                T(r, "Object identity", "对象位置", 26, 76, 204, 54, 17, UiFactory.Muted);
                var next = B(r, "Next cell object", "切换同格对象", 246, 78, 182, 46);
                next.GetComponent<UiButtonVisual>().Label.fontSize = 19;
                var clip = UiFactory.Panel(r, "Property list", 26, 142, 402, 318, Color.clear, false);
                clip.raycastTarget = true; clip.gameObject.AddComponent<RectMask2D>();
                var content = R(clip.transform, "Property content", 0, 0, 394, 318);
                var layout = content.gameObject.AddComponent<VerticalLayoutGroup>(); layout.spacing = 12;
                layout.childControlWidth = false; layout.childControlHeight = false; layout.childForceExpandWidth = false; layout.childForceExpandHeight = false;
                content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                var scroll = clip.gameObject.AddComponent<ScrollRect>(); scroll.content = content; scroll.viewport = clip.rectTransform;
                scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 32;
                clip.gameObject.AddComponent<UiList>().Configure(content, property);
                T(r, "Property notice", "继承类型默认值；修改后只影响当前实例。", 26, 478, 402, 70, 20, UiFactory.Muted);
            }, Workshop);
            string propertiesPath = AssetDatabase.GetAssetPath(properties);
            var propertiesContents = PrefabUtility.LoadPrefabContents(propertiesPath);
            try
            {
                var panel = propertiesContents.transform.Find("Properties panel");
                if (panel != null && panel.Find("Next cell object") == null)
                {
                    var next = B(panel, "Next cell object", "切换同格对象", 246, 78, 182, 46);
                    next.GetComponent<UiButtonVisual>().Label.fontSize = 19;
                    propertiesContents.GetComponent<UiView>().CaptureBindings();
                    PrefabUtility.SaveAsPrefabAsset(propertiesContents, propertiesPath);
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(propertiesContents); }
            string path = AssetDatabase.GetAssetPath(catalog.Main);
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var main = contents.GetComponent<UiView>();
                if (contents.transform.Find("Element list") == null)
                {
                    foreach (var key in new[] { "Element Floor", "Element Wall", "Element Goal", "Element Player", "Element Box" })
                    { var child = contents.transform.Find(key); if (child != null) Object.DestroyImmediate(child.gameObject); }
                    var info = main.Get<TMP_Text>("Selection information");
                    UiFactory.Place(info.rectTransform, 44, 84, 295, 40); info.fontSize = 16;
                    var mode = contents.transform.Find("Document mode"); if (mode != null) mode.gameObject.SetActive(false);
                    var clip = UiFactory.Panel(contents.transform, "Element list", 356, 36, 1196, 80, Color.clear, false);
                    clip.raycastTarget = true; clip.gameObject.AddComponent<RectMask2D>();
                    var content = R(clip.transform, "Element content", 0, 0, 1196, 78);
                    var layout = content.gameObject.AddComponent<HorizontalLayoutGroup>(); layout.spacing = 12;
                    layout.childControlWidth = false; layout.childControlHeight = false; layout.childForceExpandWidth = false; layout.childForceExpandHeight = false;
                    content.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                    var scroll = clip.gameObject.AddComponent<ScrollRect>(); scroll.content = content; scroll.viewport = clip.rectTransform;
                    scroll.vertical = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 40;
                    clip.gameObject.AddComponent<UiList>().Configure(content, element);
                    var hotkeys = contents.transform.Find("Editor hotkeys"); if (hotkeys != null) hotkeys.gameObject.SetActive(false);
                    B(contents.transform, "Object properties", "对象属性", 1394, 782, 160, 52);
                    main.CaptureBindings();
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            if (catalog.ObjectProperties == null) { catalog.ObjectProperties = properties; EditorUtility.SetDirty(catalog); }
            AssetDatabase.SaveAssets(); WorkshopViewRegistration.Install();
            Debug.Log("元素编辑界面已升级：目录元素栏、实例属性和画布关联。重复运行保留已编辑的 Prefab 样式。");
        }
    }
}
