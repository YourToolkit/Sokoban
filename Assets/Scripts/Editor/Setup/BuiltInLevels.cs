using System;
using System.Collections.Generic;
using Sokoban.Core;

namespace Sokoban.EditorTools
{
    /// <summary>
    /// Six original teaching puzzles. Every solution was verified with breadth-first search.
    /// The returned definitions are independent objects so setup and tests cannot share state.
    /// </summary>
    public static class BuiltInLevels
    {
        public static string[] Solutions => new[]
        {
            "RR",
            "RRDRUU",
            "UURDDLDRR",
            "LUUURDDRDRUU",
            "LULLULURRLDDRDDLUDDRR",
            "UULUURDDDDLDRRUURRRUUDLLLDLUU"
        };

        public static LevelDefinition[] Create()
        {
            return new[]
            {
                Parse("level-01", "初识推箱",
                    "站到箱子后方，将它推到目标点。每次成功移动计为一步。",
                    "#######",
                    "#     #",
                    "# @$ .#",
                    "#     #",
                    "#######"),
                Parse("level-02", "绕到背后",
                    "箱子只能推动。绕到箱子另一侧，改变推动的方向。",
                    "#######",
                    "#   . #",
                    "#     #",
                    "#@ $  #",
                    "#     #",
                    "#######"),
                Parse("level-03", "双箱路线",
                    "为每个箱子找到合适的路线。玩家也可以经过目标点。",
                    "########",
                    "# .    #",
                    "#      #",
                    "# $$ # #",
                    "# @    #",
                    "#    . #",
                    "########"),
                Parse("level-04", "绕墙而行",
                    "绕过墙壁安排路线。推动前，确认自己还能到达箱子的另一侧。",
                    "########",
                    "# . #  #",
                    "#   #. #",
                    "#      #",
                    "# $ $  #",
                    "#  @   #",
                    "########"),
                Parse("level-05", "腾出空间",
                    "保持下方通道畅通。先把一个箱子推到上方目标，再处理剩余两个。",
                    "########",
                    "#   .  #",
                    "# $#   #",
                    "# .    #",
                    "# $ $@ #",
                    "#      #",
                    "#    . #",
                    "########"),
                Parse("level-06", "交错通道",
                    "箱子的近路可能需要玩家绕远路。为后续箱子留出站位空间。",
                    "#########",
                    "# .   . #",
                    "# $###  #",
                    "#   $ $ #",
                    "##     ##",
                    "# @     #",
                    "#   .   #",
                    "#########")
            };
        }

        private static LevelDefinition Parse(string id, string name, string description, params string[] rows)
        {
            int width = rows[0].Length;
            int height = rows.Length;
            var level = new LevelDefinition
            {
                Id = id,
                Name = name,
                Description = description,
                Width = width,
                Height = height,
                Cells = new CellType[width * height]
            };
            var goals = new List<GridPos>();
            var boxes = new List<GridPos>();

            for (int row = 0; row < height; row++)
            {
                if (rows[row].Length != width)
                    throw new InvalidOperationException($"{name}: all rows must have the same width.");

                int y = height - row - 1;
                for (int x = 0; x < width; x++)
                {
                    char tile = rows[row][x];
                    var position = new GridPos(x, y);
                    level.Cells[y * width + x] = tile == '#' ? CellType.Wall : CellType.Floor;

                    switch (tile)
                    {
                        case '#':
                        case ' ':
                            break;
                        case '.':
                            goals.Add(position);
                            break;
                        case '$':
                            boxes.Add(position);
                            break;
                        case '*':
                            goals.Add(position);
                            boxes.Add(position);
                            break;
                        case '+':
                            goals.Add(position);
                            SetPlayer(level, position);
                            break;
                        case '@':
                            SetPlayer(level, position);
                            break;
                        default:
                            throw new InvalidOperationException($"{name}: unsupported tile '{tile}'.");
                    }
                }
            }

            level.Goals = goals.ToArray();
            level.Boxes = boxes.ToArray();
            return level;
        }

        private static void SetPlayer(LevelDefinition level, GridPos position)
        {
            if (level.HasPlayer)
                throw new InvalidOperationException($"{level.Name}: a level cannot contain two players.");

            level.HasPlayer = true;
            level.PlayerStart = position;
        }
    }
}
