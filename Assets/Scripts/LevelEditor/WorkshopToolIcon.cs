#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Sokoban.Runtime
{
    /// <summary>Small pixel-grid UI symbols, with no font or player asset dependency.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class WorkshopToolIcon : MaskableGraphic
    {
        public WorkshopTool Tool;
        private const int GridSize = 20;
        private readonly HashSet<int> pixels = new HashSet<int>();

        public WorkshopToolIcon() { useLegacyMeshGeneration = false; }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            pixels.Clear();
            switch (Tool)
            {
                case WorkshopTool.Brush:
                    Line(9, 9, 16, 16); Line(10, 8, 17, 15);
                    Line(16, 16, 17, 15); Line(7, 10, 11, 6);
                    Rect(5, 4, 4, 4); Rect(3, 3, 5, 2);
                    break;
                case WorkshopTool.Line:
                    Line(4, 4, 15, 15); Rect(3, 3, 3, 3); Rect(14, 14, 3, 3);
                    break;
                case WorkshopTool.HollowRectangle:
                    Rect(3, 4, 14, 2); Rect(3, 14, 14, 2);
                    Rect(3, 6, 2, 8); Rect(15, 6, 2, 8);
                    break;
                case WorkshopTool.FilledRectangle:
                    Rect(3, 4, 14, 12);
                    break;
                case WorkshopTool.Select:
                    for (int i = 3; i < 17; i += 4)
                    {
                        Rect(i, 3, 2, 2); Rect(i, 15, 2, 2);
                        Rect(3, i, 2, 2); Rect(15, i, 2, 2);
                    }
                    break;
                case WorkshopTool.Fill:
                    Line(3, 10, 9, 16); Line(9, 16, 15, 10);
                    Line(3, 10, 9, 4); Line(9, 4, 15, 10);
                    for (int y = 6; y <= 10; y++) Rect(13 - y, y, (y - 4) * 2 - 1, 1);
                    Line(6, 13, 6, 17); Line(6, 17, 9, 17);
                    Rect(16, 3, 3, 3); Rect(17, 6, 1, 2);
                    break;
                case WorkshopTool.Eraser:
                    Line(3, 8, 11, 16); Line(11, 16, 17, 10);
                    Line(17, 10, 10, 3); Line(10, 3, 8, 3); Line(8, 3, 3, 8);
                    Line(7, 12, 13, 6); Rect(11, 3, 6, 1);
                    break;
            }

            var bounds = GetPixelAdjustedRect();
            float unit = Mathf.Min(bounds.width, bounds.height) / GridSize;
            Vector2 origin = bounds.center - Vector2.one * (unit * GridSize / 2);
            foreach (int pixel in pixels)
            {
                float x = origin.x + pixel % GridSize * unit;
                float y = origin.y + pixel / GridSize * unit;
                int start = mesh.currentVertCount;
                mesh.AddVert(new Vector3(x, y), color, Vector2.zero);
                mesh.AddVert(new Vector3(x, y + unit), color, Vector2.zero);
                mesh.AddVert(new Vector3(x + unit, y + unit), color, Vector2.zero);
                mesh.AddVert(new Vector3(x + unit, y), color, Vector2.zero);
                mesh.AddTriangle(start, start + 1, start + 2);
                mesh.AddTriangle(start, start + 2, start + 3);
            }
        }

        private void Rect(int x, int y, int width, int height)
        {
            for (int yy = y; yy < y + height; yy++)
                for (int xx = x; xx < x + width; xx++)
                    if (xx >= 0 && xx < GridSize && yy >= 0 && yy < GridSize) pixels.Add(yy * GridSize + xx);
        }

        private void Line(int x, int y, int endX, int endY)
        {
            int dx = Mathf.Abs(endX - x), dy = -Mathf.Abs(endY - y);
            int sx = x < endX ? 1 : -1, sy = y < endY ? 1 : -1, error = dx + dy;
            while (true)
            {
                Rect(x, y, 1, 1);
                if (x == endX && y == endY) return;
                int twice = error * 2;
                if (twice >= dy) { error += dy; x += sx; }
                if (twice <= dx) { error += dx; y += sy; }
            }
        }
    }
}
#endif
