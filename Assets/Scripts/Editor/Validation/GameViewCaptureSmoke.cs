using System;
using System.IO;
using System.Reflection;
using Sokoban.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Sokoban.EditorTools
{
    /// <summary>Explicit Editor-only visual acceptance entry. Enters Play Mode, waits for the report, then exits.</summary>
    [InitializeOnLoad]
    public static class GameViewCaptureSmoke
    {
        private const string Key = "Sokoban.GameViewCapture.";
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        static GameViewCaptureSmoke()
        {
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key + "Active", false))
                    SessionState.SetBool(Key + "PlayReady", true);
            };
        }

        // Do not pass -quit or -nographics: this method owns the asynchronous Editor lifecycle and renders real graphics.
        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            string output = Argument(args, "-sokoban-capture") ?? "docs/images/editor-1600x900";
            string size = Argument(args, "-sokoban-size") ?? Argument(args, "-ScreenSize") ?? "1600x900";
            ParseSize(size, out var width, out var height);
            Begin(output, width, height, true);
        }

        [MenuItem("推箱子/验收/截取游戏与编辑器界面", priority = 85)]
        public static void RunFromMenu() { Begin("docs/images/editor-1600x900", 1600, 900, false); }

        private static void Begin(string output, int width, int height, bool exitWhenDone)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetBool(Key + "Active", false))
                throw new InvalidOperationException("请先结束当前运行或截图验收。");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("请先保存当前场景，再启动界面验收。");
            output = Path.GetFullPath(output);
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "report.txt"), "RUNNING: Unity Editor Play Mode capture\n");
            SessionState.SetBool(Key + "Active", true);
            SessionState.SetBool(Key + "Started", false);
            SessionState.SetBool(Key + "PlayReady", false);
            SessionState.SetBool(Key + "Finishing", false);
            SessionState.SetBool(Key + "Exit", exitWhenDone);
            SessionState.SetString(Key + "Output", output);
            SessionState.SetString(Key + "StartedUtc", DateTime.UtcNow.Ticks.ToString());
            SessionState.SetInt(Key + "Width", width);
            SessionState.SetInt(Key + "Height", height);
            Environment.SetEnvironmentVariable(DevelopmentCapture.OutputEnvironmentVariable, output);
            try
            {
                ConfigureGameView(width, height);
                EditorSceneManager.OpenScene(ProjectSetup.GameScene, OpenSceneMode.Single);
                EditorApplication.EnterPlaymode();
            }
            catch (Exception error) { WriteFailure(error.ToString()); StartFinishing(1); }
        }

        private static void Poll()
        {
            if (!SessionState.GetBool(Key + "Active", false)) return;
            try
            {
                string output = SessionState.GetString(Key + "Output", "");
                if (SessionState.GetBool(Key + "Finishing", false))
                {
                    if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                    Environment.SetEnvironmentVariable(DevelopmentCapture.OutputEnvironmentVariable, null);
                    ProgressStore.DefaultDirectoryOverride = null;
                    SessionState.SetBool(Key + "Active", false);
                    if (SessionState.GetBool(Key + "Exit", false)) EditorApplication.Exit(SessionState.GetInt(Key + "ExitCode", 1));
                    else Debug.Log("界面验收完成：" + Path.Combine(output, "report.txt"));
                    return;
                }
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                string reportPath = Path.Combine(output, "report.txt");
                string report = File.Exists(reportPath) ? File.ReadAllText(reportPath) : "";
                if (report.StartsWith("PASS", StringComparison.Ordinal) || report.StartsWith("FAIL", StringComparison.Ordinal))
                { StartFinishing(report.StartsWith("PASS", StringComparison.Ordinal) ? 0 : 1); return; }
                long.TryParse(SessionState.GetString(Key + "StartedUtc", "0"), out var ticks);
                if (ticks == 0 || new TimeSpan(DateTime.UtcNow.Ticks - ticks).TotalSeconds > 180)
                { WriteFailure("Editor capture timed out before a complete report."); StartFinishing(1); return; }
                if (EditorApplication.isPlaying && SessionState.GetBool(Key + "PlayReady", false) && !SessionState.GetBool(Key + "Started", false))
                {
                    SessionState.SetBool(Key + "Started", true);
                    DevelopmentCapture.Begin(output, SessionState.GetInt(Key + "Width", 1600), SessionState.GetInt(Key + "Height", 900));
                }
                else if (SessionState.GetBool(Key + "Started", false) && !EditorApplication.isPlayingOrWillChangePlaymode)
                { WriteFailure("Play Mode ended before the capture report."); StartFinishing(1); }
            }
            catch (Exception error) { WriteFailure(error.ToString()); StartFinishing(1); }
        }

        private static void StartFinishing(int exitCode)
        {
            SessionState.SetBool(Key + "Finishing", true);
            SessionState.SetInt(Key + "ExitCode", exitCode);
            if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.ExitPlaymode();
        }

        private static void WriteFailure(string error)
        {
            var output = SessionState.GetString(Key + "Output", "");
            if (!string.IsNullOrEmpty(output)) File.WriteAllText(Path.Combine(output, "report.txt"), "FAIL: " + error + "\nExecution: Unity Editor Play Mode (no player build)\n");
        }

        private static void ConfigureGameView(int width, int height)
        {
            // Unity 2022.3 exposes GameView fixed-resolution controls internally. These exact members
            // were checked against the installed UnityEditor.dll; reflection stays confined to this Editor utility.
            var assembly = typeof(Editor).Assembly;
            var viewType = assembly.GetType("UnityEditor.GameView", true);
            var sizesType = assembly.GetType("UnityEditor.GameViewSizes", true);
            var sizeType = assembly.GetType("UnityEditor.GameViewSize", true);
            var kindType = assembly.GetType("UnityEditor.GameViewSizeType", true);
            var groupType = assembly.GetType("UnityEditor.GameViewSizeGroupType", true);
            var instance = sizesType.BaseType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            var group = sizesType.GetMethod("GetGroup", Members).Invoke(instance, new[] { Enum.Parse(groupType, "Standalone") });
            var groupClass = group.GetType();
            int count = (int)groupClass.GetMethod("GetTotalCount", Members).Invoke(group, null);
            int selected = -1;
            object fixedKind = Enum.Parse(kindType, "FixedResolution");
            for (int i = 0; i < count; i++)
            {
                var item = groupClass.GetMethod("GetGameViewSize", Members).Invoke(group, new object[] { i });
                if ((int)sizeType.GetProperty("width", Members).GetValue(item) == width &&
                    (int)sizeType.GetProperty("height", Members).GetValue(item) == height &&
                    sizeType.GetProperty("sizeType", Members).GetValue(item).Equals(fixedKind)) { selected = i; break; }
            }
            if (selected < 0)
            {
                var item = Activator.CreateInstance(sizeType, Members, null,
                    new object[] { fixedKind, width, height, "Sokoban capture " + width + "x" + height }, null);
                groupClass.GetMethod("AddCustomSize", Members).Invoke(group, new[] { item });
                selected = count;
            }
            var gameView = EditorWindow.GetWindow(viewType);
            gameView.Show(); gameView.Focus();
            viewType.GetProperty("selectedSizeIndex", Members).SetValue(gameView, selected);
            viewType.GetMethod("SizeSelectionCallback", Members).Invoke(gameView, new object[] { selected, null });
            viewType.GetProperty("targetSize", Members).SetValue(gameView, new Vector2(width, height));
            gameView.Repaint();
        }

        private static string Argument(string[] args, string name)
        { int index = Array.IndexOf(args, name); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }
        private static void ParseSize(string text, out int width, out int height)
        {
            var parts = text.ToLowerInvariant().Split('x', ',', '*');
            if (parts.Length != 2 || !int.TryParse(parts[0], out width) || !int.TryParse(parts[1], out height) || width < 640 || height < 480 || width > 3840 || height > 2160)
                throw new ArgumentException("ScreenSize 必须是 640x480 至 3840x2160 范围内的宽x高，例如 1280x1024。");
        }
    }
}
