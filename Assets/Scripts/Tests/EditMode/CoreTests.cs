using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sokoban.Core;

namespace Sokoban.Tests
{
    public sealed class CoreTests
    {
        [TestCase(Direction.Up, 0, 1)]
        [TestCase(Direction.Right, 1, 0)]
        [TestCase(Direction.Down, 0, -1)]
        [TestCase(Direction.Left, -1, 0)]
        public void WalkingUsesGridCoordinatesAndCountsOneStep(Direction direction, int x, int y)
        {
            var level = MakeLevel();
            level.PlayerStart = new GridPos(3, 2);
            level.Boxes[0] = new GridPos(4, 4);
            level.Goals[0] = new GridPos(5, 4);
            var session = new GameSession(level);

            var result = session.TryMove(direction);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.From, Is.EqualTo(level.PlayerStart));
            Assert.That(result.To, Is.EqualTo(level.PlayerStart + new GridPos(x, y)));
            Assert.That(result.Pushed, Is.False);
            Assert.That(result.BoxFrom, Is.Null);
            Assert.That(result.BoxTo, Is.Null);
            Assert.That(session.State.Player, Is.EqualTo(result.To));
            Assert.That(session.State.Steps, Is.EqualTo(1));
            Assert.That(session.State.Pushes, Is.Zero);
        }

        [Test]
        public void AValidPushMovesPlayerAndOneBoxTogether()
        {
            var session = new GameSession(MakeLevel());

            var result = session.TryMove(Direction.Right);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Pushed, Is.True);
            Assert.That(result.From, Is.EqualTo(new GridPos(2, 2)));
            Assert.That(result.To, Is.EqualTo(new GridPos(3, 2)));
            Assert.That(result.BoxFrom, Is.EqualTo(new GridPos(3, 2)));
            Assert.That(result.BoxTo, Is.EqualTo(new GridPos(4, 2)));
            Assert.That(session.State.Player, Is.EqualTo(result.To));
            CollectionAssert.AreEqual(new[] { new GridPos(4, 2) }, session.State.Boxes);
            Assert.That(session.State.Steps, Is.EqualTo(1));
            Assert.That(session.State.Pushes, Is.EqualTo(1));
            Assert.That(result.Won, Is.False);
        }

        [TestCase("wall")]
        [TestCase("outside")]
        [TestCase("box against wall")]
        [TestCase("box against box")]
        public void BlockedMovesAreAtomicAndDoNotCreateUndoHistory(string obstruction)
        {
            var level = MakeLevel();
            var direction = Direction.Right;
            switch (obstruction)
            {
                case "wall":
                    level.PlayerStart = new GridPos(1, 2);
                    direction = Direction.Left;
                    break;
                case "outside":
                    level.PlayerStart = new GridPos(0, 2);
                    level.Cells[2 * level.Width] = CellType.Floor;
                    direction = Direction.Left;
                    break;
                case "box against wall":
                    level.Cells[2 * level.Width + 4] = CellType.Wall;
                    break;
                case "box against box":
                    level.Boxes = new[] { new GridPos(3, 2), new GridPos(4, 2) };
                    level.Goals = new[] { new GridPos(5, 2), new GridPos(5, 3) };
                    break;
            }
            var session = new GameSession(level);
            var before = session.State;

            var result = session.TryMove(direction);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Pushed, Is.False);
            Assert.That(result.Won, Is.False);
            Assert.That(result.Reason, Is.Not.Empty);
            Assert.That(result.BoxFrom, Is.Null);
            Assert.That(result.BoxTo, Is.Null);
            AssertSameState(before, session.State);
            Assert.That(session.CanUndo, Is.False);
            Assert.That(session.Undo(), Is.False);
        }

        [Test]
        public void FailedInputAfterSuccessfulInputDoesNotConsumeAnUndo()
        {
            var level = MakeLevel();
            level.Cells[3 * level.Width + 3] = CellType.Wall;
            var session = new GameSession(level);
            var initial = session.State;
            Assert.That(session.TryMove(Direction.Right).Succeeded, Is.True);
            Assert.That(session.TryMove(Direction.Up).Succeeded, Is.False);

            Assert.That(session.Undo(), Is.True);

            AssertSameState(initial, session.State);
            Assert.That(session.CanUndo, Is.False);
        }

        [Test]
        public void UndoRestoresWalkingPushingCountersAndVictoryInReverseOrder()
        {
            var session = new GameSession(MakeLevel());
            var states = new List<BoardState> { session.State };
            var directions = new[] { Direction.Right, Direction.Up, Direction.Down, Direction.Right };
            foreach (var direction in directions)
            {
                Assert.That(session.TryMove(direction).Succeeded, Is.True);
                states.Add(session.State);
            }
            Assert.That(session.IsWon, Is.True);
            var won = session.State;
            Assert.That(session.TryMove(Direction.Up).Succeeded, Is.False);
            AssertSameState(won, session.State);

            for (int index = states.Count - 2; index >= 0; index--)
            {
                Assert.That(session.Undo(), Is.True);
                AssertSameState(states[index], session.State);
            }

            Assert.That(session.IsWon, Is.False);
            Assert.That(session.CanUndo, Is.False);
            Assert.That(session.Undo(), Is.False);
        }

        [Test]
        public void RestartRestoresTheOriginalLayoutAndClearsHistory()
        {
            var session = new GameSession(MakeLevel());
            var initial = session.State;
            session.TryMove(Direction.Right);
            session.TryMove(Direction.Right);
            Assert.That(session.IsWon, Is.True);

            session.Restart();

            AssertSameState(initial, session.State);
            Assert.That(session.CanUndo, Is.False);
            Assert.That(session.TryMove(Direction.Right).Succeeded, Is.True);
        }

        [Test]
        public void FillingOnlyOneTargetDoesNotWinAndMovingOffItDoesNotEraseIt()
        {
            var level = MakeLevel();
            level.Boxes = new[] { new GridPos(3, 2), new GridPos(2, 4) };
            level.Goals = new[] { new GridPos(4, 2), new GridPos(5, 4) };
            var session = new GameSession(level);

            var ontoGoal = session.TryMove(Direction.Right);

            Assert.That(ontoGoal.Pushed, Is.True);
            Assert.That(ontoGoal.Won, Is.False);
            Assert.That(session.IsWon, Is.False);
            Assert.That(session.Definition.IsGoal(new GridPos(4, 2)), Is.True);

            Assert.That(session.TryMove(Direction.Right).Succeeded, Is.True);
            Assert.That(session.State.Player, Is.EqualTo(new GridPos(4, 2)));
            Assert.That(session.Definition.IsGoal(session.State.Player), Is.True);
            Assert.That(session.Undo(), Is.True);
            Assert.That(session.State.Boxes, Does.Contain(new GridPos(4, 2)));
            Assert.That(session.Definition.IsGoal(new GridPos(4, 2)), Is.True);
        }

        [Test]
        public void ADeadEndIsAReversiblePlayerMistakeRatherThanABlockedMove()
        {
            var level = MakeLevel();
            level.Goals[0] = new GridPos(1, 4);
            level.Cells[1 * level.Width + 4] = CellType.Wall;
            level.Cells[2 * level.Width + 5] = CellType.Wall;
            var session = new GameSession(level);
            var before = session.State;

            var result = session.TryMove(Direction.Right);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Pushed, Is.True);
            Assert.That(session.IsWon, Is.False);
            Assert.That(session.Undo(), Is.True);
            AssertSameState(before, session.State);
        }

        [Test]
        public void SessionOwnsACopyOfTheInputDefinition()
        {
            var original = MakeLevel();
            var session = new GameSession(original);
            original.PlayerStart = new GridPos(1, 1);
            original.Boxes[0] = new GridPos(2, 4);
            original.Goals[0] = new GridPos(4, 2);
            original.Cells[2 * original.Width + 4] = CellType.Wall;

            var result = session.TryMove(Direction.Right);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Pushed, Is.True);
            Assert.That(result.Won, Is.False, "Changing an authoring goal must not change a running game.");
            session.Restart();
            Assert.That(session.State.Player, Is.EqualTo(new GridPos(2, 2)));
            Assert.That(session.State.Boxes, Does.Contain(new GridPos(3, 2)));
        }

        [Test]
        public void PlayingUndoingAndRestartingNeverMutateTheInputDefinition()
        {
            var original = MakeLevel();
            var expected = original.DeepClone();
            var session = new GameSession(original);
            session.TryMove(Direction.Right);
            session.TryMove(Direction.Right);
            session.Undo();
            session.Restart();

            Assert.That(original.PlayerStart, Is.EqualTo(expected.PlayerStart));
            CollectionAssert.AreEqual(expected.Boxes, original.Boxes);
            CollectionAssert.AreEqual(expected.Goals, original.Goals);
            CollectionAssert.AreEqual(expected.Cells, original.Cells);
        }

        [Test]
        public void ReturnedDefinitionAndStateCannotChangeRunningRulesOrPastSnapshots()
        {
            var session = new GameSession(MakeLevel());
            var initial = session.State;
            var exposed = session.Definition;
            exposed.Boxes[0] = new GridPos(1, 1);
            exposed.Goals[0] = new GridPos(4, 2);
            exposed.Cells[2 * exposed.Width + 4] = CellType.Wall;

            Assert.That(session.TryMove(Direction.Right).Succeeded, Is.True);
            Assert.That(session.IsWon, Is.False);
            Assert.That(initial.Player, Is.EqualTo(new GridPos(2, 2)));
            CollectionAssert.AreEqual(new[] { new GridPos(3, 2) }, initial.Boxes);
            Assert.That(initial.Steps, Is.Zero);
            Assert.That(initial.Pushes, Is.Zero);
            Assert.That(initial.IsWon, Is.False);
        }

        [Test]
        public void InvalidDirectionLeavesTheGameUnchanged()
        {
            var session = new GameSession(MakeLevel());
            var before = session.State;

            Assert.Throws<ArgumentOutOfRangeException>(() => session.TryMove((Direction)99));

            AssertSameState(before, session.State);
            Assert.That(session.CanUndo, Is.False);
        }

        [Test]
        public void AnInitiallyCompletedNonemptyLayoutIsRecognizedWithoutHistory()
        {
            var level = MakeLevel();
            level.Goals = (GridPos[])level.Boxes.Clone();
            Assert.That(LevelValidator.Validate(level), Is.Empty);

            var session = new GameSession(level);

            Assert.That(session.IsWon, Is.True);
            Assert.That(session.State.Steps, Is.Zero);
            Assert.That(session.CanUndo, Is.False);
        }

        [Test]
        public void TargetsMayOverlapThePlayerOrACrate()
        {
            var level = MakeLevel();
            level.Boxes = new[] { new GridPos(3, 2), new GridPos(3, 3) };
            level.Goals = new[] { level.PlayerStart, new GridPos(3, 2) };

            Assert.That(LevelValidator.Validate(level), Is.Empty);
            Assert.That(new GameSession(level).IsWon, Is.False);
        }

        [TestCaseSource(nameof(InvalidLevels))]
        public void InvalidAuthoringDataCannotStartAGame(LevelDefinition level)
        {
            var issues = LevelValidator.Validate(level);

            Assert.That(issues, Is.Not.Empty);
            Assert.That(issues.All(issue => !string.IsNullOrWhiteSpace(issue.Message)), Is.True);
            Assert.Throws<ArgumentException>(() => new GameSession(level));
        }

        [Test]
        public void CoordinateErrorsIncludeTheCellForTheEditorToHighlight()
        {
            var level = MakeLevel();
            var invalidPosition = new GridPos(-1, 2);
            level.Boxes[0] = invalidPosition;

            var issues = LevelValidator.Validate(level);

            Assert.That(issues.Any(issue => issue.Position == invalidPosition), Is.True);
        }

        private static IEnumerable<TestCaseData> InvalidLevels()
        {
            yield return new TestCaseData(new object[] { null }).SetName("RejectsMissingLevel");
            var mutations = new Dictionary<string, Action<LevelDefinition>>
            {
                { "MissingStableId", level => level.Id = "" },
                { "MissingName", level => level.Name = " " },
                { "WidthTooSmall", level => level.Width = 1 },
                { "HeightTooSmall", level => level.Height = 1 },
                { "WidthTooLarge", level => level.Width = 33 },
                { "HeightTooLarge", level => level.Height = 33 },
                { "MissingTerrain", level => level.Cells = null },
                { "WrongTerrainCount", level => level.Cells = new CellType[3] },
                { "UnknownTerrain", level => level.Cells[8] = (CellType)99 },
                { "MissingPlayer", level => level.HasPlayer = false },
                { "PlayerOutside", level => level.PlayerStart = new GridPos(-1, 2) },
                { "PlayerInWall", level => level.PlayerStart = new GridPos(0, 0) },
                { "MissingBoxes", level => level.Boxes = Array.Empty<GridPos>() },
                { "MissingGoals", level => level.Goals = Array.Empty<GridPos>() },
                { "NullBoxes", level => level.Boxes = null },
                { "NullGoals", level => level.Goals = null },
                { "BoxGoalMismatch", level => level.Boxes = new[] { new GridPos(3, 2), new GridPos(3, 3) } },
                { "BoxOutside", level => level.Boxes[0] = new GridPos(7, 2) },
                { "BoxInWall", level => level.Boxes[0] = new GridPos(0, 0) },
                { "GoalOutside", level => level.Goals[0] = new GridPos(2, 6) },
                { "GoalInWall", level => level.Goals[0] = new GridPos(0, 0) },
                { "PlayerBoxOverlap", level => level.Boxes[0] = level.PlayerStart },
                { "DuplicateBoxes", level =>
                    {
                        level.Boxes = new[] { new GridPos(3, 2), new GridPos(3, 2) };
                        level.Goals = new[] { new GridPos(5, 2), new GridPos(5, 3) };
                    }
                },
                { "DuplicateGoals", level =>
                    {
                        level.Boxes = new[] { new GridPos(3, 2), new GridPos(3, 3) };
                        level.Goals = new[] { new GridPos(5, 2), new GridPos(5, 2) };
                    }
                }
            };
            foreach (var mutation in mutations)
            {
                var level = MakeLevel();
                mutation.Value(level);
                yield return new TestCaseData(level).SetName("Rejects" + mutation.Key);
            }
        }

        private static LevelDefinition MakeLevel()
        {
            const int width = 7;
            const int height = 6;
            var cells = new CellType[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    cells[y * width + x] = x == 0 || y == 0 || x == width - 1 || y == height - 1
                        ? CellType.Wall : CellType.Floor;
            return new LevelDefinition
            {
                Id = "core-test",
                Name = "Core test room",
                Width = width,
                Height = height,
                Cells = cells,
                HasPlayer = true,
                PlayerStart = new GridPos(2, 2),
                Boxes = new[] { new GridPos(3, 2) },
                Goals = new[] { new GridPos(5, 2) }
            };
        }

        internal static void AssertSameState(BoardState expected, BoardState actual)
        {
            Assert.That(actual.Player, Is.EqualTo(expected.Player));
            CollectionAssert.AreEqual(expected.Boxes, actual.Boxes);
            Assert.That(actual.Steps, Is.EqualTo(expected.Steps));
            Assert.That(actual.Pushes, Is.EqualTo(expected.Pushes));
            Assert.That(actual.IsWon, Is.EqualTo(expected.IsWon));
        }
    }
}
