using System;
using System.Collections.Generic;

namespace Sokoban.Core
{
    [Serializable]
    public struct GridPos : IEquatable<GridPos>
    {
        public int X;
        public int Y;
        public GridPos(int x, int y) { X = x; Y = y; }
        public bool Equals(GridPos other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is GridPos other && Equals(other);
        public override int GetHashCode() => (X * 397) ^ Y;
        public static bool operator ==(GridPos a, GridPos b) => a.Equals(b);
        public static bool operator !=(GridPos a, GridPos b) => !a.Equals(b);
        public static GridPos operator +(GridPos a, GridPos b) => new GridPos(a.X + b.X, a.Y + b.Y);
        public override string ToString() => $"({X}, {Y})";
    }

    public enum CellType { Floor, Wall }
    public enum Direction { Up, Right, Down, Left }

    [Serializable]
    public sealed class LevelDefinition
    {
        public string Id = "";
        public string Name = "未命名关卡";
        public string Description = "将所有箱子推到目标点。";
        public int LayoutVersion;
        public int Width = 8;
        public int Height = 8;
        public CellType[] Cells = new CellType[64];
        public GridPos[] Goals = Array.Empty<GridPos>();
        public bool HasPlayer;
        public GridPos PlayerStart;
        public GridPos[] Boxes = Array.Empty<GridPos>();

        public bool IsInside(GridPos p) => p.X >= 0 && p.Y >= 0 && p.X < Width && p.Y < Height;
        public CellType CellAt(GridPos p) => IsInside(p) && Cells != null && p.Y * Width + p.X < Cells.Length
            ? Cells[p.Y * Width + p.X] : CellType.Wall;
        public bool IsGoal(GridPos p) => Goals != null && Array.IndexOf(Goals, p) >= 0;

        public LevelDefinition DeepClone() => new LevelDefinition
        {
            Id = Id, Name = Name, Description = Description, LayoutVersion = LayoutVersion, Width = Width, Height = Height,
            Cells = Cells == null ? null : (CellType[])Cells.Clone(),
            Goals = Goals == null ? null : (GridPos[])Goals.Clone(),
            HasPlayer = HasPlayer, PlayerStart = PlayerStart,
            Boxes = Boxes == null ? null : (GridPos[])Boxes.Clone()
        };
    }

    public sealed class ValidationIssue
    {
        public string Message { get; }
        public GridPos? Position { get; }
        public ValidationIssue(string message, GridPos? position = null) { Message = message; Position = position; }
        public override string ToString() => Position.HasValue ? $"{Message} {Position.Value}" : Message;
    }

    public static class LevelValidator
    {
        public static List<ValidationIssue> Validate(LevelDefinition level)
        {
            var issues = new List<ValidationIssue>();
            if (level == null) { issues.Add(new ValidationIssue("没有关卡数据。")); return issues; }
            if (string.IsNullOrWhiteSpace(level.Id)) issues.Add(new ValidationIssue("关卡缺少唯一编号。"));
            if (string.IsNullOrWhiteSpace(level.Name)) issues.Add(new ValidationIssue("请输入关卡名称。"));
            if (level.LayoutVersion < 0) issues.Add(new ValidationIssue("布局版本不能小于零。"));
            if (level.Width < 2 || level.Height < 2 || level.Width > 32 || level.Height > 32)
            { issues.Add(new ValidationIssue("地图宽高必须在 2 至 32 格之间。")); return issues; }
            if (level.Cells == null || level.Cells.Length != level.Width * level.Height)
            { issues.Add(new ValidationIssue("地形数量与地图尺寸不一致。")); return issues; }
            for (int i = 0; i < level.Cells.Length; i++)
                if (level.Cells[i] != CellType.Floor && level.Cells[i] != CellType.Wall)
                    issues.Add(new ValidationIssue("未知的地形类型。", new GridPos(i % level.Width, i / level.Width)));
            if (!level.HasPlayer) issues.Add(new ValidationIssue("请放置一个玩家起点。"));
            else CheckWalkable(level, level.PlayerStart, "玩家", issues);
            var boxes = level.Boxes ?? Array.Empty<GridPos>();
            var goals = level.Goals ?? Array.Empty<GridPos>();
            if (boxes.Length == 0) issues.Add(new ValidationIssue("请至少放置一个箱子。"));
            if (goals.Length == 0) issues.Add(new ValidationIssue("请至少放置一个目标点。"));
            if (boxes.Length != goals.Length) issues.Add(new ValidationIssue("箱子和目标点的数量必须相同。"));
            var occupied = new HashSet<GridPos>();
            foreach (var p in boxes)
            {
                CheckWalkable(level, p, "箱子", issues);
                if (!occupied.Add(p)) issues.Add(new ValidationIssue("同一格内有多个箱子。", p));
                if (level.HasPlayer && p == level.PlayerStart) issues.Add(new ValidationIssue("玩家与箱子重叠。", p));
            }
            occupied.Clear();
            foreach (var p in goals)
            {
                CheckWalkable(level, p, "目标点", issues);
                if (!occupied.Add(p)) issues.Add(new ValidationIssue("同一格内有多个目标点。", p));
            }
            return issues;
        }

        private static void CheckWalkable(LevelDefinition level, GridPos p, string name, List<ValidationIssue> issues)
        {
            if (!level.IsInside(p)) issues.Add(new ValidationIssue(name + "位于地图外。", p));
            else if (level.CellAt(p) == CellType.Wall) issues.Add(new ValidationIssue(name + "位于墙内。", p));
        }
    }
}
