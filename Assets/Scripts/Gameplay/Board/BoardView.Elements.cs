using System;
using System.Collections.Generic;
using System.Globalization;
using Sokoban.Content;
using Sokoban.Core;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Sokoban.Runtime
{
    public sealed partial class BoardView
    {
        [SerializeField] private ElementCatalog elementCatalog;
        private ElementRegistry presentedRegistry;
        private readonly Dictionary<string, SpriteRenderer> elementRenderers = new Dictionary<string, SpriteRenderer>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> renderedTypes = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<Sprite, Tile> elementTiles = new Dictionary<Sprite, Tile>();
        private string playerElementId;
        private bool playbackPaused;
        private Tile unknownElementTile;
        private Action<AudioClip> playSound;
        public ElementCatalog Elements => elementCatalog;
        public void ConfigureElements(ElementCatalog catalog) => elementCatalog = catalog;
        public void ConfigureAudio(Action<AudioClip> callback) => playSound = callback;
        public void SetPlaybackPaused(bool paused) => playbackPaused = paused;
        private ElementRegistry CurrentElements() => elementCatalog != null ? elementCatalog.Snapshot() : ElementRegistry.BuiltIns();
        public SpriteRenderer RendererFor(string instanceId) => instanceId == playerElementId ? player :
            instanceId != null && elementRenderers.TryGetValue(instanceId, out var value) ? value : null;

        private void RenderElements(BoardState state, bool terrain)
        {
            CancelAnimation();
            ClearHighlights();
            if (terrain)
            {
                terrainMap.ClearAllTiles(); goalMap.ClearAllTiles();
                for (int y = 0; y < Mathf.Clamp(definition.Height, 0, 32); y++)
                for (int x = 0; x < Mathf.Clamp(definition.Width, 0, 32); x++)
                {
                    int index = y * definition.Width + x;
                    string id = definition.TerrainTypeIds != null && index < definition.TerrainTypeIds.Length ? definition.TerrainTypeIds[index] : null;
                    var asset = elementCatalog != null ? elementCatalog.Find(id) : null;
                    Tile tile;
                    if (asset != null && asset.Sprite != null)
                    {
                        if (!elementTiles.TryGetValue(asset.Sprite, out tile))
                        { tile = Tile(asset.Sprite, Color.white, id, 1); elementTiles.Add(asset.Sprite, tile); }
                    }
                    else if (!string.IsNullOrEmpty(id) && presentedRegistry.Find(id) == null)
                    { if (unknownElementTile == null) unknownElementTile = Tile(null, Color.magenta, "未知地形", 1); tile = unknownElementTile; }
                    else tile = definition.CellAt(new GridPos(x, y)) == CellType.Wall ? wallTile : floorTile;
                    terrainMap.SetTile(new Vector3Int(x, y, 0), tile);
                }
            }
            ApplyElementState(state);
            boardRoot.gameObject.SetActive(true); boardCamera.gameObject.SetActive(true);
        }

        private void ApplyElementState(BoardState state)
        {
            var alive = new HashSet<string>(StringComparer.Ordinal);
            boxes.Clear(); playerElementId = null; player.gameObject.SetActive(false);
            foreach (var element in state.Elements)
            {
                if (string.IsNullOrEmpty(element.Id)) continue;
                var asset = elementCatalog != null ? elementCatalog.Find(element.TypeId) : null;
                SpriteRenderer renderer;
                if (element.Role == ElementRole.Player)
                { renderer = player; playerElementId = element.Id; }
                else
                {
                    alive.Add(element.Id);
                    if (elementRenderers.TryGetValue(element.Id, out renderer) && renderedTypes[element.Id] != element.TypeId)
                    { renderer.gameObject.SetActive(false); DisposeOwned(renderer.gameObject); elementRenderers.Remove(element.Id); renderer = null; }
                    if (renderer == null)
                    {
                        renderer = Instantiate(asset != null && asset.Prefab != null ? asset.Prefab : boxPrefab, boardRoot, false);
                        renderer.name = (asset != null ? asset.Data.Name : element.TypeId) + " [" + element.Id + "]";
                        if (asset == null || asset.Prefab == null)
                            renderer.sortingOrder = element.Role == ElementRole.Goal ? 1 : element.Role == ElementRole.Fixture ? 2 : boxPrefab.sortingOrder;
                        elementRenderers[element.Id] = renderer; renderedTypes[element.Id] = element.TypeId;
                    }
                }
                renderer.gameObject.SetActive(definition.IsInside(element.Position));
                renderer.transform.position = Position(element.Position);
                Sprite sprite = asset != null ? ((element.Active || element.OnGoal) && asset.ActiveSprite != null ? asset.ActiveSprite : asset.Sprite) : null;
                if (asset != null)
                    foreach (var presentation in asset.StatePresentations ?? Array.Empty<ElementStatePresentation>())
                        if (presentation != null && presentation.Sprite != null && Matches(element, presentation)) sprite = presentation.Sprite;
                bool known = presentedRegistry.Find(element.TypeId) != null;
                if (sprite == null && known)
                    sprite = element.Role == ElementRole.Player ? visuals.Player : element.Role == ElementRole.Goal ? visuals.Goal :
                        element.Role == ElementRole.Box ? (element.OnGoal && visuals.BoxOnGoal != null ? visuals.BoxOnGoal : visuals.Box) : null;
                renderer.sprite = sprite != null ? sprite : FallbackSprite();
                // Authored prefab transforms and tints are retained; only an unknown entry gets an error marker.
                renderer.color = !known ? Color.magenta : element.Role == ElementRole.Player ? Color.white :
                    asset != null && asset.Prefab != null ? asset.Prefab.color : boxPrefab.color;
                if (element.Role == ElementRole.Box) boxes.Add(renderer);
            }
            var removed = new List<string>();
            foreach (var pair in elementRenderers) if (!alive.Contains(pair.Key)) removed.Add(pair.Key);
            foreach (string id in removed)
            { elementRenderers[id].gameObject.SetActive(false); DisposeOwned(elementRenderers[id].gameObject); elementRenderers.Remove(id); renderedTypes.Remove(id); }
        }

        private void PlayElementChanges(MoveFrame frame)
        {
            if (elementCatalog == null || playSound == null) return;
            foreach (var movement in frame.Moves)
                foreach (var element in frame.After.Elements)
                    if (element.Id == movement.Id)
                    { var asset = elementCatalog.Find(element.TypeId); if (asset != null && asset.MoveSound != null) playSound(asset.MoveSound); break; }
            foreach (var change in frame.Changes)
                foreach (var element in frame.After.Elements)
                    if (element.Id == change.Id)
                    {
                        var asset = elementCatalog.Find(element.TypeId);
                        if (asset != null)
                        {
                            bool configured = false;
                            foreach (var presentation in asset.StatePresentations ?? Array.Empty<ElementStatePresentation>())
                                if (presentation != null && presentation.Key == change.Key && presentation.Sound != null && Matches(element, presentation))
                                { playSound(presentation.Sound); configured = true; }
                            if (!configured && (change.Key == "active" || change.Key == "onGoal"))
                                playSound(change.After == "true" ? asset.ActivateSound : asset.DeactivateSound);
                        }
                        break;
                    }
        }

        public bool HasConfiguredMoveSound(MoveResult result)
        {
            if (elementCatalog == null || result.Frames.Count == 0) return false;
            foreach (var movement in result.Frames[0].Moves)
                foreach (var element in result.Frames[0].After.Elements)
                    if (element.Id == movement.Id && elementCatalog.Find(element.TypeId)?.MoveSound != null) return true;
            return false;
        }

        private static bool Matches(ElementState element, ElementStatePresentation presentation)
        {
            foreach (var value in element.State)
                if (value.Key == presentation.Key)
                {
                    string text;
                    switch (value.Kind)
                    {
                        case ParameterKind.Boolean: text = value.BoolValue ? "true" : "false"; break;
                        case ParameterKind.Integer: text = value.IntValue.ToString(CultureInfo.InvariantCulture); break;
                        case ParameterKind.Float: text = value.FloatValue.ToString("R", CultureInfo.InvariantCulture); break;
                        case ParameterKind.References: text = string.Join(",", value.StringValues ?? Array.Empty<string>()); break;
                        case ParameterKind.String: case ParameterKind.Enum: case ParameterKind.Reference: text = value.StringValue; break;
                        default: text = value.RawValue; break;
                    }
                    return string.Equals(text, presentation.Value, StringComparison.Ordinal);
                }
            return false;
        }
    }
}
