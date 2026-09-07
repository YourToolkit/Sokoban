using System;
using System.Collections.Generic;
using System.Linq;

namespace Sokoban.Core
{
    public enum MechanicPhase { Occupancy = 0, Connections = 1 }

    /// <summary>Registered rules are stateless. A rule may only participate in a declared resolution stage.</summary>
    public abstract class MechanicRule
    {
        public abstract string Id { get; }
        public virtual int Version => 1;
        public abstract void Validate(ElementTypeSpec type, List<ValidationIssue> issues);
        protected static void RequireRole(ElementTypeSpec type, ElementRole role, List<ValidationIssue> issues)
        {
            if (type.Role != role) issues.Add(new ValidationIssue("机制与元素层级不兼容：" + type.Id));
        }
    }
    public interface IPushMechanic { bool AllowsPush { get; } }
    public interface IContinuationMechanic { bool ContinuesAfterPush(ElementInstance instance, ElementRegistry registry); }
    public interface IBlockingMechanic { bool Blocks(bool active); }
    public interface IStateMechanic
    {
        MechanicPhase Phase { get; }
        bool Evaluate(MechanicContext context, ElementInstance instance);
    }
    public interface IStatePatchMechanic
    {
        MechanicPhase Phase { get; }
        IReadOnlyList<ParameterValue> EvaluateState(MechanicContext context, ElementInstance instance);
    }

    /// <summary>Read-only input for a rule. Returning a state value never commits an entire action.</summary>
    public sealed class MechanicContext
    {
        private readonly ElementInstance[] entries;
        private readonly IReadOnlyDictionary<string, bool> active;
        private readonly ElementRegistry registry;
        private readonly IReadOnlyDictionary<string, ParameterValue[]> states;
        internal MechanicContext(ElementInstance[] entries, IReadOnlyDictionary<string, bool> active, ElementRegistry registry,
            IReadOnlyDictionary<string, ParameterValue[]> states)
        { this.entries = entries; this.active = active; this.registry = registry; this.states = states; }
        public bool HasOccupant(GridPos position) => entries.Any(e => e != null && e.Position == position &&
            (registry.Find(e.TypeId)?.Role == ElementRole.Player || registry.Find(e.TypeId)?.Role == ElementRole.Box));
        public bool IsActive(string instanceId) => instanceId != null && active.TryGetValue(instanceId, out bool value) && value;
        public ElementInstance[] WithMechanic(string id) => entries.Where(e => e != null && registry.Find(e.TypeId)?.HasMechanic(id) == true)
            .OrderBy(e => e.Id, StringComparer.Ordinal).Select(e => e.DeepClone()).ToArray();
        public ParameterValue Value(ElementInstance instance, string key) => registry.GetValue(instance, key);
        public ParameterValue State(string instanceId, string key) => instanceId != null && states.TryGetValue(instanceId, out var values)
            ? Array.Find(values, value => value != null && value.Key == key)?.DeepClone() : null;
    }

    public sealed class MechanicRegistry
    {
        private readonly Dictionary<string, MechanicRule> rules;
        public MechanicRegistry(IEnumerable<MechanicRule> rules)
        {
            this.rules = new Dictionary<string, MechanicRule>(StringComparer.Ordinal);
            foreach (var rule in rules ?? Array.Empty<MechanicRule>())
            {
                if (rule == null || string.IsNullOrWhiteSpace(rule.Id) || this.rules.ContainsKey(rule.Id))
                    throw new ArgumentException("机制注册项为空或编号重复。", nameof(rules));
                if (rule.Version < 1) throw new ArgumentException("机制规则版本必须大于零。", nameof(rules));
                if (rule is IStateMechanic state && !Enum.IsDefined(typeof(MechanicPhase), state.Phase))
                    throw new ArgumentException("机制必须选择明确的结算阶段。", nameof(rules));
                if (rule is IStatePatchMechanic patch && !Enum.IsDefined(typeof(MechanicPhase), patch.Phase))
                    throw new ArgumentException("机制必须选择明确的结算阶段。", nameof(rules));
                this.rules.Add(rule.Id, rule);
            }
        }
        public static MechanicRegistry BuiltIns() => new MechanicRegistry(new MechanicRule[]
        { new PushMechanic(), new SlideMechanic(), new PressurePlateMechanic(), new DoorMechanic() });
        public MechanicRegistry Snapshot() => new MechanicRegistry(rules.Values);
        public MechanicRule Find(string id) => id != null && rules.TryGetValue(id, out var value) ? value : null;
        public IReadOnlyList<MechanicRule> Rules => Array.AsReadOnly(rules.Values.OrderBy(rule => rule.Id, StringComparer.Ordinal).ToArray());
    }

    public sealed class PushMechanic : MechanicRule, IPushMechanic
    {
        public override string Id => "push";
        public bool AllowsPush => true;
        public override void Validate(ElementTypeSpec type, List<ValidationIssue> issues) => RequireRole(type, ElementRole.Box, issues);
    }
    public sealed class SlideMechanic : MechanicRule, IContinuationMechanic
    {
        public override string Id => "slide";
        public override void Validate(ElementTypeSpec type, List<ValidationIssue> issues)
        {
            RequireRole(type, ElementRole.Box, issues);
            if (!type.HasMechanic(MechanicKind.Push)) issues.Add(new ValidationIssue("滑行特性需要同时启用可推动：" + type.Id));
        }
        public bool ContinuesAfterPush(ElementInstance instance, ElementRegistry registry) => true;
    }
    public sealed class PressurePlateMechanic : MechanicRule, IStateMechanic
    {
        public override string Id => "pressure-plate";
        public MechanicPhase Phase => MechanicPhase.Occupancy;
        public override void Validate(ElementTypeSpec type, List<ValidationIssue> issues)
        {
            RequireRole(type, ElementRole.Fixture, issues);
            var descriptor = Array.Find(type.Properties ?? Array.Empty<PropertyDescriptor>(), value => value != null && value.Key == "targetDoors");
            if (descriptor == null || descriptor.Kind != ParameterKind.References || descriptor.ReferenceRole != ElementRole.Fixture || descriptor.ReferenceMechanic != MechanicKind.Door)
                issues.Add(new ValidationIssue("压力板需要声明关联门列表属性 targetDoors：" + type.Id));
        }
        public bool Evaluate(MechanicContext context, ElementInstance instance) => context.HasOccupant(instance.Position);
    }
    public sealed class DoorMechanic : MechanicRule, IStateMechanic, IBlockingMechanic
    {
        public override string Id => "door";
        public MechanicPhase Phase => MechanicPhase.Connections;
        public override void Validate(ElementTypeSpec type, List<ValidationIssue> issues) => RequireRole(type, ElementRole.Fixture, issues);
        public bool Blocks(bool active) => !active;
        public bool Evaluate(MechanicContext context, ElementInstance instance)
        {
            if (context.HasOccupant(instance.Position)) return true;
            foreach (var plate in context.WithMechanic("pressure-plate"))
            {
                if (!context.IsActive(plate.Id)) continue;
                var value = context.Value(plate, "targetDoors");
                if (Array.IndexOf(value?.StringValues ?? Array.Empty<string>(), instance.Id) >= 0) return true;
                // Old experimental single links remain readable; new authoring always writes targetDoors.
                if (context.Value(plate, "targetDoor")?.StringValue == instance.Id) return true;
            }
            return false;
        }
    }
}
