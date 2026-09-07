using System;
using System.Collections.Generic;
using System.IO;
using Sokoban.Content;
using Sokoban.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Sokoban.EditorTools
{
    /// <summary>Reproducible first-time content creation. Existing authored assets are preserved.</summary>
    public static class ProjectSetup
    {
        public const string ResourcesPath = "Assets/Resources/GameResources.asset";
        public const string GameScene = "Assets/Scenes/Game.unity";

        [MenuItem("推箱子/准备项目资源", priority = 50)]
        public static void Initialize()
        {
            Directory.CreateDirectory("Assets/Resources/Levels");
            Directory.CreateDirectory("Assets/Sprites");
            Directory.CreateDirectory("Assets/Audio");
            Directory.CreateDirectory("Assets/Resources");
            Directory.CreateDirectory("Assets/Resources/Configs");
            Directory.CreateDirectory("Assets/Scenes");
            AssetDatabase.Refresh();
            if (!File.Exists("Assets/TextMesh Pro/Resources/TMP Settings.asset"))
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMPro.TMP_Text).Assembly);
                var package = Path.Combine(info.resolvedPath, "Package Resources/TMP Essential Resources.unitypackage");
                AssetDatabase.ImportPackage(package, false);
            }
            var resources = LoadOrCreate<GameResources>(ResourcesPath);
            resources.Config = resources.Config != null ? resources.Config : LoadOrCreate<GameConfig>("Assets/Resources/Configs/GameConfig.asset");
            resources.Visuals = resources.Visuals != null ? resources.Visuals : LoadOrCreate<VisualConfig>("Assets/Resources/Configs/VisualConfig.asset");
            bool createCatalog = resources.Catalog == null;
            resources.Catalog = resources.Catalog != null ? resources.Catalog : LoadOrCreate<LevelCatalog>("Assets/Resources/Configs/LevelCatalog.asset");
            if (createCatalog && resources.Catalog.Levels.Count == 0)
            {
                var levels = BuiltInLevels.Create();
                for (int i = 0; i < levels.Length; i++)
                {
                    var path = $"Assets/Resources/Levels/{i + 1:00}-{levels[i].Name.Replace(' ', '-')}.asset";
                    var asset = AssetDatabase.LoadAssetAtPath<LevelAsset>(path);
                    if (asset == null)
                    {
                        asset = ScriptableObject.CreateInstance<LevelAsset>();
                        asset.Data = levels[i]; asset.VerifiedSolution = BuiltInLevels.Solutions[i];
                        AssetDatabase.CreateAsset(asset, path);
                    }
                    resources.Catalog.Levels.Add(asset);
                }
            }
            PrepareVisuals(resources.Visuals);
            EditorUtility.SetDirty(resources);
            EditorUtility.SetDirty(resources.Catalog);
            EditorUtility.SetDirty(resources.Visuals);
            AssetDatabase.SaveAssets();
            if (!File.Exists(GameScene))
            {
                // Copy the shipped template asset without replacing an open, possibly unsaved scene.
                if (!AssetDatabase.CopyAsset("Assets/Scenes/SampleScene.unity", GameScene))
                    throw new BuildFailedException("缺少 SampleScene 场景模板，请恢复后重新准备项目。");
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(GameScene, true) };
            PlayerSettings.companyName = "Sokoban Workshop";
            PlayerSettings.productName = "Crate Shift";
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
            AssetDatabase.SaveAssets();
            PresentationSetup.Ensure();
            MechanicsContentSetup.Install();
            ValidateCatalog();
            Debug.Log("推箱子资源已准备好。打开 Game 场景或使用关卡编辑器。");
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(asset, path); return asset;
        }

        [MenuItem("推箱子/校验关卡目录", priority = 51)]
        public static void ValidateCatalog()
        {
            var resources = AssetDatabase.LoadAssetAtPath<GameResources>(ResourcesPath);
            if (resources == null || resources.Catalog == null || resources.Config == null || resources.Visuals == null)
                throw new BuildFailedException("游戏资源不完整，请执行“推箱子 > 准备项目资源”。");
            if (resources.Catalog.Levels == null || resources.Catalog.Levels.Count == 0)
                throw new BuildFailedException("关卡目录至少需要一个关卡。");
            var ids = new HashSet<string>();
            var registry = resources.Elements?.Snapshot() ?? ElementRegistry.BuiltIns();
            var typeErrors = registry.ValidateTypes();
            if (typeErrors.Count > 0) throw new BuildFailedException("元素目录配置有误：" + typeErrors[0].Message);
            foreach (var asset in resources.Catalog.Levels)
            {
                if (asset == null) throw new BuildFailedException("关卡目录存在丢失的资源引用。");
                var errors = LevelValidator.Validate(asset.Data, registry);
                if (errors.Count > 0) throw new BuildFailedException(asset.name + ": " + errors[0]);
                if (!ids.Add(asset.Data.Id)) throw new BuildFailedException("关卡编号重复：" + asset.Data.Id);
                string solution = asset.GetVerifiedSolution(registry);
                if (!string.IsNullOrEmpty(solution))
                {
                    var game = new GameSession(asset.ToDefinition(), registry);
                    foreach (char action in solution)
                    {
                        Direction d;
                        switch (action) { case 'U': d = Direction.Up; break; case 'R': d = Direction.Right; break;
                            case 'D': d = Direction.Down; break; case 'L': d = Direction.Left; break;
                            default: throw new BuildFailedException("验证解含有未知操作：" + asset.name); }
                        if (!game.TryMove(d).Succeeded) throw new BuildFailedException("验证解已失效：" + asset.name);
                    }
                    if (!game.IsWon) throw new BuildFailedException("验证解未能通关：" + asset.name);
                }
            }
            Debug.Log("关卡目录校验通过，共 " + ids.Count + " 关。没有验证解的关卡仍需试玩确认能够通关。");
        }

        [MenuItem("推箱子/构建 Windows", priority = 60)]
        public static void BuildWindows()
        {
            BuildWindowsPlayer(false);
        }

        public static void BuildValidationPlayer()
        {
            BuildWindowsPlayer(true);
        }

        private static void BuildWindowsPlayer(bool development)
        {
            ValidateCatalog();
            var directory = development ? "Builds/Validation" : "Builds/Windows";
            Directory.CreateDirectory(directory);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { GameScene }, locationPathName = directory + "/CrateShift.exe",
                target = BuildTarget.StandaloneWindows64, options = development ? BuildOptions.Development : BuildOptions.None
            });
            Directory.CreateDirectory("Logs");
            var summary = $"Result: {report.summary.result}\nErrors: {report.summary.totalErrors}\nWarnings: {report.summary.totalWarnings}\nSize: {report.summary.totalSize}\nDuration: {report.summary.totalTime}\n";
            foreach (var step in report.steps)
                foreach (var message in step.messages)
                    if (message.type == LogType.Warning || message.type == LogType.Error || message.type == LogType.Exception)
                        summary += message.type + ": " + message.content + "\n";
            File.WriteAllText(development ? "Logs/validation-build-report.txt" : "Logs/build-report.txt", summary);
            if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException("Windows build failed. See Logs/build-report.txt.");
        }

        private static void PrepareVisuals(VisualConfig v)
        {
            PixelArtAssets.Ensure(v);
            if (!v.MoveSound) v.MoveSound = CreateSound("Move", 260, .055f);
            if (!v.PushSound) v.PushSound = CreateSound("Push", 155, .10f);
            if (!v.BlockedSound) v.BlockedSound = CreateSound("Blocked", 95, .07f);
            if (!v.WinSound) v.WinSound = CreateSound("Clear", 523.25f, .46f, true);
        }

        private static AudioClip CreateSound(string name, float frequency, float seconds, bool chord = false)
        {
            var path = "Assets/Audio/" + name + ".wav";
            if (!File.Exists(path))
            {
                const int rate = 22050;
                int count = (int)(seconds * rate);
                using (var writer = new BinaryWriter(File.Create(path)))
                {
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + count * 2);
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                    writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2);
                    writer.Write((short)2); writer.Write((short)16); writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(count * 2);
                    for (int i = 0; i < count; i++)
                    {
                        float time = (float)i / rate;
                        double phase = 2 * Math.PI * frequency * time;
                        double wave = Math.Sin(phase);
                        if (chord) wave = (wave + Math.Sin(phase * 1.25) + Math.Sin(phase * 1.5)) / 3;
                        double envelope = Math.Min(time / .006, 1) * Math.Pow(1 - time / seconds, 2);
                        writer.Write((short)(wave * envelope * 11000));
                    }
                }
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }
    }

    internal sealed class CatalogBuildCheck : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report) => ProjectSetup.ValidateCatalog();
    }
}
