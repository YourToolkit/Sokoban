using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Sokoban.Core
{
    public static class LevelMigration
    {
        public const int CurrentSchemaVersion = 1;
        public static LevelDefinition Snapshot(LevelDefinition level, ElementRegistry registry = null)
        {
            if (level == null) return null;
            var copy = level.DeepClone(); Upgrade(copy, registry); return copy;
        }
        public static bool Upgrade(LevelDefinition level, ElementRegistry registry = null)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            if (level.SchemaVersion != 0) return false;
            level.TerrainTypeIds = level.Cells == null ? null : Array.ConvertAll(level.Cells,
                cell => cell == CellType.Floor ? "floor" : cell == CellType.Wall ? "wall" : "unknown-terrain-" + (int)cell);
            var entries = new List<ElementInstance>();
            if (level.HasPlayer) entries.Add(new ElementInstance("legacy-player", "player", level.PlayerStart));
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            AddLegacy(entries, counts, "goal", level.Goals);
            AddLegacy(entries, counts, "box", level.Boxes);
            level.Elements = entries.ToArray();
            level.SchemaVersion = CurrentSchemaVersion;
            return true;
        }
        private static void AddLegacy(List<ElementInstance> entries, Dictionary<string, int> counts, string type, GridPos[] positions)
        {
            foreach (var p in positions ?? Array.Empty<GridPos>())
            {
                string basis = "legacy-" + type + "-" + p.X + "-" + p.Y;
                counts.TryGetValue(basis, out int count); counts[basis] = count + 1;
                entries.Add(new ElementInstance(basis + "-" + count, type, p));
            }
        }
        public static void SyncLegacy(LevelDefinition level, ElementRegistry registry = null)
        {
            if (level == null || level.SchemaVersion == 0) return;
            registry = registry ?? ElementRegistry.BuiltIns();
            level.Cells = level.TerrainTypeIds == null ? null : Array.ConvertAll(level.TerrainTypeIds,
                id => registry.Find(id)?.Role == ElementRole.Wall ? CellType.Wall : CellType.Floor);
            var elements = (level.Elements ?? Array.Empty<ElementInstance>()).Where(value => value != null).ToArray();
            var player = elements.FirstOrDefault(value => registry.Find(value.TypeId)?.Role == ElementRole.Player);
            level.HasPlayer = player != null;
            level.PlayerStart = player?.Position ?? default(GridPos);
            level.Boxes = elements.Where(value => registry.Find(value.TypeId)?.Role == ElementRole.Box).Select(value => value.Position).ToArray();
            level.Goals = elements.Where(value => registry.Find(value.TypeId)?.Role == ElementRole.Goal).Select(value => value.Position).ToArray();
        }
    }

    public static class ElementValidation
    {
        public static List<ValidationIssue> Validate(LevelDefinition source, ElementRegistry registry)
        {
            var issues = new List<ValidationIssue>();
            if (source == null) { issues.Add(new ValidationIssue("没有关卡数据。")); return issues; }
            var level = LevelMigration.Snapshot(source, registry);
            if (level.SchemaVersion != LevelMigration.CurrentSchemaVersion) issues.Add(new ValidationIssue("不支持的关卡数据版本，请使用兼容版本的编辑器。"));
            if (string.IsNullOrWhiteSpace(level.Id)) issues.Add(new ValidationIssue("关卡缺少唯一编号。"));
            if (string.IsNullOrWhiteSpace(level.Name)) issues.Add(new ValidationIssue("请输入关卡名称。"));
            if (level.LayoutVersion < 0) issues.Add(new ValidationIssue("布局版本不能小于零。"));
            if (level.Width < 2 || level.Height < 2 || level.Width > 32 || level.Height > 32)
            { issues.Add(new ValidationIssue("地图宽高必须在 2 至 32 格之间。")); return issues; }
            issues.AddRange(registry.ValidateTypes());
            if (level.TerrainTypeIds == null || level.TerrainTypeIds.Length != level.Width * level.Height)
            { issues.Add(new ValidationIssue("地形数量与地图尺寸不一致。")); return issues; }
            for (int i = 0; i < level.TerrainTypeIds.Length; i++)
            {
                var type = registry.Find(level.TerrainTypeIds[i]);
                if (type == null || type.Role != ElementRole.Floor && type.Role != ElementRole.Wall)
                    issues.Add(new ValidationIssue("未知或无效的地形类型：" + level.TerrainTypeIds[i], new GridPos(i % level.Width, i / level.Width)));
            }
            var ids = new Dictionary<string, ElementInstance>(StringComparer.Ordinal);
            var actors = new HashSet<GridPos>(); var fixtures = new HashSet<GridPos>(); var goals = new HashSet<GridPos>();
            int playerCount = 0, boxCount = 0, goalCount = 0;
            foreach (var instance in level.Elements ?? Array.Empty<ElementInstance>())
            {
                if (instance == null) { issues.Add(new ValidationIssue("关卡包含空元素记录。")); continue; }
                if (string.IsNullOrWhiteSpace(instance.Id) || ids.ContainsKey(instance.Id)) issues.Add(new ValidationIssue("元素实例编号为空或重复。", instance.Position));
                else ids.Add(instance.Id, instance);
                var type = registry.Find(instance.TypeId);
                if (type == null) { issues.Add(new ValidationIssue("未知元素类型：" + instance.TypeId, instance.Position)); continue; }
                if (type.Role == ElementRole.Floor || type.Role == ElementRole.Wall) issues.Add(new ValidationIssue("地形应存放在地形层。", instance.Position));
                if (!level.IsInside(instance.Position)) issues.Add(new ValidationIssue("元素位于地图外。", instance.Position));
                else if (registry.Find(level.TerrainTypeIds[instance.Position.Y * level.Width + instance.Position.X])?.Role == ElementRole.Wall)
                    issues.Add(new ValidationIssue("元素位于墙内。", instance.Position));
                if (type.Role == ElementRole.Player || type.Role == ElementRole.Box)
                    if (!actors.Add(instance.Position)) issues.Add(new ValidationIssue("同一格不能重叠玩家或箱子。", instance.Position));
                if (type.Role == ElementRole.Fixture && !fixtures.Add(instance.Position)) issues.Add(new ValidationIssue("同一格不能重叠多个机关。", instance.Position));
                if (type.Role == ElementRole.Goal && !goals.Add(instance.Position)) issues.Add(new ValidationIssue("同一格内有多个目标点。", instance.Position));
                if (type.Role == ElementRole.Player) playerCount++;
                if (type.Role == ElementRole.Box) boxCount++;
                if (type.Role == ElementRole.Goal) goalCount++;
                var overrides = new HashSet<string>(StringComparer.Ordinal);
                foreach (var value in instance.Overrides ?? Array.Empty<ParameterValue>())
                {
                    if (value == null || !overrides.Add(value.Key ?? "")) { issues.Add(new ValidationIssue("实例属性为空或重复。", instance.Position)); continue; }
                    var property = Array.Find(type.Properties ?? Array.Empty<PropertyDescriptor>(), p => p != null && p.Key == value.Key);
                    if (property == null) issues.Add(new ValidationIssue("未知属性已保留，请恢复对应元素配置：" + value.Key, instance.Position));
                    else ValidateValue(value, property, issues, instance.Position);
                }
            }
            if (playerCount != 1) issues.Add(new ValidationIssue("请放置且只放置一个玩家起点。"));
            if (boxCount == 0) issues.Add(new ValidationIssue("请至少放置一个箱子。"));
            if (goalCount == 0) issues.Add(new ValidationIssue("请至少放置一个目标点。"));
            if (boxCount != goalCount) issues.Add(new ValidationIssue("箱子和目标点的数量必须相同。"));
            foreach (var instance in level.Elements ?? Array.Empty<ElementInstance>())
            {
                if (instance == null) continue;
                var type = registry.Find(instance.TypeId); if (type == null) continue;
                foreach (var property in type.Properties ?? Array.Empty<PropertyDescriptor>())
                {
                    if (property == null || property.Kind != ParameterKind.Reference && property.Kind != ParameterKind.References) continue;
                    var value = registry.GetValue(instance, property.Key);
                    var links = value == null ? Array.Empty<string>() : property.Kind == ParameterKind.Reference
                        ? (string.IsNullOrEmpty(value.StringValue) ? Array.Empty<string>() : new[] { value.StringValue }) : value.StringValues ?? Array.Empty<string>();
                    foreach (string id in links)
                    {
                        if (string.IsNullOrWhiteSpace(id) || !ids.TryGetValue(id, out var target))
                        { issues.Add(new ValidationIssue("关联对象不存在，原引用已保留：" + id, instance.Position)); continue; }
                        var targetType = registry.Find(target.TypeId);
                        if (targetType == null || targetType.Role != property.ReferenceRole || property.ReferenceMechanic != MechanicKind.None && !targetType.HasMechanic(property.ReferenceMechanic))
                            issues.Add(new ValidationIssue("关联对象的类型不符合属性要求：" + property.Name, instance.Position));
                    }
                }
            }
            return issues;
        }

        internal static void ValidateValue(ParameterValue value, PropertyDescriptor property, List<ValidationIssue> issues, GridPos? position)
        {
            if (value.Kind != property.Kind) { issues.Add(new ValidationIssue("属性值类型不正确：" + property.Name, position)); return; }
            if (value.Kind == ParameterKind.Integer && (value.IntValue < property.Min || value.IntValue > property.Max) ||
                value.Kind == ParameterKind.Float && (float.IsNaN(value.FloatValue) || float.IsInfinity(value.FloatValue) || value.FloatValue < property.Min || value.FloatValue > property.Max))
                issues.Add(new ValidationIssue("属性值超出允许范围：" + property.Name, position));
            if (value.Kind == ParameterKind.Enum && Array.IndexOf(property.Options ?? Array.Empty<string>(), value.StringValue) < 0)
                issues.Add(new ValidationIssue("属性枚举值不在可选项中：" + property.Name, position));
            if (value.Kind == ParameterKind.References && (value.StringValues ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).Count() != (value.StringValues?.Length ?? 0))
                issues.Add(new ValidationIssue("同一关联对象不能重复选择：" + property.Name, position));
        }
    }

    public static class GameplayFingerprint
    {
        public static string Compute(LevelDefinition source, ElementRegistry registry = null)
        {
            registry = registry ?? ElementRegistry.BuiltIns();
            var level = LevelMigration.Snapshot(source, registry);
            if (level == null) return "";
            var text = new StringBuilder("grid-turn/1;all-boxes-on-goals/1;");
            text.Append(level.Width).Append('x').Append(level.Height).Append(';');
            foreach (string id in level.TerrainTypeIds ?? Array.Empty<string>())
            {
                var type = registry.Find(id); AppendType(text, type, id, registry);
                foreach (var property in (type?.Properties ?? Array.Empty<PropertyDescriptor>()).Where(p => p != null).OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    var value = registry.GetValue(new ElementInstance("", id, default(GridPos)), property.Key);
                    if (value != null) text.Append(ParameterValue.Escape(property.Key)).Append('/').Append((int)value.Kind).Append('=').Append(value.CanonicalValue()).Append(';');
                }
            }
            var entries = (level.Elements ?? Array.Empty<ElementInstance>()).Where(e => e != null)
                .OrderBy(e => e.Position.Y).ThenBy(e => e.Position.X).ThenBy(e => e.TypeId, StringComparer.Ordinal).ThenBy(e => e.Id, StringComparer.Ordinal).ToArray();
            var ids = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Length; i++) if (!string.IsNullOrEmpty(entries[i].Id) && !ids.ContainsKey(entries[i].Id)) ids.Add(entries[i].Id, "e" + i);
            foreach (var entry in entries)
            {
                var type = registry.Find(entry.TypeId);
                text.Append(entry.Position.X).Append(',').Append(entry.Position.Y).Append(':'); AppendType(text, type, entry.TypeId, registry);
                var keys = (type?.Properties ?? Array.Empty<PropertyDescriptor>()).Where(p => p != null).Select(p => p.Key)
                    .Concat((entry.Overrides ?? Array.Empty<ParameterValue>()).Where(p => p != null).Select(p => p.Key)).Distinct().OrderBy(key => key, StringComparer.Ordinal);
                foreach (string key in keys)
                {
                    var value = registry.GetValue(entry, key); if (value == null) continue;
                    if (value.Kind == ParameterKind.Reference) value.StringValue = MapReference(value.StringValue, ids);
                    if (value.Kind == ParameterKind.References) value.StringValues = Array.ConvertAll(value.StringValues ?? Array.Empty<string>(), id => MapReference(id, ids));
                    text.Append(ParameterValue.Escape(key)).Append('/').Append((int)value.Kind).Append('=').Append(value.CanonicalValue()).Append(';');
                }
            }
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-", "").ToLowerInvariant();
        }
        private static string MapReference(string id, Dictionary<string, string> ids) => id != null && ids.TryGetValue(id, out string value) ? value : "missing:" + id;
        private static void AppendType(StringBuilder text, ElementTypeSpec type, string id, ElementRegistry registry)
        {
            text.Append(ParameterValue.Escape(id)).Append('/').Append(type == null ? -1 : (int)type.Role).Append('/').Append(type?.RulesVersion ?? 0).Append('[');
            foreach (string mechanism in (type?.EffectiveMechanics ?? Array.Empty<string>()).OrderBy(value => value, StringComparer.Ordinal))
                text.Append(ParameterValue.Escape(mechanism)).Append('/').Append(registry.Mechanics.Find(mechanism)?.Version ?? 0).Append(',');
            text.Append("]; ");
        }
        public static bool IsLegacyCompatible(LevelDefinition source, ElementRegistry registry = null)
        {
            registry = registry ?? ElementRegistry.BuiltIns(); var level = LevelMigration.Snapshot(source, registry);
            if (level == null || ElementValidation.Validate(level, registry).Count > 0) return false;
            foreach (string id in (level.TerrainTypeIds ?? Array.Empty<string>()).Concat((level.Elements ?? Array.Empty<ElementInstance>()).Where(e => e != null).Select(e => e.TypeId)).Distinct())
            {
                var type = registry.Find(id); if (type == null || type.RulesVersion != 1 || type.Role == ElementRole.Fixture) return false;
                if ((type.Properties?.Length ?? 0) > 0 || (type.Defaults?.Length ?? 0) > 0) return false;
                var expected = type.Role == ElementRole.Box ? new[] { "push" } : Array.Empty<string>();
                if (!type.EffectiveMechanics.OrderBy(value => value, StringComparer.Ordinal).SequenceEqual(expected)) return false;
                foreach (string mechanic in expected) if (registry.Mechanics.Find(mechanic)?.Version != 1) return false;
            }
            return true;
        }
    }
}
