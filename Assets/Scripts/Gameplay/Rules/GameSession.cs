using System;
using System.Collections.Generic;

namespace Sokoban.Core
{
    public sealed class BoardState
    {
        public GridPos Player { get; }
        public IReadOnlyList<GridPos> Boxes { get; }
        public int Steps { get; }
        public int Pushes { get; }
        public bool IsWon { get; }
        internal BoardState(GridPos player, GridPos[] boxes, int steps, int pushes, bool won)
        {
            Player = player; Boxes = Array.AsReadOnly((GridPos[])boxes.Clone());
            Steps = steps; Pushes = pushes; IsWon = won;
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
        public string Reason { get; internal set; }
    }

    /// <summary>A discrete, reversible game. Rendering and asset ownership stay outside this class.</summary>
    public sealed class GameSession
    {
        private readonly LevelDefinition definition;
        private readonly HashSet<GridPos> goals;
        private readonly Stack<BoardState> history = new Stack<BoardState>();
        private GridPos player;
        private GridPos[] boxes;
        private int steps;
        private int pushes;
        public bool IsWon
        {
            get
            {
                if (boxes.Length == 0) return false;
                foreach (var box in boxes) if (!goals.Contains(box)) return false;
                return true;
            }
        }
        public bool CanUndo => history.Count > 0;
        public BoardState State => new BoardState(player, boxes, steps, pushes, IsWon);
        public LevelDefinition Definition => definition.DeepClone();

        public GameSession(LevelDefinition level)
        {
            var issues = LevelValidator.Validate(level);
            if (issues.Count > 0) throw new ArgumentException(issues[0].Message, nameof(level));
            definition = level.DeepClone();
            goals = new HashSet<GridPos>(definition.Goals);
            Restart();
        }

        public MoveResult TryMove(Direction direction)
        {
            GridPos offset;
            switch (direction)
            {
                case Direction.Up: offset = new GridPos(0, 1); break;
                case Direction.Right: offset = new GridPos(1, 0); break;
                case Direction.Down: offset = new GridPos(0, -1); break;
                case Direction.Left: offset = new GridPos(-1, 0); break;
                default: throw new ArgumentOutOfRangeException(nameof(direction));
            }
            var target = player + offset;
            var result = new MoveResult { From = player, To = player, Reason = "" };
            if (IsWon) { result.Reason = "关卡已通关。"; return result; }
            if (definition.CellAt(target) == CellType.Wall)
            { result.Reason = "前方有墙，无法移动。"; return result; }
            int boxIndex = Array.IndexOf(boxes, target);
            var boxTarget = target + offset;
            if (boxIndex >= 0 && (definition.CellAt(boxTarget) == CellType.Wall || Array.IndexOf(boxes, boxTarget) >= 0))
            { result.Reason = "箱子后方需要一个空格。"; return result; }
            history.Push(State);
            if (boxIndex >= 0)
            {
                boxes[boxIndex] = boxTarget;
                pushes++;
                result.Pushed = true; result.BoxFrom = target; result.BoxTo = boxTarget;
            }
            player = target;
            steps++;
            result.Succeeded = true; result.To = target; result.Won = IsWon;
            return result;
        }

        public bool Undo()
        {
            if (!CanUndo) return false;
            var previous = history.Pop();
            player = previous.Player;
            boxes = new GridPos[previous.Boxes.Count];
            for (int i = 0; i < boxes.Length; i++) boxes[i] = previous.Boxes[i];
            steps = previous.Steps; pushes = previous.Pushes;
            return true;
        }

        public void Restart()
        {
            player = definition.PlayerStart;
            boxes = (GridPos[])definition.Boxes.Clone();
            steps = 0; pushes = 0; history.Clear();
        }
    }
}
