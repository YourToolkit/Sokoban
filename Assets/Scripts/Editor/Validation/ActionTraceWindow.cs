using Sokoban.Runtime;
using UnityEditor;
using UnityEngine;

namespace Sokoban.EditorTools
{
    public sealed class ActionTraceWindow : EditorWindow
    {
        private Vector2 scroll;
        [MenuItem("推箱子/查看行动记录", priority = 31)]
        public static void Open() => GetWindow<ActionTraceWindow>("行动记录");
        private void OnInspectorUpdate() => Repaint();
        private void OnGUI()
        {
            var game = Object.FindObjectOfType<GameController>();
            var action = game != null ? game.LastAction : null;
            if (!Application.isPlaying || action == null)
            { EditorGUILayout.HelpBox("进入 Play 并移动角色后，这里显示最近一次行动的完整结算。", MessageType.Info); return; }
            EditorGUILayout.LabelField(action.Succeeded ? "行动已提交" : "行动未提交，状态保持原状", EditorStyles.boldLabel);
            if (!string.IsNullOrEmpty(action.Reason)) EditorGUILayout.HelpBox(action.Reason, MessageType.Info);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            int index = 0;
            foreach (var frame in action.Frames)
            {
                EditorGUILayout.LabelField("子步 " + (++index), EditorStyles.boldLabel);
                foreach (var move in frame.Moves) EditorGUILayout.SelectableLabel(move.Id + "：" + move.From + " → " + move.To, GUILayout.Height(20));
                foreach (var change in frame.Changes) EditorGUILayout.SelectableLabel(change.Id + " / " + change.Key + "：" + change.Before + " → " + change.After, GUILayout.Height(20));
                EditorGUILayout.LabelField(frame.Trace, EditorStyles.wordWrappedLabel);
                EditorGUILayout.Space();
            }
            if (action.Frames.Count == 0 && !string.IsNullOrEmpty(action.Trace)) EditorGUILayout.TextArea(action.Trace);
            EditorGUILayout.EndScrollView();
        }
    }
}
