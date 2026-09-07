using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Sokoban.Content;
using Sokoban.Core;
using Sokoban.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Sokoban.Tests
{
    /// <summary>Exercises real runtime screens and animations using an isolated editor-playtest room.</summary>
    public sealed class RuntimeFlowTests
    {
        private GameObject root;
        private GameController app;
        private string progressPath;
        private byte[] progressBefore;
        private string temporaryProgressDirectory;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Assert.That(Resources.Load<GameResources>("GameResources"), Is.Not.Null,
                "Run Sokoban > Prepare Project before the runtime integration suite.");
            progressPath = Path.Combine(Application.persistentDataPath, "progress.json");
            progressBefore = File.Exists(progressPath) ? File.ReadAllBytes(progressPath) : null;
            PlaytestRequest.Pending = TestRoom();
            app = PresentationFixture.CreateGame(); root = app.gameObject;
            yield return null;
            if (app.CurrentScreen == GameController.ScreenState.Paused) app.Resume();
            Assert.That(app.IsPlaytest, Is.True);
            Assert.That(app.CurrentScreen, Is.EqualTo(GameController.ScreenState.Playing));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (root != null) Object.Destroy(root);
            PlaytestRequest.Pending = null;
            yield return null;
            if (temporaryProgressDirectory != null && Directory.Exists(temporaryProgressDirectory))
            {
                foreach (var name in new[] { "progress.json", "progress.json.bak", "progress.json.tmp" })
                {
                    var path = Path.Combine(temporaryProgressDirectory, name);
                    if (File.Exists(path)) File.Delete(path);
                }
                Directory.Delete(temporaryProgressDirectory);
                temporaryProgressDirectory = null;
            }
            byte[] after = File.Exists(progressPath) ? File.ReadAllBytes(progressPath) : null;
            Assert.That(after, Is.EqualTo(progressBefore), "Runtime playtests and navigation must not touch a player's saved progress.");
        }

        [UnityTest]
        public IEnumerator StartupEntrySelectsAFormalRoomWithoutCreatingAMenu()
        {
            app.ShowMenu();
            Assert.That(app.CurrentScreen, Is.EqualTo(GameController.ScreenState.Playing));
            Assert.That(app.IsPlaytest, Is.False);
            app.ShowLevelSelect();
            Assert.That(app.CurrentScreen, Is.EqualTo(GameController.ScreenState.LevelSelect));
            app.StartLevel(0);
            Assert.That(app.CurrentScreen, Is.EqualTo(GameController.ScreenState.Playing));
            Assert.That(app.CurrentLevelIndex, Is.Zero);
            Assert.That(app.Session.State.Steps, Is.Zero);
            Assert.That(app.IsPlaytest, Is.False);
            app.ShowLevelSelect();
            yield return null;
            Assert.That(app.CurrentScreen, Is.EqualTo(GameController.ScreenState.LevelSelect));
        }

        [UnityTest]
        public IEnumerator StartupUsesCurrentLayoutProgressAndWrapsAfterAllRoomsAreCleared()
        {
            var isolated = UseIsolatedProgress();
            var levels = Resources.Load<GameResources>("GameResources").Catalog.Levels;
            app.ShowMenu();
            Assert.That(app.CurrentLevelIndex, Is.Zero);
            isolated.RecordWin(levels[0].Data.Id, 2, 2, levels[0].Data.LayoutVersion);
            app.ShowMenu();
            Assert.That(app.CurrentLevelIndex, Is.EqualTo(levels.Count > 1 ? 1 : 0));
            foreach (var level in levels) isolated.RecordWin(level.Data.Id, 2, 2, level.Data.LayoutVersion);
            app.ShowMenu();
            Assert.That(app.CurrentLevelIndex, Is.Zero);
            yield return null;
        }

        [UnityTest]
        public IEnumerator InSessionPlaytestReturnsThroughItsCallbackWithoutLeavingPlayMode()
        {
            bool returned = false;
            app.StartPlaytest(TestRoom(), () => { returned = true; app.BeginWorkshopView(); });
            app.Move(Direction.Right);
            yield return WaitForBoard();
            app.OpenWorkshop();
            Assert.That(returned, Is.True);
            Assert.That(app.CurrentScreen, Is.EqualTo(GameController.ScreenState.Workshop));
            Assert.That(root.activeInHierarchy, Is.True);
            Assert.That(Object.FindObjectsOfType<GameController>().Length, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator EmptyCatalogShowsSelectionWithoutCrashingOrInventingARoom()
        {
            var gameResources = Resources.Load<GameResources>("GameResources");
            var originalCatalog = gameResources.Catalog;
            var emptyCatalog = ScriptableObject.CreateInstance<LevelCatalog>();
            try
            {
                gameResources.Catalog = emptyCatalog;
                app.ShowMenu();
                Assert.That(app.CurrentScreen, Is.EqualTo(GameController.ScreenState.LevelSelect));
                Assert.DoesNotThrow(() => app.StartLevel(0));
                Assert.That(app.CurrentScreen, Is.EqualTo(GameController.ScreenState.LevelSelect));
                yield return null;
            }
            finally
            {
                gameResources.Catalog = originalCatalog;
                Object.Destroy(emptyCatalog);
            }
        }

        [UnityTest]
        public IEnumerator AuthoringInputDisplaysMarkupLiterallyAndOwnsKeyboardFocus()
        {
            app.OpenWorkshop();
            // Use the authored dialog, rather than constructing a synthetic input field.
            typeof(RuntimeWorkshop).GetMethod("ShowSettings", BindingFlags.Instance|BindingFlags.NonPublic, null, Type.EmptyTypes, null).Invoke(app.Workshop, null);
            var input = Array.Find(app.GetComponentsInChildren<TMP_InputField>(), field => field.name == "Level name");
            input.text = "<b>中文</b>";
            Assert.That(input.richText, Is.False);
            Assert.That(input.textComponent.richText, Is.False);
            Assert.That(input.text, Is.EqualTo("<b>中文</b>"));
            input.ActivateInputField();
            yield return null;
            Assert.That(UiFactory.IsTextInputFocused(), Is.True);
            input.DeactivateInputField();
        }

        [UnityTest]
        public IEnumerator AuthoredLayoutSurvivesScreenChangesAndBoardUsesWholePixels()
        {
            var label=app.Screens.Play.Get<TMP_Text>("Room title");
            var offset=label.rectTransform.anchoredPosition+new Vector2(23,-9);
            label.rectTransform.anchoredPosition=offset;label.fontSize=31;label.color=Color.cyan;
            app.ShowLevelSelect();app.StartLevel(0);yield return null;
            Assert.That(app.Screens.Play.Get<TMP_Text>("Room title"),Is.SameAs(label));
            Assert.That(label.rectTransform.anchoredPosition,Is.EqualTo(offset));Assert.That(label.fontSize,Is.EqualTo(31));Assert.That(label.color,Is.EqualTo(Color.cyan));
            Assert.That(app.Board.Texture.filterMode,Is.EqualTo(FilterMode.Point));
            Assert.That(app.Board.ScreenRect.width,Is.EqualTo(app.Board.Texture.width*app.Board.IntegerScale).Within(.1));
            Assert.That(app.Board.ScreenRect.height,Is.EqualTo(app.Board.Texture.height*app.Board.IntegerScale).Within(.1));
            Assert.That(app.Board.ScreenRect.xMin,Is.EqualTo(Mathf.Round(app.Board.ScreenRect.xMin)).Within(.01));
            Assert.That(app.Board.ScreenRect.yMin,Is.EqualTo(Mathf.Round(app.Board.ScreenRect.yMin)).Within(.01));
            var p=app.Board.PlayerRenderer.transform.position;
            Assert.That(p.x*8,Is.EqualTo(Mathf.Round(p.x*8)));Assert.That(p.y*8,Is.EqualTo(Mathf.Round(p.y*8)));
            Assert.That(app.Board.TryScreenToCell(app.Board.ScreenRect.center,out var cell,true),Is.True);
        }

        [UnityTest]
        public IEnumerator ToolIconsSelectToolsShowHintsAndBlockBoardInput()
        {
            app.OpenWorkshop();
            var workshop = app.Workshop;
            yield return null;
            Canvas.ForceUpdateCanvases();
            var tools = (WorkshopTool[])Enum.GetValues(typeof(WorkshopTool));
            var icons = Array.ConvertAll(tools, tool => workshop.View.Get<UnityEngine.UI.Button>("Tool " + tool).GetComponent<WorkshopPointerHint>());
            Assert.That(icons.Length, Is.EqualTo(7));
            Assert.That(icons, Is.Unique, "Each drawing tool must bind a different real control.");
            var raycast = typeof(RuntimeWorkshop).GetMethod("IsPointerOverUi", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (var icon in icons)
            {
                var mesh = icon.GetComponent<UiButtonVisual>().Icon.canvasRenderer.GetMesh();
                Assert.That(mesh, Is.Not.Null);
                Assert.That(mesh.vertexCount, Is.GreaterThan(0), icon.Tool + " must produce a rendered icon mesh.");
                var button = icon.GetComponentInParent<UnityEngine.UI.Button>();
                Assert.That(button.GetComponentInChildren<TMP_Text>().text, Is.Empty);
                button.OnPointerClick(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current)
                    { button = UnityEngine.EventSystems.PointerEventData.InputButton.Left });
                Assert.That(workshop.Tool, Is.EqualTo(icon.Tool));
                var hint = button.GetComponent<WorkshopPointerHint>();
                hint.OnPointerEnter(null);
                var status = app.Workshop.View.Get<TMP_Text>("Workshop status");
                Assert.That(status.text, Does.Contain("："));
                hint.OnPointerExit(null);
                var rect = (RectTransform)button.transform;
                Vector2 point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
                Assert.That((bool)raycast.Invoke(workshop, new object[] { point }), Is.True);
                Assert.That(app.Board.ScreenRect.Contains(point), Is.False);
            }
            workshop.SetBrush(LevelBrush.Player);
            var line = Array.Find(icons, icon => icon.Tool == WorkshopTool.Line);
            var disabled = line.GetComponentInParent<UnityEngine.UI.Button>();
            Assert.That(disabled.interactable, Is.False);
            var previous = workshop.Tool;
            disabled.OnPointerClick(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current)
                { button = UnityEngine.EventSystems.PointerEventData.InputButton.Left });
            Assert.That(workshop.Tool, Is.EqualTo(previous), "Disabled batch tools must not accept clicks.");
            Assert.That(line.GetComponent<UiButtonVisual>().Icon.color.a, Is.LessThan(1));
        }

        [UnityTest]
        public IEnumerator WorkshopBlocksUiRaycastsAndShowsRedForPasteOntoPlayer()
        {
            app.OpenWorkshop();
            var workshop = app.Workshop;
            workshop.SetDocument(TestRoom());
            yield return null;
            Canvas.ForceUpdateCanvases();
            var elementList = workshop.View.Get<UiList>("Element list");
            var wallView = workshop.FindElementView("wall");
            Assert.That(elementList.Items, Does.Contain(wallView));
            var wallButton = wallView.Get<RectTransform>("Element button");
            Assert.That(wallButton, Is.Not.Null);
            wallView.Get<UnityEngine.UI.Button>("Element button").OnPointerClick(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current)
                { button = UnityEngine.EventSystems.PointerEventData.InputButton.Left });
            Assert.That(workshop.SelectedTypeId, Is.EqualTo("wall"));
            Vector2 buttonPoint = RectTransformUtility.WorldToScreenPoint(null, wallButton.TransformPoint(wallButton.rect.center));
            var raycast = typeof(RuntimeWorkshop).GetMethod("IsPointerOverUi", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That((bool)raycast.Invoke(workshop, new object[] { buttonPoint }), Is.True);
            Assert.That((bool)raycast.Invoke(workshop, new object[] { app.Board.ScreenRect.center }), Is.False);

            var before = workshop.Draft;
            workshop.SetTool(WorkshopTool.Select);
            workshop.BeginGesture(new GridPos(1, 2), true);
            workshop.EndGesture(new GridPos(2, 2));
            Assert.That(workshop.CopySelection(), Is.True);
            Assert.That(workshop.BeginPaste(), Is.True);
            workshop.UpdateGesture(new GridPos(1, 2));
            bool redPreview = false;
            foreach (var sprite in app.Board.GetComponentsInChildren<SpriteRenderer>())
                if (sprite.name == "Editor highlight" && sprite.color.r > sprite.color.g * 2) redPreview = true;
            Assert.That(redPreview, Is.True, "An illegal target must have a visible red preview.");
            Assert.That(workshop.CommitPaste(new GridPos(1, 2)), Is.False);
            Assert.That(LevelAuthoring.LayoutEquals(before, workshop.Draft), Is.True);
            Assert.That(workshop.Draft.PlayerStart, Is.EqualTo(new GridPos(1, 2)));
            Assert.That(workshop.CanUndo, Is.False);
        }

        [UnityTest]
        public IEnumerator WorkshopReturnsToExistingGameAndRestartsAfterLayoutChanges()
        {
            UseIsolatedProgress();
            app.StartLevel(0);
            app.Move(Direction.Right);
            yield return WaitForBoard();
            var originalSession = app.Session;
            int previousSteps = originalSession.State.Steps;
            app.OpenWorkshop();
            Assert.That(app.CurrentScreen, Is.EqualTo(GameController.ScreenState.Workshop));
            app.ReturnFromWorkshop();
            Assert.That(app.Session, Is.SameAs(originalSession));
            Assert.That(app.Session.State.Steps, Is.EqualTo(previousSteps));
            Assert.That(app.Session.CanUndo, Is.True);

            var source = Resources.Load<GameResources>("GameResources").Catalog.Levels[0];
            int previousVersion = source.Data.LayoutVersion;
            try
            {
                app.OpenWorkshop();
                source.Data.LayoutVersion = previousVersion + 1;
                app.ReturnFromWorkshop();
                Assert.That(app.Session, Is.Not.SameAs(originalSession));
                Assert.That(app.Session.State.Steps, Is.Zero);
                Assert.That(app.Session.Definition.LayoutVersion, Is.EqualTo(previousVersion + 1));
            }
            finally { source.Data.LayoutVersion = previousVersion; }
        }

        [UnityTest]
        public IEnumerator CommittedMoveAnimatesOnceThenUndoAndRestartRestoreTheBoard()
        {
            var definition = app.Session.Definition;
            app.Move(Direction.Right);
            Assert.That(app.Board.IsAnimating, Is.True);
            Assert.That(app.Session.State.Steps, Is.EqualTo(1));
            Assert.That(app.Session.State.Boxes[0], Is.EqualTo(new GridPos(3, 2)));
            app.Move(Direction.Right);
            app.Undo();
            Assert.That(app.Session.State.Steps, Is.EqualTo(1), "Movement and undo while animating must not enqueue another operation; explicit restart is tested separately.");
            yield return WaitForBoard();

            app.Undo();
            Assert.That(app.Session.State.Steps, Is.Zero);
            Assert.That(app.Session.State.Pushes, Is.Zero);
            Assert.That(app.Session.State.Player, Is.EqualTo(new GridPos(1, 2)));
            Assert.That(app.Session.State.Boxes[0], Is.EqualTo(new GridPos(2, 2)));
            Assert.That(app.Board.PlayerRenderer.transform.position,
                Is.EqualTo(new Vector3(1.5f, 2.5f, 0)));

            app.Move(Direction.Right);
            yield return WaitForBoard();
            app.Restart();
            Assert.That(app.Session.State.Steps, Is.Zero);
            Assert.That(app.Session.CanUndo, Is.False);
            Assert.That(app.Session.Definition.Boxes, Is.EqualTo(definition.Boxes));
            Assert.That(app.Session.Definition.PlayerStart, Is.EqualTo(definition.PlayerStart));
        }

        [UnityTest]
        public IEnumerator BlockedMoveGivesFeedbackWithoutChangingLogicalState()
        {
            app.Move(Direction.Left);
            Assert.That(app.Board.IsAnimating, Is.True);
            Assert.That(app.Session.State.Steps, Is.Zero);
            Assert.That(app.Session.CanUndo, Is.False);
            yield return WaitForBoard();
            Assert.That(app.Board.PlayerRenderer.transform.position,
                Is.EqualTo(new Vector3(1.5f, 2.5f, 0)));
        }

        [UnityTest]
        public IEnumerator PauseDuringWinningAnimationDefersResultsAndPlaytestNeverSaves()
        {
            app.Move(Direction.Right);
            yield return WaitForBoard();
            app.Pause();
            app.Move(Direction.Right);
            Assert.That(app.Session.State.Steps, Is.EqualTo(1));
            app.Resume();
            app.Move(Direction.Right);
            app.Pause();
            Assert.That(app.CurrentScreen, Is.EqualTo(GameController.ScreenState.Paused));
            var pausedPosition = app.Board.PlayerRenderer.transform.position;
            yield return new WaitForSecondsRealtime(.25f);
            Assert.That(app.Board.PlayerRenderer.transform.position, Is.EqualTo(pausedPosition));
            Assert.That(app.Board.IsAnimating, Is.True);
            Assert.That(app.CurrentScreen, Is.EqualTo(GameController.ScreenState.Paused));
            Assert.That(app.Session.IsWon, Is.True);
            app.Resume();
            yield return WaitForBoard();
            Assert.That(app.CurrentScreen, Is.EqualTo(GameController.ScreenState.Complete));
            Assert.That(app.IsPlaytest, Is.True);
            Assert.That(app.Session.State.Steps, Is.EqualTo(2));
            app.Move(Direction.Left);
            app.Undo();
            Assert.That(app.Session.State.Steps, Is.EqualTo(2));
            app.Restart();
            Assert.That(app.CurrentScreen, Is.EqualTo(GameController.ScreenState.Playing));
            Assert.That(app.Session.IsWon, Is.False);
            Assert.That(app.Session.State.Steps, Is.Zero);
        }

        [UnityTest]
        public IEnumerator LeavingDuringWinningAnimationKeepsTheEarnedFormalScore()
        {
            var isolatedProgress = UseIsolatedProgress();
            var gameResources = Resources.Load<GameResources>("GameResources");
            var originalCatalog = gameResources.Catalog;
            var catalog = ScriptableObject.CreateInstance<LevelCatalog>();
            var room = ScriptableObject.CreateInstance<LevelAsset>();
            room.Data = TestRoom();
            room.Data.LayoutVersion = 7;
            catalog.Levels.Add(room);
            try
            {
                gameResources.Catalog = catalog;
                app.StartLevel(0);
                string levelId = room.Data.Id;
                int layoutVersion = room.Data.LayoutVersion;
                app.Move(Direction.Right);
                yield return WaitForBoard();
                app.Move(Direction.Right);
                Assert.That(app.Session.IsWon, Is.True);
                Assert.That(app.Board.IsAnimating, Is.True);
                Assert.That(app.CurrentScreen, Is.EqualTo(GameController.ScreenState.Playing));
                Assert.That(isolatedProgress.Get(levelId, layoutVersion)?.Completed, Is.True,
                    "Completion must be persisted at the winning rule transaction, before its animation ends.");
                int earnedSteps = app.Session.State.Steps;
                app.ShowLevelSelect();
                yield return null;
                Assert.That(app.CurrentScreen, Is.EqualTo(GameController.ScreenState.LevelSelect));
                Assert.That(app.Board.IsAnimating, Is.False);
                var reloaded = new ProgressStore(temporaryProgressDirectory);
                Assert.That(reloaded.Get(levelId, layoutVersion)?.Completed, Is.True);
                Assert.That(reloaded.Get(levelId, layoutVersion)?.BestSteps, Is.EqualTo(earnedSteps));
                Assert.That(reloaded.Get(levelId, 0), Is.Null, "Scores must identify the played layout version.");
            }
            finally { gameResources.Catalog = originalCatalog; Object.Destroy(room); Object.Destroy(catalog); }
        }

        private ProgressStore UseIsolatedProgress()
        {
            temporaryProgressDirectory = Path.Combine(Path.GetTempPath(), "SokobanRuntimeTests-" + Guid.NewGuid().ToString("N"));
            var isolated = new ProgressStore(temporaryProgressDirectory);
            typeof(GameController).GetField("progress", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(app, isolated);
            return isolated;
        }

        private IEnumerator WaitForBoard()
        {
            float timeout = Time.realtimeSinceStartup + 3;
            while (app.Board.IsAnimating && Time.realtimeSinceStartup < timeout) yield return null;
            Assert.That(app.Board.IsAnimating, Is.False, "Board animation did not finish.");
        }

        private static LevelDefinition TestRoom()
        {
            const int width = 7, height = 5;
            var level = new LevelDefinition
            {
                Id = "runtime-test-only", Name = "A test room", Description = "Push twice to finish.",
                Width = width, Height = height, Cells = new CellType[width * height],
                Goals = new[] { new GridPos(4, 2) }, HasPlayer = true, PlayerStart = new GridPos(1, 2),
                Boxes = new[] { new GridPos(2, 2) }
            };
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                level.Cells[y * width + x] = x == 0 || y == 0 || x == width - 1 || y == height - 1 ? CellType.Wall : CellType.Floor;
            return level;
        }
    }
}
