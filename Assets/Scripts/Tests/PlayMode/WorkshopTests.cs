using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sokoban.Content;
using Sokoban.Core;
using Sokoban.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Sokoban.Tests
{
    public sealed class WorkshopTests
    {
        private GameObject root;
        private RuntimeWorkshop workshop;
        private ILevelAssetWriter previousWriter;
        private readonly List<Object> cleanup = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            previousWriter = LevelAuthoringServices.AssetWriter;
            root = new GameObject("Workshop transaction tests");
            workshop = root.AddComponent<RuntimeWorkshop>();
            workshop.SetDocument(Room());
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            LevelAuthoringServices.AssetWriter = previousWriter;
            PlaytestRequest.Pending = null;
            Object.Destroy(root);
            foreach (var item in cleanup) if (item != null) Object.Destroy(item);
            cleanup.Clear();
            yield return null;
        }

        [Test]
        public void ContinuousStrokeIsOneUndoAndDoesNotTouchItsSource()
        {
            var original = Room();
            workshop.SetDocument(original);
            workshop.SetBrush(LevelBrush.Wall);
            workshop.BeginGesture(new GridPos(2, 4));
            workshop.UpdateGesture(new GridPos(3, 4));
            workshop.EndGesture(new GridPos(5, 4));
            for (int x = 2; x <= 5; x++) Assert.That(workshop.Draft.CellAt(new GridPos(x, 4)), Is.EqualTo(CellType.Wall));
            Assert.That(original.CellAt(new GridPos(2, 4)), Is.EqualTo(CellType.Floor));
            Assert.That(workshop.IsDirty, Is.True);
            workshop.UndoEdit();
            Assert.That(LevelAuthoring.LayoutEquals(original, workshop.Draft), Is.True);
            Assert.That(workshop.CanUndo, Is.False);
            Assert.That(workshop.IsDirty, Is.False);
            workshop.RedoEdit();
            Assert.That(workshop.Draft.CellAt(new GridPos(4, 4)), Is.EqualTo(CellType.Wall));
        }

        [TestCase(WorkshopTool.Line)]
        [TestCase(WorkshopTool.HollowRectangle)]
        [TestCase(WorkshopTool.FilledRectangle)]
        public void ShapePreviewCommitsOnlyOnReleaseAndEscRestoresDraft(WorkshopTool tool)
        {
            var before = workshop.Draft;
            workshop.SetTool(tool);
            workshop.SetBrush(LevelBrush.Wall);
            workshop.BeginGesture(new GridPos(2, 3));
            workshop.UpdateGesture(new GridPos(4, 5));
            Assert.That(LevelAuthoring.LayoutEquals(before, workshop.Draft), Is.True);
            Assert.That(workshop.CanUndo, Is.False);
            workshop.CancelGesture();
            Assert.That(LevelAuthoring.LayoutEquals(before, workshop.Draft), Is.True);
            workshop.BeginGesture(new GridPos(2, 3));
            workshop.EndGesture(new GridPos(4, 5));
            Assert.That(workshop.Draft.CellAt(new GridPos(4, 5)), Is.EqualTo(CellType.Wall));
            workshop.UndoEdit();
            Assert.That(LevelAuthoring.LayoutEquals(before, workshop.Draft), Is.True);
            Assert.That(workshop.CanUndo, Is.False);
        }

        [Test]
        public void CancelingAnActiveStrokeLeavesNoHistory()
        {
            var before = workshop.Draft;
            workshop.BeginGesture(new GridPos(2, 4));
            workshop.UpdateGesture(new GridPos(5, 4));
            workshop.CancelGesture();
            Assert.That(LevelAuthoring.LayoutEquals(before, workshop.Draft), Is.True);
            Assert.That(workshop.IsDirty, Is.False);
            Assert.That(workshop.CanUndo, Is.False);
        }

        [Test]
        public void StrokeLeavingMapIsCanceledAndDoesNotBridgeOnReentry()
        {
            var before = workshop.Draft;
            workshop.BeginGesture(new GridPos(2, 4));
            workshop.UpdateGesture(new GridPos(2, -1));
            workshop.UpdateGesture(new GridPos(6, 4));
            workshop.EndGesture(new GridPos(6, 4));
            Assert.That(LevelAuthoring.LayoutEquals(before, workshop.Draft), Is.True);
            Assert.That(workshop.CanUndo, Is.False);
            Assert.That(workshop.HasPendingGesture, Is.False);
        }

        [Test]
        public void CopyUsesDetachedSnapshotAndOutOfBoundsPasteIsAtomic()
        {
            var level = Room();
            LevelAuthoring.Paint(level, new GridPos(2, 4), LevelBrush.Wall);
            workshop.SetDocument(level);
            Select(new GridPos(2, 4), new GridPos(3, 4));
            Assert.That(workshop.CopySelection(), Is.True);
            workshop.SetBrush(LevelBrush.Goal);
            workshop.SetTool(WorkshopTool.Brush);
            workshop.BeginGesture(new GridPos(2, 4));
            workshop.EndGesture(new GridPos(2, 4));
            Assert.That(workshop.BeginPaste(), Is.True);
            var before = workshop.Draft;
            Assert.That(workshop.CommitPaste(new GridPos(7, 4)), Is.False);
            Assert.That(LevelAuthoring.LayoutEquals(before, workshop.Draft), Is.True);
            Assert.That(workshop.CommitPaste(new GridPos(4, 4)), Is.True);
            Assert.That(workshop.Draft.CellAt(new GridPos(4, 4)), Is.EqualTo(CellType.Wall));
            Assert.That(workshop.Draft.IsGoal(new GridPos(4, 4)), Is.False);
            workshop.UndoEdit();
            Assert.That(LevelAuthoring.LayoutEquals(before, workshop.Draft), Is.True);
        }

        [Test]
        public void ActorDragPreservesBothGoalTilesAndUndo()
        {
            var level = Room();
            level.Goals = new[] { new GridPos(3, 2), new GridPos(4, 2) };
            workshop.SetDocument(level);
            workshop.SetTool(WorkshopTool.Select);
            workshop.BeginGesture(new GridPos(3, 2));
            workshop.UpdateGesture(new GridPos(4, 2));
            Assert.That(workshop.Draft.Boxes[0], Is.EqualTo(new GridPos(3, 2)), "Preview must stay detached.");
            workshop.EndGesture(new GridPos(4, 2));
            Assert.That(workshop.Draft.Boxes[0], Is.EqualTo(new GridPos(4, 2)));
            Assert.That(workshop.Draft.IsGoal(new GridPos(3, 2)), Is.True);
            Assert.That(workshop.Draft.IsGoal(new GridPos(4, 2)), Is.True);
            workshop.UndoEdit();
            Assert.That(LevelAuthoring.LayoutEquals(level, workshop.Draft), Is.True);
        }

        [Test]
        public void CopyCannotOverwriteTheOnlyPlayerAndMoveCanCarryIt()
        {
            Select(new GridPos(2, 2), new GridPos(3, 2));
            workshop.CopySelection(); workshop.BeginPaste();
            var before = workshop.Draft;
            Assert.That(workshop.CommitPaste(new GridPos(1, 2)), Is.False);
            Assert.That(LevelAuthoring.LayoutEquals(before, workshop.Draft), Is.True);
            workshop.CancelGesture();
            workshop.SetTool(WorkshopTool.Select);
            workshop.BeginGesture(new GridPos(2, 2));
            workshop.EndGesture(new GridPos(3, 3));
            Assert.That(workshop.Draft.PlayerStart, Is.EqualTo(new GridPos(3, 3)));
            Assert.That(workshop.Draft.Boxes, Does.Contain(new GridPos(4, 3)));
            workshop.UndoEdit();
            Assert.That(LevelAuthoring.LayoutEquals(before, workshop.Draft), Is.True);
        }

        [Test]
        public void ShiftSelectionDragsAsARegionEvenWhenBrushToolWasActive()
        {
            var before = workshop.Draft;
            workshop.SetTool(WorkshopTool.Brush);
            Select(new GridPos(2, 2), new GridPos(3, 2));
            workshop.BeginGesture(new GridPos(2, 2));
            workshop.EndGesture(new GridPos(2, 3));
            Assert.That(workshop.Draft.PlayerStart, Is.EqualTo(new GridPos(2, 3)));
            Assert.That(workshop.Draft.Boxes, Does.Contain(new GridPos(3, 3)));
            workshop.UndoEdit();
            Assert.That(LevelAuthoring.LayoutEquals(before, workshop.Draft), Is.True);
        }

        [Test]
        public void FailedSavePreservesDraftHistoryAndSuccessfulSaveAdoptsWriterIdentity()
        {
            var asset = ScriptableObject.CreateInstance<LevelAsset>(); cleanup.Add(asset);
            asset.Data = Room();
            var writer = new MemoryWriter(asset);
            LevelAuthoringServices.AssetWriter = writer;
            workshop.SetDocument(asset.ToDefinition(), asset);
            workshop.BeginGesture(new GridPos(2, 4)); workshop.EndGesture(new GridPos(2, 4));
            var edited = workshop.Draft;
            Assert.That(workshop.Save(), Is.False);
            Assert.That(workshop.IsDirty, Is.True);
            Assert.That(workshop.CanUndo, Is.True);
            Assert.That(LevelAuthoring.LayoutEquals(edited, workshop.Draft), Is.True);
            Assert.That(asset.Data.CellAt(new GridPos(2, 4)), Is.EqualTo(CellType.Floor));
            writer.Succeed = true;
            Assert.That(workshop.Save(), Is.True);
            Assert.That(workshop.IsDirty, Is.False);
            Assert.That(workshop.Draft.LayoutVersion, Is.EqualTo(1));
            workshop.UndoEdit();
            Assert.That(workshop.Draft.LayoutVersion, Is.EqualTo(1), "Undo must not roll back the saved identity/version.");
            Assert.That(workshop.IsDirty, Is.True);
            Assert.That(asset.Data.CellAt(new GridPos(2, 4)), Is.EqualTo(CellType.Wall));
        }

        [UnityTest]
        public IEnumerator BoardRendersIncompleteDraftAndReusesActorsAndView()
        {
            PlaytestRequest.Pending = Room();
            var app = PresentationFixture.CreateGame(); cleanup.Add(app.gameObject);
            yield return null;
            if (app.CurrentScreen == GameController.ScreenState.Paused) app.Resume();
            app.OpenWorkshop();
            var board = app.Board;
            board.ShowDraft(LevelAuthoring.CreateBlank(), true);
            Assert.That(board.PlayerRenderer.transform.gameObject.activeSelf, Is.False);
            board.ShowDraft(Room());
            var player = board.PlayerRenderer.transform;
            var box = board.BoxRenderers[0].transform;
            board.ZoomAt(board.ScreenRect.center, 2);
            board.Pan(new Vector2(20, 10));
            var view = board.CaptureView();
            for (int i = 0; i < 20; i++) board.ShowDraft(Room());
            Assert.That(board.PlayerRenderer.transform, Is.SameAs(player));
            Assert.That(board.BoxRenderers[0].transform, Is.SameAs(box));
            Assert.That(board.CaptureView().Center, Is.EqualTo(view.Center));
            Assert.That(board.CaptureView().Zoom, Is.EqualTo(view.Zoom));
            board.Show(Room(), new GameSession(Room()).State);
            board.ShowDraft(Room()); board.RestoreView(view);
            Assert.That(board.CaptureView().Zoom, Is.EqualTo(view.Zoom));
            yield return null;
        }

        [UnityTest]
        public IEnumerator PlaytestReturnsToSameDraftToolsHistoryAndCamera()
        {
            PlaytestRequest.Pending = Room();
            var app = PresentationFixture.CreateGame(); cleanup.Add(app.gameObject);
            yield return null;
            if (app.CurrentScreen == GameController.ScreenState.Paused) app.Resume();
            app.OpenWorkshop();
            var editor = app.Workshop;
            editor.SetBrush(LevelBrush.Goal);
            editor.BeginGesture(new GridPos(4, 4)); editor.EndGesture(new GridPos(4, 4));
            editor.UndoEdit();
            Assert.That(editor.CanRedo, Is.True);
            editor.SetTool(WorkshopTool.Select);
            app.Board.ZoomAt(app.Board.ScreenRect.center, 2);
            app.Board.Pan(new Vector2(50, 25));
            var view = app.Board.CaptureView();
            var before = editor.Draft;
            Assert.That(editor.StartPlaytest(), Is.True);
            Assert.That(app.IsPlaytest, Is.True);
            Assert.That(app.Session.Definition.Id, Is.EqualTo(before.Id));
            app.Move(Direction.Up);
            app.OpenWorkshop(); // The playtest's editor action invokes its return callback.
            yield return null;
            Assert.That(app.CurrentScreen, Is.EqualTo(GameController.ScreenState.Workshop));
            Assert.That(LevelAuthoring.LayoutEquals(before, editor.Draft), Is.True);
            Assert.That(editor.Tool, Is.EqualTo(WorkshopTool.Select));
            Assert.That(editor.CanRedo, Is.True);
            Assert.That(app.Board.CaptureView().Center, Is.EqualTo(view.Center));
            Assert.That(app.Board.CaptureView().Zoom, Is.EqualTo(view.Zoom));
            Assert.That(app.GetComponentsInChildren<Button>().Any(button => button.name == "Return to game"), Is.True);
        }

        [UnityTest]
        public IEnumerator InvalidPlaytestIsBlockedAndDirtyExitCanCancelOrDiscard()
        {
            PlaytestRequest.Pending = Room();
            var app = PresentationFixture.CreateGame(); cleanup.Add(app.gameObject);
            yield return null;
            if (app.CurrentScreen == GameController.ScreenState.Paused) app.Resume();
            var originalSession = app.Session;
            app.OpenWorkshop();
            var editor = app.Workshop;
            editor.SetBrush(LevelBrush.Wall);
            editor.BeginGesture(new GridPos(5, 2)); editor.EndGesture(new GridPos(5, 2));
            Assert.That(editor.StartPlaytest(), Is.False, "Removing the only goal must fail structural validation.");
            Assert.That(app.CurrentScreen, Is.EqualTo(GameController.ScreenState.Workshop));
            Click(app, "Close validation");
            editor.RequestReturn();
            Click(app, "Cancel leaving");
            Assert.That(app.CurrentScreen, Is.EqualTo(GameController.ScreenState.Workshop));
            Assert.That(editor.IsDirty, Is.True);
            editor.RequestReturn();
            Click(app, "Discard changes");
            Assert.That(app.CurrentScreen, Is.EqualTo(GameController.ScreenState.Playing));
            Assert.That(app.Session, Is.SameAs(originalSession));
            Assert.That(app.Session.Definition.IsGoal(new GridPos(5, 2)), Is.True);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ShrinkRequiresConfirmationAndCancelPreservesPendingSettings()
        {
            PlaytestRequest.Pending = Room();
            var app = PresentationFixture.CreateGame(); cleanup.Add(app.gameObject);
            yield return null;
            if (app.CurrentScreen == GameController.ScreenState.Paused) app.Resume();
            app.OpenWorkshop();
            var editor = app.Workshop;
            editor.InputEnabled = false;
            var before = editor.Draft;
            Click(app, "Workshop settings");
            Input(app, "Level name").text = "缩小后的草稿";
            Input(app, "Map width").text = "6";
            Input(app, "Map height").text = "6";
            Click(app, "Apply settings");
            Assert.That(editor.Draft.Width, Is.EqualTo(8));
            Assert.That(editor.CanUndo, Is.False);
            Click(app, "Cancel resize");
            Assert.That(Input(app, "Level name").text, Is.EqualTo("缩小后的草稿"));
            Assert.That(Input(app, "Map width").text, Is.EqualTo("6"));
            Assert.That(LevelAuthoring.LayoutEquals(before, editor.Draft), Is.True);
            Click(app, "Apply settings"); Click(app, "Confirm resize");
            Assert.That(editor.Draft.Width, Is.EqualTo(6));
            Assert.That(editor.Draft.Name, Is.EqualTo("缩小后的草稿"));
            editor.UndoEdit();
            Assert.That(LevelAuthoring.LayoutEquals(before, editor.Draft), Is.True);
            Assert.That(editor.Draft.Name, Is.EqualTo(before.Name));
            Assert.That(editor.CanUndo, Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ClickingValidationPositionFocusesTheOffendingCell()
        {
            PlaytestRequest.Pending = Room();
            var app = PresentationFixture.CreateGame(); cleanup.Add(app.gameObject);
            yield return null;
            if (app.CurrentScreen == GameController.ScreenState.Paused) app.Resume();
            app.OpenWorkshop();
            var invalid = Room();
            invalid.Cells[2 * invalid.Width + 2] = CellType.Wall;
            app.Workshop.SetDocument(invalid); app.Workshop.Open();
            app.Workshop.InputEnabled = false;
            Assert.That(app.Workshop.StartPlaytest(), Is.False);
            app.GetComponentsInChildren<UiList>().Single(list => list.Items.Count > 0).Items[0]
                .Get<Button>("Validation issue").onClick.Invoke();
            Assert.That(app.Board.CaptureView().Center, Is.EqualTo(new Vector2(2.5f, 2.5f)));
            Assert.That(app.Board.CaptureView().Zoom, Is.GreaterThanOrEqualTo(1.6f));
            Assert.That(LevelAuthoring.LayoutEquals(invalid, app.Workshop.Draft), Is.True);
            yield return null;
        }

        private static TMP_InputField Input(GameController app, string name)
        { return app.GetComponentsInChildren<TMP_InputField>().Single(input => input.name == name); }

        private static void Click(GameController app, string name)
        { app.GetComponentsInChildren<Button>().Single(button => button.name == name).onClick.Invoke(); }

        private void Select(GridPos a, GridPos b)
        { workshop.BeginGesture(a, true); workshop.EndGesture(b); }

        private static LevelDefinition Room()
        {
            var level = LevelAuthoring.CreateBlank(8, 8);
            level.Id = "workshop-test"; level.Name = "中文测试关卡";
            level.HasPlayer = true; level.PlayerStart = new GridPos(2, 2);
            level.Boxes = new[] { new GridPos(3, 2) };
            level.Goals = new[] { new GridPos(5, 2) };
            return level;
        }

        private sealed class MemoryWriter : ILevelAssetWriter
        {
            private readonly LevelAsset asset;
            public bool Succeed;
            public MemoryWriter(LevelAsset asset) { this.asset = asset; }
            public LevelSaveResult Save(LevelAsset target, LevelDefinition snapshot, string solution, LevelSaveIntent intent)
            {
                if (!Succeed) return new LevelSaveResult(false, null, "模拟保存失败");
                snapshot.LayoutVersion = asset.Data.LayoutVersion + 1;
                asset.Data = snapshot.DeepClone();
                return new LevelSaveResult(true, asset, "保存成功");
            }
            public IReadOnlyList<LevelAsset> ListLevels() => new[] { asset };
            public bool IsInCatalog(LevelAsset value) => value == asset;
        }
    }
}
