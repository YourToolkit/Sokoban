using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Player;

namespace Sokoban.EditorTools
{
    /// <summary>Compiles player scripts only; no scenes, assets or executable are built.</summary>
    public static class PlayerScriptBoundaryCheck
    {
        public static void Run()
        {
            const string report = "Logs/player-script-boundary.txt";
            Directory.CreateDirectory("Logs");
            File.WriteAllText(report, "Player script boundary check (no game package)\n");
            foreach (string asset in AssetDatabase.GetDependencies(new[] { ProjectSetup.GameScene, PresentationSetup.RootPath }, true))
                if (asset.StartsWith("Assets/") && asset.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new InvalidOperationException("Editor asset leaked into the game dependency graph: " + asset);
            File.AppendAllText(report, "Scene/prefab dependencies: PASS - no Editor assets.\n");
            foreach (bool development in new[] { false, true })
            {
                string output = "Library/PlayerScriptBoundary/" + (development ? "Development" : "Release");
                Directory.CreateDirectory(output);
                var result = PlayerBuildInterface.CompilePlayerScripts(new ScriptCompilationSettings
                {
                    target = BuildTarget.StandaloneWindows64,
                    group = BuildTargetGroup.Standalone,
                    options = development ? ScriptCompilationOptions.DevelopmentBuild : ScriptCompilationOptions.None
                }, output);
                if (result.assemblies == null || result.assemblies.Count == 0)
                    throw new InvalidOperationException("Player script compilation failed.");
                if (result.assemblies.Any(path => Path.GetFileNameWithoutExtension(path) == "Sokoban.Editor" ||
                    Path.GetFileNameWithoutExtension(path).StartsWith("Sokoban.") && Path.GetFileNameWithoutExtension(path).EndsWith("Tests")))
                    throw new InvalidOperationException("An Editor or test assembly entered the player compilation.");
                var runtime = Assembly.Load(File.ReadAllBytes(Path.Combine(output, "Sokoban.Runtime.dll")));
                foreach (string type in new[] { "RuntimeWorkshop", "WorkshopTool", "WorkshopToolIcon", "WorkshopPointerHint",
                    "WorkshopViews", "WorkshopViewCatalog", "WorkshopEntry", "PlaytestRequest", "ILevelAssetWriter", "LevelAuthoringServices", "LevelSaveIntent", "LevelSaveResult", "DevelopmentCapture" })
                    if (runtime.GetType("Sokoban.Runtime." + type) != null)
                        throw new InvalidOperationException("Editor-only type remains in player scripts: " + type);
                var app = runtime.GetType("Sokoban.Runtime.GameController", true);
                foreach (string method in new[] { "OpenWorkshop", "StartPlaytest", "BeginWorkshopView", "ReturnFromWorkshop" })
                    if (app.GetMethod(method, BindingFlags.Public | BindingFlags.Instance) != null)
                        throw new InvalidOperationException("Editor entry remains in player scripts: " + method);
                if (runtime.GetReferencedAssemblies().Any(reference => reference.Name.StartsWith("UnityEditor")))
                    throw new InvalidOperationException("Player runtime references UnityEditor.");
                File.AppendAllText(report, (development ? "Development" : "Release") +
                    ": PASS - compiled; authoring types, entries and Editor references absent.\n");
            }
        }
    }
}
