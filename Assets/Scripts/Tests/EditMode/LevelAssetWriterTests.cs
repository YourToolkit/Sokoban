using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Sokoban.Content;
using Sokoban.Core;
using Sokoban.EditorTools;
using Sokoban.Runtime;
using UnityEditor;
using UnityEngine;

namespace Sokoban.Tests
{
    public sealed class LevelAssetWriterTests
    {
        private string folder;
        private string catalogPath;
        private LevelCatalog catalog;
        private EditorLevelAssetWriter writer;
        private LevelEditorWindow window;

        [SetUp]
        public void CreateIsolatedProjectAssets()
        {
            string folderName = "__AuthoringTests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folderName);
            folder = "Assets/" + folderName;
            AssetDatabase.CreateFolder(folder, "Levels");
            catalog = ScriptableObject.CreateInstance<LevelCatalog>();
            catalogPath = folder + "/Catalog.asset";
            AssetDatabase.CreateAsset(catalog, catalogPath);
            var resources = ScriptableObject.CreateInstance<GameResources>();
            resources.Catalog = catalog;
            AssetDatabase.CreateAsset(resources, folder + "/GameResources.asset");
            writer = new EditorLevelAssetWriter(folder + "/GameResources.asset", folder + "/Levels");
            AssetDatabase.SaveAssetIfDirty(resources);
        }

        [TearDown]
        public void RemoveOnlyTheIsolatedTestAssets()
        {
            if (window != null) { window.DiscardChanges(); UnityEngine.Object.DestroyImmediate(window); }
            string absolute = Path.GetFullPath(folder);
            string expectedPrefix = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar + "__AuthoringTests_";
            Assert.That(absolute.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase), Is.True);
            foreach (string path in Directory.GetFiles(absolute, "*", SearchOption.AllDirectories)) File.SetAttributes(path, FileAttributes.Normal);
            AssetDatabase.DeleteAsset(folder);
        }

        [Test]
        public void AnIncompleteNewDraftSavesWithoutJoiningTheCatalogOrMutatingItsInput()
        {
            var draft = LevelAuthoring.CreateBlank();
            draft.LayoutVersion = 12;
            string before = JsonUtility.ToJson(draft);
            var result = writer.Save(null, draft, "", LevelSaveIntent.Save);
            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(AssetDatabase.Contains(result.Asset), Is.True);
            Assert.That(result.Asset.Data.Id, Is.Not.EqualTo(draft.Id));
            Assert.That(result.Asset.Data.LayoutVersion, Is.Zero);
            Assert.That(LevelValidator.Validate(result.Asset.Data), Is.Not.Empty);
            Assert.That(writer.IsInCatalog(result.Asset), Is.False);
            Assert.That(writer.ListLevels(), Does.Contain(result.Asset));
            Assert.That(JsonUtility.ToJson(draft), Is.EqualTo(before));
            Assert.That(File.Exists(AssetDatabase.GetAssetPath(result.Asset)), Is.True);
        }

        [Test]
        public void MetadataSavePreservesIdentityVersionAndVerifiedSolution()
        {
            var source = Existing(3);
            var snapshot = source.ToDefinition();
            snapshot.Name = "改名后的关卡"; snapshot.Description = "新的说明";
            var result = writer.Save(source, snapshot, source.VerifiedSolution, LevelSaveIntent.Save);
            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(result.Asset, Is.SameAs(source));
            Assert.That(source.Data.Id, Is.EqualTo("writer-test"));
            Assert.That(source.Data.LayoutVersion, Is.EqualTo(3));
            Assert.That(source.VerifiedSolution, Is.EqualTo("RR"));
        }

        [Test]
        public void LayoutSaveUsesTheCurrentAssetVersionAndInvalidatesTheOldSolution()
        {
            var source = Existing(5);
            var snapshot = source.ToDefinition();
            LevelAuthoring.Paint(snapshot, new GridPos(1, 2), LevelBrush.Wall);
            snapshot.Id = "must-not-change-identity";
            snapshot.LayoutVersion = 0; // An Undo snapshot can legitimately contain an older version.
            var result = writer.Save(source, snapshot, "RR", LevelSaveIntent.Save);
            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(source.Data.Id, Is.EqualTo("writer-test"));
            Assert.That(source.Data.LayoutVersion, Is.EqualTo(6));
            Assert.That(source.VerifiedSolution, Is.Empty);
            Assert.That(source.Data.CellAt(new GridPos(1, 2)), Is.EqualTo(CellType.Wall));
        }

        [Test]
        public void SaveAsCreatesANewVersionZeroAssetAndKeepsTheSourceUntouched()
        {
            var source = Existing(4);
            string before = JsonUtility.ToJson(source);
            var result = writer.Save(source, source.ToDefinition(), source.VerifiedSolution, LevelSaveIntent.SaveAs);
            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(result.Asset.Data.Id, Is.Not.EqualTo(source.Data.Id));
            Assert.That(result.Asset.Data.LayoutVersion, Is.Zero);
            Assert.That(result.Asset.VerifiedSolution, Is.EqualTo("RR"));
            Assert.That(JsonUtility.ToJson(source), Is.EqualTo(before));
        }

        [Test]
        public void CatalogReferencesPersistAfterImportAndInvalidUpdatesCannotReplaceThem()
        {
            var result = writer.Save(null, ValidLevel(), "RR", LevelSaveIntent.SaveAndAddToCatalog);
            Assert.That(result.Success, Is.True, result.Message);
            string assetPath = AssetDatabase.GetAssetPath(result.Asset);
            string id = result.Asset.Data.Id;
            AssetDatabase.ImportAsset(catalogPath, ImportAssetOptions.ForceUpdate);
            var reloadedCatalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>(catalogPath);
            Assert.That(reloadedCatalog.Levels, Has.Count.EqualTo(1));
            Assert.That(AssetDatabase.GetAssetPath(reloadedCatalog.Levels[0]), Is.EqualTo(assetPath));
            Assert.That(reloadedCatalog.Levels[0].Data.Id, Is.EqualTo(id));
            string oldAsset = File.ReadAllText(assetPath);
            var invalid = result.Asset.ToDefinition();
            LevelAuthoring.Paint(invalid, invalid.PlayerStart, LevelBrush.Erase);

            var rejected = writer.Save(result.Asset, invalid, "", LevelSaveIntent.Save);

            Assert.That(rejected.Success, Is.False);
            Assert.That(File.ReadAllText(assetPath), Is.EqualTo(oldAsset));
            Assert.That(result.Asset.Data.HasPlayer, Is.True);
            var draftCopy = writer.Save(result.Asset, invalid, "", LevelSaveIntent.SaveAs);
            Assert.That(draftCopy.Success, Is.True, draftCopy.Message);
            Assert.That(writer.IsInCatalog(draftCopy.Asset), Is.False);
            Assert.That(draftCopy.Asset.Data.HasPlayer, Is.False);
        }

        [Test]
        public void AReadOnlySourceRejectsSavingWithoutChangingMemoryOrDisk()
        {
            var source = Existing(2);
            string path = AssetDatabase.GetAssetPath(source);
            string beforeMemory = JsonUtility.ToJson(source);
            byte[] beforeFile = File.ReadAllBytes(path);
            var snapshot = source.ToDefinition(); snapshot.Name = "不能写入的改名";
            FileAttributes attributes = File.GetAttributes(path);
            try
            {
                File.SetAttributes(path, attributes | FileAttributes.ReadOnly);
                var result = writer.Save(source, snapshot, "RR", LevelSaveIntent.Save);
                Assert.That(result.Success, Is.False);
                Assert.That(result.Message, Does.Contain("只读"));
                Assert.That(JsonUtility.ToJson(source), Is.EqualTo(beforeMemory));
                CollectionAssert.AreEqual(beforeFile, File.ReadAllBytes(path));
            }
            finally { File.SetAttributes(path, attributes); }
        }

        [Test]
        public void AReadOnlyCatalogCannotLeaveAnUnrequestedPartiallySavedAsset()
        {
            FileAttributes attributes = File.GetAttributes(catalogPath);
            try
            {
                File.SetAttributes(catalogPath, attributes | FileAttributes.ReadOnly);
                var result = writer.Save(null, ValidLevel(), "RR", LevelSaveIntent.SaveAndAddToCatalog);
                Assert.That(result.Success, Is.False);
                Assert.That(catalog.Levels, Is.Empty);
                Assert.That(AssetDatabase.FindAssets("t:LevelAsset", new[] { folder }), Is.Empty);
            }
            finally { File.SetAttributes(catalogPath, attributes); }
        }

        [Test]
        public void SaveAsNeverOverwritesAnExistingAssetPath()
        {
            var source = Existing(0);
            string path = AssetDatabase.GetAssetPath(source);
            byte[] before = File.ReadAllBytes(path);
            var result = writer.SaveAtPath(source, source.ToDefinition(), "RR", LevelSaveIntent.SaveAs, path);
            Assert.That(result.Success, Is.False);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
        }

        [Test]
        public void ExternalSavesRefreshACleanWindowButPreserveAndBlockADirtyDraft()
        {
            var source = Existing(0);
            window = ScriptableObject.CreateInstance<LevelEditorWindow>();
            InvokeWindow("LoadLevel", source);
            var external = source.ToDefinition(); external.Name = "外部改名";
            Assert.That(writer.Save(source, external, "RR", LevelSaveIntent.Save).Success, Is.True);
            Assert.That(WindowDraft.Data.Name, Is.EqualTo("外部改名"));
            Assert.That(window.hasUnsavedChanges, Is.False);
            Type brushType = typeof(LevelEditorWindow).GetNestedType("Brush", BindingFlags.NonPublic);
            InvokeWindow("Paint", new GridPos(1, 2), Enum.Parse(brushType, "Wall"));
            string dirtyDraft = JsonUtility.ToJson(WindowDraft);
            external = source.ToDefinition(); external.Name = "第二次外部改名";
            Assert.That(writer.Save(source, external, "RR", LevelSaveIntent.Save).Success, Is.True);
            Assert.That(JsonUtility.ToJson(WindowDraft), Is.EqualTo(dirtyDraft));
            Assert.That(window.hasUnsavedChanges, Is.True);
            Assert.That((bool)InvokeWindow("Save", false), Is.False);
            Assert.That(source.Data.Name, Is.EqualTo("第二次外部改名"));
            Assert.That(source.Data.CellAt(new GridPos(1, 2)), Is.EqualTo(CellType.Floor));
        }

        private LevelEditorDraft WindowDraft => (LevelEditorDraft)typeof(LevelEditorWindow).GetField("draft", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
        private object InvokeWindow(string method, params object[] arguments) => typeof(LevelEditorWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, arguments);
        private LevelAsset Existing(int version)
        {
            var asset = ScriptableObject.CreateInstance<LevelAsset>();
            asset.Data = ValidLevel(); asset.Data.LayoutVersion = version; asset.VerifiedSolution = "RR";
            AssetDatabase.CreateAsset(asset, folder + "/Levels/Existing.asset");
            AssetDatabase.SaveAssetIfDirty(asset);
            return asset;
        }

        private static LevelDefinition ValidLevel() => new LevelDefinition
        {
            Id = "writer-test", Name = "保存测试", Width = 7, Height = 5, Cells = new CellType[35],
            HasPlayer = true, PlayerStart = new GridPos(2, 2), Boxes = new[] { new GridPos(3, 2) }, Goals = new[] { new GridPos(5, 2) }
        };
    }
}
