using System;
using System.Reflection;
using NUnit.Framework;
using Sokoban.Content;
using Sokoban.Core;
using Sokoban.EditorTools;
using UnityEditor;
using UnityEngine;

namespace Sokoban.Tests
{
    public sealed class EditorWorkflowTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private LevelAsset source;
        private LevelEditorWindow window;
        private LevelEditorDraft Draft => (LevelEditorDraft)Field("draft").GetValue(window);

        [SetUp]
        public void OpenAnIsolatedWorkshop()
        {
            source = ScriptableObject.CreateInstance<LevelAsset>();
            source.Data = new LevelDefinition
            {
                Id = "editor-source",
                Name = "Workshop source",
                Width = 5,
                Height = 4,
                Cells = new CellType[20],
                HasPlayer = true,
                PlayerStart = new GridPos(1, 1),
                Boxes = new[] { new GridPos(2, 1) },
                Goals = new[] { new GridPos(3, 1) }
            };
            source.Data.Cells[0] = CellType.Wall;
            source.Data.Cells[3 * source.Data.Width + 2] = CellType.Wall;
            source.VerifiedSolution = "R";
            // CreateInstance exercises the real EditorWindow lifecycle without opening UI or dialogs.
            window = ScriptableObject.CreateInstance<LevelEditorWindow>();
            Invoke("LoadLevel", source);
            Undo.IncrementCurrentGroup();
        }

        [TearDown]
        public void ReleaseTemporaryAuthoringObjects()
        {
            if (window != null)
            {
                if (Draft != null) Undo.ClearUndo(Draft);
                window.DiscardChanges();
                UnityEngine.Object.DestroyImmediate(window);
            }
            if (source != null) UnityEngine.Object.DestroyImmediate(source);
        }

        [Test]
        public void EditingALoadedRoomChangesOnlyTheWorkingCopy()
        {
            string before = JsonUtility.ToJson(source);
            Assert.That(window.hasUnsavedChanges, Is.False);

            Paint(new GridPos(2, 1), "Wall");
            Paint(new GridPos(4, 2), "Player");

            Assert.That(JsonUtility.ToJson(source), Is.EqualTo(before), "Authoring must not mutate the selected room asset before Save.");
            Assert.That(Draft.Data.PlayerStart, Is.EqualTo(new GridPos(4, 2)));
            Assert.That(Draft.Data.CellAt(new GridPos(2, 1)), Is.EqualTo(CellType.Wall));
            CollectionAssert.DoesNotContain(Draft.Data.Boxes, new GridPos(2, 1));
            Assert.That(window.hasUnsavedChanges, Is.True);
        }

        [Test]
        public void ABoxAndAGoalCanBePaintedInEitherOrderWithoutDuplicateOccupancy()
        {
            var boxFirst = new GridPos(2, 2);
            var goalFirst = new GridPos(3, 2);

            Paint(boxFirst, "Box");
            Paint(boxFirst, "Goal");
            Paint(boxFirst, "Box");
            Paint(goalFirst, "Goal");
            Paint(goalFirst, "Box");
            Paint(goalFirst, "Goal");

            Assert.That(Draft.Data.IsGoal(boxFirst), Is.True);
            Assert.That(Draft.Data.IsGoal(goalFirst), Is.True);
            Assert.That(Array.FindAll(Draft.Data.Boxes, p => p == boxFirst), Has.Length.EqualTo(1));
            Assert.That(Array.FindAll(Draft.Data.Boxes, p => p == goalFirst), Has.Length.EqualTo(1));
            Assert.That(Array.FindAll(Draft.Data.Goals, p => p == goalFirst), Has.Length.EqualTo(1));
            Assert.That(LevelValidator.Validate(Draft.Data), Is.Empty);
        }

        [Test]
        public void PlayerPlacementPreservesAGoalButReplacesTheBoxAndOldPlayer()
        {
            var position = new GridPos(3, 1);
            Paint(position, "Box");

            Paint(position, "Player");

            Assert.That(Draft.Data.HasPlayer, Is.True);
            Assert.That(Draft.Data.PlayerStart, Is.EqualTo(position));
            CollectionAssert.DoesNotContain(Draft.Data.Boxes, position);
            Assert.That(Draft.Data.IsGoal(position), Is.True);
            Paint(position, "Erase");
            Assert.That(Draft.Data.HasPlayer, Is.False);
            Assert.That(Draft.Data.IsGoal(position), Is.False);
            Assert.That(Draft.Data.CellAt(position), Is.EqualTo(CellType.Floor));
        }

        [Test]
        public void ALayoutEditInvalidatesTheSolutionAndUndoRestoresTheCompleteDraft()
        {
            string before = JsonUtility.ToJson(Draft);

            Paint(new GridPos(2, 1), "Wall");
            Undo.FlushUndoRecordObjects();
            Assert.That(Draft.VerifiedSolution, Is.Empty);
            Assert.That(window.hasUnsavedChanges, Is.True);
            Undo.PerformUndo();

            Assert.That(JsonUtility.ToJson(Draft), Is.EqualTo(before));
            Assert.That(window.hasUnsavedChanges, Is.False);
            Assert.That(source.VerifiedSolution, Is.EqualTo("R"));
            Undo.PerformRedo();
            Assert.That(Draft.VerifiedSolution, Is.Empty);
            Assert.That(window.hasUnsavedChanges, Is.True);
        }

        [Test]
        public void GrowingTheRoomPreservesCoordinatesAndCanBeUndone()
        {
            string originalDraft = JsonUtility.ToJson(Draft);
            string originalSource = JsonUtility.ToJson(source);
            Field("desiredWidth").SetValue(window, 8);
            Field("desiredHeight").SetValue(window, 6);

            Invoke("ResizeRoom");
            Undo.FlushUndoRecordObjects();

            Assert.That(Draft.Data.Width, Is.EqualTo(8));
            Assert.That(Draft.Data.Height, Is.EqualTo(6));
            Assert.That(Draft.Data.Cells, Has.Length.EqualTo(48));
            Assert.That(Draft.Data.CellAt(new GridPos(0, 0)), Is.EqualTo(CellType.Wall));
            Assert.That(Draft.Data.CellAt(new GridPos(2, 3)), Is.EqualTo(CellType.Wall), "Terrain must retain its coordinates when the row stride changes.");
            Assert.That(Draft.Data.CellAt(new GridPos(1, 2)), Is.EqualTo(CellType.Floor));
            Assert.That(Draft.Data.CellAt(new GridPos(7, 5)), Is.EqualTo(CellType.Floor));
            Assert.That(Draft.Data.PlayerStart, Is.EqualTo(source.Data.PlayerStart));
            CollectionAssert.AreEqual(source.Data.Boxes, Draft.Data.Boxes);
            CollectionAssert.AreEqual(source.Data.Goals, Draft.Data.Goals);
            Assert.That(Draft.VerifiedSolution, Is.Empty);
            Assert.That(LevelValidator.Validate(Draft.Data), Is.Empty);
            Assert.That(JsonUtility.ToJson(source), Is.EqualTo(originalSource));
            Undo.PerformUndo();
            Assert.That(JsonUtility.ToJson(Draft), Is.EqualTo(originalDraft));
        }

        [Test]
        public void SaveAsCreatesIndependentIdentityAndPreservesAnUnchangedSolution()
        {
            var firstCopy = (LevelAsset)Invoke("CreateAssetCopy", true);
            var secondCopy = (LevelAsset)Invoke("CreateAssetCopy", true);
            try
            {
                Assert.That(Guid.TryParseExact(firstCopy.Data.Id, "N", out _), Is.True);
                Assert.That(firstCopy.Data.Id, Is.Not.EqualTo(source.Data.Id));
                Assert.That(secondCopy.Data.Id, Is.Not.EqualTo(firstCopy.Data.Id));
                Assert.That(Draft.Data.Id, Is.EqualTo(source.Data.Id), "Preparing a copy must not rewrite the source draft's identity.");
                Assert.That(firstCopy.VerifiedSolution, Is.EqualTo(source.VerifiedSolution));
                CollectionAssert.AreEqual(source.Data.Cells, firstCopy.Data.Cells);
                CollectionAssert.AreEqual(source.Data.Boxes, firstCopy.Data.Boxes);
                CollectionAssert.AreEqual(source.Data.Goals, firstCopy.Data.Goals);

                firstCopy.Data.Boxes[0] = new GridPos(4, 3);
                firstCopy.Data.Cells[0] = CellType.Floor;

                Assert.That(Draft.Data.Boxes[0], Is.EqualTo(new GridPos(2, 1)));
                Assert.That(source.Data.Boxes[0], Is.EqualTo(new GridPos(2, 1)));
                Assert.That(Draft.Data.Cells[0], Is.EqualTo(CellType.Wall));
                Assert.That(secondCopy.Data.Cells[0], Is.EqualTo(CellType.Wall));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(firstCopy);
                UnityEngine.Object.DestroyImmediate(secondCopy);
            }
        }

        [Test]
        public void OrdinaryAssetCreationRetainsTheDraftIdentityButDoesNotReviveAnOldSolution()
        {
            Paint(new GridPos(4, 3), "Wall");

            var copy = (LevelAsset)Invoke("CreateAssetCopy", false);
            try
            {
                Assert.That(copy.Data.Id, Is.EqualTo(source.Data.Id));
                Assert.That(copy.VerifiedSolution, Is.Empty);
                Assert.That(source.VerifiedSolution, Is.EqualTo("R"));
            }
            finally { UnityEngine.Object.DestroyImmediate(copy); }
        }

        private void Paint(GridPos position, string brushName)
        {
            Type brush = typeof(LevelEditorWindow).GetNestedType("Brush", BindingFlags.NonPublic);
            Assert.That(brush, Is.Not.Null);
            Invoke("Paint", position, Enum.Parse(brush, brushName));
        }

        private object Invoke(string name, params object[] arguments)
        {
            MethodInfo method = typeof(LevelEditorWindow).GetMethod(name, PrivateInstance);
            Assert.That(method, Is.Not.Null, name);
            return method.Invoke(window, arguments);
        }

        private static FieldInfo Field(string name)
        {
            FieldInfo field = typeof(LevelEditorWindow).GetField(name, PrivateInstance);
            Assert.That(field, Is.Not.Null, name);
            return field;
        }
    }
}
