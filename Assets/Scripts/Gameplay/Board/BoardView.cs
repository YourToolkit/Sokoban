using System;
using System.Collections;
using System.Collections.Generic;
using Sokoban.Content;
using Sokoban.Core;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Sokoban.Runtime
{
    public struct BoardViewSnapshot
    {
        public Vector2 Center;
        public float Zoom;
        public bool Valid;
    }

    /// <summary>Renders committed board states. Never makes gameplay decisions.</summary>
    public sealed class BoardView : MonoBehaviour
    {
        public static readonly Color FloorTint = Color.white;
        public static readonly Color WallTint = Color.white;
        [SerializeField] private Camera boardCamera;
        [SerializeField] private Transform boardRoot;
        [SerializeField] private Tilemap terrainMap, goalMap;
        private Tile floorTile, wallTile, goalTile;
        [SerializeField] private SpriteRenderer player;
        private SpriteRenderer brushGhost;
        private readonly List<SpriteRenderer> boxes = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> highlights = new List<SpriteRenderer>();
        private readonly List<GridPos> boxPositions = new List<GridPos>();
        private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        private LevelDefinition definition;
        [SerializeField] private VisualConfig visuals;
        private Action animationCompleted;
        [SerializeField] private SpriteRenderer boxPrefab;
        [SerializeField] private int pixelsPerCell = 32;
        [SerializeField] private Color backgroundColor = new Color32(35, 39, 53, 255);
        private BoardViewport viewport;
        private RenderTexture texture;
        private Rect lastArea;
        private bool initialized;
        private int integerScale = 1;
        public Camera RenderCamera => boardCamera;
        public SpriteRenderer PlayerRenderer => player;
        public IReadOnlyList<SpriteRenderer> BoxRenderers => boxes;
        public VisualConfig Visuals => visuals;
        public int IntegerScale => integerScale;
        public RenderTexture Texture => texture;
        private float zoom = 1;
        private Vector2 viewCenter;
        private Sprite fallbackSprite;
        private bool editorView;
        private int lastWidth;
        private int lastHeight;
        public bool IsAnimating { get; private set; }
        public Rect ScreenRect => viewport == null ? new Rect() : viewport.Display;

        public void Initialize()
        {
            if (initialized) return;
            if (!boardCamera || !boardRoot || !terrainMap || !goalMap || !player || !boxPrefab)
                throw new InvalidOperationException("棋盘引用不完整，请检查 BoardView 的相机、地形、目标、玩家和箱子 Prefab。");
            if (!visuals) throw new InvalidOperationException("棋盘缺少视觉配置。");
            floorTile = Tile(visuals.Floor, Color.white, "Floor", 1);
            wallTile = Tile(visuals.Wall, Color.white, "Wall", 1);
            goalTile = Tile(visuals.Goal, Color.white, "Goal", 1);
            player.sprite = visuals.Player;
            initialized = true;
        }
#if UNITY_EDITOR
        public void Configure(Camera camera, Transform root, Tilemap terrain, Tilemap goals, SpriteRenderer actor, SpriteRenderer crate, VisualConfig config)
        { boardCamera = camera; boardRoot = root; terrainMap = terrain; goalMap = goals; player = actor; boxPrefab = crate; visuals = config; }
        public void PrepareScenePreview(LevelDefinition level)
        { Initialize(); ShowDraft(level); boardCamera.enabled = false; }
#endif

        public void SetViewport(BoardViewport value)
        {
            if (viewport != null && viewport != value) viewport.Surface.gameObject.SetActive(false);
            viewport = value;
            FitCamera();
        }

        public void Show(LevelDefinition level, BoardState state)
        {
            editorView = false;
            definition = level;
            Render(level, true, state.Player, state.Boxes);
            ResetView();
        }

        public void ShowDraft(LevelDefinition level, bool resetView = false)
        {
            if (level == null) return;
            bool changedSize = definition == null || definition.Width != level.Width || definition.Height != level.Height;
            editorView = true;
            definition = level;
            Render(level, level.HasPlayer, level.PlayerStart, level.Boxes ?? Array.Empty<GridPos>());
            if (resetView || changedSize) ResetView();
            else FitCamera();
        }

        private void Render(LevelDefinition level, bool hasPlayer, GridPos playerPosition, IReadOnlyList<GridPos> positions)
        {
            CancelAnimation();
            terrainMap.ClearAllTiles();
            goalMap.ClearAllTiles();
            ClearHighlights();
            for (int y = 0; y < Mathf.Clamp(level.Height, 0, 32); y++)
            for (int x = 0; x < Mathf.Clamp(level.Width, 0, 32); x++)
            {
                var p = new GridPos(x, y);
                terrainMap.SetTile(new Vector3Int(x, y, 0), level.CellAt(p) == CellType.Wall ? wallTile : floorTile);
                if (level.IsGoal(p)) goalMap.SetTile(new Vector3Int(x, y, 0), goalTile);
            }
            player.gameObject.SetActive(hasPlayer && level.IsInside(playerPosition));
            player.transform.position = Position(playerPosition);
            while (boxes.Count < positions.Count)
                {
                var box = Instantiate(boxPrefab, boardRoot, false);
                box.name = "Box " + (boxes.Count + 1); boxes.Add(box);
            }
            boxPositions.Clear();
            for (int i = 0; i < boxes.Count; i++)
            {
                boxes[i].gameObject.SetActive(i < positions.Count && level.IsInside(positions[i]));
                if (i >= positions.Count) continue;
                boxPositions.Add(positions[i]);
                boxes[i].transform.position = Position(positions[i]);
                UpdateBox(boxes[i], level.IsGoal(positions[i]));
            }
            boardRoot.gameObject.SetActive(true);
            boardCamera.gameObject.SetActive(true);
        }

        public void Highlight(IEnumerable<GridPos> cells, bool valid = true)
        {
            int count = 0;
            if (cells != null)
                foreach (var p in cells)
                {
                    if (count >= 2048) break;
                    if (count == highlights.Count) highlights.Add(Actor("Editor highlight", FallbackSprite(), Color.white, 1, 20));
                    var renderer = highlights[count++];
                    renderer.gameObject.SetActive(true);
                    renderer.transform.position = Position(p);
                    renderer.transform.localScale = Vector3.one;
                    renderer.color = valid ? new Color(1, .84f, .35f, .28f) : new Color(1, .2f, .2f, .46f);
                }
            for (int i = count; i < highlights.Count; i++) highlights[i].gameObject.SetActive(false);
        }

        public void ClearHighlights()
        {
            foreach (var item in highlights) item.gameObject.SetActive(false);
            ClearBrushPreview();
        }
        public void ShowBrushPreview(GridPos cell, Sprite sprite, Color color)
        {
            if (brushGhost == null) brushGhost = Actor("Brush preview", FallbackSprite(), Color.white, 1, 22);
            brushGhost.sprite = sprite != null ? sprite : FallbackSprite();
            float extent = Mathf.Max(brushGhost.sprite.bounds.size.x, brushGhost.sprite.bounds.size.y);
            brushGhost.transform.localScale = Vector3.one * (1f / Mathf.Max(.001f, extent));
            brushGhost.transform.position = Position(cell);
            color.a = .64f;
            brushGhost.color = color;
            brushGhost.gameObject.SetActive(true);
        }
        public void ClearBrushPreview() { if (brushGhost != null) brushGhost.gameObject.SetActive(false); }
        public void FocusCell(GridPos cell)
        {
            if (definition == null) return;
            viewCenter = new Vector2(Mathf.Clamp(cell.X, 0, definition.Width - 1) + .5f,
                Mathf.Clamp(cell.Y, 0, definition.Height - 1) + .5f);
            zoom = Mathf.Max(zoom, 1.6f);
            FitCamera();
        }
        public bool ContainsScreenPoint(Vector2 point) => viewport != null && viewport.gameObject.activeInHierarchy && boardRoot.gameObject.activeSelf && ScreenRect.Contains(point);
        public bool TryScreenToCell(Vector2 point, out GridPos cell, bool allowOutside = false)
        {
            cell = default;
            if (!ContainsScreenPoint(point) || definition == null) return false;
            var world = ScreenToWorld(point);
            cell = new GridPos(Mathf.FloorToInt(world.x), Mathf.FloorToInt(world.y));
            return allowOutside || definition.IsInside(cell);
        }

        public BoardViewSnapshot CaptureView() => new BoardViewSnapshot { Center = viewCenter, Zoom = zoom, Valid = definition != null };
        public void RestoreView(BoardViewSnapshot snapshot)
        {
            if (!snapshot.Valid) { ResetView(); return; }
            viewCenter = snapshot.Center;
            zoom = Mathf.Clamp(snapshot.Zoom, .3f, 6);
            FitCamera();
        }
        public void ResetView()
        {
            if (definition == null) return;
            zoom = 1;
            viewCenter = new Vector2(definition.Width / 2f, definition.Height / 2f);
            FitCamera();
        }
        public void ZoomAt(Vector2 screenPoint, float scroll)
        {
            if (!editorView || !ContainsScreenPoint(screenPoint)) return;
            var before = ScreenToWorld(screenPoint);
            zoom = Mathf.Clamp(zoom * Mathf.Pow(1.15f, scroll), .3f, 6);
            FitCamera();
            var after = ScreenToWorld(screenPoint);
            viewCenter += (Vector2)(before - after);
            ClampCenter();
            FitCamera();
        }
        public void Pan(Vector2 screenDelta)
        {
            if (!editorView || boardCamera == null) return;
            float units = 1f / (pixelsPerCell * integerScale);
            viewCenter -= screenDelta * units;
            ClampCenter();
            FitCamera();
        }
        private void ClampCenter()
        {
            viewCenter.x = Mathf.Clamp(viewCenter.x, -definition.Width, definition.Width * 2);
            viewCenter.y = Mathf.Clamp(viewCenter.y, -definition.Height, definition.Height * 2);
        }

        public void Hide()
        {
            CancelAnimation();
            if (boardRoot != null) boardRoot.gameObject.SetActive(false);
            if (boardCamera != null) boardCamera.gameObject.SetActive(false);
            if (viewport != null) viewport.Surface.gameObject.SetActive(false);
        }

        public void Sync(BoardState state)
        {
            CancelAnimation();
            player.transform.position = Position(state.Player);
            for (int i = 0; i < state.Boxes.Count && i < boxes.Count; i++)
            {
                boxPositions[i] = state.Boxes[i];
                boxes[i].transform.position = Position(state.Boxes[i]);
                UpdateBox(boxes[i], definition.IsGoal(state.Boxes[i]));
            }
        }

        public void Animate(MoveResult move, BoardState state, float duration, Action complete)
        {
            CancelAnimation();
            animationCompleted = complete;
            StartCoroutine(AnimateRoutine(move, state, Mathf.Clamp(duration, .02f, .6f)));
        }

        public void Blocked(Direction direction)
        {
            if (!IsAnimating) StartCoroutine(BumpRoutine(direction));
        }

        public void CancelAnimation()
        {
            StopAllCoroutines();
            IsAnimating = false;
            animationCompleted = null;
        }

        private IEnumerator AnimateRoutine(MoveResult move, BoardState state, float duration)
        {
            IsAnimating = true;
            int boxIndex = -1;
            if (move.BoxFrom.HasValue)
                for (int i = 0; i < boxPositions.Count; i++)
                    if (boxPositions[i].Equals(move.BoxFrom.Value)) { boxIndex = i; break; }
            Vector3 from = Position(move.From), to = Position(move.To);
            float elapsed = 0;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0, 1, elapsed / duration);
                player.transform.position = PixelPosition(Vector3.Lerp(from, to, t));
                if (boxIndex >= 0 && move.BoxTo.HasValue)
                    boxes[boxIndex].transform.position = PixelPosition(Vector3.Lerp(Position(move.BoxFrom.Value), Position(move.BoxTo.Value), t));
                yield return null;
            }
            // Sync by position, so presentation does not rely on a state collection's ordering.
            player.transform.position = to;
            if (boxIndex >= 0 && move.BoxTo.HasValue)
            {
                boxPositions[boxIndex] = move.BoxTo.Value;
                boxes[boxIndex].transform.position = Position(move.BoxTo.Value);
                UpdateBox(boxes[boxIndex], definition.IsGoal(move.BoxTo.Value));
            }
            IsAnimating = false;
            var callback = animationCompleted;
            animationCompleted = null;
            callback?.Invoke();
        }

        private IEnumerator BumpRoutine(Direction direction)
        {
            IsAnimating = true;
            Vector3 origin = player.transform.position;
            Vector3 vector = direction == Direction.Up ? Vector3.up : direction == Direction.Down ? Vector3.down :
                direction == Direction.Left ? Vector3.left : Vector3.right;
            float elapsed = 0;
            const float duration = .13f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                player.transform.position = PixelPosition(origin + vector * (.125f * Mathf.Sin(Mathf.Clamp01(elapsed / duration) * Mathf.PI)));
                yield return null;
            }
            player.transform.position = origin;
            IsAnimating = false;
        }

        private void LateUpdate()
        {
            if (viewport != null && viewport.gameObject.activeInHierarchy && (Screen.width != lastWidth || Screen.height != lastHeight || viewport.Area != lastArea)) FitCamera();
        }

        private void FitCamera()
        {
            if (boardCamera == null || definition == null || viewport == null) return;
            Canvas.ForceUpdateCanvases();
            lastWidth = Screen.width; lastHeight = Screen.height; lastArea = viewport.Area;
            int width = Mathf.Max(1, Mathf.FloorToInt(lastArea.width)), height = Mathf.Max(1, Mathf.FloorToInt(lastArea.height));
            float fit = Mathf.Min(width / ((definition.Width + 1f) * pixelsPerCell), height / ((definition.Height + 1f) * pixelsPerCell));
            integerScale = Mathf.Max(1, Mathf.FloorToInt(fit * zoom));
            int w = Mathf.Max(1, width / integerScale), h = Mathf.Max(1, height / integerScale);
            if (texture == null || texture.width != w || texture.height != h)
            {
                ReleaseTexture();
                texture = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32)
                    { name = "棋盘像素画面", filterMode = FilterMode.Point, antiAliasing = 1, hideFlags = HideFlags.DontSave };
                texture.Create();
            }
            viewport.Present(texture, integerScale);
            boardCamera.targetTexture = texture; boardCamera.rect = new Rect(0, 0, 1, 1);
            boardCamera.orthographicSize = h / (2f * pixelsPerCell); boardCamera.aspect = (float)w / h;
            boardCamera.backgroundColor = backgroundColor;
            // Align the lower-left edge of the rendered world to the source pixel grid.
            float left = Mathf.Round((viewCenter.x - w / (2f * pixelsPerCell)) * pixelsPerCell) / pixelsPerCell;
            float bottom = Mathf.Round((viewCenter.y - h / (2f * pixelsPerCell)) * pixelsPerCell) / pixelsPerCell;
            boardCamera.transform.position = new Vector3(left + w / (2f * pixelsPerCell), bottom + h / (2f * pixelsPerCell), -10);
            boardCamera.enabled = true;
        }

        private Vector3 ScreenToWorld(Vector2 point)
        {
            var rect = ScreenRect;
            return boardCamera.ViewportToWorldPoint(new Vector3((point.x - rect.x) / rect.width, (point.y - rect.y) / rect.height, 10));
        }
        private Vector3 PixelPosition(Vector3 point) => new Vector3(Mathf.Round(point.x * pixelsPerCell) / pixelsPerCell, Mathf.Round(point.y * pixelsPerCell) / pixelsPerCell, point.z);
        private void ReleaseTexture()
        {
            if (!texture) return;
            if (boardCamera) boardCamera.targetTexture = null;
            if (viewport && viewport.Surface.texture == texture) viewport.Surface.texture = null;
            texture.Release(); DisposeOwned(texture); texture = null;
        }
        private static void DisposeOwned(UnityEngine.Object item)
        { if (Application.isPlaying) Destroy(item); else DestroyImmediate(item); }



        private Tile Tile(Sprite sprite, Color fallback, string label, float scale = .94f, bool tintSprite = false)
        {
            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.name = label;
            tile.sprite = sprite != null ? sprite : FallbackSprite();
            float extent = Mathf.Max(tile.sprite.bounds.size.x, tile.sprite.bounds.size.y);
            tile.color = sprite != null ? Color.white : fallback;
            tile.transform = Matrix4x4.Scale(Vector3.one * ((sprite != null ? 1f : scale) / Mathf.Max(.001f, extent)));
            tile.hideFlags = HideFlags.DontSave;
            owned.Add(tile);
            return tile;
        }

        private SpriteRenderer Actor(string label, Sprite sprite, Color fallback, float size, int order)
        {
            var go = new GameObject(label, typeof(SpriteRenderer));
            go.transform.SetParent(boardRoot, false); go.layer = boardRoot.gameObject.layer;
            var renderer = go.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite != null ? sprite : FallbackSprite();
            renderer.color = sprite != null ? Color.white : fallback;
            float extent = Mathf.Max(renderer.sprite.bounds.size.x, renderer.sprite.bounds.size.y);
            renderer.transform.localScale = Vector3.one * ((sprite != null ? 1f : size) / Mathf.Max(.001f, extent));
            renderer.sortingOrder = order;
            return renderer;
        }

        private Sprite BoxSprite(bool onGoal) => visuals == null ? null : onGoal && visuals.BoxOnGoal != null ? visuals.BoxOnGoal : visuals.Box;

        private void UpdateBox(SpriteRenderer renderer, bool onGoal)
        {
            var sprite = BoxSprite(onGoal);
            if (sprite != null)
            {
                renderer.sprite = sprite;
            }
            else renderer.color = onGoal ? UiFactory.Teal : UiFactory.Gold;
        }

        private Sprite FallbackSprite()
        {
            if (fallbackSprite != null) return fallbackSprite;
            var texture = new Texture2D(2, 2) { name = "Fallback cell", filterMode = FilterMode.Point };
            texture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
            texture.Apply();
            fallbackSprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), new Vector2(.5f, .5f), 2);
            owned.Add(texture);
            owned.Add(fallbackSprite);
            return fallbackSprite;
        }

        private static Vector3 Position(GridPos position) => new Vector3(position.X + .5f, position.Y + .5f, 0);

        private void OnDestroy()
        {
            ReleaseTexture();
            foreach (var asset in owned) if (asset != null) DisposeOwned(asset);
        }
    }
}
