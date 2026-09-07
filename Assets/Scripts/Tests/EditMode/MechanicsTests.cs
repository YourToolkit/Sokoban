using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sokoban.Core;

namespace Sokoban.Tests
{
    public sealed class MechanicsTests
    {
        private static ElementRegistry Registry => ElementRegistry.BuiltIns();
        private static LevelDefinition Room(bool sliding = true)
        {
            var level = LevelMigration.Snapshot(LevelAuthoring.CreateBlank(10, 5));
            level.Id = "mechanic-test";
            level.Elements = new[]
            {
                new ElementInstance("player", "player", new GridPos(1, 2)),
                new ElementInstance("box", sliding ? "sliding-box" : "box", new GridPos(2, 2)),
                new ElementInstance("goal", "goal", new GridPos(8, 2))
            };
            LevelMigration.SyncLegacy(level); return level;
        }
        private static ElementInstance Plate(string id, int x, int y, params string[] doors) => new ElementInstance(id, "pressure-plate", new GridPos(x, y),
            new[] { new ParameterValue { Key = "targetDoors", Kind = ParameterKind.References, StringValues = doors } });
        private static void Add(LevelDefinition level, params ElementInstance[] values) { level.Elements = level.Elements.Concat(values).ToArray(); LevelMigration.SyncLegacy(level); }
        private static ElementState Element(BoardState state, string id) => state.Elements.Single(e => e.Id == id);
        private static string StateKey(BoardState state) => state.Steps + "/" + state.Pushes + "/" + state.IsWon + "/" + string.Join(";", state.Elements.Select(e => e.Id + e.TypeId + e.Position + e.Active + e.OnGoal));

        [Test]
        public void SlidingIsOneTurnWithSimultaneousFirstMoveAndPerCellFrames()
        {
            var session = new GameSession(Room()); var initial = session.State;
            var move = session.TryMove(Direction.Right);
            Assert.That(move.Succeeded, Is.True); Assert.That(move.Won, Is.True);
            Assert.That(move.Frames, Has.Count.EqualTo(6));
            Assert.That(move.Frames[0].Moves.Select(e => e.Id), Is.EquivalentTo(new[] { "player", "box" }));
            Assert.That(move.Frames.Skip(1).All(frame => frame.Moves.Count == 1 && frame.Moves[0].Id == "box"), Is.True);
            Assert.That(move.Frames.Take(5).All(frame => !frame.After.IsWon), Is.True);
            Assert.That(move.FinalState.Player, Is.EqualTo(new GridPos(2, 2)));
            Assert.That(move.FinalState.Steps, Is.EqualTo(1)); Assert.That(move.FinalState.Pushes, Is.EqualTo(1));
            Assert.That(session.Undo(), Is.True); Assert.That(StateKey(session.State), Is.EqualTo(StateKey(initial))); Assert.That(session.CanUndo, Is.False);
            Assert.That(session.TryMove(Direction.Right).Won, Is.True); session.Restart(); Assert.That(StateKey(session.State), Is.EqualTo(StateKey(initial)));
        }

        [Test]
        public void SlidingPlateDoorTransitionsUseStableSubstepStateAndCloseAfterLeaving()
        {
            var level = Room();
            Add(level, Plate("plate", 3, 2, "near", "far"), new ElementInstance("near", "door", new GridPos(4, 2)), new ElementInstance("far", "door", new GridPos(6, 2)));
            var session = new GameSession(level); var before = StateKey(session.State);
            var move = session.TryMove(Direction.Right);
            Assert.That(move.Succeeded, Is.True); Assert.That(move.Won, Is.False);
            Assert.That(move.Frames, Has.Count.EqualTo(3)); Assert.That(move.BoxTo, Is.EqualTo(new GridPos(5, 2)));
            Assert.That(Element(move.Frames[0].After, "plate").Active, Is.True);
            Assert.That(Element(move.Frames[0].After, "near").Active, Is.True); Assert.That(Element(move.Frames[0].After, "far").Active, Is.True);
            Assert.That(Element(move.Frames[1].After, "plate").Active, Is.False);
            Assert.That(Element(move.Frames[1].After, "near").Active, Is.True); Assert.That(Element(move.Frames[1].After, "far").Active, Is.False);
            Assert.That(Element(move.FinalState, "near").Active, Is.False);
            Assert.That(move.Frames.Any(frame => frame.Changes.Any(change => change.Id == "near")), Is.True);
            session.Undo(); Assert.That(StateKey(session.State), Is.EqualTo(before));
        }

        [Test]
        public void InitialOccupancyOpensDoorsAndPlayerReplacingPushedBoxKeepsPlatePressed()
        {
            var level = Room(false);
            Add(level, Plate("plate", 2, 2, "linked"), new ElementInstance("linked", "door", new GridPos(7, 2)), new ElementInstance("occupied", "door", new GridPos(1, 2)));
            var session = new GameSession(level);
            Assert.That(Element(session.State, "linked").Active, Is.True); Assert.That(Element(session.State, "occupied").Active, Is.True);
            var move = session.TryMove(Direction.Right);
            Assert.That(Element(move.FinalState, "plate").Active, Is.True); Assert.That(Element(move.FinalState, "linked").Active, Is.True);
            Assert.That(Element(move.FinalState, "occupied").Active, Is.False);
            Assert.That(move.Frames[0].Changes.Any(change => change.Id == "plate"), Is.False, "Replacing a box with the player must not flicker the plate.");
        }

        [Test]
        public void MultiplePlatesUseOrAndAnUnlinkedEmptyDoorStartsClosed()
        {
            var level = Room(false);
            Add(level, Plate("first", 1, 2, "door"), Plate("second", 2, 2, "door"), new ElementInstance("door", "door", new GridPos(7, 2)), new ElementInstance("unlinked", "door", new GridPos(7, 3)));
            var session = new GameSession(level);
            Assert.That(Element(session.State, "door").Active, Is.True); Assert.That(Element(session.State, "unlinked").Active, Is.False);
            session.TryMove(Direction.Up);
            Assert.That(Element(session.State, "first").Active, Is.False); Assert.That(Element(session.State, "second").Active, Is.True); Assert.That(Element(session.State, "door").Active, Is.True);
        }

        [Test]
        public void PassingOverGoalDoesNotWinAndClosedDoorStopsSlideWithoutCancelingTurn()
        {
            var level = Room(); level.Elements.Single(e => e.Id == "goal").Position = new GridPos(3, 2);
            Add(level, new ElementInstance("door", "door", new GridPos(6, 2)));
            var session = new GameSession(level); var move = session.TryMove(Direction.Right);
            Assert.That(move.Succeeded, Is.True); Assert.That(move.BoxTo, Is.EqualTo(new GridPos(5, 2))); Assert.That(move.Won, Is.False);
            Assert.That(move.Frames[0].After.GoalsPlaced, Is.EqualTo(1)); Assert.That(move.Frames.All(frame => !frame.After.IsWon), Is.True);
            Assert.That(session.CanUndo, Is.True);
        }

        [Test]
        public void BlockedInitialPushAndSubstepLimitNeverPublishPartialFramesOrHistory()
        {
            var level = Room(); Add(level, new ElementInstance("door", "door", new GridPos(3, 2)));
            var blocked = new GameSession(level); string before = StateKey(blocked.State);
            var move = blocked.TryMove(Direction.Right);
            Assert.That(move.Succeeded, Is.False); Assert.That(move.Frames, Is.Empty); Assert.That(blocked.CanUndo, Is.False); Assert.That(StateKey(blocked.State), Is.EqualTo(before));
            var limited = new GameSession(Room(), null, 2); before = StateKey(limited.State); move = limited.TryMove(Direction.Right);
            Assert.That(move.Succeeded, Is.False); Assert.That(move.Frames, Is.Empty); Assert.That(limited.CanUndo, Is.False);
            Assert.That(move.Trace, Does.Contain("子步")); Assert.That(StateKey(limited.State), Is.EqualTo(before));
        }

        [Test]
        public void IdenticalInputsProduceIdenticalStatesFramesAndTraces()
        {
            var first = new GameSession(Room()); var second = new GameSession(Room());
            foreach (var direction in new[] { Direction.Up, Direction.Down, Direction.Right })
            {
                var a = first.TryMove(direction); var b = second.TryMove(direction);
                Assert.That(a.Trace, Is.EqualTo(b.Trace)); Assert.That(a.Frames.Count, Is.EqualTo(b.Frames.Count));
                Assert.That(StateKey(a.FinalState), Is.EqualTo(StateKey(b.FinalState)));
            }
        }

        [Test]
        public void PreviewKeepsIncompleteAndUnknownRecordsWithoutMutatingDraft()
        {
            var level = Room(); level.Elements = new[] { new ElementInstance("unknown", "future-widget", new GridPos(3, 2), new[] { new ParameterValue { Key = "future", Kind = (ParameterKind)123, RawValue = "{opaque:true}" } }) };
            var copy = level.DeepClone(); var state = GameSession.Preview(level);
            Assert.That(state.Elements.Single().TypeId, Is.EqualTo("future-widget")); Assert.That(state.Steps, Is.Zero);
            Assert.That(LevelValidator.Validate(level), Is.Not.Empty); Assert.That(LevelAuthoring.LayoutEquals(level, copy), Is.True);
            Assert.That(level.Elements[0].Overrides[0].RawValue, Is.EqualTo("{opaque:true}"));
        }

        [Test]
        public void LegacyMigrationIsDeterministicIdempotentAndDoesNotChangeGameplayVersion()
        {
            var old = LevelAuthoring.CreateBlank(8, 8); old.HasPlayer = true; old.PlayerStart = new GridPos(1, 1);
            old.Boxes = new[] { new GridPos(2, 1) }; old.Goals = new[] { new GridPos(6, 1) }; old.LayoutVersion = 7;
            var copy = old.DeepClone(); var upgraded = LevelMigration.Snapshot(old); var repeated = LevelMigration.Snapshot(copy);
            Assert.That(upgraded.SchemaVersion, Is.EqualTo(1)); Assert.That(upgraded.LayoutVersion, Is.EqualTo(7)); Assert.That(upgraded.Id, Is.EqualTo(old.Id));
            Assert.That(upgraded.Elements.Select(e => e.Id), Is.EqualTo(repeated.Elements.Select(e => e.Id)));
            Assert.That(LevelMigration.Upgrade(upgraded), Is.False); Assert.That(old.SchemaVersion, Is.Zero);
            Assert.That(LevelAuthoring.LayoutEquals(old, upgraded), Is.True);
            Assert.That(GameplayFingerprint.Compute(old), Is.EqualTo(GameplayFingerprint.Compute(upgraded)));
            Assert.That(GameplayFingerprint.IsLegacyCompatible(upgraded), Is.True);
        }

        [Test]
        public void SignaturesTrackEffectiveDefaultsTerrainParametersAndRuleVersionsButNotMetadata()
        {
            var level = Room(false); var types = Registry.Types.ToArray(); var box = types.Single(t => t.Id == "box");
            box.Properties = new[] { new PropertyDescriptor { Key = "weight", Name = "重量", Kind = ParameterKind.Integer, Min = 1, Max = 9,
                DefaultValue = new ParameterValue { Key = "weight", Kind = ParameterKind.Integer, IntValue = 1 } } };
            var one = new ElementRegistry(types); var session = new GameSession(level, one); string before = session.GameplaySignature;
            level.Name = "新名称"; level.Description = "新描述"; level.LayoutVersion++;
            box.Name = "改名";
            Assert.That(GameplayFingerprint.Compute(level, new ElementRegistry(types)), Is.EqualTo(before));
            box.Properties[0].DefaultValue.IntValue = 2;
            Assert.That(GameplayFingerprint.Compute(level, new ElementRegistry(types)), Is.Not.EqualTo(before));
            Assert.That(session.GameplaySignature, Is.EqualTo(before), "Running sessions freeze their type defaults.");
            Assert.That(GameplayFingerprint.IsLegacyCompatible(level, one), Is.False);
            var floor = types.Single(t => t.Id == "floor"); floor.Properties = new[] { new PropertyDescriptor { Key = "traction", Name = "牵引", Kind = ParameterKind.Float,
                DefaultValue = new ParameterValue { Key = "traction", Kind = ParameterKind.Float, FloatValue = 1 } } };
            string terrainBefore = GameplayFingerprint.Compute(level, new ElementRegistry(types)); floor.Properties[0].DefaultValue.FloatValue = 2;
            Assert.That(GameplayFingerprint.Compute(level, new ElementRegistry(types)), Is.Not.EqualTo(terrainBefore));
            string rulesBefore = GameplayFingerprint.Compute(level, new ElementRegistry(types)); box.RulesVersion++;
            Assert.That(GameplayFingerprint.Compute(level, new ElementRegistry(types)), Is.Not.EqualTo(rulesBefore));
        }

        [Test]
        public void InvalidCombinationsUnknownParametersAndTemplateReferencesAreRejected()
        {
            var types = Registry.Types.ToArray(); var sliding = types.Single(t => t.Id == "sliding-box"); sliding.Mechanics = new[] { MechanicKind.Slide };
            Assert.That(new ElementRegistry(types).ValidateTypes().Any(issue => issue.Message.Contains("可推动")), Is.True);
            types = Registry.Types.ToArray(); types.Single(t => t.Id == "pressure-plate").Properties[0].DefaultValue.StringValues = new[] { "door-in-level" };
            Assert.That(new ElementRegistry(types).ValidateTypes().Any(issue => issue.Message.Contains("实例")), Is.True);
            var level = Room(); level.Elements[1].Overrides = new[] { new ParameterValue { Key = "future", Kind = ParameterKind.String, StringValue = "keep me" } };
            Assert.That(LevelValidator.Validate(level).Any(issue => issue.Message.Contains("未知属性")), Is.True);
            Assert.That(level.DeepClone().Elements[1].Overrides[0].StringValue, Is.EqualTo("keep me"));
        }

        private sealed class CounterRule : MechanicRule, IStatePatchMechanic
        {
            private readonly int failAt;
            public CounterRule(int failAt = int.MaxValue) { this.failAt = failAt; }
            public override string Id => "test-counter";
            public MechanicPhase Phase => MechanicPhase.Occupancy;
            public override void Validate(ElementTypeSpec type, List<ValidationIssue> issues) => RequireRole(type, ElementRole.Box, issues);
            public IReadOnlyList<ParameterValue> EvaluateState(MechanicContext context, ElementInstance instance)
            {
                int count = (context.State(instance.Id, "visits")?.IntValue ?? 0) + 1;
                if (count == failAt) throw new InvalidOperationException("Injected state resolution failure");
                return new[] { new ParameterValue { Key = "visits", Kind = ParameterKind.Integer, IntValue = count } };
            }
        }
        private static ElementRegistry CounterRegistry(CounterRule rule)
        {
            var types = Registry.Types.ToArray(); var box = types.Single(type => type.Id == "sliding-box");
            box.MechanicIds = box.EffectiveMechanics.Concat(new[] { rule.Id }).ToArray();
            return new ElementRegistry(types, new MechanicRegistry(MechanicRegistry.BuiltIns().Rules.Concat(new[] { rule })));
        }
        [Test]
        public void RegisteredTypedStateParticipatesInFramesUndoRestartAndFailureRollback()
        {
            var session = new GameSession(Room(), CounterRegistry(new CounterRule()));
            Func<BoardState, int> count = state => Element(state, "box").State.Single(value => value.Key == "visits").IntValue;
            Assert.That(count(session.State), Is.EqualTo(1));
            var exposed = Element(session.State, "box").State.Single(value => value.Key == "visits"); exposed.IntValue = 99;
            Assert.That(count(session.State), Is.EqualTo(1), "Exposed state values are detached copies.");
            var move = session.TryMove(Direction.Right);
            Assert.That(move.Succeeded, Is.True); Assert.That(count(session.State), Is.EqualTo(7));
            Assert.That(move.Frames.All(frame => frame.Changes.Any(change => change.Key == "visits")), Is.True);
            Assert.That(move.Trace, Does.Contain("visits"));
            session.Undo(); Assert.That(count(session.State), Is.EqualTo(1));
            session.TryMove(Direction.Right); session.Restart(); Assert.That(count(session.State), Is.EqualTo(1));
            var failing = new GameSession(Room(), CounterRegistry(new CounterRule(3))); string before = StateKey(failing.State);
            move = failing.TryMove(Direction.Right);
            Assert.That(move.Succeeded, Is.False); Assert.That(move.Frames, Is.Empty); Assert.That(failing.CanUndo, Is.False);
            Assert.That(StateKey(failing.State), Is.EqualTo(before)); Assert.That(count(failing.State), Is.EqualTo(1));
            Assert.That(move.Trace, Does.Contain("Injected"));
        }
    }

    public sealed class ElementAuthoringTests
    {
        private static ElementRegistry Registry => ElementRegistry.BuiltIns();
        private static LevelDefinition Draft() => LevelMigration.Snapshot(LevelAuthoring.CreateBlank(12, 10));
        private static ElementInstance At(LevelDefinition level, string type, GridPos position) => level.Elements.Single(e => e.TypeId == type && e.Position == position);
        private static ElementInstance Paint(LevelDefinition level, string type, int x, int y)
        {
            LevelAuthoring.Paint(level, new GridPos(x, y), type, Registry); return At(level, type, new GridPos(x, y));
        }

        [Test]
        public void CopyRemapsInternalLinksPreservesExternalLinksAndMoveKeepsIdentity()
        {
            var draft = Draft(); var plate = Paint(draft, "pressure-plate", 2, 2); var door = Paint(draft, "door", 3, 2); var outside = Paint(draft, "door", 9, 2);
            LevelAuthoring.TrySetParameter(draft, plate.Id, new ParameterValue { Key = "targetDoors", Kind = ParameterKind.References, StringValues = new[] { door.Id, outside.Id } }, Registry, out _);
            Assert.That(LevelAuthoring.TryCopyRegion(draft, new GridRect(2, 2, 2, 1), new GridPos(5, 5), out string error, Registry), Is.True, error);
            var newPlate = At(draft, "pressure-plate", new GridPos(5, 5)); var newDoor = At(draft, "door", new GridPos(6, 5));
            Assert.That(newPlate.Id, Is.Not.EqualTo(plate.Id)); Assert.That(newDoor.Id, Is.Not.EqualTo(door.Id));
            Assert.That(newPlate.Overrides.Single().StringValues, Is.EquivalentTo(new[] { newDoor.Id, outside.Id }));
            Assert.That(LevelAuthoring.TryMoveRegion(draft, new GridRect(5, 5, 2, 1), new GridPos(6, 5), out error, Registry), Is.True, error);
            Assert.That(At(draft, "pressure-plate", new GridPos(6, 5)).Id, Is.EqualTo(newPlate.Id));
            Assert.That(At(draft, "door", new GridPos(7, 5)).Id, Is.EqualTo(newDoor.Id));
        }

        [Test]
        public void CroppingOrErasingTargetsLeavesDanglingReferencesAndUnknownDataIntact()
        {
            var draft = Draft(); var plate = Paint(draft, "pressure-plate", 2, 2); var door = Paint(draft, "door", 9, 2);
            LevelAuthoring.TrySetParameter(draft, plate.Id, new ParameterValue { Key = "targetDoors", Kind = ParameterKind.References, StringValues = new[] { door.Id } }, Registry, out _);
            draft.Elements = draft.Elements.Concat(new[] { new ElementInstance("unknown", "missing-type", new GridPos(3, 3), new[] { new ParameterValue { Key = "future", Kind = (ParameterKind)333, RawValue = "opaque" } }) }).ToArray();
            Assert.That(LevelAuthoring.Resize(draft, 6, 6, Registry), Is.True);
            Assert.That(draft.Elements.Single(e => e.Id == plate.Id).Overrides.Single().StringValues, Is.EqualTo(new[] { door.Id }));
            Assert.That(draft.Elements.Single(e => e.Id == "unknown").Overrides.Single().RawValue, Is.EqualTo("opaque"));
            Assert.That(LevelValidator.Validate(draft).Any(issue => issue.Position == new GridPos(2, 2) && issue.Message.Contains("不存在")), Is.True);
            Assert.That(LevelAuthoring.TryCopyRegion(draft, new GridRect(3, 3, 1, 1), new GridPos(4, 3), out _, Registry), Is.True);
            Assert.That(At(draft, "missing-type", new GridPos(4, 3)).Overrides.Single().RawValue, Is.EqualTo("opaque"));
        }

        [Test]
        public void RegionOperationsSkipPlayerRejectOutOfBoundsAndReplaceEmptyCells()
        {
            var draft = Draft(); var player = Paint(draft, "player", 2, 2); Paint(draft, "sliding-box", 3, 2); Paint(draft, "door", 6, 2);
            Assert.That(LevelAuthoring.TryCopyRegion(draft, new GridRect(2, 2, 2, 1), new GridPos(6, 2), out _, Registry), Is.True);
            Assert.That(draft.Elements.Count(e => e.TypeId == "player"), Is.EqualTo(1)); Assert.That(draft.Elements.Any(e => e.TypeId == "door"), Is.False);
            Assert.That(At(draft, "sliding-box", new GridPos(7, 2)), Is.Not.Null);
            var before = draft.DeepClone(); Assert.That(LevelAuthoring.TryCopyRegion(draft, new GridRect(6, 2, 2, 1), new GridPos(2, 2), out _, Registry), Is.False);
            Assert.That(LevelAuthoring.LayoutEquals(draft, before), Is.True);
            Assert.That(LevelAuthoring.TryMoveRegion(draft, new GridRect(6, 2, 2, 1), new GridPos(11, 9), out _, Registry), Is.False);
            Assert.That(LevelAuthoring.LayoutEquals(draft, before), Is.True);
            Assert.That(LevelAuthoring.TryMoveRegion(draft, new GridRect(2, 2, 2, 1), new GridPos(2, 4), out _, Registry), Is.True);
            Assert.That(draft.Elements.Single(e => e.Id == player.Id).Position, Is.EqualTo(new GridPos(2, 4)));
        }

        [Test]
        public void ShapeAndFillUseTypeIdentityAndPropertyOverridesWithoutDuplicatingPlayer()
        {
            var draft = Draft();
            Assert.That(LevelAuthoring.PaintLine(draft, new GridPos(1, 1), new GridPos(5, 1), "sliding-box", Registry), Is.True);
            Assert.That(draft.Elements.Count(e => e.TypeId == "sliding-box"), Is.EqualTo(5));
            Assert.That(LevelAuthoring.PaintRectangle(draft, new GridPos(4, 4), new GridPos(4, 4), "door", false, Registry), Is.True);
            Assert.That(draft.Elements.Count(e => e.TypeId == "door"), Is.EqualTo(1));
            Assert.That(LevelAuthoring.PaintLine(draft, new GridPos(1, 2), new GridPos(5, 2), "player", Registry), Is.False);
            Paint(draft, "player", 1, 2); Paint(draft, "player", 2, 2); Assert.That(draft.Elements.Count(e => e.TypeId == "player"), Is.EqualTo(1));
            Assert.That(LevelAuthoring.FloodFill(draft, new GridPos(1, 1), "box", Registry), Is.True);
            Assert.That(draft.Elements.Count(e => e.TypeId == "box"), Is.EqualTo(5)); Assert.That(draft.Elements.Any(e => e.TypeId == "door"), Is.True);
        }

        [Test]
        public void ParameterOverrideAndResetPreserveDefaultsAndAreSemanticallyCompared()
        {
            var types = Registry.Types.ToArray(); var box = types.Single(t => t.Id == "box");
            box.Properties = new[] { new PropertyDescriptor { Key = "weight", Name = "重量", Kind = ParameterKind.Integer, Min = 1, Max = 9,
                DefaultValue = new ParameterValue { Key = "weight", Kind = ParameterKind.Integer, IntValue = 2 } } };
            var registry = new ElementRegistry(types); var draft = Draft(); var instance = Paint(draft, "box", 2, 2); var initial = draft.DeepClone();
            Assert.That(LevelAuthoring.TrySetParameter(draft, instance.Id, new ParameterValue { Key = "weight", Kind = ParameterKind.Integer, IntValue = 7 }, registry, out _), Is.True);
            Assert.That(LevelAuthoring.LayoutEquals(draft, initial), Is.False); Assert.That(registry.GetValue(draft.Elements.Single(e => e.Id == instance.Id), "weight").IntValue, Is.EqualTo(7));
            Assert.That(box.Properties[0].DefaultValue.IntValue, Is.EqualTo(2));
            Assert.That(LevelAuthoring.ResetParameter(draft, instance.Id, "weight", registry, out _), Is.True); Assert.That(LevelAuthoring.LayoutEquals(draft, initial), Is.True);
        }
    }
}
