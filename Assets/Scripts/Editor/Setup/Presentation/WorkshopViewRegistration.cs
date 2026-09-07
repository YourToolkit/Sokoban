using Sokoban.Runtime;
using UnityEditor;

namespace Sokoban.EditorTools
{
    [InitializeOnLoad]
    public static class WorkshopViewRegistration
    {
        public const string Path = "Assets/Prefabs/Editor/Workshop/WorkshopViews.asset";
        static WorkshopViewRegistration()
        {
            EditorApplication.delayCall += Install;
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.EnteredPlayMode) Install(); };
        }
        public static void Install() => WorkshopViews.Catalog = AssetDatabase.LoadAssetAtPath<WorkshopViewCatalog>(Path);
    }
}
