using System.Linq;
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
        private const string ToolTemplatePath = Workshop + "Tool button.prefab";

        // One-time conversion of the original seven authored buttons to instances of a shared template.
        // Re-running setup leaves existing template instances and their Inspector overrides untouched.
        private static void UpgradeToolTemplates()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<WorkshopViewCatalog>(WorkshopViewRegistration.Path);
            if (catalog.Main.GetComponentsInChildren<WorkshopPointerHint>(true).All(hint =>
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(hint.gameObject) == ToolTemplatePath)) return;

            var template = AssetDatabase.LoadAssetAtPath<GameObject>(ToolTemplatePath);
            if (!template)
            {
                var source = catalog.Main.GetComponentsInChildren<WorkshopPointerHint>(true).First(h => h.Tool == WorkshopTool.Brush);
                var copy = Object.Instantiate(source.gameObject);
                copy.name = "Tool button";
                template = PrefabUtility.SaveAsPrefabAsset(copy, ToolTemplatePath);
                Object.DestroyImmediate(copy);
            }

            string path = AssetDatabase.GetAssetPath(catalog.Main);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var view = root.GetComponent<UiView>();
                foreach (var old in root.GetComponentsInChildren<WorkshopPointerHint>(true))
                {
                    if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(old.gameObject) == ToolTemplatePath) continue;
                    var next = (GameObject)PrefabUtility.InstantiatePrefab(template, old.transform.parent);
                    next.name = old.name;
                    var from = (RectTransform)old.transform;
                    var to = (RectTransform)next.transform;
                    to.SetSiblingIndex(from.GetSiblingIndex());
                    to.anchorMin = from.anchorMin; to.anchorMax = from.anchorMax; to.pivot = from.pivot;
                    to.anchoredPosition = from.anchoredPosition; to.sizeDelta = from.sizeDelta;
                    var hint = next.GetComponent<WorkshopPointerHint>();
                    hint.Tool = old.Tool; hint.DisplayName = old.DisplayName; hint.Description = old.Description;
                    next.GetComponent<UiButtonVisual>().Icon.sprite = old.GetComponent<UiButtonVisual>().Icon.sprite;
                    next.GetComponent<Button>().colors = old.GetComponent<Button>().colors;
                    foreach (var child in old.GetComponentsInChildren<Transform>(true))
                    {
                        var replacement = child == old.transform ? next.transform : next.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == child.name);
                        if (replacement) view.ReplaceBindingTarget(child.gameObject, replacement.gameObject);
                    }
                    foreach (var component in next.GetComponentsInChildren<Component>(true))
                        PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(next);
                    Object.DestroyImmediate(old.gameObject);
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
