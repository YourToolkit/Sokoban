using System;
using Sokoban.Content;
using Sokoban.Core;
using Sokoban.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Sokoban.EditorTools
{
    /// <summary>Starts the real game with an explicit, isolated level snapshot.</summary>
    [InitializeOnLoad]
    public static class EditorPlaytestBridge
    {
        private const string Prefix = "Sokoban.Playtest.";
        private const string GameScene = "Assets/Scenes/Game.unity";

        static EditorPlaytestBridge()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            PlaytestRequest.ExitRequested -= ExitPlaytest;
            PlaytestRequest.ExitRequested += ExitPlaytest;
            // Static runtime state is recreated during a normal domain reload.
            if (EditorApplication.isPlayingOrWillChangePlaymode && SessionState.GetBool(Prefix + "Active", false))
                RestorePendingRequest();
        }

        public static bool Start(LevelAsset level)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || level == null) return false;
            var definition = level.ToDefinition();
            var issues = LevelValidator.Validate(definition);
            if (issues.Count > 0)
            {
                EditorUtility.DisplayDialog("无法开始试玩", issues[0].Message, "确定");
                return false;
            }

            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(GameScene);
            if (scene == null)
            {
                EditorUtility.DisplayDialog("游戏场景缺失", "请先准备工程资源，再开始试玩。", "确定");
                return false;
            }

            SessionState.SetString(Prefix + "PreviousStartScene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            SessionState.SetString(Prefix + "LevelPath", AssetDatabase.GetAssetPath(level));
            SessionState.SetString(Prefix + "Definition", JsonUtility.ToJson(definition));
            SessionState.SetBool(Prefix + "Active", true);
            RestorePendingRequest();
            // The edit-time scene setup is never replaced. Unity restores it on leaving Play mode,
            // including unsaved scene content. Only the scene used to ENTER Play mode is changed.
            EditorSceneManager.playModeStartScene = scene;
            EditorApplication.isPlaying = true;
            return true;
        }

        private static void RestorePendingRequest()
        {
            string json = SessionState.GetString(Prefix + "Definition", "");
            if (!string.IsNullOrEmpty(json)) PlaytestRequest.Pending = JsonUtility.FromJson<LevelDefinition>(json);
        }

        private static void ExitPlaytest()
        {
            if (SessionState.GetBool(Prefix + "Active", false)) EditorApplication.isPlaying = false;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Prefix + "Active", false)) return;
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                // Also works when Enter Play Mode Options disables domain reload.
                RestorePendingRequest();
            }
            else if (state == PlayModeStateChange.EnteredPlayMode)
            {
                SessionState.EraseString(Prefix + "Definition");
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                string previous = SessionState.GetString(Prefix + "PreviousStartScene", "");
                EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(previous)
                    ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(previous);
                string levelPath = SessionState.GetString(Prefix + "LevelPath", "");
                SessionState.SetBool(Prefix + "Active", false);
                SessionState.EraseString(Prefix + "Definition");
                SessionState.EraseString(Prefix + "LevelPath");
                SessionState.EraseString(Prefix + "PreviousStartScene");
                PlaytestRequest.Pending = null;
                EditorApplication.delayCall += () =>
                {
                    var level = AssetDatabase.LoadAssetAtPath<LevelAsset>(levelPath);
                    if (level != null && !Application.isBatchMode) LevelEditorWindow.OpenLevel(level);
                };
            }
        }
    }
}
