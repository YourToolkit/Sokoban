using System;
using System.Collections.Generic;
using System.Linq;

namespace Sokoban.Core
{
    public sealed class ElementState
    {
        public string Id { get; }
        public string TypeId { get; }
        public ElementRole Role { get; }
        public GridPos Position { get; }
        public bool Active { get; }
        public bool OnGoal { get; }
        private readonly ParameterValue[] state;
        public IReadOnlyList<ParameterValue> State => Array.AsReadOnly(Array.ConvertAll(state, p => p.DeepClone()));
        internal ElementState(ElementInstance instance, ElementRole role, bool active, bool onGoal, ParameterValue[] values)
        {
            Id = instance.Id; TypeId = instance.TypeId; Position = instance.Position; Role = role; Active = active; OnGoal = onGoal;
            state = (values ?? Array.Empty<ParameterValue>()).Where(value => value != null && value.Key != "active" && value.Key != "onGoal").Select(value => value.DeepClone())
                .Concat(new[] { new ParameterValue { Key = "active", Kind = ParameterKind.Boolean, BoolValue = active },
                new ParameterValue { Key = "onGoal", Kind = ParameterKind.Boolean, BoolValue = onGoal } }).ToArray();
        }
    }
    public sealed class BoardState
    {
        public GridPos Player { get; }
        public IReadOnlyList<GridPos> Boxes { get; }
        public IReadOnlyList<ElementState> Elements { get; }
        public int Steps { get; }
        public int Pushes { get; }
        public bool IsWon { get; }
        public int GoalsPlaced { get; }
        public int GoalsTotal { get; }
        internal BoardState(ElementState[] elements, int steps, int pushes, bool allowVictory = true)
        {
            Elements = Array.AsReadOnly((ElementState[])elements.Clone());
            Player = elements.FirstOrDefault(e => e.Role == ElementRole.Player)?.Position ?? default(GridPos);
            Boxes = Array.AsReadOnly(elements.Where(e => e.Role == ElementRole.Box).Select(e => e.Position).ToArray());
            Steps = steps; Pushes = pushes;
            GoalsTotal = elements.Count(e => e.Role == ElementRole.Goal);
            GoalsPlaced = elements.Count(e => e.Role == ElementRole.Box && e.OnGoal);
            IsWon = allowVictory && Boxes.Count > 0 && GoalsPlaced == Boxes.Count && GoalsTotal == Boxes.Count;
        }
    }
    public sealed class ElementMove
    {
        public string Id { get; }
        public GridPos From { get; }
        public GridPos To { get; }
        public ElementMove(string id, GridPos from, GridPos to) { Id = id; From = from; To = to; }
    }
    public sealed class ElementChange
    {
        public string Id { get; }
        public string Key { get; }
        public string Before { get; }
        public string After { get; }
        public ElementChange(string id, string key, string before, string after) { Id = id; Key = key; Before = before; After = after; }
    }
    public sealed class MoveFrame
    {
        public BoardState Before { get; }
        public BoardState After { get; internal set; }
        public IReadOnlyList<ElementMove> Moves { get; }
        public IReadOnlyList<ElementChange> Changes { get; }
        public string Trace { get; }
        internal MoveFrame(BoardState before, BoardState after, ElementMove[] moves, string trace)
        {
            Before = before; After = after; Moves = Array.AsReadOnly(moves); Trace = trace;
            var changes = new List<ElementChange>();
            foreach (var current in after.Elements)
            {
                var previous = before.Elements.FirstOrDefault(e => e.Id == current.Id); if (previous == null) continue;
                if (previous.Active != current.Active) changes.Add(new ElementChange(current.Id, "active", previous.Active ? "true" : "false", current.Active ? "true" : "false"));
                if (previous.OnGoal != current.OnGoal) changes.Add(new ElementChange(current.Id, "onGoal", previous.OnGoal ? "true" : "false", current.OnGoal ? "true" : "false"));
                foreach (var value in current.State.Where(value => value.Key != "active" && value.Key != "onGoal"))
                {
                    var beforeValue = previous.State.FirstOrDefault(p => p.Key == value.Key);
                    if (beforeValue == null || beforeValue.Kind != value.Kind || beforeValue.CanonicalValue() != value.CanonicalValue())
                        changes.Add(new ElementChange(current.Id, value.Key, beforeValue?.CanonicalValue() ?? "", value.CanonicalValue()));
                }
            }
            Changes = changes.AsReadOnly();
        }
    }
    public sealed class MoveResult
    {
        public bool Succeeded { get; internal set; }
        public bool Pushed { get; internal set; }
        public bool Won { get; internal set; }
        public GridPos From { get; internal set; }
        public GridPos To { get; internal set; }
        public GridPos? BoxFrom { get; internal set; }
        public GridPos? BoxTo { get; internal set; }
        public string Reason { get; internal set; } = "";
        public string Trace { get; internal set; } = "";
        public BoardState FinalState { get; internal set; }
        public IReadOnlyList<MoveFrame> Frames { get; internal set; } = Array.AsReadOnly(Array.Empty<MoveFrame>());
    }

    /// <summary>One input is one atomic turn. Rendering observes committed frames, never drives resolution.</summary>
    public sealed class GameSession
    {
        private sealed class Snapshot
        {
            public ElementInstance[] Entries;
            public Dictionary<string, bool> Active;
            public Dictionary<string, ParameterValue[]> States;
            public int Steps;
            public int Pushes;
            public Snapshot Clone() => new Snapshot
            {
                Entries = Array.ConvertAll(Entries, e => e.DeepClone()), Active = new Dictionary<string, bool>(Active, StringComparer.Ordinal),
                States = States.ToDictionary(pair => pair.Key, pair => Array.ConvertAll(pair.Value, value => value.DeepClone()), StringComparer.Ordinal),
                Steps = Steps, Pushes = Pushes
            };
        }
        private readonly LevelDefinition definition;
        private readonly ElementRegistry registry;
        private readonly MechanicRegistry mechanics;
        private readonly Stack<Snapshot> history = new Stack<Snapshot>();
        private readonly int maxSubsteps;
        private Snapshot current;
        public bool IsWon => State.IsWon;
        public bool CanUndo => history.Count > 0;
        public BoardState State => ToBoardState(current, registry);
        public LevelDefinition Definition => definition.DeepClone();
        public string GameplaySignature { get; }
        public bool LegacyCompatible { get; }

        public GameSession(LevelDefinition level, ElementRegistry registry = null, int maxSubsteps = 4096)
        {
            if (maxSubsteps < 1) throw new ArgumentOutOfRangeException(nameof(maxSubsteps));
            this.registry = (registry ?? ElementRegistry.BuiltIns()).Snapshot();
            var issues = LevelValidator.Validate(level, this.registry);
            if (issues.Count > 0) throw new ArgumentException(issues[0].Message, nameof(level));
            definition = LevelMigration.Snapshot(level, this.registry);
            issues = ElementValidation.Validate(definition, this.registry);
            if (issues.Count > 0) throw new ArgumentException(issues[0].Message, nameof(level));
            LevelMigration.SyncLegacy(definition, this.registry);
            mechanics = this.registry.Mechanics;
            this.maxSubsteps = maxSubsteps;
            GameplaySignature = GameplayFingerprint.Compute(definition, this.registry);
            LegacyCompatible = GameplayFingerprint.IsLegacyCompatible(definition, this.registry);
            Restart();
        }

        public static BoardState Preview(LevelDefinition level, ElementRegistry registry = null)
        {
            registry = (registry ?? ElementRegistry.BuiltIns()).Snapshot();
            var copy = LevelMigration.Snapshot(level, registry);
            var initial = Initial(copy);
            try { Settle(initial, registry, registry.Mechanics); }
            catch (Exception) { initial = Initial(copy); }
            return ToBoardState(initial, registry, false);
        }

        public MoveResult TryMove(Direction direction)
        {
            var offset = Offset(direction);
            var initial = State;
            var result = new MoveResult { From = initial.Player, To = initial.Player, FinalState = initial };
            if (initial.IsWon) { result.Reason = "关卡已通关。"; return result; }
            var next = current.Clone();
            var frames = new List<MoveFrame>();
            try
            {
                var player = next.Entries.First(e => Role(e) == ElementRole.Player);
                var target = player.Position + offset;
                var box = ActorAt(next, target);
                if (Blocked(next, target, box?.Id)) { result.Reason = "前方被墙或关闭的门阻挡。"; return result; }
                if (box != null && (Role(box) != ElementRole.Box || !Rules(box).OfType<IPushMechanic>().Any(rule => rule.AllowsPush)))
                { result.Reason = "前方对象无法推动。"; return result; }
                var firstMoves = new List<ElementMove> { new ElementMove(player.Id, player.Position, target) };
                GridPos? boxFrom = null;
                if (box != null)
                {
                    if (Blocked(next, box.Position + offset)) { result.Reason = "箱子后方需要一个可通行的空格。"; return result; }
                    boxFrom = box.Position;
                    firstMoves.Add(new ElementMove(box.Id, box.Position, box.Position + offset));
                }
                ApplyFrame(next, firstMoves.ToArray(), frames);
                var seen = new HashSet<string>(StringComparer.Ordinal) { StateKey(next) };
                while (box != null && Rules(box).OfType<IContinuationMechanic>().Any(rule => rule.ContinuesAfterPush(box.DeepClone(), registry)))
                {
                    var destination = box.Position + offset;
                    if (Blocked(next, destination)) break;
                    if (frames.Count >= maxSubsteps) throw new InvalidOperationException("行动超过最大结算子步数：" + maxSubsteps);
                    ApplyFrame(next, new[] { new ElementMove(box.Id, box.Position, destination) }, frames);
                    if (!seen.Add(StateKey(next))) throw new InvalidOperationException("行动出现重复状态，已取消本轮结算。");
                }
                next.Steps++; if (box != null) next.Pushes++;
                var final = ToBoardState(next, registry);
                frames[frames.Count - 1].After = final;
                history.Push(current);
                current = next;
                result.Succeeded = true; result.Pushed = box != null; result.Won = final.IsWon;
                result.To = final.Player; result.BoxFrom = boxFrom; result.BoxTo = box?.Position;
                result.FinalState = final; result.Frames = frames.AsReadOnly();
                result.Trace = string.Join("\n", frames.Select(frame => frame.Trace));
                return result;
            }
            catch (Exception error)
            {
                result.Reason = "本次行动无法完成，棋盘已保持原状。";
                result.Trace = error.GetType().Name + ": " + error.Message;
                return result;
            }
        }

        private ElementRole Role(ElementInstance instance) => registry.Find(instance.TypeId)?.Role ?? ElementRole.Fixture;
        private IEnumerable<MechanicRule> Rules(ElementInstance instance) => (registry.Find(instance.TypeId)?.EffectiveMechanics ?? Array.Empty<string>()).Select(id =>
            mechanics.Find(id) ?? throw new InvalidOperationException("没有注册的机制：" + id));
        private ElementInstance ActorAt(Snapshot snapshot, GridPos p) => snapshot.Entries.FirstOrDefault(e => e.Position == p && (Role(e) == ElementRole.Player || Role(e) == ElementRole.Box));
        private bool Blocked(Snapshot snapshot, GridPos position, string ignoreActor = null)
        {
            if (!definition.IsInside(position)) return true;
            var terrain = registry.Find(definition.TerrainTypeIds[position.Y * definition.Width + position.X]);
            if (terrain == null || terrain.Role == ElementRole.Wall) return true;
            foreach (var entry in snapshot.Entries)
            {
                if (entry.Position != position) continue;
                if (entry.Id != ignoreActor && (Role(entry) == ElementRole.Player || Role(entry) == ElementRole.Box)) return true;
                bool active = snapshot.Active.TryGetValue(entry.Id, out bool value) && value;
                if (Rules(entry).OfType<IBlockingMechanic>().Any(rule => rule.Blocks(active))) return true;
            }
            return false;
        }
        private void ApplyFrame(Snapshot snapshot, ElementMove[] moves, List<MoveFrame> frames)
        {
            var before = ToBoardState(snapshot, registry, false);
            if (moves.Select(move => move.To).Distinct().Count() != moves.Length) throw new InvalidOperationException("移动组的落点重叠。");
            foreach (var move in moves) snapshot.Entries.First(e => e.Id == move.Id).Position = move.To;
            Settle(snapshot, registry, mechanics);
            var after = ToBoardState(snapshot, registry, false);
            string trace = "子步 " + (frames.Count + 1) + "：" + string.Join("；", moves.Select(move => move.Id + " " + move.From + " → " + move.To));
            var frame = new MoveFrame(before, after, moves, trace);
            if (frame.Changes.Count > 0) frame = new MoveFrame(before, after, moves, trace + "；" + string.Join("；", frame.Changes.Select(change => change.Id + "." + change.Key + "=" + change.After)));
            frames.Add(frame);
        }
        private static void Settle(Snapshot snapshot, ElementRegistry registry, MechanicRegistry mechanics)
        {
            var context = new MechanicContext(snapshot.Entries, snapshot.Active, registry, snapshot.States);
            foreach (MechanicPhase phase in new[] { MechanicPhase.Occupancy, MechanicPhase.Connections })
            {
                var pending = new Dictionary<string, Dictionary<string, ParameterValue>>(StringComparer.Ordinal);
                foreach (var entry in snapshot.Entries.OrderBy(e => e.Id, StringComparer.Ordinal))
                foreach (string id in registry.Find(entry.TypeId)?.EffectiveMechanics ?? Array.Empty<string>())
                {
                    var mechanism = mechanics.Find(id);
                    if (mechanism is IStateMechanic rule && rule.Phase == phase)
                    {
                        QueueState(pending, entry.Id, new ParameterValue { Key = "active", Kind = ParameterKind.Boolean, BoolValue = rule.Evaluate(context, entry.DeepClone()) });
                    }
                    if (mechanism is IStatePatchMechanic patch && patch.Phase == phase)
                        foreach (var value in patch.EvaluateState(context, entry.DeepClone()) ?? Array.Empty<ParameterValue>()) QueueState(pending, entry.Id, value);
                }
                foreach (var item in pending)
                {
                    var values = snapshot.States.TryGetValue(item.Key, out var previous) ? previous.ToDictionary(value => value.Key, value => value.DeepClone(), StringComparer.Ordinal)
                        : new Dictionary<string, ParameterValue>(StringComparer.Ordinal);
                    foreach (var value in item.Value.Values) values[value.Key] = value.DeepClone();
                    snapshot.States[item.Key] = values.Values.OrderBy(value => value.Key, StringComparer.Ordinal).ToArray();
                    if (item.Value.TryGetValue("active", out var active)) snapshot.Active[item.Key] = active.BoolValue;
                }
            }
        }
        private static void QueueState(Dictionary<string, Dictionary<string, ParameterValue>> pending, string instanceId, ParameterValue value)
        {
            if (value == null || string.IsNullOrWhiteSpace(value.Key) || value.Key == "onGoal" || !Enum.IsDefined(typeof(ParameterKind), value.Kind))
                throw new InvalidOperationException("机制返回了无效或保留的动态状态字段。");
            if (value.Key == "active" && value.Kind != ParameterKind.Boolean) throw new InvalidOperationException("激活状态必须为布尔值。");
            if (!pending.TryGetValue(instanceId, out var values)) { values = new Dictionary<string, ParameterValue>(StringComparer.Ordinal); pending.Add(instanceId, values); }
            if (values.ContainsKey(value.Key)) throw new InvalidOperationException("同一阶段不能有多个机制写入同一状态：" + instanceId + "." + value.Key);
            values.Add(value.Key, value.DeepClone());
        }
        private static Snapshot Initial(LevelDefinition level) => new Snapshot
        {
            Entries = (level?.Elements ?? Array.Empty<ElementInstance>()).Where(e => e != null).Select(e => e.DeepClone()).ToArray(),
            Active = new Dictionary<string, bool>(StringComparer.Ordinal), States = new Dictionary<string, ParameterValue[]>(StringComparer.Ordinal)
        };
        private static BoardState ToBoardState(Snapshot snapshot, ElementRegistry registry, bool allowVictory = true)
        {
            var goals = new HashSet<GridPos>(snapshot.Entries.Where(e => registry.Find(e.TypeId)?.Role == ElementRole.Goal).Select(e => e.Position));
            return new BoardState(snapshot.Entries.Select(e => new ElementState(e, registry.Find(e.TypeId)?.Role ?? ElementRole.Fixture,
                !string.IsNullOrEmpty(e.Id) && snapshot.Active.TryGetValue(e.Id, out bool active) && active,
                registry.Find(e.TypeId)?.Role == ElementRole.Box && goals.Contains(e.Position),
                !string.IsNullOrEmpty(e.Id) && snapshot.States.TryGetValue(e.Id, out var values) ? values : Array.Empty<ParameterValue>())).ToArray(), snapshot.Steps, snapshot.Pushes, allowVictory);
        }
        private static string StateKey(Snapshot snapshot) => string.Join(";", snapshot.Entries.OrderBy(e => e.Id, StringComparer.Ordinal)
            .Select(e => e.Id + ":" + e.Position.X + "," + e.Position.Y + ":" + (snapshot.Active.TryGetValue(e.Id, out bool active) && active ? "1" : "0") + ":" +
                (snapshot.States.TryGetValue(e.Id, out var values) ? string.Join("|", values.Select(value => value.Key + "/" + (int)value.Kind + "=" + value.CanonicalValue())) : "")));
        public bool Undo()
        {
            if (!CanUndo) return false;
            current = history.Pop(); return true;
        }
        public void Restart()
        {
            var initial = Initial(definition); Settle(initial, registry, mechanics);
            current = initial; history.Clear();
        }
        private static GridPos Offset(Direction direction)
        {
            switch (direction)
            {
                case Direction.Up: return new GridPos(0, 1);
                case Direction.Right: return new GridPos(1, 0);
                case Direction.Down: return new GridPos(0, -1);
                case Direction.Left: return new GridPos(-1, 0);
                default: throw new ArgumentOutOfRangeException(nameof(direction));
            }
        }
    }
}
