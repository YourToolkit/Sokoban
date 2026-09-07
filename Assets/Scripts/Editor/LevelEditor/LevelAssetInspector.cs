using Sokoban.Content;
using Sokoban.Core;
using UnityEditor;
using UnityEngine;

namespace Sokoban.EditorTools
{
    [CustomEditor(typeof(LevelAsset))]
    public sealed class LevelAssetInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var level = (LevelAsset)target;
            if (level.Data == null)
            {
                EditorGUILayout.HelpBox("关卡没有布局数据，请在关卡工坊中新建布局。", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.LabelField(level.Data.Name ?? "未命名关卡", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("地图尺寸", $"{level.Data.Width} × {level.Data.Height}");
                EditorGUILayout.LabelField("箱子 / 目标", $"{level.Data.Boxes?.Length ?? 0} / {level.Data.Goals?.Length ?? 0}");
                EditorGUILayout.LabelField("关卡说明", level.Data.Description ?? "", EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("固定 ID", level.Data.Id ?? "");
                EditorGUILayout.LabelField("布局版本", level.Data.LayoutVersion.ToString());
                EditorGUILayout.LabelField("数据格式版本", level.Data.SchemaVersion.ToString());
                var registry = AssetDatabase.LoadAssetAtPath<GameResources>(ProjectSetup.ResourcesPath)?.Elements?.Snapshot() ?? ElementRegistry.BuiltIns();
                var issues = LevelValidator.Validate(level.Data, registry);
                if (issues.Count == 0)
                    EditorGUILayout.HelpBox("结构检查通过，请试玩确认关卡能够通关。", MessageType.Info);
                else
                    foreach (var issue in issues) EditorGUILayout.HelpBox(issue.ToString(), MessageType.Warning);
            }
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("在关卡工坊中打开", GUILayout.Height(30))) LevelEditorWindow.OpenLevel(level);
            EditorGUILayout.HelpBox("关卡工坊使用独立草稿，正式关卡保存前会进行结构检查。双击此资产也可打开工坊。", MessageType.None);
        }
    }
}
