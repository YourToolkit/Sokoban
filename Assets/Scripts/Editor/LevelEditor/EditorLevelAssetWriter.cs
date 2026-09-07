using System;
using System.Collections.Generic;
using System.IO;
using Sokoban.Content;
using Sokoban.Core;
using Sokoban.Runtime;
using UnityEditor;
using UnityEngine;

namespace Sokoban.EditorTools
{
    /// <summary>The single project-asset persistence adapter used by both authoring interfaces.</summary>
    public sealed class EditorLevelAssetWriter : ILevelAssetWriter
    {
        public const string DefaultResourcesPath = "Assets/Resources/GameResources.asset";
        public const string DefaultLevelFolder = "Assets/Resources/Levels";
        public static event Action<LevelAsset> AssetSaved;
        private readonly string resourcesPath;
        private readonly string levelFolder;

        // Configurable project paths keep integration tests isolated; there is still one .asset backend.
        public EditorLevelAssetWriter(string resourcesPath = DefaultResourcesPath, string levelFolder = DefaultLevelFolder)
        { this.resourcesPath = resourcesPath; this.levelFolder = levelFolder; }

        public IReadOnlyList<LevelAsset> ListLevels()
        {
            var paths = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:LevelAsset")) paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            paths.Sort(StringComparer.Ordinal);
            var levels = new List<LevelAsset>();
            foreach (string path in paths)
            {
                var asset = AssetDatabase.LoadAssetAtPath<LevelAsset>(path);
                if (asset != null) levels.Add(asset);
            }
            return levels.AsReadOnly();
        }

        public bool IsInCatalog(LevelAsset asset)
        {
            var catalog = LoadCatalog();
            return asset != null && catalog != null && catalog.Levels != null && catalog.Levels.Contains(asset);
        }

        public LevelSaveResult Save(LevelAsset target, LevelDefinition snapshot, string verifiedSolution, LevelSaveIntent intent)
            => SaveAtPath(target, snapshot, verifiedSolution, intent, null);

        public LevelSaveResult SaveAtPath(LevelAsset target, LevelDefinition snapshot, string verifiedSolution,
            LevelSaveIntent intent, string newAssetPath)
        {
            LevelAsset destination = null;
            LevelDefinition oldDefinition = null;
            string oldSolution = null;
            LevelCatalog catalog = null;
            List<LevelAsset> oldCatalogEntries = null;
            string path = null;
            bool created = false;
            bool assetTouched = false;
            bool catalogTouched = false;
            try
            {
                if (snapshot == null) return Failure("没有可保存的关卡草稿。");
                if (!Enum.IsDefined(typeof(LevelSaveIntent), intent)) return Failure("无法识别保存操作。");
                if (target != null && !AssetDatabase.Contains(target)) return Failure("保存目标不是工程中的关卡资产，请先新建或另存为关卡。");
                if (snapshot.Width < 2 || snapshot.Width > 32 || snapshot.Height < 2 || snapshot.Height > 32 ||
                    snapshot.Cells == null || snapshot.Cells.Length != snapshot.Width * snapshot.Height)
                    return Failure("地图尺寸或地形数组损坏，请修复后再保存。");

                bool makeNew = target == null || intent == LevelSaveIntent.SaveAs;
                var definition = snapshot.DeepClone();
                bool layoutChanged = target != null && target.Data != null && !LevelAuthoring.LayoutEquals(target.Data, definition);
                if (makeNew)
                {
                    definition.Id = Guid.NewGuid().ToString("N");
                    definition.LayoutVersion = 0;
                }
                else if (target.Data != null)
                {
                    definition.Id = target.Data.Id;
                    definition.LayoutVersion = layoutChanged ? checked(target.Data.LayoutVersion + 1) : target.Data.LayoutVersion;
                }
                if (string.IsNullOrWhiteSpace(definition.Id)) definition.Id = Guid.NewGuid().ToString("N");

                catalog = LoadCatalog();
                bool published = !makeNew && catalog != null && catalog.Levels != null && catalog.Levels.Contains(target);
                bool addToCatalog = intent == LevelSaveIntent.SaveAndAddToCatalog;
                if (published || addToCatalog)
                {
                    var issues = LevelValidator.Validate(definition);
                    if (issues.Count > 0) return Failure("正式关卡必须通过校验：" + issues[0].Message + " 可另存为草稿保留修改。");
                }
                if (addToCatalog && (catalog == null || catalog.Levels == null)) return Failure("关卡目录未配置，请先准备工程资源。");
                if (published || addToCatalog)
                    foreach (var entry in catalog.Levels)
                        if (entry != null && entry != target && entry.Data != null && entry.Data.Id == definition.Id)
                            return Failure("目录中存在相同的关卡 ID，请另存为独立关卡。");

                if (makeNew)
                {
                    EnsureFolder(levelFolder);
                    path = string.IsNullOrWhiteSpace(newAssetPath)
                        ? AssetDatabase.GenerateUniqueAssetPath(levelFolder.TrimEnd('/') + "/" + SafeName(definition.Name) + ".asset")
                        : newAssetPath.Replace('\\', '/');
                    ValidateAssetPath(path);
                    if (AssetDatabase.LoadMainAssetAtPath(path) != null || File.Exists(path)) return Failure("该位置已有资产，请选择其他文件名。");
                    EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));
                }
                else
                {
                    destination = target;
                    path = AssetDatabase.GetAssetPath(target);
                    ValidateAssetPath(path);
                    EnsureWritable(path);
                }
                if (addToCatalog && (makeNew || !catalog.Levels.Contains(target))) EnsureWritable(AssetDatabase.GetAssetPath(catalog));

                if (makeNew)
                {
                    destination = ScriptableObject.CreateInstance<LevelAsset>();
                    destination.Data = definition;
                    destination.VerifiedSolution = layoutChanged ? "" : verifiedSolution ?? "";
                    AssetDatabase.CreateAsset(destination, path);
                    created = true;
                    if (!AssetDatabase.Contains(destination) || !File.Exists(path)) throw new IOException("未能创建关卡资产。");
                }
                else
                {
                    oldDefinition = destination.Data?.DeepClone();
                    oldSolution = destination.VerifiedSolution;
                    destination.Data = definition;
                    destination.VerifiedSolution = layoutChanged ? "" : verifiedSolution ?? "";
                    assetTouched = true;
                }
                EditorUtility.SetDirty(destination);
                AssetDatabase.SaveAssetIfDirty(destination);
                if (EditorUtility.IsDirty(destination)) throw new IOException("关卡资产没有成功写入磁盘。");

                if (addToCatalog && !catalog.Levels.Contains(destination))
                {
                    oldCatalogEntries = new List<LevelAsset>(catalog.Levels);
                    catalog.Levels.Add(destination); // The reference now points to an asset with a persistent GUID.
                    catalogTouched = true;
                    EditorUtility.SetDirty(catalog);
                    AssetDatabase.SaveAssetIfDirty(catalog);
                    if (EditorUtility.IsDirty(catalog)) throw new IOException("关卡目录没有成功写入磁盘。");
                }
                NotifySaved(destination);
                string message = IsInCatalog(destination) ? "关卡已保存到工程，并保留在正式目录中。" : "关卡草稿已保存到工程。";
                if (layoutChanged && !makeNew) message += " 布局版本已更新，旧布局成绩不再沿用。";
                return new LevelSaveResult(true, destination, message);
            }
            catch (Exception exception)
            {
                // A failed save must not leave a changed source object in the running game.
                if (catalogTouched && catalog != null)
                {
                    catalog.Levels = oldCatalogEntries;
                    TryRestore(catalog);
                }
                if (assetTouched && destination != null)
                {
                    destination.Data = oldDefinition;
                    destination.VerifiedSolution = oldSolution;
                    TryRestore(destination);
                }
                if (created && destination != null && !string.IsNullOrEmpty(path) && AssetDatabase.LoadMainAssetAtPath(path) == destination)
                {
                    try { ValidateAssetPath(path); AssetDatabase.DeleteAsset(path); } catch (Exception) { }
                }
                else if (destination != null && destination != target && !AssetDatabase.Contains(destination)) UnityEngine.Object.DestroyImmediate(destination);
                Debug.LogWarning("工程关卡保存失败：" + exception);
                return Failure("保存失败，草稿仍保留在编辑器中。请检查保存位置、文件只读状态和写入权限。");
            }
        }

        private LevelCatalog LoadCatalog() => AssetDatabase.LoadAssetAtPath<GameResources>(resourcesPath)?.Catalog;
        private static LevelSaveResult Failure(string message) => new LevelSaveResult(false, null, message);
        private static void TryRestore(UnityEngine.Object asset)
        {
            try { EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset); } catch (Exception) { }
        }

        private static void NotifySaved(LevelAsset asset)
        {
            if (AssetSaved == null) return;
            foreach (Action<LevelAsset> observer in AssetSaved.GetInvocationList())
            {
                try { observer(asset); }
                catch (Exception exception) { Debug.LogWarning("关卡已保存，但有一个编辑窗口未能刷新：" + exception.Message); }
            }
        }

        private static string SafeName(string name)
        {
            string result = string.IsNullOrWhiteSpace(name) ? "新关卡" : name.Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars()) result = result.Replace(invalid, '_');
            result = result.TrimEnd('.', ' ');
            return string.IsNullOrEmpty(result) ? "新关卡" : result;
        }

        private static void ValidateAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) ||
                !string.Equals(Path.GetExtension(path), ".asset", StringComparison.OrdinalIgnoreCase)) throw new IOException("关卡只能保存为 Assets 内的 .asset 文件。");
            string fullPath = Path.GetFullPath(path);
            string assetsRoot = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase)) throw new IOException("保存位置超出了工程 Assets 目录。");
        }

        private static void EnsureWritable(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) throw new IOException("目标资产文件不存在。");
            if ((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0) throw new IOException("目标资产为只读文件，请先解除只读属性。");
        }

        private static void EnsureFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || folder == "Assets") return;
            if (!folder.StartsWith("Assets/", StringComparison.Ordinal)) throw new IOException("关卡目录必须位于 Assets 内。");
            string absolute = Path.GetFullPath(folder);
            string root = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!absolute.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("关卡目录超出了工程 Assets 范围。");
            if (File.Exists(folder)) throw new IOException("保存目录的位置已被文件占用。");
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            EnsureFolder(parent);
            string guid = AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
            if (string.IsNullOrEmpty(guid)) throw new IOException("无法创建关卡目录。");
        }
    }

    [InitializeOnLoad]
    internal static class EditorLevelAssetWriterRegistration
    {
        static EditorLevelAssetWriterRegistration()
        {
            Install();
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void Install() => LevelAuthoringServices.AssetWriter = new EditorLevelAssetWriter();
        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode || change == PlayModeStateChange.EnteredPlayMode || change == PlayModeStateChange.EnteredEditMode) Install();
        }
    }
}
