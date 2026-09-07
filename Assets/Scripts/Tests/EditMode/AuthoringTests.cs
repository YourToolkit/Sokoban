using System;
using System.Linq;
using NUnit.Framework;
using Sokoban.Core;
using UnityEngine;

namespace Sokoban.Tests
{
    public sealed class AuthoringTests
    {
        [TestCase(0, 0, 6, 3)]
        [TestCase(6, 3, 0, 0)]
        [TestCase(2, 6, 4, 0)]
        [TestCase(6, 6, 0, 0)]
        [TestCase(3, 1, 3, 5)]
        [TestCase(1, 3, 5, 3)]
        public void LinesIncludeBothEndsAndRemainOneCellWide(int x0, int y0, int x1, int y1)
        {
            var draft = Empty(7, 7);
            Assert.That(LevelAuthoring.PaintLine(draft, new GridPos(x0, y0), new GridPos(x1, y1), LevelBrush.Wall), Is.True);
            Assert.That(draft.CellAt(new GridPos(x0, y0)), Is.EqualTo(CellType.Wall));
            Assert.That(draft.CellAt(new GridPos(x1, y1)), Is.EqualTo(CellType.Wall));
            Assert.That(draft.Cells.Count(c => c == CellType.Wall), Is.EqualTo(Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)) + 1));
        }

        [Test]
        public void RectangleOutlineAndFillIncludeTheirBorderAndHandleReversedCorners()
        {
            var outline = Empty(7, 7);
            var fill = Empty(7, 7);
            LevelAuthoring.PaintRectangle(outline, new GridPos(5, 4), new GridPos(1, 1), LevelBrush.Wall, false);
            LevelAuthoring.PaintRectangle(fill, new GridPos(1, 1), new GridPos(5, 4), LevelBrush.Wall, true);
            Assert.That(outline.Cells.Count(c => c == CellType.Wall), Is.EqualTo(14));
            Assert.That(fill.Cells.Count(c => c == CellType.Wall), Is.EqualTo(20));
            Assert.That(outline.CellAt(new GridPos(2, 2)), Is.EqualTo(CellType.Floor));
            Assert.That(fill.CellAt(new GridPos(2, 2)), Is.EqualTo(CellType.Wall));
            Assert.That(outline.CellAt(new GridPos(0, 0)), Is.EqualTo(CellType.Floor));
        }

        [Test]
        public void OneRowRectanglesHaveTheSameOutlineAndFill()
        {
            var a = Empty(7, 7);
            var b = a.DeepClone();
            LevelAuthoring.PaintRectangle(a, new GridPos(1, 2), new GridPos(5, 2), LevelBrush.Box, false);
            LevelAuthoring.PaintRectangle(b, new GridPos(1, 2), new GridPos(5, 2), LevelBrush.Box, true);
            Assert.That(LevelAuthoring.LayoutEquals(a, b), Is.True);
            Assert.That(a.Boxes, Has.Length.EqualTo(5));
        }

        [Test]
        public void FloodFillUsesFourNeighborsAndTheWholeOriginalCellSignature()
        {
            var draft = Empty(5, 5);
            for (int i = 0; i < draft.Cells.Length; i++) draft.Cells[i] = CellType.Wall;
            var start = new GridPos(1, 1);
            foreach (var p in new[] { start, new GridPos(2, 1), new GridPos(1, 2), new GridPos(2, 2) }) draft.Cells[p.Y * draft.Width + p.X] = CellType.Floor;
            draft.Goals = new[] { new GridPos(2, 1) };
            draft.Boxes = new[] { new GridPos(1, 2) };

            LevelAuthoring.FloodFill(draft, start, LevelBrush.Wall);

            Assert.That(draft.CellAt(start), Is.EqualTo(CellType.Wall));
            Assert.That(draft.CellAt(new GridPos(2, 2)), Is.EqualTo(CellType.Floor), "A diagonally touching cell is not connected.");
            Assert.That(draft.Goals, Does.Contain(new GridPos(2, 1)));
            Assert.That(draft.Boxes, Does.Contain(new GridPos(1, 2)));
        }

        [Test]
        public void FloodFillFindsItsWholeRegionBeforeChangingAnyCell()
        {
            var draft = Empty(8, 6);
            Assert.That(LevelAuthoring.FloodFill(draft, new GridPos(0, 0), LevelBrush.Wall), Is.True);
            Assert.That(draft.Cells.All(c => c == CellType.Wall), Is.True);
            Assert.That(LevelAuthoring.FloodFill(draft, new GridPos(0, 0), LevelBrush.Wall), Is.False, "A no-op must not create a new editing transaction.");
        }

        [Test]
        public void ATargetUnderABoxParticipatesInTheFillSignature()
        {
            var draft = Empty(6, 4);
            draft.Boxes = new[] { new GridPos(1, 1), new GridPos(2, 1), new GridPos(3, 1) };
            draft.Goals = new[] { new GridPos(1, 1), new GridPos(2, 1) };
            LevelAuthoring.FloodFill(draft, new GridPos(1, 1), LevelBrush.Erase);
            CollectionAssert.AreEqual(new[] { new GridPos(3, 1) }, draft.Boxes);
            Assert.That(draft.Goals, Is.Empty);
        }

        [Test]
        public void PlayerCannotBeAppliedByABatchTool()
        {
            var draft = Empty(7, 7);
            string before = JsonUtility.ToJson(draft);
            Assert.That(LevelAuthoring.PaintLine(draft, new GridPos(1, 1), new GridPos(4, 4), LevelBrush.Player), Is.False);
            Assert.That(LevelAuthoring.PaintRectangle(draft, new GridPos(1, 1), new GridPos(4, 4), LevelBrush.Player, true), Is.False);
            Assert.That(LevelAuthoring.FloodFill(draft, new GridPos(1, 1), LevelBrush.Player), Is.False);
            Assert.That(JsonUtility.ToJson(draft), Is.EqualTo(before));
            Assert.That(LevelAuthoring.Paint(draft, new GridPos(2, 2), LevelBrush.Player), Is.True);
            Assert.That(draft.PlayerStart, Is.EqualTo(new GridPos(2, 2)));
        }

        [Test]
        public void AnOverlappingMoveUsesTheOriginalRegionAndMovesThePlayerWithItsFloor()
        {
            var draft = Empty(7, 4);
            draft.Cells[1 * 7 + 1] = CellType.Wall;
            draft.Boxes = new[] { new GridPos(2, 1), new GridPos(4, 1) };
            draft.Goals = new[] { new GridPos(2, 1), new GridPos(4, 1) };
            draft.HasPlayer = true; draft.PlayerStart = new GridPos(3, 1);

            Assert.That(LevelAuthoring.TryMoveRegion(draft, new GridRect(1, 1, 3, 1), new GridPos(2, 1), out string error), Is.True, error);

            Assert.That(draft.CellAt(new GridPos(1, 1)), Is.EqualTo(CellType.Floor));
            Assert.That(draft.CellAt(new GridPos(2, 1)), Is.EqualTo(CellType.Wall));
            CollectionAssert.AreEqual(new[] { new GridPos(3, 1) }, draft.Boxes);
            CollectionAssert.AreEqual(new[] { new GridPos(3, 1) }, draft.Goals);
            Assert.That(draft.PlayerStart, Is.EqualTo(new GridPos(4, 1)));
        }

        [Test]
        public void AClipboardSnapshotSurvivesLaterSourceEditsAndReplacesEmptyDestinationCells()
        {
            var draft = Empty(8, 6);
            draft.Boxes = new[] { new GridPos(1, 1) };
            draft.Goals = new[] { new GridPos(1, 1) };
            var clipboard = LevelAuthoring.ReadRegion(draft, new GridRect(1, 1, 2, 1));
            LevelAuthoring.Paint(draft, new GridPos(1, 1), LevelBrush.Erase);
            LevelAuthoring.Paint(draft, new GridPos(5, 3), LevelBrush.Wall);

            Assert.That(LevelAuthoring.TryPasteRegion(draft, clipboard, new GridPos(4, 3), out string error), Is.True, error);

            Assert.That(draft.Boxes, Does.Contain(new GridPos(4, 3)));
            Assert.That(draft.IsGoal(new GridPos(4, 3)), Is.True);
            Assert.That(draft.CellAt(new GridPos(5, 3)), Is.EqualTo(CellType.Floor), "Clipboard floor is a complete replacement, not transparent.");
            CollectionAssert.DoesNotContain(draft.Boxes, new GridPos(1, 1));
        }

        [Test]
        public void CopySkipsThePlayerButCopiesItsTargetAndLeavesTheSourceIntact()
        {
            var draft = Empty(8, 6);
            draft.HasPlayer = true; draft.PlayerStart = new GridPos(1, 1);
            draft.Goals = new[] { draft.PlayerStart };
            Assert.That(LevelAuthoring.TryCopyRegion(draft, new GridRect(1, 1, 2, 2), new GridPos(4, 3), out string error), Is.True, error);
            Assert.That(draft.PlayerStart, Is.EqualTo(new GridPos(1, 1)));
            Assert.That(draft.IsGoal(new GridPos(1, 1)), Is.True);
            Assert.That(draft.IsGoal(new GridPos(4, 3)), Is.True);
        }

        [Test]
        public void CopyOntoTheExistingPlayerAndOutOfBoundsMovesRejectTheWholeTransaction()
        {
            var draft = Empty(8, 6);
            draft.HasPlayer = true; draft.PlayerStart = new GridPos(4, 3);
            draft.Boxes = new[] { new GridPos(1, 1) };
            string before = JsonUtility.ToJson(draft);
            Assert.That(LevelAuthoring.TryCopyRegion(draft, new GridRect(1, 1, 2, 2), new GridPos(4, 3), out string copyError), Is.False);
            Assert.That(copyError, Is.Not.Empty);
            Assert.That(LevelAuthoring.TryMoveRegion(draft, new GridRect(1, 1, 2, 2), new GridPos(7, 5), out string moveError), Is.False);
            Assert.That(moveError, Is.Not.Empty);
            Assert.That(JsonUtility.ToJson(draft), Is.EqualTo(before));
        }

        [Test]
        public void AnOverlappingCopyDoesNotReadBackItsOwnWrites()
        {
            var draft = Empty(7, 4);
            draft.Cells[8] = CellType.Wall;
            draft.Boxes = new[] { new GridPos(2, 1) };
            draft.Goals = new[] { new GridPos(3, 1) };
            Assert.That(LevelAuthoring.TryCopyRegion(draft, new GridRect(1, 1, 3, 1), new GridPos(2, 1), out string error), Is.True, error);
            Assert.That(draft.CellAt(new GridPos(1, 1)), Is.EqualTo(CellType.Wall));
            Assert.That(draft.CellAt(new GridPos(2, 1)), Is.EqualTo(CellType.Wall));
            CollectionAssert.AreEqual(new[] { new GridPos(3, 1) }, draft.Boxes);
            CollectionAssert.AreEqual(new[] { new GridPos(4, 1) }, draft.Goals);
        }

        [Test]
        public void ActorDraggingPreservesTargetsAndCannotOverwriteAnotherActor()
        {
            var draft = Empty(7, 4);
            var from = new GridPos(1, 1); var to = new GridPos(3, 1);
            draft.Boxes = new[] { from };
            draft.Goals = new[] { from, to };
            draft.HasPlayer = true; draft.PlayerStart = new GridPos(4, 1);
            Assert.That(LevelAuthoring.TryMoveActor(draft, from, to, out string error), Is.True, error);
            Assert.That(draft.IsGoal(from) && draft.IsGoal(to), Is.True);
            string before = JsonUtility.ToJson(draft);
            Assert.That(LevelAuthoring.TryMoveActor(draft, to, draft.PlayerStart, out _), Is.False);
            Assert.That(JsonUtility.ToJson(draft), Is.EqualTo(before));
        }

        [Test]
        public void ShrinkingCropsAllLayersWithoutMovingRemainingCoordinates()
        {
            var draft = Empty(7, 6);
            draft.HasPlayer = true; draft.PlayerStart = new GridPos(6, 5);
            draft.Boxes = new[] { new GridPos(2, 2), new GridPos(6, 4) };
            draft.Goals = new[] { new GridPos(2, 2), new GridPos(6, 5) };
            LevelAuthoring.Resize(draft, 4, 4);
            Assert.That(draft.HasPlayer, Is.False);
            CollectionAssert.AreEqual(new[] { new GridPos(2, 2) }, draft.Boxes);
            CollectionAssert.AreEqual(new[] { new GridPos(2, 2) }, draft.Goals);
            Assert.That(draft.Cells, Has.Length.EqualTo(16));
        }

        [Test]
        public void LayoutComparisonIgnoresMetadataAndObjectOrderButDetectsActualMultiplicityChanges()
        {
            var a = Empty(7, 6);
            a.Boxes = new[] { new GridPos(1, 1), new GridPos(2, 1) };
            var b = a.DeepClone();
            b.Id = "another-id"; b.Name = "另一个名字"; b.Description = "不同说明"; b.LayoutVersion = 99;
            Array.Reverse(b.Boxes);
            Assert.That(LevelAuthoring.LayoutEquals(a, b), Is.True);
            a.Boxes = new[] { new GridPos(1, 1), new GridPos(1, 1), new GridPos(2, 1) };
            b.Boxes = new[] { new GridPos(1, 1), new GridPos(2, 1), new GridPos(2, 1) };
            Assert.That(LevelAuthoring.LayoutEquals(a, b), Is.False);
        }

        private static LevelDefinition Empty(int width, int height) => new LevelDefinition
        {
            Id = "authoring-test", Name = "编辑测试", Width = width, Height = height,
            Cells = new CellType[width * height], Goals = Array.Empty<GridPos>(), Boxes = Array.Empty<GridPos>()
        };
    }
}
