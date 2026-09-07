using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Sokoban.Content;
using Sokoban.Core;
using Sokoban.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Sokoban.EditorTools
{
    /// <summary>
    /// Explicit disposable-copy batch check. Run without -quit using
    /// -executeMethod Sokoban.EditorTools.RuntimeAuthoringSmoke.Begin.
    /// </summary>
    [InitializeOnLoad]
    public static class RuntimeAuthoringSmoke
    {
        private const string Key = "Sokoban.RuntimeAuthoringSmoke";
        private const string ReportPath = "Logs/runtime-authoring-results.txt";
        private static readonly string[] ProgressFiles = { "progress.json", "progress.json.bak", "progress.json.tmp", "progress.json.unreadable" };
        private static State state;

        [Serializable]
        private sealed class FileSnapshot { public string Name; public bool Exists; public string Bytes; }
        [Serializable]
        private sealed class State
        {
            public string Stage, Folder, ResourcesPath, CatalogPath, SourcePath, SourceJson;
            public string[] OriginalCatalogPaths;
            public string SavedPath, SavedJson, SavedBytes, OriginalProduct, SmokeProduct, OriginalProgressPath, SmokeProgressPath;
            public string OriginalStartScene, Report, Error;
            public bool OriginalOptionsEnabled;
            public int OriginalOptions, Round;
            public long Deadline;
            public FileSnapshot[] OriginalProgress;
        }

        static RuntimeAuthoringSmoke()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        public static void Begin()
        {
            if (!Application.isBatchMode || Array.IndexOf(Environment.GetCommandLineArgs(), "-quit") >= 0)
                throw new InvalidOperationException("RuntimeAuthoringSmoke requires a disposable project copy in batch mode without -quit.");
            try
            {
                Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Start this check in Edit mode.");
                var resources = AssetDatabase.LoadAssetAtPath<GameResources>(ProjectSetup.ResourcesPath);
                Require(resources != null && resources.Catalog != null && resources.Catalog.Levels.Count > 0, "Prepare the project resources first.");
                var source = resources.Catalog.Levels[0];
                Require(source != null && LevelValidator.Validate(source.Data).Count == 0, "The source room must be valid.");
                string token = Guid.NewGuid().ToString("N");
                state = new State
                {
                    Folder = "Assets/__RuntimeAuthoringSmoke_" + token,
                    SourcePath = AssetDatabase.GetAssetPath(source), SourceJson = JsonUtility.ToJson(source.Data),
                    OriginalCatalogPaths = CatalogPaths(resources.Catalog),
                    OriginalProduct = PlayerSettings.productName, SmokeProduct = "SokobanAuthoringSmoke-" + token,
                    OriginalProgressPath = Path.GetFullPath(Application.persistentDataPath),
                    OriginalOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled,
                    OriginalOptions = (int)EditorSettings.enterPlayModeOptions,
                    OriginalStartScene = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),
                    Report = "Runtime authoring persistence: default and disabled domain reload\n"
                };
                state.OriginalProgress = CaptureProgress(state.OriginalProgressPath);
                AssetDatabase.CreateFolder("Assets", Path.GetFileName(state.Folder));
                state.ResourcesPath = state.Folder + "/GameResources.asset";
                state.CatalogPath = state.Folder + "/LevelCatalog.asset";
                var catalog = ScriptableObject.CreateInstance<LevelCatalog>();
                AssetDatabase.CreateAsset(catalog, state.CatalogPath);
                var isolatedResources = ScriptableObject.CreateInstance<GameResources>();
                isolatedResources.Config = resources.Config;
                isolatedResources.Visuals = resources.Visuals;
                isolatedResources.Catalog = catalog;
                AssetDatabase.CreateAsset(isolatedResources, state.ResourcesPath);
                AssetDatabase.SaveAssets();
                SessionState.SetBool(Key + ".Active", true);
                SetStage("IsolateProgress");
                PlayerSettings.productName = state.SmokeProduct;
            }
            catch (Exception error)
            {
                if (state != null) Fail(error);
                else { Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, error.ToString()); EditorApplication.Exit(1); }
            }
        }

        private static void Tick()
        {
            if (!Application.isBatchMode || !SessionState.GetBool(Key + ".Active", false)) return;
            if (state == null) state = JsonUtility.FromJson<State>(SessionState.GetString(Key, ""));
            if (state == null) return;
            try
            {
                if (state.Stage == "Failing")
                {
                    if (!EditorApplication.isPlayingOrWillChangePlaymode || DateTime.UtcNow.Ticks > state.Deadline) Finish(false);
                    return;
                }
                Require(DateTime.UtcNow.Ticks < state.Deadline, "Timed out in " + state.Stage);
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                switch (state.Stage)
                {
                    case "IsolateProgress":
                        state.SmokeProgressPath = Path.GetFullPath(Application.persistentDataPath);
                        Require(!SamePath(state.SmokeProgressPath, state.OriginalProgressPath) &&
                            new DirectoryInfo(state.SmokeProgressPath).Name == state.SmokeProduct, "Persistent data isolation failed.");
                        SetStage("EnterPlay");
                        break;
                    case "EnterPlay": EnterPlay(); break;
                    case "Author": Author(); break;
                    case "Reload": Reload(); break;
                    default: throw new InvalidOperationException("Unknown smoke stage: " + state.Stage);
                }
            }
            catch (Exception error) { Fail(error); }
        }

        private static void EnterPlay()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "The previous round is still running.");
            EditorSettings.enterPlayModeOptionsEnabled = state.Round != 0;
            EditorSettings.enterPlayModeOptions = state.Round == 0 ? EnterPlayModeOptions.None : EnterPlayModeOptions.DisableDomainReload;
            SetStage("Author");
            Require(EditorPlaytestBridge.Start(AssetDatabase.LoadAssetAtPath<LevelAsset>(state.SourcePath)), "Could not start isolated playtest.");
        }

        private static void Author()
        {
            if (!EditorApplication.isPlaying) return;
            var app = UnityEngine.Object.FindObjectOfType<GameController>();
            if (app == null) return;
            Require(app.IsPlaytest && app.Session != null, "Expected a playtest session before authoring.");
            LevelAuthoringServices.AssetWriter = new EditorLevelAssetWriter(state.ResourcesPath, state.Folder + "/Levels");
            app.OpenWorkshop();
            var workshop = app.Workshop;
            Require(workshop != null && app.CurrentScreen == GameController.ScreenState.Workshop, "Runtime workshop did not open.");
            var definition = JsonUtility.FromJson<LevelDefinition>(state.SourceJson);
            definition.Name = "运行保存验证 " + state.Round;
            workshop.SetDocument(definition, null);
            Require(workshop.Save(LevelSaveIntent.SaveAndAddToCatalog), "Save and add to catalog failed during Play mode.");
            var created = workshop.Source;
            Require(created != null && created.Data.LayoutVersion == 0 && AssetDatabase.Contains(created), "The new room is not a persistent version-zero asset.");
            definition = workshop.Draft;
            bool changed = false;
            for (int y = 1; y < definition.Height - 1 && !changed; y++)
            for (int x = 1; x < definition.Width - 1 && !changed; x++)
            {
                var cell = new GridPos(x, y);
                if (definition.CellAt(cell) == CellType.Floor && cell != definition.PlayerStart && !definition.IsGoal(cell) && Array.IndexOf(definition.Boxes, cell) < 0)
                { definition.Cells[y * definition.Width + x] = CellType.Wall; changed = true; }
            }
            Require(changed, "The source room has no free terrain cell for a layout-edit check.");
            workshop.SetDocument(definition, created);
            Require(workshop.Save(), "Saving a layout edit failed during Play mode.");
            Require(workshop.Source.Data.LayoutVersion == 1, "A structural edit did not increment LayoutVersion.");
            state.SavedPath = AssetDatabase.GetAssetPath(workshop.Source);
            state.SavedJson = JsonUtility.ToJson(workshop.Source.Data);
            state.SavedBytes = Convert.ToBase64String(File.ReadAllBytes(state.SavedPath));
            Require(workshop.StartPlaytest(), "Runtime workshop could not start its own playtest.");
            Require(app.IsPlaytest && app.Session.Definition.Id == workshop.Source.Data.Id, "Playtest did not consume the saved draft.");
            app.OpenWorkshop();
            Require(app.CurrentScreen == GameController.ScreenState.Workshop && JsonUtility.ToJson(workshop.Draft) == state.SavedJson,
                "Returning from playtest lost the in-session authoring document.");
            workshop.RequestReturn();
            Require(app.IsPlaytest && app.Session.Definition.Id == JsonUtility.FromJson<LevelDefinition>(state.SourceJson).Id,
                "The original game session was not restored after leaving authoring.");
            SetStage("Reload");
            PlaytestRequest.RequestExit();
        }

        private static void Reload()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            Require(File.Exists(state.SavedPath), "The saved .asset disappeared on leaving Play mode.");
            Require(Convert.ToBase64String(File.ReadAllBytes(state.SavedPath)) == state.SavedBytes, "The on-disk .asset changed on leaving Play mode.");
            AssetDatabase.ImportAsset(state.SavedPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(state.CatalogPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var level = AssetDatabase.LoadAssetAtPath<LevelAsset>(state.SavedPath);
            var catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>(state.CatalogPath);
            Require(level != null && JsonUtility.ToJson(level.Data) == state.SavedJson, "Reloaded .asset does not match the runtime save.");
            Require(catalog != null && catalog.Levels.Contains(level), "The saved catalog reference did not survive leaving Play mode.");
            LevelEditorWindow.OpenLevel(level);
            var window = EditorWindow.GetWindow<LevelEditorWindow>();
            var opened = typeof(LevelEditorWindow).GetField("source", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window) as LevelAsset;
            Require(opened == level && !window.hasUnsavedChanges, "The original EditorWindow could not open the runtime-authored asset cleanly.");
            window.Close();
            Require(JsonUtility.ToJson(AssetDatabase.LoadAssetAtPath<LevelAsset>(state.SourcePath).Data) == state.SourceJson, "The original source asset was mutated.");
            var originalResources = AssetDatabase.LoadAssetAtPath<GameResources>(ProjectSetup.ResourcesPath);
            Require(string.Join("\n", CatalogPaths(originalResources.Catalog)) == string.Join("\n", state.OriginalCatalogPaths), "The production catalog was mutated.");
            AssertProgressUnchanged();
            Append("PASS " + (state.Round == 0 ? "default-domain-reload" : "disabled-domain-reload") +
                ": save, version increment, in-session playtest return, exit Play, disk reload, catalog reference and legacy EditorWindow.");
            state.Round++;
            if (state.Round < 2) SetStage("EnterPlay");
            else Finish(true);
        }

        private static void AssertProgressUnchanged()
        {
            foreach (var item in state.OriginalProgress)
            {
                string path = Path.Combine(state.OriginalProgressPath, item.Name);
                Require(File.Exists(path) == item.Exists, "User progress file existence changed: " + item.Name);
                if (item.Exists) Require(Convert.ToBase64String(File.ReadAllBytes(path)) == item.Bytes, "User progress changed: " + item.Name);
            }
            if (!string.IsNullOrEmpty(state.SmokeProgressPath))
                foreach (string name in ProgressFiles) Require(!File.Exists(Path.Combine(state.SmokeProgressPath, name)), "Authoring playtest wrote progress: " + name);
        }

        private static FileSnapshot[] CaptureProgress(string directory)
        {
            var result = new List<FileSnapshot>();
            foreach (string name in ProgressFiles)
            {
                string path = Path.Combine(directory, name);
                bool exists = File.Exists(path);
                result.Add(new FileSnapshot { Name = name, Exists = exists, Bytes = exists ? Convert.ToBase64String(File.ReadAllBytes(path)) : "" });
            }
            return result.ToArray();
        }

        private static string[] CatalogPaths(LevelCatalog catalog)
        {
            var paths = new List<string>();
            foreach (var asset in catalog.Levels) paths.Add(asset != null ? AssetDatabase.GetAssetPath(asset) : "<missing>");
            return paths.ToArray();
        }

        private static void Fail(Exception error)
        {
            if (state.Stage == "Failing") return;
            state.Error = error.ToString();
            Append("FAIL " + state.Error);
            SetStage("Failing", 35);
            if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = false;
            else Finish(false);
        }

        private static void Finish(bool success)
        {
            var failures = new List<string>();
            Cleanup(() => AssertProgressUnchanged(), failures);
            Cleanup(() => EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)state.OriginalOptions, failures);
            Cleanup(() => EditorSettings.enterPlayModeOptionsEnabled = state.OriginalOptionsEnabled, failures);
            Cleanup(() => EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(state.OriginalStartScene) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(state.OriginalStartScene), failures);
            Cleanup(() => PlayerSettings.productName = state.OriginalProduct, failures);
            Cleanup(() => LevelAuthoringServices.AssetWriter = new EditorLevelAssetWriter(), failures);
            Cleanup(() =>
            {
                Require(state.Folder.StartsWith("Assets/__RuntimeAuthoringSmoke_", StringComparison.Ordinal) &&
                    Path.GetFullPath(state.Folder).StartsWith(Path.GetFullPath("Assets") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Refusing to remove an unverified smoke folder.");
                if (AssetDatabase.IsValidFolder(state.Folder)) AssetDatabase.DeleteAsset(state.Folder);
            }, failures);
            foreach (string failure in failures) Append("FAIL during cleanup: " + failure);
            success &= failures.Count == 0;
            Append(success ? "PASS: both runtime-authoring rounds completed; production assets and user progress preserved." : "FAIL: runtime-authoring smoke did not complete.");
            SessionState.SetBool(Key + ".Active", false);
            SessionState.EraseString(Key);
            EditorApplication.Exit(success ? 0 : 1);
        }

        private static void Cleanup(Action action, List<string> failures)
        { try { action(); } catch (Exception error) { failures.Add(error.Message); } }
        private static bool SamePath(string a, string b) => string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void SetStage(string stage, int seconds = 150)
        { state.Stage = stage; state.Deadline = DateTime.UtcNow.AddSeconds(seconds).Ticks; Persist(); }
        private static void Persist() => SessionState.SetString(Key, JsonUtility.ToJson(state));
        private static void Append(string message)
        {
            state.Report += DateTime.UtcNow.ToString("O") + " " + message + "\n";
            Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, state.Report); Persist();
        }
    }
}
