using System;
using System.Collections.Generic;
using System.IO;
using Sokoban.Content;
using Sokoban.Core;
using Sokoban.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Sokoban.EditorTools
{
    /// <summary>
    /// Explicit batch-mode integration check. Run on a disposable project copy with
    /// -executeMethod Sokoban.EditorTools.EditorBridgeSmoke.Begin, without -quit.
    /// </summary>
    [InitializeOnLoad]
    public static class EditorBridgeSmoke
    {
        private const string StateKey = "Sokoban.EditorBridgeSmoke.State";
        private const string ActiveKey = "Sokoban.EditorBridgeSmoke.Active";
        private const string ResultPath = "Logs/editor-bridge-results.txt";
        private static readonly string[] ProgressFileNames = { "progress.json", "progress.json.bak", "progress.json.tmp", "progress.json.unreadable" };
        private static SmokeState state;

        [Serializable]
        private sealed class FileSnapshot
        {
            public string Name;
            public bool Exists;
            public string Bytes;
        }

        [Serializable]
        private sealed class SceneSnapshot
        {
            public string Path;
            public string Name;
            public bool Loaded;
            public bool Active;
            public bool Dirty;
        }

        [Serializable]
        private sealed class SmokeState
        {
            public string Stage;
            public long Deadline;
            public int Round;
            public int Moves;
            public string LevelPath;
            public string LevelId;
            public string DefinitionJson;
            public string Solution;
            public string OriginalSolution;
            public string OriginalProduct;
            public string OriginalProgressDirectory;
            public FileSnapshot[] OriginalProgress;
            public string SmokeProduct;
            public string SmokeProgressDirectory;
            public FileSnapshot[] SmokeProgress;
            public bool OriginalOptionsEnabled;
            public int OriginalOptions;
            public string OriginalStartScene;
            public SceneSnapshot[] OriginalScenes;
            public string Report;
            public string Failure;
        }

        static EditorBridgeSmoke()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        public static void Begin()
        {
            // Never start or terminate a person's interactive Editor from this helper.
            if (!Application.isBatchMode)
                throw new InvalidOperationException("EditorBridgeSmoke requires a disposable project copy in batch mode.");
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-quit") >= 0)
                throw new InvalidOperationException("EditorBridgeSmoke must run without -quit so its play-mode stages can complete.");

            try
            {
                Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Begin must be called in Edit mode.");
                var resources = AssetDatabase.LoadAssetAtPath<GameResources>(ProjectSetup.ResourcesPath);
                Require(resources != null && resources.Catalog != null && resources.Catalog.Levels.Count > 0,
                    "Prepared game resources and at least one catalog room are required.");
                var level = resources.Catalog.Levels[0];
                Require(level != null && LevelValidator.Validate(level.Data).Count == 0, "The first room must be structurally valid.");
                string solution = level.VerifiedSolution;
                if (string.IsNullOrEmpty(solution))
                {
                    var expected = BuiltInLevels.Create();
                    int index = Array.FindIndex(expected, item => item.Id == level.Data.Id && LevelAuthoring.LayoutEquals(item, level.Data));
                    Require(index >= 0, "A smoke room without a cached solution must match a verified teaching layout.");
                    solution = BuiltInLevels.Solutions[index];
                }
                Require(AssetDatabase.LoadAssetAtPath<SceneAsset>(ProjectSetup.GameScene) != null, "The Game scene is missing.");

                string originalDirectory = Path.GetFullPath(Application.persistentDataPath);
                state = new SmokeState
                {
                    LevelPath = AssetDatabase.GetAssetPath(level),
                    LevelId = level.Data.Id,
                    DefinitionJson = JsonUtility.ToJson(level.Data),
                    Solution = solution,
                    OriginalSolution = level.VerifiedSolution,
                    OriginalProduct = PlayerSettings.productName,
                    OriginalProgressDirectory = originalDirectory,
                    OriginalProgress = CaptureFiles(originalDirectory),
                    SmokeProduct = "SokobanBridgeSmoke-" + Guid.NewGuid().ToString("N"),
                    OriginalOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled,
                    OriginalOptions = (int)EditorSettings.enterPlayModeOptions,
                    OriginalStartScene = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),
                    OriginalScenes = CaptureScenes(),
                    Report = "Editor playtest bridge integration check\n"
                };
                SessionState.SetBool(ActiveKey, true);
                SetStage("PrepareIsolatedProgress");
                PlayerSettings.productName = state.SmokeProduct;
                Append("Started in explicit batch mode. User progress is read-only; gameplay will use a unique smoke product.");
            }
            catch (Exception exception)
            {
                if (state != null) Fail(exception);
                else
                {
                    Directory.CreateDirectory("Logs");
                    File.WriteAllText(ResultPath, "FAIL\n" + exception);
                    EditorApplication.Exit(1);
                }
            }
        }

        private static void Tick()
        {
            if (!Application.isBatchMode || !SessionState.GetBool(ActiveKey, false)) return;
            if (state == null) state = JsonUtility.FromJson<SmokeState>(SessionState.GetString(StateKey, ""));
            if (state == null) return;
            try
            {
                if (state.Stage == "Failing")
                {
                    if (!EditorApplication.isPlayingOrWillChangePlaymode || DateTime.UtcNow.Ticks > state.Deadline) Finish(false);
                    return;
                }
                Require(DateTime.UtcNow.Ticks <= state.Deadline, "Timed out in " + state.Stage + ".");
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                switch (state.Stage)
                {
                    case "PrepareIsolatedProgress": PrepareIsolatedProgress(); break;
                    case "StartRound": StartRound(); break;
                    case "WaitForRuntime": WaitForRuntime(); break;
                    case "Move": Move(); break;
                    case "WaitForExit": WaitForExit(); break;
                    default: throw new InvalidOperationException("Unknown smoke stage: " + state.Stage);
                }
            }
            catch (Exception exception) { Fail(exception); }
        }

        private static void PrepareIsolatedProgress()
        {
            string isolatedDirectory = Path.GetFullPath(Application.persistentDataPath);
            Require(!SamePath(isolatedDirectory, state.OriginalProgressDirectory),
                "PlayerSettings product override did not isolate persistentDataPath. No progress files were written.");
            Require(string.Equals(new DirectoryInfo(isolatedDirectory).Name, state.SmokeProduct, StringComparison.Ordinal),
                "The smoke progress path does not match the unique product name.");
            state.SmokeProgressDirectory = isolatedDirectory;
            Directory.CreateDirectory(isolatedDirectory);
            File.WriteAllText(Path.Combine(isolatedDirectory, "progress.json"),
                "{\"Version\":1,\"Records\":[{\"LevelId\":\"smoke-existing-room\",\"Completed\":true,\"BestSteps\":7,\"BestPushes\":3}]}");
            state.SmokeProgress = CaptureFiles(isolatedDirectory);
            Append("Persistent data isolation verified; a valid sentinel progress file was created only in the unique smoke directory.");
            SetStage("StartRound");
        }

        private static void StartRound()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "A new round cannot start while Play mode is active.");
            AssertEditorRestored();
            EditorSettings.enterPlayModeOptions = state.Round == 0 ? EnterPlayModeOptions.None : EnterPlayModeOptions.DisableDomainReload;
            EditorSettings.enterPlayModeOptionsEnabled = state.Round != 0;
            state.Moves = 0;
            Append("Starting " + ModeName + " round.");
            SetStage("WaitForRuntime");
            var level = AssetDatabase.LoadAssetAtPath<LevelAsset>(state.LevelPath);
            Require(EditorPlaytestBridge.Start(level), "The production bridge refused to start the prepared room.");
        }

        private static void WaitForRuntime()
        {
            if (!EditorApplication.isPlaying) return;
            var app = UnityEngine.Object.FindObjectOfType<GameController>();
            if (app == null) return;
            Require(app.IsPlaytest, "GameController started without explicit playtest mode; Pending was lost across the domain transition.");
            Require(app.Session != null, "GameController did not initialize a session.");
            Require(app.Session.Definition.Id == state.LevelId, "The runtime loaded a different room.");
            Require(app.CurrentLevelIndex == -1, "Playtest mode must not masquerade as a catalog slot.");
            Require(PlaytestRequest.Pending == null, "The runtime did not consume the one-shot playtest request.");
            Require(SceneManager.GetActiveScene().name == "Game", "The production bridge did not start the Game scene.");
            Require(SamePath(Application.persistentDataPath, state.SmokeProgressDirectory), "Play mode lost the isolated persistent data path.");
            Append("GameController initialized in explicit playtest mode for " + state.LevelId + ".");
            SetStage("Move");
        }

        private static void Move()
        {
            Require(EditorApplication.isPlaying, "Play mode exited before smoke moves finished.");
            var app = UnityEngine.Object.FindObjectOfType<GameController>();
            Require(app != null && app.IsPlaytest && app.Session != null, "The playtest session disappeared.");
            if (app.CurrentScreen == GameController.ScreenState.Paused) app.Resume();
            if (app.Board != null && app.Board.IsAnimating) return;
            int moveCount = Math.Min(2, state.Solution.Length);
            if (state.Moves < moveCount)
            {
                int before = app.Session.State.Steps;
                app.Move(ParseDirection(state.Solution[state.Moves]));
                Require(app.Session.State.Steps == before + 1, "The real GameController.Move entry point rejected a verified move.");
                state.Moves++;
                Persist();
                return;
            }
            if (moveCount == state.Solution.Length)
                Require(app.Session.IsWon && app.CurrentScreen == GameController.ScreenState.Complete, "The verified first-room solution did not reach its completion screen.");
            AssertDataUnchanged();
            Append("PASS " + ModeName + ": " + state.Moves + " real moves, won=" + app.Session.IsWon + "; authoring and both progress snapshots unchanged.");
            SetStage("WaitForExit");
            PlaytestRequest.RequestExit();
        }

        private static void WaitForExit()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            AssertEditorRestored();
            Require(PlaytestRequest.Pending == null, "The playtest request leaked after returning to Edit mode.");
            AssertDataUnchanged();
            Append("PASS " + ModeName + ": RequestExit returned to the original editor scene setup and playModeStartScene.");
            state.Round++;
            if (state.Round < 2) SetStage("StartRound");
            else Finish(true);
        }

        private static void AssertDataUnchanged()
        {
            var level = AssetDatabase.LoadAssetAtPath<LevelAsset>(state.LevelPath);
            Require(level != null && JsonUtility.ToJson(level.Data) == state.DefinitionJson, "Playtest mutated the room asset definition.");
            Require(level.VerifiedSolution == state.OriginalSolution, "Playtest mutated the room's verified solution.");
            AssertFilesUnchanged(state.OriginalProgressDirectory, state.OriginalProgress, "User progress");
            AssertFilesUnchanged(state.SmokeProgressDirectory, state.SmokeProgress, "Smoke progress");
        }

        private static void AssertEditorRestored()
        {
            Require(AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) == state.OriginalStartScene,
                "The original playModeStartScene was not restored.");
            var current = CaptureScenes();
            Require(current.Length == state.OriginalScenes.Length, "The editor scene count changed.");
            for (int i = 0; i < current.Length; i++)
            {
                var expected = state.OriginalScenes[i];
                var actual = current[i];
                Require(actual.Path == expected.Path && actual.Name == expected.Name && actual.Loaded == expected.Loaded &&
                    actual.Active == expected.Active && actual.Dirty == expected.Dirty,
                    "The original editor scene setup or its unsaved status changed at index " + i + ".");
            }
        }

        private static void Fail(Exception exception)
        {
            if (state.Stage == "Failing") return;
            state.Failure = exception.ToString();
            Append("FAIL: " + state.Failure);
            SetStage("Failing", 35);
            if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = false;
            else Finish(false);
        }

        private static void Finish(bool success)
        {
            try
            {
                var errors = new List<string>();
                // Each restoration is independent so one failed cleanup cannot skip the others.
                Cleanup(() => EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)state.OriginalOptions, "Enter Play Mode options", errors);
                Cleanup(() => EditorSettings.enterPlayModeOptionsEnabled = state.OriginalOptionsEnabled, "Enter Play Mode toggle", errors);
                Cleanup(() => EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(state.OriginalStartScene)
                    ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(state.OriginalStartScene), "Play Mode start scene", errors);
                Cleanup(() => PlayerSettings.productName = state.OriginalProduct, "Product name", errors);
                Cleanup(() => AssertFilesUnchanged(state.OriginalProgressDirectory, state.OriginalProgress, "User progress"), "User progress comparison", errors);
                Cleanup(RemoveIsolatedProgress, "Isolated progress cleanup", errors);
                if (errors.Count > 0)
                {
                    success = false;
                    foreach (string error in errors) Append("FAIL during cleanup: " + error);
                }
                else Append("Restored product name and Enter Play Mode settings; removed isolated progress.");
                Append(success ? "PASS: both bridge rounds completed." : "FAIL: bridge smoke did not complete.");
            }
            catch (Exception exception)
            {
                success = false;
                Append("FAIL during cleanup: " + exception);
            }
            finally
            {
                SessionState.SetBool(ActiveKey, false);
                SessionState.EraseString(StateKey);
                EditorApplication.Exit(success ? 0 : 1);
            }
        }

        private static void Cleanup(Action action, string label, List<string> errors)
        {
            try { action(); }
            catch (Exception exception) { errors.Add(label + ": " + exception); }
        }

        private static void RemoveIsolatedProgress()
        {
            if (string.IsNullOrEmpty(state.SmokeProgressDirectory)) return;
            string path = Path.GetFullPath(state.SmokeProgressDirectory);
            Require(!SamePath(path, state.OriginalProgressDirectory) &&
                string.Equals(new DirectoryInfo(path).Name, state.SmokeProduct, StringComparison.Ordinal),
                "Refusing to clean an unverified progress directory.");
            // Only explicitly named test files in the verified unique directory are removed.
            foreach (string fileName in ProgressFileNames)
            {
                string filePath = Path.Combine(path, fileName);
                if (File.Exists(filePath)) File.Delete(filePath);
            }
            if (Directory.Exists(path) && Directory.GetFileSystemEntries(path).Length == 0) Directory.Delete(path, false);
        }

        private static FileSnapshot[] CaptureFiles(string directory)
        {
            var snapshots = new List<FileSnapshot>();
            foreach (string name in ProgressFileNames)
            {
                string path = Path.Combine(directory, name);
                bool exists = File.Exists(path);
                snapshots.Add(new FileSnapshot { Name = name, Exists = exists, Bytes = exists ? Convert.ToBase64String(File.ReadAllBytes(path)) : "" });
            }
            return snapshots.ToArray();
        }

        private static void AssertFilesUnchanged(string directory, FileSnapshot[] snapshots, string label)
        {
            if (snapshots == null) return;
            foreach (var snapshot in snapshots)
            {
                string path = Path.Combine(directory, snapshot.Name);
                Require(File.Exists(path) == snapshot.Exists, label + " file existence changed: " + snapshot.Name);
                if (snapshot.Exists)
                    Require(Convert.ToBase64String(File.ReadAllBytes(path)) == snapshot.Bytes, label + " bytes changed: " + snapshot.Name);
            }
        }

        private static SceneSnapshot[] CaptureScenes()
        {
            var result = new SceneSnapshot[SceneManager.sceneCount];
            for (int i = 0; i < result.Length; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                result[i] = new SceneSnapshot { Path = scene.path, Name = scene.name, Loaded = scene.isLoaded,
                    Active = scene == SceneManager.GetActiveScene(), Dirty = scene.isDirty };
            }
            return result;
        }

        private static Direction ParseDirection(char character)
        {
            switch (character)
            {
                case 'U': return Direction.Up;
                case 'R': return Direction.Right;
                case 'D': return Direction.Down;
                case 'L': return Direction.Left;
                default: throw new InvalidOperationException("Unsupported solution character: " + character);
            }
        }

        private static string ModeName => state.Round == 0 ? "normal-domain-reload" : "disabled-domain-reload";
        private static bool SamePath(string a, string b) => string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

        private static void SetStage(string stage, int timeoutSeconds = 120)
        {
            state.Stage = stage;
            state.Deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds).Ticks;
            Persist();
        }

        private static void Append(string line)
        {
            state.Report += DateTime.UtcNow.ToString("O") + "  " + line + "\n";
            Directory.CreateDirectory("Logs");
            File.WriteAllText(ResultPath, state.Report);
            Persist();
        }

        private static void Persist() => SessionState.SetString(StateKey, JsonUtility.ToJson(state));
    }
}
