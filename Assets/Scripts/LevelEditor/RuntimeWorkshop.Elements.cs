#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Sokoban.Content;
using Sokoban.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sokoban.Runtime
{
    public sealed partial class RuntimeWorkshop
    {
        private ElementRegistry registry;
        private int elementRevision = -1;
        private ElementCatalog elementCatalog;
        private readonly List<string> paletteIds = new List<string>();
        private string selectedTypeId = "wall", selectedInstanceId;
        private string selectionBeforeGesture;
        private UiView objectPanel;
        private bool propertiesVisible;
        private PropertyDescriptor referenceProperty;
        private readonly List<string> referenceTargets = new List<string>();
        private string referenceOwner;
        public string SelectedTypeId => selectedTypeId;
        public string SelectedInstanceId => selectedInstanceId;
        public bool IsPickingReferences => referenceProperty != null;
        public UiView FindElementView(string typeId)
        {
            int index = paletteIds.IndexOf(typeId);
            var entries = ui != null ? ui.Get<UiList>("Element list").Items : null;
            return index >= 0 && entries != null && index < entries.Count ? entries[index] : null;
        }
        private ElementRegistry Registry => registry ?? (registry = resources != null && resources.Elements != null ? resources.Elements.Snapshot() : ElementRegistry.BuiltIns());
        private ElementTypeSpec SelectedType => Registry.Find(selectedTypeId);

        public void SetElement(string typeId)
        {
            CancelGesture(); CancelReferencePicking();
            selectedTypeId = typeId;
            if (Enum.TryParse(typeId, true, out LevelBrush oldBrush)) Brush = oldBrush;
            if (SelectedType?.Role == ElementRole.Player || Tool == WorkshopTool.Eraser) Tool = WorkshopTool.Brush;
            selectedInstanceId = null; selection = null; issueHighlight = null;
            CloseObjectProperties(); RenderDraft();
        }

        private void PaintCell(LevelDefinition level, GridPos cell)
        {
            if (Tool == WorkshopTool.Eraser) LevelAuthoring.Paint(level, cell, LevelBrush.Erase);
            else LevelAuthoring.Paint(level, cell, selectedTypeId, Registry);
        }

        private ElementInstance ElementAt(GridPos cell) => draft?.Elements?
            .Where(element => element != null && element.Position == cell)
            .OrderByDescending(element => { var role = Registry.Find(element.TypeId)?.Role; return role == ElementRole.Player || role == ElementRole.Box ? 2 : role == ElementRole.Fixture ? 1 : 0; })
            .FirstOrDefault();

        private ElementInstance SelectedInstance => draft?.Elements?.FirstOrDefault(element => element != null && element.Id == selectedInstanceId);

        private Sprite ElementSprite(string id)
        {
            var definition = resources != null && resources.Elements != null ? resources.Elements.Find(id) : null;
            if (definition != null) return definition.Icon != null ? definition.Icon : definition.Sprite;
            var visuals = board != null ? board.Visuals : resources != null ? resources.Visuals : null;
            if (visuals == null) return null;
            switch (id)
            {
                case "floor": return visuals.Floor;
                case "wall": return visuals.Wall;
                case "goal": return visuals.Goal;
                case "player": return visuals.Player;
                case "box": case "sliding-box": return visuals.Box;
                default: return null;
            }
        }

        private void RefreshElementConfiguration(bool force = false)
        {
            var catalog = resources != null ? resources.Elements : null;
            if (!force && elementRevision == ElementCatalog.Revision && elementCatalog == catalog) return;
            // A stroke, a clipboard placement and unfinished input all own the configuration with which they began.
            if (HasPendingGesture || referenceProperty != null || UiFactory.IsTextInputFocused()) return;
            bool changed = elementRevision >= 0;
            elementCatalog = catalog; elementRevision = ElementCatalog.Revision;
            registry = catalog != null ? catalog.Snapshot() : ElementRegistry.BuiltIns();
            if (ui != null)
            {
                BuildElementPalette(); RefreshObjectProperties(); RenderDraft();
                if (changed) SetStatus("元素配置已更新。草稿与选择已保留，下一次试玩使用更新后的配置。");
            }
        }

        private void BuildElementPalette()
        {
            if (ui == null) return;
            var list = ui.Get<UiList>("Element list"); list.Clear();
            brushButtons.Clear(); paletteIds.Clear();
            foreach (var type in Registry.Types)
            {
                var item = list.Add(); string id = type.Id;
                var button = item.Button("Element button", () => SetElement(id));
                var visual = button.GetComponent<UiButtonVisual>();
                visual.Label.text = type.Name ?? id; visual.Label.richText = false;
                visual.Icon.sprite = ElementSprite(id); visual.Icon.enabled = visual.Icon.sprite != null;
                var hint = button.GetComponent<WorkshopPointerHint>();
                hint.DisplayName = type.Name; hint.Description = type.Name + "：选择后在画布放置，使用框选工具选中对象后可编辑属性。";
                hint.Entered = () => SetStatus(hint.Description);
                hint.Exited = () => SetStatus("左键绘制 · 中键平移 · 滚轮缩放 · Shift 框选");
                brushButtons.Add(button); paletteIds.Add(id);
            }
            ui.Button("Object properties", () => { if (referenceProperty != null) CommitReferencePicking(); else if (propertiesVisible) CloseObjectProperties(); else OpenObjectProperties(); });
            RefreshElementSelection();
        }

        private void RefreshElementSelection()
        {
            for (int i = 0; i < brushButtons.Count; i++) MarkButton(brushButtons[i], selectedTypeId == paletteIds[i] && Tool != WorkshopTool.Eraser);
            if (ui == null) return;
            var entry = ui.Get<Button>("Object properties");
            entry.interactable = SelectedInstance != null || referenceProperty != null;
            entry.GetComponent<UiButtonVisual>().Label.text = referenceProperty != null ? "完成关联" : "对象属性";
        }

        public bool SelectElementAt(GridPos cell)
        {
            var candidates = (draft?.Elements ?? Array.Empty<ElementInstance>()).Where(element => element != null && element.Position == cell)
                .OrderByDescending(element => { var role = Registry.Find(element.TypeId)?.Role; return role == ElementRole.Player || role == ElementRole.Box ? 2 : role == ElementRole.Fixture ? 1 : 0; }).ToArray();
            int current = Array.FindIndex(candidates, element => element.Id == selectedInstanceId);
            selectedInstanceId = candidates.Length == 0 ? null : candidates[(current + 1) % candidates.Length].Id;
            RefreshObjectProperties(); Refresh();
            return selectedInstanceId != null;
        }

        public bool SelectElementInstance(string instanceId)
        {
            if (draft?.Elements?.Any(element => element != null && element.Id == instanceId) != true) return false;
            selectedInstanceId = instanceId; RefreshObjectProperties(); Refresh(); return true;
        }

        public void OpenObjectProperties()
        {
            if (SelectedInstance == null) { SetStatus("请先使用框选工具，单击一个对象。"); return; }
            if (root == null) return;
            CancelGesture();
            if (objectPanel == null)
            {
                if (WorkshopViews.Catalog == null || WorkshopViews.Catalog.ObjectProperties == null)
                { SetStatus("对象属性界面缺失，请运行“升级元素编辑界面”。"); return; }
                objectPanel = Instantiate(WorkshopViews.Catalog.ObjectProperties, root, false);
            }
            propertiesVisible = true; objectPanel.gameObject.SetActive(true); objectPanel.transform.SetAsLastSibling();
            objectPanel.Button("Close properties", CloseObjectProperties);
            objectPanel.Button("Next cell object", () => { if (SelectedInstance != null) SelectElementAt(SelectedInstance.Position); });
            RefreshObjectProperties();
            HighlightSelectedReferences();
        }

        private void CloseObjectProperties()
        {
            propertiesVisible = false;
            if (objectPanel != null) objectPanel.gameObject.SetActive(false);
        }

        private void RefreshObjectProperties()
        {
            if (objectPanel == null || !propertiesVisible || UiFactory.IsTextInputFocused()) return;
            var instance = SelectedInstance;
            var list = objectPanel.Get<UiList>("Property list"); list.Clear();
            if (instance == null) { CloseObjectProperties(); return; }
            var type = Registry.Find(instance.TypeId);
            objectPanel.Text("Object heading", type?.Name ?? "未知元素：" + instance.TypeId).richText = false;
            objectPanel.Text("Object identity", $"位置 {instance.Position}").richText = false;
            objectPanel.Get<Button>("Next cell object").interactable = draft.Elements.Count(element => element != null && element.Position == instance.Position) > 1;
            objectPanel.Text("Property notice", type == null ? "该类型未注册。原始数据已保留，修复目录后再试玩。" : (type.Properties?.Length ?? 0) == 0 ? "这个元素没有可调整的实例属性。" : "继承类型默认值；修改后只影响当前实例。");
            if (type == null) return;
            foreach (var descriptor in type.Properties ?? Array.Empty<PropertyDescriptor>())
            {
                var property = descriptor;
                var value = Registry.GetValue(instance, property.Key) ?? property.DefaultValue?.DeepClone() ?? new ParameterValue { Key = property.Key, Kind = property.Kind };
                bool overridden = instance.Overrides != null && instance.Overrides.Any(item => item != null && item.Key == property.Key);
                var row = list.Add();
                row.Text("Property name", property.Name ?? property.Key).richText = false;
                row.Text("Property source", overridden ? "实例覆盖" : "继承默认");
                var reset = row.Button("Reset property", () => ResetSelectedParameter(property.Key)); reset.interactable = overridden;
                var input = row.Get<TMP_InputField>("Property input");
                var action = row.Get<Button>("Property action");
                bool useInput = property.Kind == ParameterKind.Integer || property.Kind == ParameterKind.Float || property.Kind == ParameterKind.String;
                input.gameObject.SetActive(useInput); action.gameObject.SetActive(!useInput);
                if (useInput)
                {
                    input.richText = false; input.textComponent.richText = false;
                    input.contentType = property.Kind == ParameterKind.Integer ? TMP_InputField.ContentType.IntegerNumber : property.Kind == ParameterKind.Float ? TMP_InputField.ContentType.DecimalNumber : TMP_InputField.ContentType.Standard;
                    input.SetTextWithoutNotify(FormatParameter(value));
                    input.onEndEdit.AddListener(text => { if (TryParseParameter(property, text, out var parsed)) SetSelectedParameter(parsed); else SetStatus("属性值格式不正确，请按提示输入。"); });
                }
                else if (property.Kind == ParameterKind.Boolean)
                    row.Button("Property action", () => { var next = value.DeepClone(); next.BoolValue = !value.BoolValue; SetSelectedParameter(next); }, value.BoolValue ? "是" : "否");
                else if (property.Kind == ParameterKind.Enum)
                    row.Button("Property action", () => { var options = property.Options ?? Array.Empty<string>(); if (options.Length == 0) return; var next = value.DeepClone(); next.StringValue = options[(Array.IndexOf(options, value.StringValue) + 1) % options.Length]; SetSelectedParameter(next); }, string.IsNullOrEmpty(value.StringValue) ? "选择" : value.StringValue);
                else if (property.Kind == ParameterKind.Reference || property.Kind == ParameterKind.References)
                    row.Button("Property action", () => BeginReferencePicking(property.Key), ReferenceSummary(value) + " · 点选");
                else { action.interactable = false; action.GetComponent<UiButtonVisual>().Label.text = "未支持的属性（数据保留）"; }
            }
        }

        public bool SetSelectedParameter(ParameterValue value)
        {
            if (SelectedInstance == null) return false;
            var before = draft.DeepClone();
            if (!LevelAuthoring.TrySetParameter(draft, selectedInstanceId, value, Registry, out string error)) { SetStatus(error); return false; }
            CommitChange(before); RenderDraft(); RefreshObjectProperties(); return true;
        }

        public bool ResetSelectedParameter(string key)
        {
            if (SelectedInstance == null) return false;
            var before = draft.DeepClone();
            if (!LevelAuthoring.ResetParameter(draft, selectedInstanceId, key, Registry, out string error)) { SetStatus(error); return false; }
            CommitChange(before); RenderDraft(); RefreshObjectProperties(); return true;
        }

        public bool BeginReferencePicking(string key)
        {
            var instance = SelectedInstance;
            var property = Registry.Find(instance?.TypeId)?.Properties?.FirstOrDefault(item => item.Key == key);
            if (instance == null || property == null || property.Kind != ParameterKind.Reference && property.Kind != ParameterKind.References) return false;
            CancelGesture(); referenceProperty = property; referenceOwner = instance.Id; referenceTargets.Clear();
            var value = Registry.GetValue(instance, key);
            if (property.Kind == ParameterKind.References) referenceTargets.AddRange(value?.StringValues ?? Array.Empty<string>());
            else if (!string.IsNullOrEmpty(value?.StringValue)) referenceTargets.Add(value.StringValue);
            CloseObjectProperties();
            SetStatus("点选画布上的关联对象，再次点击取消选择；点击“完成关联”提交，Esc 取消。");
            HighlightReferences(); RefreshElementSelection(); return true;
        }

        public bool PickReferenceAt(GridPos cell)
        {
            if (referenceProperty == null) return false;
            var target = draft.Elements?.FirstOrDefault(element => element != null && element.Position == cell && MatchesReference(element, referenceProperty));
            if (target == null) { SetStatus("这个格子没有符合当前关联条件的对象。"); return false; }
            if (referenceTargets.Contains(target.Id)) referenceTargets.Remove(target.Id);
            else { if (referenceProperty.Kind == ParameterKind.Reference) referenceTargets.Clear(); referenceTargets.Add(target.Id); }
            HighlightReferences(); return true;
        }

        private bool MatchesReference(ElementInstance element, PropertyDescriptor property)
        {
            var type = Registry.Find(element.TypeId);
            return type != null && type.Role == property.ReferenceRole && (property.ReferenceMechanic == MechanicKind.None || type.HasMechanic(property.ReferenceMechanic));
        }

        public bool CommitReferencePicking()
        {
            if (referenceProperty == null) return false;
            var value = new ParameterValue { Key = referenceProperty.Key, Kind = referenceProperty.Kind, StringValue = referenceTargets.FirstOrDefault() ?? "", StringValues = referenceTargets.ToArray() };
            selectedInstanceId = referenceOwner;
            referenceProperty = null; referenceOwner = null; referenceTargets.Clear();
            bool result = SetSelectedParameter(value); OpenObjectProperties(); return result;
        }

        public void CancelReferencePicking()
        {
            referenceProperty = null; referenceOwner = null; referenceTargets.Clear();
            if (board != null) board.ClearHighlights();
            RefreshElementSelection();
        }

        private void HighlightReferences()
        {
            if (board == null || draft == null) return;
            board.Highlight((draft.Elements ?? Array.Empty<ElementInstance>()).Where(element => element != null && (referenceTargets.Contains(element.Id) || element.Id == referenceOwner)).Select(element => element.Position));
        }

        private void HighlightSelectedReferences()
        {
            var instance = SelectedInstance;
            if (instance == null || board == null || !propertiesVisible) return;
            var ids = new HashSet<string> { instance.Id };
            foreach (var property in Registry.Find(instance.TypeId)?.Properties ?? Array.Empty<PropertyDescriptor>())
            {
                var value = Registry.GetValue(instance, property.Key);
                if (value?.Kind == ParameterKind.Reference && !string.IsNullOrEmpty(value.StringValue)) ids.Add(value.StringValue);
                if (value?.Kind == ParameterKind.References) foreach (string id in value.StringValues ?? Array.Empty<string>()) ids.Add(id);
            }
            board.Highlight((draft.Elements ?? Array.Empty<ElementInstance>()).Where(element => element != null && ids.Contains(element.Id)).Select(element => element.Position));
        }

        private string ReferenceSummary(ParameterValue value)
        {
            var ids = value.Kind == ParameterKind.References ? value.StringValues ?? Array.Empty<string>() : string.IsNullOrEmpty(value.StringValue) ? Array.Empty<string>() : new[] { value.StringValue };
            int missing = ids.Count(id => !draft.Elements.Any(element => element != null && element.Id == id));
            return ids.Length == 0 ? "未关联" : missing > 0 ? $"{ids.Length} 个关联（{missing} 个失效）" : $"{ids.Length} 个关联";
        }

        private static string FormatParameter(ParameterValue value) => value.Kind == ParameterKind.Integer ? value.IntValue.ToString(CultureInfo.InvariantCulture) : value.Kind == ParameterKind.Float ? value.FloatValue.ToString(CultureInfo.InvariantCulture) : value.StringValue ?? "";
        private static bool TryParseParameter(PropertyDescriptor property, string text, out ParameterValue result)
        {
            result = new ParameterValue { Key = property.Key, Kind = property.Kind, StringValue = text };
            if (property.Kind == ParameterKind.Integer) return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out result.IntValue);
            if (property.Kind == ParameterKind.Float) return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out result.FloatValue);
            return true;
        }
    }
}
#endif
