using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Sokoban.Core
{
    public static partial class LevelAuthoring
    {
        private static string BrushType(LevelBrush brush)
        {
            switch (brush)
            {
                case LevelBrush.Floor: return "floor"; case LevelBrush.Wall: return "wall";
                case LevelBrush.Goal: return "goal"; case LevelBrush.Player: return "player";
                case LevelBrush.Box: return "box"; case LevelBrush.Erase: return "erase";
                default: throw new ArgumentOutOfRangeException(nameof(brush));
            }
        }
        public static bool Paint(LevelDefinition draft, GridPos cell, string typeId, ElementRegistry registry)
        {
            if (!CanEdit(draft) || !draft.IsInside(cell)) return false;
            return PaintElements(draft, new[] { cell }, typeId, registry ?? ElementRegistry.BuiltIns());
        }
        public static bool PaintLine(LevelDefinition draft, GridPos from, GridPos to, string typeId, ElementRegistry registry)
        {
            registry = registry ?? ElementRegistry.BuiltIns();
            if (!CanEdit(draft) || !draft.IsInside(from) || !draft.IsInside(to) || registry.Find(typeId)?.Role == ElementRole.Player) return false;
            return PaintElements(draft, LineCells(from, to), typeId, registry);
        }
        public static bool PaintRectangle(LevelDefinition draft, GridPos from, GridPos to, string typeId, bool filled, ElementRegistry registry)
        {
            registry = registry ?? ElementRegistry.BuiltIns();
            if (!CanEdit(draft) || !draft.IsInside(from) || !draft.IsInside(to) || registry.Find(typeId)?.Role == ElementRole.Player) return false;
            var rect = GridRect.FromPoints(from, to); var cells = new List<GridPos>();
            for (int y = rect.MinY; y <= rect.MaxY; y++) for (int x = rect.MinX; x <= rect.MaxX; x++)
                if (filled || x == rect.MinX || x == rect.MaxX || y == rect.MinY || y == rect.MaxY) cells.Add(new GridPos(x, y));
            return PaintElements(draft, cells, typeId, registry);
        }
        public static bool FloodFill(LevelDefinition draft, GridPos start, string typeId, ElementRegistry registry)
        {
            registry = registry ?? ElementRegistry.BuiltIns();
            if (!CanEdit(draft) || !draft.IsInside(start) || registry.Find(typeId)?.Role == ElementRole.Player) return false;
            var source = LevelMigration.Snapshot(draft, registry);
            string match = CellContent(source, start);
            var cells = new List<GridPos>(); var pending = new Queue<GridPos>(); var visited = new HashSet<GridPos>(); pending.Enqueue(start);
            while (pending.Count > 0)
            {
                var p = pending.Dequeue();
                if (!source.IsInside(p) || !visited.Add(p) || CellContent(source, p) != match) continue;
                cells.Add(p); pending.Enqueue(new GridPos(p.X - 1, p.Y)); pending.Enqueue(new GridPos(p.X + 1, p.Y));
                pending.Enqueue(new GridPos(p.X, p.Y - 1)); pending.Enqueue(new GridPos(p.X, p.Y + 1));
            }
            return PaintElements(draft, cells, typeId, registry);
        }
        private static IEnumerable<GridPos> LineCells(GridPos from, GridPos to)
        {
            int x = from.X, y = from.Y, dx = Math.Abs(to.X - from.X), dy = -Math.Abs(to.Y - from.Y);
            int sx = from.X < to.X ? 1 : -1, sy = from.Y < to.Y ? 1 : -1, error = dx + dy;
            while (true)
            {
                yield return new GridPos(x, y); if (x == to.X && y == to.Y) yield break;
                int twice = 2 * error; if (twice >= dy) { error += dy; x += sx; } if (twice <= dx) { error += dx; y += sy; }
            }
        }
        private static bool PaintElements(LevelDefinition draft, IEnumerable<GridPos> cells, string typeId, ElementRegistry registry)
        {
            bool erase = typeId == "erase";
            var type = registry.Find(typeId); if (type == null && !erase) return false;
            var next = LevelMigration.Snapshot(draft, registry); EnsureElementTerrain(next);
            var instances = new List<ElementInstance>(next.Elements ?? Array.Empty<ElementInstance>());
            foreach (var p in cells.Distinct())
            {
                if (!next.IsInside(p)) continue;
                int index = p.Y * next.Width + p.X;
                if (erase || type.Role == ElementRole.Wall)
                {
                    next.TerrainTypeIds[index] = erase ? "floor" : typeId;
                    instances.RemoveAll(e => e != null && e.Position == p);
                }
                else if (type.Role == ElementRole.Floor) next.TerrainTypeIds[index] = typeId;
                else
                {
                    if (registry.Find(next.TerrainTypeIds[index])?.Role == ElementRole.Wall) next.TerrainTypeIds[index] = "floor";
                    var existing = instances.FirstOrDefault(e => e != null && e.Position == p && e.TypeId == typeId);
                    if (existing != null) continue;
                    if (type.Role == ElementRole.Player) instances.RemoveAll(e => e != null && registry.Find(e.TypeId)?.Role == ElementRole.Player);
                    instances.RemoveAll(e => e != null && e.Position == p && SameLayer(type.Role, registry.Find(e.TypeId)?.Role ?? ElementRole.Fixture));
                    instances.Add(new ElementInstance(Guid.NewGuid().ToString("N"), typeId, p));
                }
            }
            next.Elements = instances.ToArray(); LevelMigration.SyncLegacy(next, registry);
            if (ElementLayoutsEqual(draft, next)) return false;
            CommitLayout(draft, next); return true;
        }
        private static bool SameLayer(ElementRole a, ElementRole b) => a == b ||
            (a == ElementRole.Player || a == ElementRole.Box) && (b == ElementRole.Player || b == ElementRole.Box);
        private static void EnsureElementTerrain(LevelDefinition level)
        {
            int count = level.Width * level.Height;
            if (level.TerrainTypeIds != null && level.TerrainTypeIds.Length == count) return;
            var ids = Enumerable.Repeat("floor", count).ToArray();
            if (level.TerrainTypeIds != null) Array.Copy(level.TerrainTypeIds, ids, Math.Min(ids.Length, level.TerrainTypeIds.Length));
            level.TerrainTypeIds = ids;
        }
        private static bool ResizeElements(LevelDefinition draft, int width, int height, ElementRegistry registry)
        {
            CheckSize(width, height);
            if (draft.Width == width && draft.Height == height && draft.TerrainTypeIds != null && draft.TerrainTypeIds.Length == width * height) return false;
            var next = draft.DeepClone(); var ids = Enumerable.Repeat("floor", width * height).ToArray();
            for (int y = 0; y < Math.Min(height, draft.Height); y++) for (int x = 0; x < Math.Min(width, draft.Width); x++)
            {
                int oldIndex = y * draft.Width + x;
                if (draft.TerrainTypeIds != null && oldIndex < draft.TerrainTypeIds.Length) ids[y * width + x] = draft.TerrainTypeIds[oldIndex];
            }
            next.Width = width; next.Height = height; next.TerrainTypeIds = ids;
            next.Elements = Array.FindAll(next.Elements ?? Array.Empty<ElementInstance>(), e => e == null || next.IsInside(e.Position));
            LevelMigration.SyncLegacy(next, registry); CommitLayout(draft, next); return true;
        }
        public static bool TryMoveElement(LevelDefinition draft, string instanceId, GridPos to, ElementRegistry registry, out string error)
        {
            error = ""; registry = registry ?? ElementRegistry.BuiltIns();
            if (!CanEdit(draft) || !draft.IsInside(to)) { error = "对象只能移动到地图内。"; return false; }
            var next = LevelMigration.Snapshot(draft, registry);
            var instance = Array.Find(next.Elements ?? Array.Empty<ElementInstance>(), e => e != null && e.Id == instanceId);
            if (instance == null) { error = "没有找到选中的对象。"; return false; }
            if (instance.Position == to) return true;
            EnsureElementTerrain(next);
            if (registry.Find(next.TerrainTypeIds[to.Y * next.Width + to.X])?.Role == ElementRole.Wall)
            { error = "对象不能放在墙内。"; return false; }
            var role = registry.Find(instance.TypeId)?.Role ?? ElementRole.Fixture;
            if (next.Elements.Any(e => e != null && e.Id != instanceId && e.Position == to && SameLayer(role, registry.Find(e.TypeId)?.Role ?? ElementRole.Fixture)))
            { error = "目标格的同一层已有对象。"; return false; }
            instance.Position = to; LevelMigration.SyncLegacy(next, registry); CommitLayout(draft, next); return true;
        }
        private static bool MoveLegacyActor(LevelDefinition draft, GridPos from, GridPos to, out string error)
        {
            var registry = ElementRegistry.BuiltIns();
            var instance = (draft.Elements ?? Array.Empty<ElementInstance>()).FirstOrDefault(e => e != null && e.Position == from &&
                (registry.Find(e.TypeId)?.Role == ElementRole.Player || registry.Find(e.TypeId)?.Role == ElementRole.Box));
            if (instance == null) { error = "这个格子没有可移动的对象。"; return false; }
            return TryMoveElement(draft, instance.Id, to, registry, out error);
        }

        private static bool TransferElements(LevelDefinition draft, GridRect source, GridPos destination, bool copy, out string error, ElementRegistry registry)
        {
            error = "";
            if (!CanEdit(draft) || !source.IsInside(draft)) { error = "选区必须完整位于地图内。"; return false; }
            var snapshot = ReadRegion(draft, source, registry); var next = draft.DeepClone();
            if (!copy) ClearElements(next, source);
            if (!PasteElements(next, snapshot, destination, copy, out error, registry)) return false;
            CommitLayout(draft, next); return true;
        }
        private static bool PasteElements(LevelDefinition draft, LevelRegion snapshot, GridPos destination, bool copy, out string error, ElementRegistry registry)
        {
            error = "";
            if (!CanEdit(draft) || snapshot == null) { error = "没有可粘贴的选区。"; return false; }
            var target = new GridRect(destination.X, destination.Y, snapshot.Width, snapshot.Height);
            if (!target.IsInside(draft)) { error = "目标选区超出地图边界，本次操作没有修改任何格子。"; return false; }
            var next = LevelMigration.Snapshot(draft, registry);
            if (copy && (next.Elements ?? Array.Empty<ElementInstance>()).Any(e => e != null && target.Contains(e.Position) && registry.Find(e.TypeId)?.Role == ElementRole.Player))
            { error = "复制目标覆盖了玩家起点，请将选区移到其他位置。"; return false; }
            EnsureElementTerrain(next); ClearElements(next, target);
            for (int y = 0; y < snapshot.Height; y++) for (int x = 0; x < snapshot.Width; x++)
                next.TerrainTypeIds[(destination.Y + y) * next.Width + destination.X + x] = snapshot.TerrainTypeAt(x, y);
            var incoming = snapshot.Instances.Where(e => e != null && (!copy || registry.Find(e.TypeId)?.Role != ElementRole.Player)).Select(e => e.DeepClone()).ToArray();
            if (copy)
            {
                var remap = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var instance in incoming)
                {
                    string id = Guid.NewGuid().ToString("N");
                    if (!string.IsNullOrEmpty(instance.Id) && !remap.ContainsKey(instance.Id)) remap.Add(instance.Id, id);
                    instance.Id = id;
                }
                foreach (var instance in incoming) RemapReferences(instance, remap);
            }
            foreach (var instance in incoming) instance.Position = destination + instance.Position;
            next.Elements = (next.Elements ?? Array.Empty<ElementInstance>()).Concat(incoming).ToArray();
            LevelMigration.SyncLegacy(next, registry); CommitLayout(draft, next); return true;
        }
        private static void ClearElements(LevelDefinition draft, GridRect rect)
        {
            EnsureElementTerrain(draft);
            for (int y = rect.MinY; y <= rect.MaxY; y++) for (int x = rect.MinX; x <= rect.MaxX; x++) draft.TerrainTypeIds[y * draft.Width + x] = "floor";
            draft.Elements = Array.FindAll(draft.Elements ?? Array.Empty<ElementInstance>(), e => e == null || !rect.Contains(e.Position));
        }
        private static void RemapReferences(ElementInstance instance, Dictionary<string, string> remap)
        {
            foreach (var value in instance.Overrides ?? Array.Empty<ParameterValue>())
            {
                if (value == null) continue;
                if (value.Kind == ParameterKind.Reference && value.StringValue != null && remap.TryGetValue(value.StringValue, out string replacement)) value.StringValue = replacement;
                if (value.Kind == ParameterKind.References && value.StringValues != null)
                    value.StringValues = Array.ConvertAll(value.StringValues, id => id != null && remap.TryGetValue(id, out string mapped) ? mapped : id);
            }
        }
        public static bool TrySetParameter(LevelDefinition draft, string instanceId, ParameterValue value, ElementRegistry registry, out string error)
        {
            error = ""; registry = registry ?? ElementRegistry.BuiltIns();
            if (draft == null || value == null) { error = "没有要修改的属性。"; return false; }
            var next = LevelMigration.Snapshot(draft, registry);
            var instance = Array.Find(next.Elements ?? Array.Empty<ElementInstance>(), e => e != null && e.Id == instanceId);
            if (instance == null) { error = "没有找到选中的对象。"; return false; }
            var property = Array.Find(registry.Find(instance.TypeId)?.Properties ?? Array.Empty<PropertyDescriptor>(), p => p != null && p.Key == value.Key);
            if (property == null) { error = "此元素没有声明该属性。"; return false; }
            var issues = new List<ValidationIssue>(); ElementValidation.ValidateValue(value, property, issues, instance.Position);
            if (issues.Count > 0) { error = issues[0].Message; return false; }
            var overrides = new List<ParameterValue>(instance.Overrides ?? Array.Empty<ParameterValue>());
            overrides.RemoveAll(p => p != null && p.Key == value.Key); overrides.Add(value.DeepClone()); instance.Overrides = overrides.ToArray();
            LevelMigration.SyncLegacy(next, registry); CommitLayout(draft, next); return true;
        }
        public static bool ResetParameter(LevelDefinition draft, string instanceId, string key, ElementRegistry registry, out string error)
        {
            error = ""; registry = registry ?? ElementRegistry.BuiltIns();
            if (draft == null) { error = "没有可修改的关卡。"; return false; }
            var next = LevelMigration.Snapshot(draft, registry);
            var instance = Array.Find(next.Elements ?? Array.Empty<ElementInstance>(), e => e != null && e.Id == instanceId);
            if (instance == null) { error = "没有找到选中的对象。"; return false; }
            instance.Overrides = Array.FindAll(instance.Overrides ?? Array.Empty<ParameterValue>(), value => value == null || value.Key != key);
            LevelMigration.SyncLegacy(next, registry); CommitLayout(draft, next); return true;
        }

        private static bool ElementLayoutsEqual(LevelDefinition a, LevelDefinition b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Width != b.Width || a.Height != b.Height) return false;
            a = LevelMigration.Snapshot(a); b = LevelMigration.Snapshot(b);
            if (a.SchemaVersion != b.SchemaVersion) return false;
            if (!(a.TerrainTypeIds ?? Array.Empty<string>()).SequenceEqual(b.TerrainTypeIds ?? Array.Empty<string>())) return false;
            return CanonicalElements(a).SequenceEqual(CanonicalElements(b));
        }
        private static IEnumerable<string> CanonicalElements(LevelDefinition level)
        {
            var entries = level.Elements ?? Array.Empty<ElementInstance>();
            var idMap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in entries.Where(e => e != null))
                if (!string.IsNullOrEmpty(entry.Id) && !idMap.ContainsKey(entry.Id)) idMap.Add(entry.Id, entry.TypeId + "@" + entry.Position.X + "," + entry.Position.Y);
            return entries.Select(e =>
            {
                if (e == null) return "<null>";
                var copy = e.DeepClone(); RemapReferences(copy, idMap);
                return copy.Position.X + "," + copy.Position.Y + ":" + ParameterValue.Escape(copy.TypeId) + ":" + ParametersKey(copy.Overrides);
            }).OrderBy(value => value, StringComparer.Ordinal);
        }
        private static string CellContent(LevelDefinition level, GridPos p)
        {
            string terrain = level.TerrainTypeIds != null && p.Y * level.Width + p.X < level.TerrainTypeIds.Length ? level.TerrainTypeIds[p.Y * level.Width + p.X] : "<missing>";
            return ParameterValue.Escape(terrain) + string.Join(";", (level.Elements ?? Array.Empty<ElementInstance>()).Where(e => e != null && e.Position == p)
                .Select(e => ParameterValue.Escape(e.TypeId) + ParametersKey(e.Overrides)).OrderBy(value => value, StringComparer.Ordinal));
        }
        private static string ParametersKey(ParameterValue[] values) => string.Join(";", (values ?? Array.Empty<ParameterValue>()).Select(value => value == null ? "<null>" :
            ParameterValue.Escape(value.Key) + "/" + (int)value.Kind + "/" + value.BoolValue + "/" + value.IntValue + "/" + value.FloatValue.ToString("R", CultureInfo.InvariantCulture) + "/" +
            ParameterValue.Escape(value.StringValue) + "/" + string.Join(",", (value.StringValues ?? Array.Empty<string>()).Select(ParameterValue.Escape)) + "/" + ParameterValue.Escape(value.RawValue))
            .OrderBy(value => value, StringComparer.Ordinal));
    }
}
