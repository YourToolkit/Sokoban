using System;
using System.Collections.Generic;

namespace Sokoban.Core
{
    public enum LevelBrush { Floor, Wall, Goal, Player, Box, Erase }

    public readonly struct GridRect
    {
        public int MinX { get; }
        public int MinY { get; }
        public int Width { get; }
        public int Height { get; }
        public int MaxX => MinX + Width - 1;
        public int MaxY => MinY + Height - 1;

        public GridRect(int minX, int minY, int width, int height)
        {
            if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(width), "选区尺寸必须为正数。");
            MinX = minX; MinY = minY; Width = width; Height = height;
        }

        public static GridRect FromPoints(GridPos a, GridPos b) => new GridRect(
            Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X) + 1, Math.Abs(a.Y - b.Y) + 1);
        public bool Contains(GridPos p) => p.X >= MinX && p.Y >= MinY && p.X <= MaxX && p.Y <= MaxY;
        public bool IsInside(LevelDefinition level) => level != null && Width > 0 && Height > 0 && MinX >= 0 && MinY >= 0 &&
            Width <= level.Width && Height <= level.Height && MinX <= level.Width - Width && MinY <= level.Height - Height;
    }

    /// <summary>A detached, complete cell snapshot. Coordinates are local to its lower-left corner.</summary>
    public sealed class LevelRegion
    {
        internal readonly string[] TerrainIds;
        internal readonly ElementInstance[] Instances;
        internal readonly bool IsModern;
        internal readonly ElementRegistry Registry;
        public IReadOnlyList<ElementInstance> Elements => Array.AsReadOnly(Array.ConvertAll(Instances, e => e?.DeepClone()));
        private readonly CellType[] cells;
        private readonly bool[] goals;
        private readonly bool[] boxes;
        public int Width { get; }
        public int Height { get; }
        public GridPos? Player { get; }
        public bool HasPlayer => Player.HasValue;

        internal LevelRegion(LevelDefinition level, GridRect area, ElementRegistry registry = null)
        {
            Registry = (registry ?? ElementRegistry.BuiltIns()).Snapshot();
            IsModern = level.SchemaVersion > 0;
            var normalized = LevelMigration.Snapshot(level, Registry);
            LevelMigration.SyncLegacy(normalized, Registry);
            level = normalized;
            Width = area.Width; Height = area.Height;
            TerrainIds = new string[Width * Height];
            var instances = new List<ElementInstance>();
            foreach (var value in normalized.Elements ?? Array.Empty<ElementInstance>())
                if (value != null && area.Contains(value.Position))
                {
                    var copy = value.DeepClone(); copy.Position = new GridPos(copy.Position.X - area.MinX, copy.Position.Y - area.MinY); instances.Add(copy);
                }
            Instances = instances.ToArray();
            cells = new CellType[Width * Height];
            goals = new bool[cells.Length];
            boxes = new bool[cells.Length];
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                var p = new GridPos(area.MinX + x, area.MinY + y);
                int index = y * Width + x;
                cells[index] = level.CellAt(p);
                TerrainIds[index] = normalized.TerrainTypeIds[p.Y * normalized.Width + p.X];
                goals[index] = level.IsGoal(p);
                boxes[index] = Array.IndexOf(level.Boxes ?? Array.Empty<GridPos>(), p) >= 0;
            }
            if (level.HasPlayer && area.Contains(level.PlayerStart))
                Player = new GridPos(level.PlayerStart.X - area.MinX, level.PlayerStart.Y - area.MinY);
        }

        public CellType TerrainAt(int x, int y) => cells[Index(x, y)];
        public bool IsGoal(int x, int y) => goals[Index(x, y)];
        public bool HasBox(int x, int y) => boxes[Index(x, y)];
        public string TerrainTypeAt(int x, int y) => TerrainIds[Index(x, y)];
        private int Index(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) throw new ArgumentOutOfRangeException(nameof(x));
            return y * Width + x;
        }
    }

    /// <summary>Shared, engine-free authoring rules. Every batch is committed as one layout transaction.</summary>
    public static partial class LevelAuthoring
    {
        public static LevelDefinition CreateBlank(int width = 8, int height = 8)
        {
            CheckSize(width, height);
            var level = new LevelDefinition
            {
                Id = Guid.NewGuid().ToString("N"), Name = "新关卡", Description = "把所有箱子推到目标点。",
                LayoutVersion = 0, Width = width, Height = height, Cells = new CellType[width * height]
            };
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if (x == 0 || y == 0 || x == width - 1 || y == height - 1) level.Cells[y * width + x] = CellType.Wall;
            return level;
        }

        public static LevelDefinition Duplicate(LevelDefinition source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var copy = source.DeepClone();
            copy.Id = Guid.NewGuid().ToString("N");
            copy.LayoutVersion = 0;
            return copy;
        }

        public static bool Paint(LevelDefinition draft, GridPos cell, LevelBrush brush)
        {
            if (draft != null && draft.SchemaVersion > 0) return Paint(draft, cell, BrushType(brush), ElementRegistry.BuiltIns());
            if (!CanEdit(draft) || !draft.IsInside(cell)) return false;
            return ApplyCells(draft, new[] { cell }, brush);
        }

        public static bool PaintLine(LevelDefinition draft, GridPos from, GridPos to, LevelBrush brush)
        {
            if (draft != null && draft.SchemaVersion > 0) return PaintLine(draft, from, to, BrushType(brush), ElementRegistry.BuiltIns());
            if (brush == LevelBrush.Player || !CanEdit(draft) || !draft.IsInside(from) || !draft.IsInside(to)) return false;
            var cells = new List<GridPos>();
            int x = from.X, y = from.Y;
            int dx = Math.Abs(to.X - x), dy = -Math.Abs(to.Y - y);
            int sx = x < to.X ? 1 : -1, sy = y < to.Y ? 1 : -1;
            int error = dx + dy;
            while (true)
            {
                cells.Add(new GridPos(x, y));
                if (x == to.X && y == to.Y) break;
                int twiceError = 2 * error;
                if (twiceError >= dy) { error += dy; x += sx; }
                if (twiceError <= dx) { error += dx; y += sy; }
            }
            return ApplyCells(draft, cells, brush);
        }

        public static bool PaintRectangle(LevelDefinition draft, GridPos from, GridPos to, LevelBrush brush, bool filled)
        {
            if (draft != null && draft.SchemaVersion > 0) return PaintRectangle(draft, from, to, BrushType(brush), filled, ElementRegistry.BuiltIns());
            if (brush == LevelBrush.Player || !CanEdit(draft) || !draft.IsInside(from) || !draft.IsInside(to)) return false;
            var rect = GridRect.FromPoints(from, to);
            var cells = new List<GridPos>();
            for (int y = rect.MinY; y <= rect.MaxY; y++)
            for (int x = rect.MinX; x <= rect.MaxX; x++)
                if (filled || x == rect.MinX || x == rect.MaxX || y == rect.MinY || y == rect.MaxY) cells.Add(new GridPos(x, y));
            return ApplyCells(draft, cells, brush);
        }

        public static bool FloodFill(LevelDefinition draft, GridPos start, LevelBrush brush)
        {
            if (draft != null && draft.SchemaVersion > 0) return FloodFill(draft, start, BrushType(brush), ElementRegistry.BuiltIns());
            if (brush == LevelBrush.Player || !CanEdit(draft) || !draft.IsInside(start)) return false;
            var cells = new List<GridPos>();
            var pending = new Queue<GridPos>();
            var visited = new HashSet<GridPos>();
            pending.Enqueue(start);
            while (pending.Count > 0)
            {
                var p = pending.Dequeue();
                if (!draft.IsInside(p) || !visited.Add(p) || !SameCell(draft, start, p)) continue;
                cells.Add(p);
                pending.Enqueue(new GridPos(p.X - 1, p.Y));
                pending.Enqueue(new GridPos(p.X + 1, p.Y));
                pending.Enqueue(new GridPos(p.X, p.Y - 1));
                pending.Enqueue(new GridPos(p.X, p.Y + 1));
            }
            // Search uses the original layout; writes cannot alter the connected component.
            return ApplyCells(draft, cells, brush);
        }

        public static bool Resize(LevelDefinition draft, int width, int height, ElementRegistry registry = null)
        {
            if (draft != null && draft.SchemaVersion > 0) return ResizeElements(draft, width, height, registry ?? ElementRegistry.BuiltIns());
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            CheckSize(width, height);
            if (draft.Width == width && draft.Height == height && draft.Cells != null && draft.Cells.Length == width * height) return false;
            var next = draft.DeepClone();
            var terrain = new CellType[width * height];
            for (int y = 0; y < Math.Min(draft.Height, height); y++)
            for (int x = 0; x < Math.Min(draft.Width, width); x++) terrain[y * width + x] = draft.CellAt(new GridPos(x, y));
            next.Width = width; next.Height = height; next.Cells = terrain;
            next.Goals = Array.FindAll(next.Goals ?? Array.Empty<GridPos>(), next.IsInside);
            next.Boxes = Array.FindAll(next.Boxes ?? Array.Empty<GridPos>(), next.IsInside);
            if (next.HasPlayer && !next.IsInside(next.PlayerStart)) next.HasPlayer = false;
            CommitLayout(draft, next);
            return true;
        }

        public static LevelRegion ReadRegion(LevelDefinition draft, GridRect source, ElementRegistry registry = null)
        {
            if (!CanEdit(draft) || !source.IsInside(draft)) throw new ArgumentOutOfRangeException(nameof(source), "选区必须完整位于地图内。");
            return new LevelRegion(draft, source, registry);
        }

        public static bool TryMoveRegion(LevelDefinition draft, GridRect source, GridPos destination, out string error, ElementRegistry registry = null)
            => draft != null && draft.SchemaVersion > 0 ? TransferElements(draft, source, destination, false, out error, registry ?? ElementRegistry.BuiltIns()) : TransferRegion(draft, source, destination, false, out error);

        public static bool TryCopyRegion(LevelDefinition draft, GridRect source, GridPos destination, out string error, ElementRegistry registry = null)
            => draft != null && draft.SchemaVersion > 0 ? TransferElements(draft, source, destination, true, out error, registry ?? ElementRegistry.BuiltIns()) : TransferRegion(draft, source, destination, true, out error);

        public static bool TryPasteRegion(LevelDefinition draft, LevelRegion snapshot, GridPos destination, out string error, ElementRegistry registry = null)
        {
            if (draft != null && (draft.SchemaVersion > 0 || snapshot?.IsModern == true)) return PasteElements(draft, snapshot, destination, true, out error, registry ?? snapshot?.Registry ?? ElementRegistry.BuiltIns());
            error = "";
            if (!CanEdit(draft) || snapshot == null) { error = "没有可粘贴的选区。"; return false; }
            var target = new GridRect(destination.X, destination.Y, snapshot.Width, snapshot.Height);
            if (!target.IsInside(draft)) { error = "目标选区超出地图边界，本次操作没有修改任何格子。"; return false; }
            if (draft.HasPlayer && target.Contains(draft.PlayerStart))
            { error = "复制目标覆盖了玩家起点，请将选区移到其他位置。"; return false; }
            var next = draft.DeepClone();
            EnsureTerrain(next);
            StampRegion(next, snapshot, destination, false);
            if (!LayoutEquals(draft, next)) CommitLayout(draft, next);
            return true;
        }

        public static bool TryMoveActor(LevelDefinition draft, GridPos from, GridPos to, out string error)
        {
            if (draft != null && draft.SchemaVersion > 0) return MoveLegacyActor(draft, from, to, out error);
            error = "";
            if (!CanEdit(draft) || !draft.IsInside(from) || !draft.IsInside(to))
            { error = "对象只能移动到地图内。"; return false; }
            bool isPlayer = draft.HasPlayer && draft.PlayerStart == from;
            int boxIndex = Array.IndexOf(draft.Boxes ?? Array.Empty<GridPos>(), from);
            if (!isPlayer && boxIndex < 0) { error = "这个格子没有可移动的对象。"; return false; }
            if (from == to) return true;
            if (draft.CellAt(to) == CellType.Wall || (draft.HasPlayer && draft.PlayerStart == to) ||
                Array.IndexOf(draft.Boxes ?? Array.Empty<GridPos>(), to) >= 0)
            { error = "目标格必须是没有玩家或箱子的地面。"; return false; }
            if (isPlayer) draft.PlayerStart = to;
            else draft.Boxes[boxIndex] = to;
            return true;
        }

        private static bool TransferRegion(LevelDefinition draft, GridRect source, GridPos destination, bool copy, out string error)
        {
            error = "";
            if (!CanEdit(draft) || !source.IsInside(draft)) { error = "选区必须完整位于地图内。"; return false; }
            var target = new GridRect(destination.X, destination.Y, source.Width, source.Height);
            if (!target.IsInside(draft)) { error = "目标选区超出地图边界，本次操作没有修改任何格子。"; return false; }
            if (copy && draft.HasPlayer && target.Contains(draft.PlayerStart))
            { error = "复制目标覆盖了玩家起点，请将选区移到其他位置。"; return false; }
            var snapshot = ReadRegion(draft, source);
            var next = draft.DeepClone();
            EnsureTerrain(next);
            if (!copy) ClearRegion(next, source);
            StampRegion(next, snapshot, destination, !copy);
            if (!LayoutEquals(draft, next)) CommitLayout(draft, next);
            return true;
        }

        private static void StampRegion(LevelDefinition next, LevelRegion snapshot, GridPos destination, bool includePlayer)
        {
            var target = new GridRect(destination.X, destination.Y, snapshot.Width, snapshot.Height);
            ClearRegion(next, target);
            var goals = new List<GridPos>(next.Goals ?? Array.Empty<GridPos>());
            var boxes = new List<GridPos>(next.Boxes ?? Array.Empty<GridPos>());
            for (int y = 0; y < snapshot.Height; y++)
            for (int x = 0; x < snapshot.Width; x++)
            {
                var p = new GridPos(destination.X + x, destination.Y + y);
                next.Cells[p.Y * next.Width + p.X] = snapshot.TerrainAt(x, y);
                if (snapshot.IsGoal(x, y)) goals.Add(p);
                if (snapshot.HasBox(x, y)) boxes.Add(p);
            }
            next.Goals = goals.ToArray(); next.Boxes = boxes.ToArray();
            if (includePlayer && snapshot.Player.HasValue)
            {
                next.HasPlayer = true;
                next.PlayerStart = destination + snapshot.Player.Value;
            }
        }

        private static void ClearRegion(LevelDefinition level, GridRect region)
        {
            for (int y = region.MinY; y <= region.MaxY; y++)
            for (int x = region.MinX; x <= region.MaxX; x++) level.Cells[y * level.Width + x] = CellType.Floor;
            level.Goals = Array.FindAll(level.Goals ?? Array.Empty<GridPos>(), p => !region.Contains(p));
            level.Boxes = Array.FindAll(level.Boxes ?? Array.Empty<GridPos>(), p => !region.Contains(p));
            if (level.HasPlayer && region.Contains(level.PlayerStart)) level.HasPlayer = false;
        }

        private static bool ApplyCells(LevelDefinition draft, IEnumerable<GridPos> cells, LevelBrush brush)
        {
            if (!Enum.IsDefined(typeof(LevelBrush), brush)) throw new ArgumentOutOfRangeException(nameof(brush));
            var next = draft.DeepClone();
            EnsureTerrain(next);
            var goals = new List<GridPos>(next.Goals ?? Array.Empty<GridPos>());
            var boxes = new List<GridPos>(next.Boxes ?? Array.Empty<GridPos>());
            foreach (var p in cells)
            {
                int index = p.Y * next.Width + p.X;
                if (brush == LevelBrush.Wall || brush == LevelBrush.Erase)
                {
                    next.Cells[index] = brush == LevelBrush.Wall ? CellType.Wall : CellType.Floor;
                    goals.RemoveAll(value => value == p); boxes.RemoveAll(value => value == p);
                    if (next.HasPlayer && next.PlayerStart == p) next.HasPlayer = false;
                }
                else
                {
                    next.Cells[index] = CellType.Floor;
                    if (brush == LevelBrush.Goal && !goals.Contains(p)) goals.Add(p);
                    if (brush == LevelBrush.Player)
                    {
                        next.HasPlayer = true; next.PlayerStart = p;
                        boxes.RemoveAll(value => value == p);
                    }
                    if (brush == LevelBrush.Box)
                    {
                        if (!boxes.Contains(p)) boxes.Add(p);
                        if (next.HasPlayer && next.PlayerStart == p) next.HasPlayer = false;
                    }
                }
            }
            next.Goals = goals.ToArray(); next.Boxes = boxes.ToArray();
            if (LayoutEquals(draft, next)) return false;
            CommitLayout(draft, next);
            return true;
        }

        private static bool SameCell(LevelDefinition level, GridPos a, GridPos b) =>
            level.CellAt(a) == level.CellAt(b) && level.IsGoal(a) == level.IsGoal(b) &&
            (Array.IndexOf(level.Boxes ?? Array.Empty<GridPos>(), a) >= 0) == (Array.IndexOf(level.Boxes ?? Array.Empty<GridPos>(), b) >= 0) &&
            (level.HasPlayer && level.PlayerStart == a) == (level.HasPlayer && level.PlayerStart == b);

        public static bool LayoutEquals(LevelDefinition a, LevelDefinition b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a != null && b != null && (a.SchemaVersion > 0 || b.SchemaVersion > 0)) return ElementLayoutsEqual(a, b);
            if (a == null || b == null || a.Width != b.Width || a.Height != b.Height || a.HasPlayer != b.HasPlayer) return false;
            if (a.HasPlayer && a.PlayerStart != b.PlayerStart) return false;
            if (!SameTerrain(a.Cells, b.Cells)) return false;
            return SamePositions(a.Goals, b.Goals) && SamePositions(a.Boxes, b.Boxes);
        }

        private static bool SameTerrain(CellType[] a, CellType[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static bool SamePositions(GridPos[] a, GridPos[] b)
        {
            a = a ?? Array.Empty<GridPos>(); b = b ?? Array.Empty<GridPos>();
            if (a.Length != b.Length) return false;
            var counts = new Dictionary<GridPos, int>();
            foreach (var p in a) { counts.TryGetValue(p, out int count); counts[p] = count + 1; }
            foreach (var p in b)
            {
                if (!counts.TryGetValue(p, out int count) || count == 0) return false;
                counts[p] = count - 1;
            }
            return true;
        }

        private static bool CanEdit(LevelDefinition level) => level != null && level.Width >= 2 && level.Height >= 2 && level.Width <= 32 && level.Height <= 32;
        private static void CheckSize(int width, int height)
        {
            if (width < 2 || height < 2 || width > 32 || height > 32) throw new ArgumentOutOfRangeException(nameof(width), "地图宽高必须在 2 到 32 之间。");
        }

        private static void EnsureTerrain(LevelDefinition level)
        {
            int length = level.Width * level.Height;
            if (level.Cells != null && level.Cells.Length == length) return;
            var terrain = new CellType[length];
            if (level.Cells != null) Array.Copy(level.Cells, terrain, Math.Min(length, level.Cells.Length));
            level.Cells = terrain;
        }

        private static void CommitLayout(LevelDefinition target, LevelDefinition source)
        {
            target.SchemaVersion = source.SchemaVersion; target.TerrainTypeIds = source.TerrainTypeIds; target.Elements = source.Elements;
            target.Width = source.Width; target.Height = source.Height; target.Cells = source.Cells;
            target.Goals = source.Goals; target.Boxes = source.Boxes;
            target.HasPlayer = source.HasPlayer; target.PlayerStart = source.PlayerStart;
        }
    }
}
