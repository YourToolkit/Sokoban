using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Sokoban.Content;
using Sokoban.EditorTools;
using Sokoban.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Sokoban.Tests
{
    public sealed class PresentationTests
    {
        [Test]
        public void GamePrefabHasAuthoredReferencesAndNoEditorAssetDependencies()
        {
            var game=AssetDatabase.LoadAssetAtPath<GameController>(PresentationSetup.RootPath);
            Assert.That(game,Is.Not.Null); Assert.That(game.Board,Is.Not.Null); Assert.That(game.Canvas,Is.Not.Null);
            Assert.That(game.PlayerController, Is.Not.Null);
            Assert.That(game.PlayerController.Game, Is.SameAs(game));
            Assert.That(game.PlayerController.Settings, Is.SameAs(game.ResourcesConfig.Config));
            Assert.DoesNotThrow(()=>game.Screens.Validate());
            Assert.That(game.GetComponentsInChildren<Camera>(true).Length,Is.EqualTo(2));
            Assert.That(game.GetComponentsInChildren<AudioListener>(true).Length,Is.EqualTo(1));
            Assert.That(game.Screens.Play.Get<BoardViewport>("Board viewport"),Is.Not.Null);
            var dependencies=AssetDatabase.GetDependencies(new[]{ProjectSetup.GameScene,PresentationSetup.RootPath},true);
            Assert.That(dependencies.Where(path=>path.StartsWith("Assets/")&&path.Contains("/Editor/")),Is.Empty);
            foreach(var prefab in dependencies.Where(path=>path.EndsWith(".prefab")))
                foreach(var transform in AssetDatabase.LoadAssetAtPath<GameObject>(prefab).GetComponentsInChildren<Transform>(true))
                    Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject),Is.Zero,prefab);
        }
        [Test]
        public void BoardArtUsesThirtyTwoSourcePixelsAndStandardPixelImportSettings()
        {
            var config=AssetDatabase.LoadAssetAtPath<GameResources>(ProjectSetup.ResourcesPath).Visuals;
            foreach(var sprite in new[]{config.Floor,config.Wall,config.Goal,config.Player,config.Box,config.BoxOnGoal})
            {
                Assert.That(sprite.texture.width,Is.EqualTo(32));Assert.That(sprite.texture.height,Is.EqualTo(32));Assert.That(sprite.pixelsPerUnit,Is.EqualTo(32));
                var importer=(TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(sprite));
                Assert.That(importer.filterMode,Is.EqualTo(FilterMode.Point));Assert.That(importer.mipmapEnabled,Is.False);Assert.That(importer.textureCompression,Is.EqualTo(TextureImporterCompression.Uncompressed));
                var settings=new TextureImporterSettings();importer.ReadTextureSettings(settings);
                Assert.That(settings.spriteMeshType,Is.EqualTo(SpriteMeshType.FullRect));
                Assert.That(settings.spriteGenerateFallbackPhysicsShape,Is.False);
            }
        }
        [Test]
        public void BoardRenderingUsesNamedBoardLayer()
        {
            int boardLayer=LayerMask.NameToLayer("Board");
            Assert.That(boardLayer,Is.EqualTo(8));
            var board=AssetDatabase.LoadAssetAtPath<BoardView>(PresentationSetup.BoardPath);
            Assert.That(board.RenderCamera.cullingMask,Is.EqualTo(1<<boardLayer));
            foreach(var renderer in board.GetComponentsInChildren<Renderer>(true)) Assert.That(renderer.gameObject.layer,Is.EqualTo(boardLayer));
        }
        [Test]
        public void WorkshopPrefabRetainsAllSevenHintComponentsAndSpriteReferences()
        {
            var catalog=AssetDatabase.LoadAssetAtPath<WorkshopViewCatalog>(WorkshopViewRegistration.Path);
            Assert.That(catalog,Is.Not.Null);
            var hints=catalog.Main.GetComponentsInChildren<WorkshopPointerHint>(true);
            Assert.That(hints.Length,Is.EqualTo(7));
            foreach(var hint in hints)
            {
                Assert.That(hint.Description,Is.Not.Empty);
                Assert.That(hint.GetComponent<UiButtonVisual>().Icon.sprite,Is.Not.Null);
            }
        }
        [Test]
        public void PreviewLeavesLevelAndCleanSceneUnchangedAndCannotBeSaved()
        {
            for(int i=0;i<SceneManager.sceneCount;i++)
                if(SceneManager.GetSceneAt(i).isDirty) Assert.Ignore("Save your open scenes before running the scene persistence test.");
            var setup=EditorSceneManager.GetSceneManagerSetup();
            string folder="Assets/__PreviewCheck_"+Guid.NewGuid().ToString("N");AssetDatabase.CreateFolder("Assets",Path.GetFileName(folder));
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            try
            {
                var root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PresentationSetup.RootPath),scene);
                var app=root.GetComponent<GameController>();var level=app.ResourcesConfig.Catalog.Levels[0];string before=EditorJsonUtility.ToJson(level);
                EditorSceneManager.SaveScene(scene,folder+"/Preview.unity"); Assert.That(scene.isDirty,Is.False);
                BoardScenePreview.Show(app.Board,level);
                Assert.That(BoardScenePreview.Current,Is.Not.Null);Assert.That(BoardScenePreview.Current.PlayerRenderer.gameObject.activeSelf,Is.True);
                Assert.That(app.Session,Is.Null);Assert.That(scene.isDirty,Is.False);Assert.That(EditorJsonUtility.ToJson(level),Is.EqualTo(before));
                EditorSceneManager.SaveScene(scene);
                Assert.That(BoardScenePreview.Current,Is.Null);
                Assert.That(File.ReadAllText(folder+"/Preview.unity"),Does.Not.Contain("临时关卡预览"));
                Assert.That(root.GetComponentsInChildren<Camera>(true).Length,Is.EqualTo(2));
                scene=EditorSceneManager.OpenScene(folder+"/Preview.unity",OpenSceneMode.Single);
                Assert.That(scene.GetRootGameObjects().Length,Is.EqualTo(1));
                var reopened=scene.GetRootGameObjects()[0].GetComponent<GameController>();
                Assert.That(reopened.Board,Is.Not.Null);Assert.DoesNotThrow(()=>reopened.Screens.Validate());
                BoardScenePreview.Show(reopened.Board,level);
                Assert.That(BoardScenePreview.Current,Is.Not.Null);
                Assert.That(scene.isDirty,Is.False);
            }
            finally
            {
                BoardScenePreview.Clear();
                if(setup.Length>0 && setup.Any(item=>item.isActive) && setup.All(item=>!string.IsNullOrEmpty(item.path))) EditorSceneManager.RestoreSceneManagerSetup(setup);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                AssetDatabase.DeleteAsset(folder);
            }
        }
        [Test]
        public void PresentationReloadKeepsUnsavedSceneChangesInASeparateRecoveryAsset()
        {
            for (int i=0;i<SceneManager.sceneCount;i++)
                if (SceneManager.GetSceneAt(i).isDirty) Assert.Ignore("Save open scenes before running scene persistence tests.");
            var setup=EditorSceneManager.GetSceneManagerSetup();
            const string folder="Assets/Scenes/Recovery";
            bool folderExisted=AssetDatabase.IsValidFolder(folder);
            var existing=Directory.Exists(folder) ? Directory.GetFiles(folder,"*.unity") : Array.Empty<string>();
            try
            {
                var scene=EditorSceneManager.OpenScene(ProjectSetup.GameScene,OpenSceneMode.Single);
                var note=new GameObject("Unsaved designer note");
                SceneManager.MoveGameObjectToScene(note,scene);EditorSceneManager.MarkSceneDirty(scene);
                typeof(ProjectSetup).Assembly.GetType("Sokoban.EditorTools.LocalValidationBridge")
                    .GetMethod("ReloadPresentationWithBackup",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
                var copies=Directory.GetFiles(folder,"*.unity").Except(existing).ToArray();
                Assert.That(copies.Length,Is.EqualTo(1));
                Assert.That(File.ReadAllText(copies[0]),Does.Contain("Unsaved designer note"));
                var reloaded=SceneManager.GetSceneByPath(ProjectSetup.GameScene);
                Assert.That(reloaded.isDirty,Is.False);
                Assert.That(reloaded.GetRootGameObjects().Any(root=>root.name=="Unsaved designer note"),Is.False);
                Assert.That(reloaded.GetRootGameObjects().Single().GetComponent<GameController>().Board,Is.Not.Null);
            }
            finally
            {
                if(setup.Length>0 && setup.Any(item=>item.isActive) && setup.All(item=>!string.IsNullOrEmpty(item.path))) EditorSceneManager.RestoreSceneManagerSetup(setup);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                if(Directory.Exists(folder))
                    foreach(var copy in Directory.GetFiles(folder,"*.unity").Except(existing)) AssetDatabase.DeleteAsset(copy.Replace('\\','/'));
                if(!folderExisted && AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
            }
        }
    }
}
