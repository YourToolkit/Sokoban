using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Sokoban.EditorTools
{
    /// <summary>Local, opt-in validation commands for an already-open Editor; no network listener.</summary>
    [InitializeOnLoad]
    internal static class LocalValidationBridge
    {
        [Serializable] private sealed class Command { public string Id = ""; public string Action = ""; }
        [Serializable] private sealed class Reply { public string Id; public bool Success; public string Message; }
        private const string RequestPath = "Temp/SokobanCommand.json";
        private const string ReplyPath = "Temp/SokobanCommandResult.json";
        private static double nextPoll;
        private static TestRunnerApi runner;
        static LocalValidationBridge()
        {
            EditorApplication.update += Poll;
            Application.logMessageReceived += (message, stack, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                {
                    try { File.AppendAllText("Temp/SokobanErrors.log", message + "\n" + stack + "\n"); }
                    catch (IOException) { }
                }
            };
        }
        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            nextPoll = EditorApplication.timeSinceStartup + .5;
            if (!File.Exists(RequestPath)) return;
            Command command;
            try { command = JsonUtility.FromJson<Command>(File.ReadAllText(RequestPath)); File.Delete(RequestPath); }
            catch (IOException) { return; }
            try
            {
                switch (command.Action)
                {
                    case "prepare": ProjectSetup.Initialize(); break;
                    case "validate": ProjectSetup.ValidateCatalog(); break;
                    case "build": ProjectSetup.BuildWindows(); break;
                    case "tests": RunTests(command.Id); return;
                    case "status": ReplyTo(command.Id, true, SceneStatus()); return;
                    case "reload-presentation": ReloadPresentationWithBackup(); break;
                    default: throw new ArgumentException("Unknown local validation command.");
                }
                ReplyTo(command.Id, true, command.Action + " complete");
            }
            catch (Exception ex) { Debug.LogException(ex); ReplyTo(command.Id, false, ex.ToString()); }
        }
        private static string SceneStatus()
        {
            var text = new StringBuilder("Playing=" + EditorApplication.isPlaying);
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                text.Append("; Scene=" + scene.path + ", Dirty=" + scene.isDirty);
            }
            return text.ToString();
        }
        private static void ReloadPresentationWithBackup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先结束 Play，再载入场景修改。");
            // Preserve the complete in-memory scene before accepting external presentation edits.
            // Save-as-copy leaves the current scene and its dirty flag untouched until the backup succeeds.
            var scene = SceneManager.GetSceneByPath(ProjectSetup.GameScene);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("Game 场景尚未打开，请先检查当前场景。");
            string folder = "Assets/Scenes/Recovery";
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            string backup = AssetDatabase.GenerateUniqueAssetPath(folder + "/Game-before-presentation.unity");
            if (!EditorSceneManager.SaveScene(scene, backup, true) || !File.Exists(backup) || new FileInfo(backup).Length == 0)
                throw new IOException("保存场景副本失败，当前场景保持打开。");
            Debug.Log("已保留原窗口的场景副本：" + backup);
            // Reload only Game, keeping any other open scene and its unsaved contents intact.
            if (SceneManager.sceneCount == 1)
                EditorSceneManager.OpenScene(ProjectSetup.GameScene, OpenSceneMode.Single);
            else
            {
                bool wasActive = SceneManager.GetActiveScene() == scene;
                if (!EditorSceneManager.CloseScene(scene, true))
                    throw new InvalidOperationException("场景副本已保存，但未能关闭原场景。");
                var replacement = EditorSceneManager.OpenScene(ProjectSetup.GameScene, OpenSceneMode.Additive);
                if (wasActive) SceneManager.SetActiveScene(replacement);
            }
        }
        private static void ReplyTo(string id, bool success, string message)
        { File.WriteAllText(ReplyPath, JsonUtility.ToJson(new Reply { Id = id, Success = success, Message = message }, true)); }
        private static void RunTests(string id)
        {
            runner = ScriptableObject.CreateInstance<TestRunnerApi>();
            runner.RegisterCallbacks(new Results(id));
            runner.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode, assemblyNames = new[] { "Sokoban.Tests" } }));
        }
        private sealed class Results : ICallbacks
        {
            private readonly string id;
            public Results(string commandId) { id = commandId; }
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
            public void RunFinished(ITestResultAdaptor result)
            {
                Directory.CreateDirectory("Logs");
                var text = new StringBuilder();
                Dump(result, text, "");
                File.WriteAllText("Logs/editmode-results.txt", text.ToString());
                ReplyTo(id, result.FailCount == 0 && result.PassCount > 0, $"Passed={result.PassCount}; Failed={result.FailCount}; Skipped={result.SkipCount}\n" + text);
                UnityEngine.Object.DestroyImmediate(runner); runner = null;
            }
            private static void Dump(ITestResultAdaptor result, StringBuilder output, string indent)
            {
                output.AppendLine(indent + result.Name + ": " + result.ResultState);
                if (!string.IsNullOrEmpty(result.Message)) output.AppendLine(indent + result.Message);
                foreach (var child in result.Children) Dump(child, output, indent + "  ");
            }
        }
    }
}
