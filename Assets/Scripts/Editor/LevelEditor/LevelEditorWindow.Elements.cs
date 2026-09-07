using System;
using System.Collections.Generic;
using System.Linq;
using Sokoban.Content;
using Sokoban.Core;
using UnityEditor;
using UnityEngine;

namespace Sokoban.EditorTools
{
    public sealed partial class LevelEditorWindow
    {
        [SerializeField] private string selectedTypeId = "wall", selectedElementId;
        [SerializeField] private bool selectElements, showInstanceProperties;
        [SerializeField] private Vector2 elementScroll, propertyScroll;
        private ElementRegistry elementRegistry;
        private ElementCatalog elementCatalog;
        private int elementRevision = -1;
        private PropertyDescriptor referenceProperty;
        private readonly List<string> referenceTargets = new List<string>();
        private GridPos? elementDragStart;
        private LevelDefinition elementDragSnapshot;
        private ElementRegistry Registry => elementRegistry ?? (elementRegistry = GetElementCatalog()?.Snapshot() ?? ElementRegistry.BuiltIns());
        private static ElementCatalog GetElementCatalog() => AssetDatabase.LoadAssetAtPath<GameResources>(ResourcesPath)?.Elements;
        private ElementInstance SelectedElement => draft?.Data?.Elements?.FirstOrDefault(element => element != null && element.Id == selectedElementId);
        private void OnElementCatalogChanged() => Repaint();

        private void RefreshElementRegistry()
        {
            var catalog = GetElementCatalog();
            if (elementRevision == ElementCatalog.Revision && elementCatalog == catalog) return;
            if (paintUndoGroup >= 0 || elementDragStart.HasValue || referenceProperty != null || EditorGUIUtility.editingTextField || !string.IsNullOrEmpty(Input.compositionString)) return;
            bool changed = elementRevision >= 0;
            elementCatalog = catalog; elementRevision = ElementCatalog.Revision;
            elementRegistry = catalog?.Snapshot() ?? ElementRegistry.BuiltIns();
            if (draft != null) RefreshValidation();
            if (changed) notice = "元素配置已刷新。当前草稿及实例覆盖值已保留，下一次试玩使用新配置。";
        }

        private void DrawElementPalette()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Toggle(selectElements, "选择", EditorStyles.miniButton, GUILayout.Width(52))) selectElements = true;
                if (GUILayout.Button("橡皮", GUILayout.Width(52))) { brush = Brush.Erase; selectElements = false; }
                using (new EditorGUI.DisabledScope(SelectedElement == null))
                    if (GUILayout.Button(showInstanceProperties ? "收起属性" : "对象属性", GUILayout.Width(76))) showInstanceProperties = !showInstanceProperties;
                elementScroll = EditorGUILayout.BeginScrollView(elementScroll, false, false, GUILayout.Height(70));
                using (new EditorGUILayout.HorizontalScope())
                foreach (var type in Registry.Types)
                {
                    var definition = elementCatalog?.Find(type.Id);
                    var sprite = definition == null ? null : definition.Icon != null ? definition.Icon : definition.Sprite;
                    var content = new GUIContent(type.Name, sprite?.texture, type.Name + "：点击选择，然后在地图绘制");
                    bool current = !selectElements && brush != Brush.Erase && selectedTypeId == type.Id;
                    if (GUILayout.Toggle(current, content, "Button", GUILayout.MinWidth(74), GUILayout.Height(48)) && !current)
                    {
                        selectedTypeId = type.Id; selectElements = false; referenceProperty = null;
                        if (Enum.TryParse(type.Id, true, out Brush legacy)) brush = legacy; else brush = Brush.Box;
                    }
                }
                EditorGUILayout.EndScrollView();
            }
        }

        private void PaintElement(GridPos position, bool erase)
        {
            RecordEdit("绘制关卡", true);
            bool changed;
            if (erase) changed = lastPaintedCell.HasValue ? LevelAuthoring.PaintLine(draft.Data, lastPaintedCell.Value, position, LevelBrush.Erase) : LevelAuthoring.Paint(draft.Data, position, LevelBrush.Erase);
            else if (lastPaintedCell.HasValue && Registry.Find(selectedTypeId)?.Role != ElementRole.Player)
                changed = LevelAuthoring.PaintLine(draft.Data, lastPaintedCell.Value, position, selectedTypeId, Registry);
            else changed = LevelAuthoring.Paint(draft.Data, position, selectedTypeId, Registry);
            if (!changed) return;
            draft.VerifiedSolution = ""; FinishEdit();
        }

        private ElementInstance ElementAt(GridPos position) => draft?.Data?.Elements?
            .Where(element => element != null && element.Position == position)
            .OrderByDescending(element => { var role = Registry.Find(element.TypeId)?.Role; return role == ElementRole.Box || role == ElementRole.Player ? 2 : role == ElementRole.Fixture ? 1 : 0; })
            .FirstOrDefault();

        private bool ProcessElementSelection(Rect board, Event evt)
        {
            if (referenceProperty != null && evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            { referenceProperty = null; referenceTargets.Clear(); evt.Use(); Repaint(); return true; }
            if (!selectElements && referenceProperty == null && !elementDragStart.HasValue) return false;
            if (evt.type == EventType.MouseUp && elementDragStart.HasValue)
            {
                if (board.Contains(evt.mousePosition))
                {
                    var position = PointToCell(board, evt.mousePosition);
                    if (draft.Data.IsInside(position) && position != elementDragStart.Value)
                    {
                        var next = elementDragSnapshot.DeepClone();
                        if (LevelAuthoring.TryMoveElement(next, selectedElementId, position, Registry, out string error))
                        { RecordEdit("移动元素", true); draft.Data = next; draft.VerifiedSolution = ""; FinishEdit(); }
                        else notice = error;
                    }
                }
                elementDragStart = null; elementDragSnapshot = null; evt.Use(); Repaint(); return true;
            }
            if (evt.type != EventType.MouseDown || evt.button != 0 || !board.Contains(evt.mousePosition)) return selectElements || referenceProperty != null;
            var cell = PointToCell(board, evt.mousePosition);
            if (!draft.Data.IsInside(cell)) return true;
            GUI.FocusControl(null);
            if (referenceProperty != null)
            {
                var target = draft.Data.Elements.FirstOrDefault(element => element != null && element.Position == cell && MatchesReference(element, referenceProperty));
                if (target == null) notice = "这个格子没有符合关联条件的对象。";
                else if (referenceTargets.Contains(target.Id)) referenceTargets.Remove(target.Id);
                else { if (referenceProperty.Kind == ParameterKind.Reference) referenceTargets.Clear(); referenceTargets.Add(target.Id); }
            }
            else
            {
                var element = ElementAt(cell); selectedElementId = element?.Id;
                showInstanceProperties = element != null;
                if (element != null) { elementDragStart = cell; elementDragSnapshot = draft.Data.DeepClone(); }
            }
            evt.Use(); Repaint(); return true;
        }

        private GridPos PointToCell(Rect board, Vector2 point) => new GridPos(Mathf.FloorToInt((point.x - board.x) / cellSize), draft.Data.Height - 1 - Mathf.FloorToInt((point.y - board.y) / cellSize));

        private void DrawElementCell(GridPos position, Rect rect)
        {
            if (draft.Data.Elements == null) return;
            int index = position.Y * draft.Data.Width + position.X;
            string terrainId = draft.Data.TerrainTypeIds != null && index < draft.Data.TerrainTypeIds.Length ? draft.Data.TerrainTypeIds[index] : null;
            var terrainSprite = elementCatalog?.Find(terrainId)?.Sprite;
            if (terrainSprite != null) DrawElementSprite(rect, terrainSprite);
            foreach (var element in draft.Data.Elements.Where(item => item != null && item.Position == position)
                .OrderBy(item => { var role = Registry.Find(item.TypeId)?.Role; return role == ElementRole.Player ? 3 : role == ElementRole.Box ? 2 : role == ElementRole.Fixture ? 1 : 0; }))
            {
                var type = Registry.Find(element.TypeId);
                var definition = elementCatalog?.Find(element.TypeId);
                var sprite = definition?.Sprite;
                if (sprite != null)
                    DrawElementSprite(rect, sprite);
                else if (type == null) GUI.Label(rect, "?", centeredCell);
                else GUI.Label(rect, type.Role == ElementRole.Box ? "箱" : type.Role == ElementRole.Player ? "人" : type.Role == ElementRole.Goal ? "◇" : type.Name, centeredCell);
                if (element.Id == selectedElementId) DrawOutline(rect, new Color(.4f, .95f, .8f));
                if (referenceProperty != null && referenceTargets.Contains(element.Id)) DrawOutline(new Rect(rect.x + 3, rect.y + 3, rect.width - 6, rect.height - 6), Color.yellow);
            }
        }

        private static void DrawElementSprite(Rect rect, Sprite sprite)
        {
            var texture = sprite.texture; var uv = sprite.textureRect;
            uv = new Rect(uv.x / texture.width, uv.y / texture.height, uv.width / texture.width, uv.height / texture.height);
            GUI.DrawTextureWithTexCoords(rect, texture, uv, true);
        }

        private void DrawInstanceProperties()
        {
            if (!showInstanceProperties) return;
            var instance = SelectedElement;
            if (instance == null) { showInstanceProperties = false; return; }
            EditorGUILayout.Space(8);
            var type = Registry.Find(instance.TypeId);
            EditorGUILayout.LabelField(type?.Name ?? "未知元素：" + instance.TypeId, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("位置 " + instance.Position, EditorStyles.miniLabel);
            var layers = draft.Data.Elements.Where(element => element != null && element.Position == instance.Position).ToArray();
            if (layers.Length > 1)
            {
                int current = Array.FindIndex(layers, element => element.Id == instance.Id);
                int chosen = EditorGUILayout.Popup("同格对象", current, layers.Select(element => Registry.Find(element.TypeId)?.Name ?? "未知元素").ToArray());
                if (chosen != current) { selectedElementId = layers[chosen].Id; Repaint(); return; }
            }
            if (type == null) { EditorGUILayout.HelpBox("类型未注册，原始数据仍保留。请修复元素目录后再试玩。", MessageType.Warning); return; }
            if (referenceProperty != null)
            {
                EditorGUILayout.HelpBox("点击画布中的目标对象，再次点击取消；完成后统一提交。Esc 取消。", MessageType.Info);
                EditorGUILayout.LabelField($"当前选择 {referenceTargets.Count} 个对象");
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("完成关联"))
                    {
                        var value = new ParameterValue { Key = referenceProperty.Key, Kind = referenceProperty.Kind, StringValue = referenceTargets.FirstOrDefault() ?? "", StringValues = referenceTargets.ToArray() };
                        referenceProperty = null; ApplyInstanceParameter(value);
                    }
                    if (GUILayout.Button("取消")) { referenceProperty = null; referenceTargets.Clear(); }
                }
                return;
            }
            var properties = type.Properties ?? Array.Empty<PropertyDescriptor>();
            if (properties.Length == 0) { EditorGUILayout.LabelField("没有可调整的实例属性。", EditorStyles.wordWrappedMiniLabel); return; }
            propertyScroll = EditorGUILayout.BeginScrollView(propertyScroll, GUILayout.MaxHeight(250));
            foreach (var property in properties)
            {
                var value = Registry.GetValue(instance, property.Key) ?? property.DefaultValue?.DeepClone() ?? new ParameterValue { Key = property.Key, Kind = property.Kind };
                bool overridden = instance.Overrides?.Any(item => item != null && item.Key == property.Key) == true;
                EditorGUILayout.LabelField(property.Name ?? property.Key, EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                var next = value.DeepClone();
                switch (property.Kind)
                {
                    case ParameterKind.Boolean: next.BoolValue = EditorGUILayout.Toggle("", value.BoolValue); break;
                    case ParameterKind.Integer: next.IntValue = EditorGUILayout.DelayedIntField(value.IntValue); break;
                    case ParameterKind.Float: next.FloatValue = EditorGUILayout.DelayedFloatField(value.FloatValue); break;
                    case ParameterKind.String: next.StringValue = EditorGUILayout.DelayedTextField(value.StringValue ?? ""); break;
                    case ParameterKind.Enum:
                        var options = property.Options ?? Array.Empty<string>();
                        if (options.Length > 0) next.StringValue = options[Mathf.Clamp(EditorGUILayout.Popup(Array.IndexOf(options, value.StringValue), options), 0, options.Length - 1)];
                        break;
                    case ParameterKind.Reference: case ParameterKind.References:
                        var ids = property.Kind == ParameterKind.References ? value.StringValues ?? Array.Empty<string>() : string.IsNullOrEmpty(value.StringValue) ? Array.Empty<string>() : new[] { value.StringValue };
                        int broken = ids.Count(id => !draft.Data.Elements.Any(element => element != null && element.Id == id));
                        if (GUILayout.Button($"点选关联（{ids.Length} 个" + (broken > 0 ? $"，{broken} 个失效）" : "）")))
                        { referenceProperty = property; referenceTargets.Clear(); referenceTargets.AddRange(ids); }
                        break;
                    default: EditorGUILayout.LabelField("未支持的属性，数据已保留。"); break;
                }
                if (EditorGUI.EndChangeCheck() && property.Kind != ParameterKind.Reference && property.Kind != ParameterKind.References) ApplyInstanceParameter(next);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(overridden ? "实例覆盖" : "继承默认", EditorStyles.miniLabel);
                    using (new EditorGUI.DisabledScope(!overridden))
                        if (GUILayout.Button("恢复默认", EditorStyles.miniButton, GUILayout.Width(70)))
                        {
                            RecordEdit("恢复默认属性", true);
                            if (LevelAuthoring.ResetParameter(draft.Data, selectedElementId, property.Key, Registry, out string error)) { draft.VerifiedSolution = ""; FinishEdit(); }
                            else notice = error;
                        }
                }
                EditorGUILayout.Space(4);
            }
            EditorGUILayout.EndScrollView();
        }

        private bool MatchesReference(ElementInstance element, PropertyDescriptor property)
        {
            var type = Registry.Find(element.TypeId);
            return type != null && type.Role == property.ReferenceRole && (property.ReferenceMechanic == MechanicKind.None || type.HasMechanic(property.ReferenceMechanic));
        }

        private void ApplyInstanceParameter(ParameterValue value)
        {
            RecordEdit("修改元素属性", true);
            if (!LevelAuthoring.TrySetParameter(draft.Data, selectedElementId, value, Registry, out string error)) { notice = error; return; }
            draft.VerifiedSolution = ""; FinishEdit();
        }
    }
}
