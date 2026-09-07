using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Sokoban.Core
{
    public enum ElementRole { Floor = 0, Wall = 1, Goal = 2, Player = 3, Box = 4, Fixture = 5 }
    public enum MechanicKind { None = 0, Push = 1, Slide = 2, PressurePlate = 3, Door = 4 }
    public enum ParameterKind { Boolean = 0, Integer = 1, Float = 2, String = 3, Enum = 4, Reference = 5, References = 6 }

    [Serializable]
    public sealed class ParameterValue
    {
        public string Key = "";
        public ParameterKind Kind;
        public bool BoolValue;
        public int IntValue;
        public float FloatValue;
        public string StringValue = "";
        public string[] StringValues = Array.Empty<string>();
        public string RawValue = "";
        public ParameterValue DeepClone() => new ParameterValue
        {
            Key = Key, Kind = Kind, BoolValue = BoolValue, IntValue = IntValue, FloatValue = FloatValue,
            StringValue = StringValue, StringValues = StringValues == null ? null : (string[])StringValues.Clone(), RawValue = RawValue
        };
        public string CanonicalValue()
        {
            switch (Kind)
            {
                case ParameterKind.Boolean: return BoolValue ? "1" : "0";
                case ParameterKind.Integer: return IntValue.ToString(CultureInfo.InvariantCulture);
                case ParameterKind.Float: return FloatValue.ToString("R", CultureInfo.InvariantCulture);
                case ParameterKind.References: return string.Join("|", (StringValues ?? Array.Empty<string>()).OrderBy(value => value, StringComparer.Ordinal).Select(Escape));
                case ParameterKind.String: case ParameterKind.Enum: case ParameterKind.Reference: return Escape(StringValue);
                default: return Escape(RawValue);
            }
        }
        internal static string Escape(string value) => (value ?? "").Length.ToString(CultureInfo.InvariantCulture) + ":" + (value ?? "");
    }

    [Serializable]
    public sealed class PropertyDescriptor
    {
        public string Key = "";
        public string Name = "";
        public ParameterKind Kind;
        public ParameterValue DefaultValue;
        public float Min = float.MinValue;
        public float Max = float.MaxValue;
        public string[] Options = Array.Empty<string>();
        public ElementRole ReferenceRole = ElementRole.Fixture;
        public MechanicKind ReferenceMechanic = MechanicKind.None;
        public PropertyDescriptor DeepClone() => new PropertyDescriptor
        {
            Key = Key, Name = Name, Kind = Kind, DefaultValue = DefaultValue?.DeepClone(), Min = Min, Max = Max,
            Options = Options == null ? null : (string[])Options.Clone(), ReferenceRole = ReferenceRole, ReferenceMechanic = ReferenceMechanic
        };
    }

    [Serializable]
    public sealed class ElementTypeSpec
    {
        public string Id = "";
        public string Name = "";
        public ElementRole Role;
        public MechanicKind[] Mechanics = Array.Empty<MechanicKind>();
        public string[] MechanicIds = Array.Empty<string>();
        public ParameterValue[] Defaults = Array.Empty<ParameterValue>();
        public PropertyDescriptor[] Properties = Array.Empty<PropertyDescriptor>();
        public int RulesVersion = 1;
        public ElementTypeSpec DeepClone() => new ElementTypeSpec
        {
            Id = Id, Name = Name, Role = Role, RulesVersion = RulesVersion,
            Mechanics = Mechanics == null ? null : (MechanicKind[])Mechanics.Clone(),
            MechanicIds = MechanicIds == null ? null : (string[])MechanicIds.Clone(),
            Defaults = Defaults == null ? null : Array.ConvertAll(Defaults, value => value?.DeepClone()),
            Properties = Properties == null ? null : Array.ConvertAll(Properties, value => value?.DeepClone())
        };
        public string[] EffectiveMechanics => MechanicIds != null && MechanicIds.Length > 0
            ? (string[])MechanicIds.Clone() : (Mechanics ?? Array.Empty<MechanicKind>()).Where(value => value != MechanicKind.None).Select(MechanicNames.Id).ToArray();
        public bool HasMechanic(MechanicKind kind) => HasMechanic(MechanicNames.Id(kind));
        public bool HasMechanic(string id) => Array.IndexOf(EffectiveMechanics, id) >= 0;
    }

    [Serializable]
    public sealed class ElementInstance
    {
        public string Id = "";
        public string TypeId = "";
        public GridPos Position;
        public ParameterValue[] Overrides = Array.Empty<ParameterValue>();
        public ElementInstance() { }
        public ElementInstance(string id, string typeId, GridPos position, ParameterValue[] overrides = null)
        { Id = id; TypeId = typeId; Position = position; Overrides = overrides ?? Array.Empty<ParameterValue>(); }
        public ElementInstance DeepClone() => new ElementInstance(Id, TypeId, Position,
            Overrides == null ? null : Array.ConvertAll(Overrides, value => value?.DeepClone()));
    }

    public static class MechanicNames
    {
        public static string Id(MechanicKind kind)
        {
            switch (kind)
            {
                case MechanicKind.None: return "";
                case MechanicKind.Push: return "push";
                case MechanicKind.Slide: return "slide";
                case MechanicKind.PressurePlate: return "pressure-plate";
                case MechanicKind.Door: return "door";
                default: return "unknown-" + (int)kind;
            }
        }
    }

    /// <summary>A detached catalog of explicit, registered mechanics. Names and artwork do not affect rules.</summary>
    public sealed class ElementRegistry
    {
        private readonly ElementTypeSpec[] types;
        private readonly Dictionary<string, ElementTypeSpec> byId;
        private readonly MechanicRegistry mechanics;
        public IReadOnlyList<ElementTypeSpec> Types => Array.AsReadOnly(Array.ConvertAll(types, value => value.DeepClone()));
        public MechanicRegistry Mechanics => mechanics.Snapshot();
        public ElementRegistry(IEnumerable<ElementTypeSpec> types = null, MechanicRegistry mechanics = null)
        {
            this.types = (types ?? DefaultTypes()).Where(value => value != null).Select(value => value.DeepClone()).ToArray();
            byId = new Dictionary<string, ElementTypeSpec>(StringComparer.Ordinal);
            foreach (var type in this.types)
                if (!string.IsNullOrWhiteSpace(type.Id) && !byId.ContainsKey(type.Id)) byId.Add(type.Id, type);
            this.mechanics = (mechanics ?? MechanicRegistry.BuiltIns()).Snapshot();
        }
        public static ElementRegistry BuiltIns() => new ElementRegistry();
        public ElementRegistry Snapshot() => new ElementRegistry(types, mechanics);
        public ElementTypeSpec Find(string id) => id != null && byId.TryGetValue(id, out var value) ? value.DeepClone() : null;
        public bool HasMechanic(string typeId, MechanicKind mechanic) => Find(typeId)?.HasMechanic(mechanic) == true;
        public ParameterValue GetValue(ElementInstance instance, string key)
        {
            if (instance == null) return null;
            var value = Array.Find(instance.Overrides ?? Array.Empty<ParameterValue>(), entry => entry != null && entry.Key == key);
            if (value != null) return value.DeepClone();
            var type = Find(instance.TypeId);
            value = Array.Find(type?.Defaults ?? Array.Empty<ParameterValue>(), entry => entry != null && entry.Key == key);
            if (value != null) return value.DeepClone();
            return Array.Find(type?.Properties ?? Array.Empty<PropertyDescriptor>(), entry => entry != null && entry.Key == key)?.DefaultValue?.DeepClone();
        }
        public IReadOnlyList<ValidationIssue> ValidateTypes()
        {
            var issues = new List<ValidationIssue>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var type in types)
            {
                if (string.IsNullOrWhiteSpace(type.Id) || !seen.Add(type.Id)) issues.Add(new ValidationIssue("元素类型编号为空或重复：" + type.Id));
                if (!Enum.IsDefined(typeof(ElementRole), type.Role)) issues.Add(new ValidationIssue("未知元素层级：" + type.Id));
                if (type.RulesVersion < 1) issues.Add(new ValidationIssue("元素规则版本必须大于零：" + type.Id));
                var mechanicIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (string id in type.EffectiveMechanics)
                {
                    if (!mechanicIds.Add(id ?? "")) issues.Add(new ValidationIssue("元素重复配置了同一机制：" + type.Id));
                    var rule = mechanics.Find(id);
                    if (rule == null) issues.Add(new ValidationIssue("没有注册的机制：" + id));
                    else rule.Validate(type, issues);
                }
                if (type.HasMechanic(MechanicKind.PressurePlate) && type.HasMechanic(MechanicKind.Door))
                    issues.Add(new ValidationIssue("压力板与门不能组合在同一个元素类型中：" + type.Id));
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in type.Properties ?? Array.Empty<PropertyDescriptor>())
                {
                    if (property == null || string.IsNullOrWhiteSpace(property.Key) || !keys.Add(property.Key))
                    { issues.Add(new ValidationIssue("元素属性编号为空或重复：" + type.Id)); continue; }
                    if (!Enum.IsDefined(typeof(ParameterKind), property.Kind)) issues.Add(new ValidationIssue("不支持的属性类型：" + property.Key));
                    if (float.IsNaN(property.Min) || float.IsNaN(property.Max) || property.Min > property.Max) issues.Add(new ValidationIssue("属性范围无效：" + property.Key));
                    if (property.DefaultValue != null)
                    {
                        if (property.DefaultValue.Key != property.Key) issues.Add(new ValidationIssue("属性默认值编号与声明不一致：" + property.Key));
                        ElementValidation.ValidateValue(property.DefaultValue, property, issues, null);
                        if (HasReference(property.DefaultValue)) issues.Add(new ValidationIssue("关卡对象引用只能设置在实例上：" + property.Key));
                    }
                }
                var defaults = new HashSet<string>(StringComparer.Ordinal);
                foreach (var value in type.Defaults ?? Array.Empty<ParameterValue>())
                {
                    if (value == null || !defaults.Add(value.Key ?? "")) { issues.Add(new ValidationIssue("元素默认属性为空或重复：" + type.Id)); continue; }
                    var descriptor = Array.Find(type.Properties ?? Array.Empty<PropertyDescriptor>(), p => p != null && p.Key == value.Key);
                    if (descriptor == null) issues.Add(new ValidationIssue("未声明的默认属性：" + value.Key));
                    else
                    {
                        ElementValidation.ValidateValue(value, descriptor, issues, null);
                        if (HasReference(value))
                            issues.Add(new ValidationIssue("关卡对象引用只能设置在实例上：" + value.Key));
                    }
                }
            }
            return issues;
        }
        private static bool HasReference(ParameterValue value) => value.Kind == ParameterKind.Reference && !string.IsNullOrEmpty(value.StringValue) ||
            value.Kind == ParameterKind.References && (value.StringValues?.Length ?? 0) > 0;
        private static IEnumerable<ElementTypeSpec> DefaultTypes()
        {
            yield return Basic("floor", "地板", ElementRole.Floor);
            yield return Basic("wall", "墙", ElementRole.Wall);
            yield return Basic("goal", "目标", ElementRole.Goal);
            yield return Basic("player", "玩家", ElementRole.Player);
            yield return Basic("box", "箱子", ElementRole.Box, MechanicKind.Push);
            yield return Basic("sliding-box", "滑行箱", ElementRole.Box, MechanicKind.Push, MechanicKind.Slide);
            var plate = Basic("pressure-plate", "压力板", ElementRole.Fixture, MechanicKind.PressurePlate);
            plate.Properties = new[] { new PropertyDescriptor
            {
                Key = "targetDoors", Name = "关联门", Kind = ParameterKind.References,
                ReferenceRole = ElementRole.Fixture, ReferenceMechanic = MechanicKind.Door,
                DefaultValue = new ParameterValue { Key = "targetDoors", Kind = ParameterKind.References }
            } };
            yield return plate;
            yield return Basic("door", "门", ElementRole.Fixture, MechanicKind.Door);
        }
        private static ElementTypeSpec Basic(string id, string name, ElementRole role, params MechanicKind[] mechanisms) =>
            new ElementTypeSpec { Id = id, Name = name, Role = role, Mechanics = mechanisms };
    }
}
