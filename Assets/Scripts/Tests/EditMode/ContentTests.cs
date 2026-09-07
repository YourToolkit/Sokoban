using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Sokoban.Content;
using Sokoban.Core;
using Sokoban.EditorTools;
using Sokoban.Runtime;
using UnityEditor;
using UnityEngine;

namespace Sokoban.Tests
{
    public sealed class ContentTests
    {
        private const string ResourcesPath = "Assets/Resources/GameResources.asset";

        [Test]
        public void BuiltInRoomsHaveUniqueIdsAndIncreaseFromOneToThreeCrates()
        {
            var levels = BuiltInLevels.Create();

            Assert.That(levels.Length, Is.EqualTo(6));
            Assert.That(BuiltInLevels.Solutions.Length, Is.EqualTo(levels.Length));
            Assert.That(levels.Select(level => level.Id).Distinct().Count(), Is.EqualTo(levels.Length));
            CollectionAssert.AreEqual(new[] { 1, 1, 2, 2, 3, 3 }, levels.Select(level => level.Boxes.Length));
            foreach (var level in levels)
            {
                Assert.That(LevelValidator.Validate(level), Is.Empty, level.Name);
                Assert.That(level.Name, Is.Not.Empty);
                Assert.That(level.Description, Is.Not.Empty);
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void EveryOriginalRoomHasAVerifiedReversibleSolution(int index)
        {
            ReplayAndUndoSolution(BuiltInLevels.Create()[index], BuiltInLevels.Solutions[index]);
        }

        [Test]
        public void PackagedResourcesContainTheSixRoomsInTeachingOrder()
        {
            var resources = LoadResources();
            var expected = BuiltInLevels.Create();

            Assert.That(resources.Catalog, Is.Not.Null);
            Assert.That(resources.Config, Is.Not.Null);
            Assert.That(resources.Visuals, Is.Not.Null);
            Assert.That(resources.Catalog.Levels, Has.Count.EqualTo(expected.Length));
            Assert.That(resources.Catalog.Levels.All(level => level != null && level.Data != null), Is.True);
            CollectionAssert.AreEqual(expected.Select(level => level.Id),
                resources.Catalog.Levels.Select(level => level.Data.Id));
            Assert.That(resources.Catalog.Levels.Select(level => level.Data.Id).Distinct().Count(),
                Is.EqualTo(expected.Length), "Catalog entries must be identified by unique stable IDs.");

            var sprites = new[]
            {
                resources.Visuals.Floor, resources.Visuals.Wall, resources.Visuals.Goal,
                resources.Visuals.Player, resources.Visuals.Box, resources.Visuals.BoxOnGoal
            };
            Assert.That(sprites.All(sprite => sprite != null), Is.True, "Every board tile needs a configured visual.");
            Assert.That(resources.Config.MoveDuration, Is.GreaterThan(0f));
            Assert.That(resources.Config.RepeatDelay, Is.GreaterThan(0f));
            Assert.That(resources.Config.RepeatInterval, Is.GreaterThan(0f));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void SavedRoomAssetsValidateAgainstTheirAuthoredVersion(int index)
        {
            var resources = LoadResources();
            Assert.That(resources.Catalog, Is.Not.Null);
            Assert.That(resources.Catalog.Levels.Count, Is.GreaterThan(index));
            var asset = resources.Catalog.Levels[index];
            Assert.That(asset, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(asset), Does.StartWith("Assets/Resources/Levels/"));
            var definition = asset.ToDefinition();
            var expected = BuiltInLevels.Create()[index];

            Assert.That(definition.Id, Is.EqualTo(expected.Id));
            Assert.That(LevelValidator.Validate(definition), Is.Empty, definition.Name);
            if (definition.LayoutVersion == 0)
            {
                Assert.That(definition.Width, Is.EqualTo(expected.Width));
                Assert.That(definition.Height, Is.EqualTo(expected.Height));
                Assert.That(definition.PlayerStart, Is.EqualTo(expected.PlayerStart));
                CollectionAssert.AreEqual(expected.Cells, definition.Cells);
                CollectionAssert.AreEqual(expected.Boxes, definition.Boxes);
                CollectionAssert.AreEqual(expected.Goals, definition.Goals);
                ReplayAndUndoSolution(definition, BuiltInLevels.Solutions[index]);
            }
            // An authored new version is not the built-in template. Never restore it from test data.
            else if (string.IsNullOrEmpty(asset.VerifiedSolution))
                TestContext.WriteLine("Authored layout v" + definition.LayoutVersion + " has no cached solution; structural validation only: " + definition.Name);
            if (!string.IsNullOrEmpty(asset.VerifiedSolution)) ReplayAndUndoSolution(definition, asset.VerifiedSolution);
        }

        [Test]
        public void AssetConversionAndGameplayDoNotModifyAuthoringData()
        {
            var asset = ScriptableObject.CreateInstance<LevelAsset>();
            try
            {
                asset.Data = BuiltInLevels.Create()[0];
                var original = asset.Data.DeepClone();
                var copy = asset.ToDefinition();
                copy.Name = "Temporary edit";
                copy.Boxes[0] = new GridPos(1, 1);
                copy.Goals[0] = new GridPos(2, 1);
                copy.Cells[0] = CellType.Floor;
                var session = new GameSession(asset.ToDefinition());
                session.TryMove(Direction.Right);
                session.TryMove(Direction.Right);
                session.Undo();
                session.Restart();

                Assert.That(asset.Data.Name, Is.EqualTo(original.Name));
                Assert.That(asset.Data.PlayerStart, Is.EqualTo(original.PlayerStart));
                CollectionAssert.AreEqual(original.Boxes, asset.Data.Boxes);
                CollectionAssert.AreEqual(original.Goals, asset.Data.Goals);
                CollectionAssert.AreEqual(original.Cells, asset.Data.Cells);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        private static GameResources LoadResources()
        {
            var resources = AssetDatabase.LoadAssetAtPath<GameResources>(ResourcesPath);
            Assert.That(resources, Is.Not.Null,
                "Generate the shipped content using the Sokoban project setup before running content tests.");
            return resources;
        }

        private static void ReplayAndUndoSolution(LevelDefinition definition, string solution)
        {
            Assert.That(LevelValidator.Validate(definition), Is.Empty, definition.Name);
            Assert.That(solution, Is.Not.Empty, definition.Name);
            var session = new GameSession(definition);
            var states = new List<BoardState> { session.State };
            Assert.That(session.IsWon, Is.False, "Teaching rooms should begin as unfinished puzzles.");
            for (int index = 0; index < solution.Length; index++)
            {
                var direction = ParseDirection(solution[index]);
                var result = session.TryMove(direction);
                Assert.That(result.Succeeded, Is.True,
                    $"{definition.Name}: move {index + 1} ({solution[index]}) was rejected: {result.Reason}");
                Assert.That(result.Won, Is.EqualTo(index == solution.Length - 1),
                    $"{definition.Name}: the solution must end on the winning move.");
                states.Add(session.State);
            }

            Assert.That(session.IsWon, Is.True, definition.Name);
            Assert.That(session.State.Steps, Is.EqualTo(solution.Length));
            Assert.That(session.State.Pushes, Is.GreaterThan(0));
            Assert.That(session.State.Boxes.All(definition.IsGoal), Is.True);
            for (int index = states.Count - 2; index >= 0; index--)
            {
                Assert.That(session.Undo(), Is.True, definition.Name);
                CoreTests.AssertSameState(states[index], session.State);
            }
            Assert.That(session.CanUndo, Is.False);
            Assert.That(session.IsWon, Is.False);
        }

        private static Direction ParseDirection(char character)
        {
            switch (character)
            {
                case 'U': return Direction.Up;
                case 'R': return Direction.Right;
                case 'D': return Direction.Down;
                case 'L': return Direction.Left;
                default: throw new ArgumentException($"Unknown solution direction '{character}'.");
            }
        }
    }

    public sealed class ProgressTests
    {
        private string directory;

        [SetUp]
        public void CreateTemporarySaveDirectory()
        {
            directory = Path.Combine(Path.GetTempPath(), "SokobanProgressTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
        }

        [TearDown]
        public void DeleteTemporarySaveDirectory()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        [Test]
        public void ProgressSurvivesReloadAndIsAddressedByIdAfterCatalogReordering()
        {
            var levels = BuiltInLevels.Create();
            var store = new ProgressStore(directory);
            Assert.That(store.RecordWin(levels[0].Id, 2, 2), Is.True);
            Assert.That(store.RecordWin(levels[2].Id, 9, 6), Is.True);
            string firstId = levels[0].Id;
            string thirdId = levels[2].Id;
            Array.Reverse(levels);
            var reloaded = new ProgressStore(directory);

            foreach (var level in levels)
            {
                var record = reloaded.Get(level.Id);
                if (level.Id == firstId || level.Id == thirdId)
                {
                    Assert.That(record, Is.Not.Null);
                    Assert.That(record.Completed, Is.True);
                    Assert.That(record.BestSteps, Is.EqualTo(level.Id == firstId ? 2 : 9));
                }
                else Assert.That(record, Is.Null);
            }
            Assert.That(reloaded.Get("a-removed-or-unknown-room"), Is.Null);
            Assert.That(reloaded.LastError, Is.Empty);
        }

        [Test]
        public void BestPushesBelongToTheBestStepRunAndBreakStepTies()
        {
            var store = new ProgressStore(directory);
            Assert.That(store.RecordWin("room", 10, 4), Is.True);
            Assert.That(store.RecordWin("room", 12, 3), Is.True);
            Assert.That(store.Get("room").BestSteps, Is.EqualTo(10));
            Assert.That(store.Get("room").BestPushes, Is.EqualTo(4));
            Assert.That(store.RecordWin("room", 10, 2), Is.True);

            var saved = new ProgressStore(directory).Get("room");

            Assert.That(saved.BestSteps, Is.EqualTo(10));
            Assert.That(saved.BestPushes, Is.EqualTo(2));
        }

        [TestCase("{broken json")]
        [TestCase("{\"Version\":99,\"Records\":[]}")]
        [TestCase("{\"Version\":1,\"Records\":[{\"LevelId\":\"room\",\"Completed\":true,\"BestSteps\":1,\"BestPushes\":2}]}")]
        public void UnreadableProgressPreservesTheOriginalAndAllowsFuturePlay(string contents)
        {
            string path = Path.Combine(directory, "progress.json");
            File.WriteAllText(path, contents);

            var store = new ProgressStore(directory);

            Assert.That(store.LastError, Is.Not.Empty);
            Assert.That(store.Get("room"), Is.Null);
            Assert.That(File.ReadAllText(path), Is.EqualTo(contents));
            Assert.That(File.ReadAllText(path + ".unreadable"), Is.EqualTo(contents));
            Assert.That(store.RecordWin("recovered-room", 4, 2), Is.True);
            Assert.That(new ProgressStore(directory).Get("recovered-room"), Is.Not.Null);
            Assert.That(File.ReadAllText(path + ".unreadable"), Is.EqualTo(contents));
        }

        [Test]
        public void ChangingAReturnedProgressRecordDoesNotChangeTheStore()
        {
            var store = new ProgressStore(directory);
            store.RecordWin("room", 10, 4);
            var returned = store.Get("room");

            returned.BestSteps = 0;
            returned.Completed = false;

            Assert.That(store.Get("room").BestSteps, Is.EqualTo(10));
            Assert.That(store.Get("room").Completed, Is.True);
        }

        [Test]
        public void LayoutVersionsDoNotReuseScoresFromAnotherPuzzle()
        {
            var store = new ProgressStore(directory);
            store.RecordWin("room", 4, 2, 0);
            Assert.That(store.Get("room", 1), Is.Null);
            store.RecordWin("room", 12, 7, 1);
            var reloaded = new ProgressStore(directory);
            Assert.That(reloaded.Get("room", 0), Is.Null);
            Assert.That(reloaded.Get("room", 1).BestSteps, Is.EqualTo(12));
            Assert.That(reloaded.Get("room", 1).BestPushes, Is.EqualTo(7));
        }

        [Test]
        public void LegacyScoresBecomeInitialLayoutVersionWithoutLosingProgress()
        {
            File.WriteAllText(Path.Combine(directory, "progress.json"),
                "{\"Version\":1,\"Records\":[{\"LevelId\":\"old-room\",\"Completed\":true,\"BestSteps\":9,\"BestPushes\":6}]}");
            var store = new ProgressStore(directory);
            Assert.That(store.Get("old-room", 0).BestSteps, Is.EqualTo(9));
            Assert.That(store.Get("old-room", 1), Is.Null);
            Assert.That(store.RecordWin("new-room", 2, 1, 3), Is.True);
            var reloaded = new ProgressStore(directory);
            Assert.That(reloaded.Get("old-room", 0).BestPushes, Is.EqualTo(6));
            Assert.That(reloaded.Get("new-room", 3).Completed, Is.True);
        }
    }
}
