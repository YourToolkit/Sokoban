#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Sokoban.Content;
using Sokoban.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Sokoban.Runtime
{
    public enum WorkshopTool { Brush, Line, HollowRectangle, FilledRectangle, Select, Fill, Eraser }

    /// <summary>Owns a detached authoring document, gesture transactions and UI. Asset writes use an injected service.</summary>
    public sealed class RuntimeWorkshop : MonoBehaviour
    {
        private enum Gesture { None, Stroke, Shape, Selection, MoveRegion, MoveActor }
        private GameController app;
        private WorkshopViews views;
        private UiView ui;
        public UiView View => ui;
        public void HideView() { if (views != null) views.Hide(); }
        private Canvas canvas;
        private BoardView board;
        private GameResources resources;
        private RectTransform root, modal;
        private LevelDefinition draft, saved, beforeGesture, preview;
        private LevelAsset source;
        private readonly List<LevelDefinition> undo = new List<LevelDefinition>();
        private readonly List<LevelDefinition> redo = new List<LevelDefinition>();
        private readonly List<Button> toolButtons = new List<Button>();
        private readonly List<Button> brushButtons = new List<Button>();
        private readonly List<RaycastResult> raycasts = new List<RaycastResult>();
        private Button undoButton, redoButton, saveButton, playButton;
        private TMP_Text titleText, statusText, selectionText, modalFeedback, currentToolText;
        private Action modalCancel;
        private Gesture gesture;
        private GridPos gestureStart, lastCell, hoverCell;
        private GridRect? selection, movingSelection;
        private GridPos? issueHighlight;
        private LevelRegion clipboard;
        private bool pastePending, previewValid, loaded, visible, panning, pointerGesture, hoverVisible;
        private Vector2 lastPointer;
        private BoardViewSnapshot view;
        private string status = "左键绘制 · 中键平移 · 滚轮缩放 · Shift 框选";
        private const int HistoryLimit = 100;

        public LevelDefinition Draft => draft?.DeepClone();
        public LevelAsset Source => source;
        public WorkshopTool Tool { get; private set; } = WorkshopTool.Brush;
        public LevelBrush Brush { get; private set; } = LevelBrush.Wall;
        public bool IsDirty => draft != null && !SameDocument(draft, saved);
        public bool CanUndo => undo.Count > 0;
        public bool CanRedo => redo.Count > 0;
        public bool HasPendingGesture => gesture != Gesture.None || pastePending;
        public bool HasSelection => selection.HasValue;
        public bool IsVisible => visible;
        public bool InputEnabled { get; set; } = true;

        public void Initialize(GameController owner, Canvas interfaceCanvas, BoardView boardView, GameResources gameResources)
        { app = owner; canvas = interfaceCanvas; board = boardView; resources = gameResources; }

        public void Open()
        {
            if (!loaded) SetDocument(app.WorkshopSourceDefinition ?? LevelAuthoring.CreateBlank(), app.WorkshopSourceAsset);
            app.BeginWorkshopView();
            if (views == null) views = new WorkshopViews(canvas.transform);
            ui = views.Main; root = ui.Rect; root.gameObject.SetActive(true); root.SetAsLastSibling();
            visible = true;
            modal = null;
            BuildInterface();
            board.SetViewport(ui.Get<BoardViewport>("Board viewport"));
            board.ShowDraft(draft, !view.Valid);
            if (view.Valid) board.RestoreView(view);
            RenderDraft();
        }

        public void SetDocument(LevelDefinition definition, LevelAsset asset = null)
        {
            CancelGesture();
            draft = (definition ?? LevelAuthoring.CreateBlank()).DeepClone();
            saved = draft.DeepClone();
            source = asset;
            undo.Clear(); redo.Clear();
            selection = null; clipboard = null; issueHighlight = null; hoverVisible = false;
            view = default;
            loaded = true;
            status = "左键绘制 · 中键平移 · 滚轮缩放 · Shift 框选";
        }

        public void SetTool(WorkshopTool tool)
        {
            CancelGesture();
            Tool = tool;
            issueHighlight = null;
            if (tool != WorkshopTool.Select) selection = null;
            if (Brush == LevelBrush.Player && IsBatchTool(tool)) Brush = LevelBrush.Wall;
            RenderDraft();
        }

        public void SetBrush(LevelBrush brush)
        {
            CancelGesture();
            Brush = brush;
            issueHighlight = null;
            selection = null;
            if (brush == LevelBrush.Player || Tool == WorkshopTool.Eraser) Tool = WorkshopTool.Brush;
            RenderDraft();
        }

        public void BeginGesture(GridPos cell, bool forceSelection = false)
        {
            if (draft == null || !draft.IsInside(cell)) return;
            issueHighlight = null;
            if (pastePending) { CommitPaste(cell); return; }
            CancelGesture();
            gestureStart = lastCell = hoverCell = cell;
            beforeGesture = draft.DeepClone();
            previewValid = true;
            if (forceSelection || Tool == WorkshopTool.Select || selection.HasValue && selection.Value.Contains(cell))
            {
                if (!forceSelection && selection.HasValue && selection.Value.Contains(cell))
                { gesture = Gesture.MoveRegion; movingSelection = selection; }
                else if (!forceSelection && HasActor(cell)) gesture = Gesture.MoveActor;
                else { gesture = Gesture.Selection; selection = new GridRect(cell.X, cell.Y, 1, 1); }
            }
            else if (Tool == WorkshopTool.Fill)
            {
                LevelAuthoring.FloodFill(draft, cell, Brush);
                CommitChange(beforeGesture);
                beforeGesture = null;
            }
            else if (IsBatchTool(Tool)) gesture = Gesture.Shape;
            else
            {
                gesture = Gesture.Stroke;
                LevelAuthoring.Paint(draft, cell, Tool == WorkshopTool.Eraser ? LevelBrush.Erase : Brush);
            }
            RenderDraft();
        }

        public void UpdateGesture(GridPos cell)
        {
            bool sameCell = hoverCell == cell;
            hoverCell = cell;
            if (pastePending) { if (!sameCell || preview == null) PreviewPaste(cell); return; }
            if (gesture == Gesture.None || draft == null) return;
            if (cell == lastCell && (gesture == Gesture.Stroke || preview != null)) return;
            if (gesture == Gesture.Stroke)
            {
                if (!draft.IsInside(cell))
                {
                    CancelGesture();
                    SetStatus("笔画已移出地图，本次笔画已取消。");
                    return;
                }
                if (draft.IsInside(cell))
                {
                    var brush = Tool == WorkshopTool.Eraser ? LevelBrush.Erase : Brush;
                    if (brush == LevelBrush.Player) LevelAuthoring.Paint(draft, cell, brush);
                    else LevelAuthoring.PaintLine(draft, lastCell, cell, brush);
                    lastCell = cell;
                }
                RenderDraft();
                return;
            }
            preview = beforeGesture.DeepClone();
            previewValid = draft.IsInside(cell);
            string error = "";
            if (gesture == Gesture.Selection)
            {
                cell = ClampCell(cell);
                selection = GridRect.FromPoints(gestureStart, cell);
                preview = null;
            }
            else if (gesture == Gesture.Shape && previewValid)
            {
                if (Tool == WorkshopTool.Line) LevelAuthoring.PaintLine(preview, gestureStart, cell, Brush);
                else LevelAuthoring.PaintRectangle(preview, gestureStart, cell, Brush, Tool == WorkshopTool.FilledRectangle);
            }
            else if (gesture == Gesture.MoveActor)
                previewValid = LevelAuthoring.TryMoveActor(preview, gestureStart, cell, out error);
            else if (gesture == Gesture.MoveRegion && movingSelection.HasValue)
            {
                var area = movingSelection.Value;
                var destination = new GridPos(area.MinX + cell.X - gestureStart.X, area.MinY + cell.Y - gestureStart.Y);
                previewValid = LevelAuthoring.TryMoveRegion(preview, area, destination, out error);
            }
            if (!previewValid && !string.IsNullOrEmpty(error)) SetStatus(error);
            lastCell = cell;
            RenderDraft();
        }

        public void EndGesture(GridPos cell)
        {
            if (gesture == Gesture.None) return;
            UpdateGesture(cell);
            if (gesture != Gesture.Selection)
            {
                if (gesture != Gesture.Stroke && previewValid && preview != null)
                {
                    draft = preview;
                    if (gesture == Gesture.MoveRegion && movingSelection.HasValue)
                    {
                        var area = movingSelection.Value;
                        selection = new GridRect(area.MinX + cell.X - gestureStart.X, area.MinY + cell.Y - gestureStart.Y, area.Width, area.Height);
                    }
                }
                CommitChange(beforeGesture);
            }
            gesture = Gesture.None; beforeGesture = null; preview = null; movingSelection = null; pointerGesture = false;
            RenderDraft();
        }

        public void CancelGesture()
        {
            if (gesture == Gesture.Stroke && beforeGesture != null) draft = beforeGesture;
            gesture = Gesture.None; beforeGesture = null; preview = null; movingSelection = null; pointerGesture = false;
            pastePending = false;
            RenderDraft();
        }

        public void UndoEdit()
        {
            CancelGesture();
            if (!CanUndo) return;
            redo.Add(draft.DeepClone());
            RestoreHistory(undo[undo.Count - 1]); undo.RemoveAt(undo.Count - 1);
            selection = null; SetStatus("已撤销。Ctrl+Y 可以重做。"); RenderDraft();
        }

        public void RedoEdit()
        {
            CancelGesture();
            if (!CanRedo) return;
            undo.Add(draft.DeepClone());
            RestoreHistory(redo[redo.Count - 1]); redo.RemoveAt(redo.Count - 1);
            selection = null; SetStatus("已重做。"); RenderDraft();
        }

        private void RestoreHistory(LevelDefinition snapshot)
        {
            var restored = snapshot.DeepClone();
            restored.Id = draft.Id; restored.LayoutVersion = draft.LayoutVersion;
            draft = restored;
        }

        private void CommitChange(LevelDefinition before)
        {
            if (before == null || SameDocument(before, draft)) { Refresh(); return; }
            undo.Add(before.DeepClone());
            if (undo.Count > HistoryLimit) undo.RemoveAt(0);
            redo.Clear();
            SetStatus("草稿已更新。Ctrl+Z 撤销；保存后修改才会写入项目关卡。");
            Refresh();
        }

        public bool CopySelection()
        {
            if (!selection.HasValue || draft == null) { SetStatus("请先使用框选工具或按住 Shift 选择区域。"); return false; }
            clipboard = LevelAuthoring.ReadRegion(draft, selection.Value);
            SetStatus(clipboard.HasPlayer ? "已复制区域。粘贴将跳过玩家，点击画布放置，Esc 取消。" : "已复制区域。Ctrl+V 预览粘贴，点击画布放置。");
            return true;
        }

        public bool BeginPaste()
        {
            if (clipboard == null) { SetStatus("请先框选区域并按 Ctrl+C 复制。"); return false; }
            CancelGesture();
            pastePending = true;
            PreviewPaste(hoverCell);
            SetStatus("粘贴预览：点击确认，Esc 取消；空地也会覆盖目标区域。");
            return true;
        }

        private void PreviewPaste(GridPos cell)
        {
            preview = draft.DeepClone();
            previewValid = LevelAuthoring.TryPasteRegion(preview, clipboard, cell, out var error);
            if (!previewValid) SetStatus(error);
            hoverCell = cell;
            RenderDraft();
        }

        public bool CommitPaste(GridPos cell)
        {
            if (!pastePending || clipboard == null) return false;
            PreviewPaste(cell);
            if (!previewValid) return false;
            var before = draft;
            draft = preview;
            selection = new GridRect(cell.X, cell.Y, clipboard.Width, clipboard.Height);
            pastePending = false; preview = null;
            CommitChange(before); RenderDraft();
            return true;
        }

        public void DeleteSelection()
        {
            CancelGesture();
            if (!selection.HasValue) return;
            var before = draft.DeepClone();
            foreach (var cell in Cells(selection.Value)) LevelAuthoring.Paint(draft, cell, LevelBrush.Erase);
            CommitChange(before); RenderDraft();
        }

        public bool Save(LevelSaveIntent intent = LevelSaveIntent.Save)
        {
            CancelGesture();
            var writer = LevelAuthoringServices.AssetWriter;
            if (writer == null) { SetStatus("当前环境不支持保存项目资产。请在 Unity 中进入运行模式编辑。"); return false; }
            var solution = source != null && LevelAuthoring.LayoutEquals(source.Data, draft) ? source.VerifiedSolution : "";
            LevelSaveResult result;
            try { result = writer.Save(source, draft.DeepClone(), solution, intent); }
            catch (Exception error) { Debug.LogException(error); SetStatus("保存失败，请检查项目路径和文件权限后重试。"); return false; }
            if (result == null || !result.Success || result.Asset == null)
            { SetStatus(result?.Message ?? "保存未完成，草稿和编辑历史已保留。"); return false; }
            source = result.Asset;
            draft = source.ToDefinition();
            saved = draft.DeepClone();
            SetStatus(result.Message ?? "关卡已保存。");
            RenderDraft();
            return true;
        }

        public bool StartPlaytest()
        {
            CancelGesture();
            var issues = LevelValidator.Validate(draft);
            if (issues.Count > 0) { ShowIssues(issues); return false; }
            if (app == null) return false;
            view = board.CaptureView();
            visible = false;
            app.StartPlaytest(draft.DeepClone(), Open);
            return true;
        }

        public void RequestReturn()
        {
            CancelGesture();
            ConfirmDiscard(() =>
            {
                visible = false; loaded = false; view = default;
                CloseModal(); app.ReturnFromWorkshop();
            });
        }

        private void Update()
        {
            if (!InputEnabled || !visible || app == null || app.CurrentScreen != GameController.ScreenState.Workshop || draft == null) return;
            // Some Editor window changes swallow MouseUp without changing application focus.
            // Only mouse-owned gestures use this fallback; programmatic tests/previews remain deterministic.
            if (pointerGesture && !Input.GetMouseButton(0) && !Input.GetMouseButtonUp(0)) CancelGesture();
            if (UiFactory.IsTextInputFocused())
            {
                if (gesture != Gesture.None) CancelGesture();
                ClearHover();
                panning = false; return;
            }
            if (modal != null)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) { if (modalCancel != null) modalCancel(); else CloseModal(); }
                return;
            }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (HasPendingGesture) CancelGesture();
                else if (selection.HasValue) { selection = null; RenderDraft(); }
                else ShowMenu();
                return;
            }
            bool control = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
            if (control)
            {
                if (Input.GetKeyDown(KeyCode.Z)) { if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) RedoEdit(); else UndoEdit(); }
                if (Input.GetKeyDown(KeyCode.Y)) RedoEdit();
                if (Input.GetKeyDown(KeyCode.C)) CopySelection();
                if (Input.GetKeyDown(KeyCode.V)) BeginPaste();
                if (Input.GetKeyDown(KeyCode.S)) Save();
            }
            if (Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace)) DeleteSelection();
            var pointer = (Vector2)Input.mousePosition;
            bool blocked = IsPointerOverUi(pointer);
            bool inside = !blocked && board.TryScreenToCell(pointer, out _, true);
            if (Input.GetMouseButtonDown(2) && inside) { panning = true; lastPointer = pointer; }
            if (panning)
            {
                if (!Input.GetMouseButton(2)) panning = false;
                else { board.Pan(pointer - lastPointer); lastPointer = pointer; }
            }
            if (inside && Mathf.Abs(Input.mouseScrollDelta.y) > .001f) board.ZoomAt(pointer, Input.mouseScrollDelta.y);
            if (inside && board.TryScreenToCell(pointer, out var cell, true))
            {
                if (Input.GetMouseButtonDown(0))
                {
                    BeginGesture(cell, Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
                    pointerGesture = gesture != Gesture.None;
                }
                else if ((Input.GetMouseButton(0) && gesture != Gesture.None) || pastePending) UpdateGesture(cell);
                else if (gesture == Gesture.None) UpdateHover(cell);
                if (Input.GetMouseButtonUp(0)) EndGesture(cell);
            }
            else if (gesture == Gesture.Stroke && Input.GetMouseButton(0))
            {
                CancelGesture();
                SetStatus("笔画已离开画布，本次笔画已取消。");
            }
            else if (Input.GetMouseButtonUp(0) && gesture != Gesture.None) CancelGesture();
            if (!inside && gesture == Gesture.None && !pastePending) ClearHover();
        }

        private void UpdateHover(GridPos cell)
        {
            if (!draft.IsInside(cell)) { ClearHover(); return; }
            if (hoverVisible && cell == hoverCell) return;
            hoverCell = cell; hoverVisible = true;
            if (selection.HasValue) board.Highlight(Cells(selection.Value).Concat(new[] { cell }));
            else board.Highlight(new[] { cell });
            if (Tool == WorkshopTool.Select || selection.HasValue) { board.ClearBrushPreview(); return; }
            var visuals = board != null ? board.Visuals : null;
            Sprite sprite = null;
            Color tint = Color.white;
            if (Tool == WorkshopTool.Eraser) tint = new Color(1, .3f, .3f);
            else
            {
                switch (Brush)
                {
                    case LevelBrush.Floor: sprite = visuals != null ? visuals.Floor : null; tint = BoardView.FloorTint; break;
                    case LevelBrush.Wall: sprite = visuals != null ? visuals.Wall : null; tint = BoardView.WallTint; break;
                    case LevelBrush.Goal: sprite = visuals != null ? visuals.Goal : null; break;
                    case LevelBrush.Player: sprite = visuals != null ? visuals.Player : null; break;
                    case LevelBrush.Box: sprite = visuals != null ? visuals.Box : null; break;
                }
            }
            board.ShowBrushPreview(cell, sprite, tint);
        }

        private void ClearHover()
        {
            if (!hoverVisible || board == null) return;
            hoverVisible = false;
            board.ClearBrushPreview();
            if (selection.HasValue) board.Highlight(Cells(selection.Value));
            else if (issueHighlight.HasValue) board.Highlight(new[] { issueHighlight.Value }, false);
            else board.ClearHighlights();
        }

        private bool IsPointerOverUi(Vector2 pointer)
        {
            if (EventSystem.current == null) return false;
            raycasts.Clear();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = pointer }, raycasts);
            return raycasts.Count > 0;
        }

        private void OnApplicationFocus(bool focus) { if (!focus) { CancelGesture(); panning = false; } }
        private bool HasActor(GridPos cell) => draft.HasPlayer && draft.PlayerStart == cell || Array.IndexOf(draft.Boxes ?? Array.Empty<GridPos>(), cell) >= 0;
        private GridPos ClampCell(GridPos cell) => new GridPos(Mathf.Clamp(cell.X, 0, draft.Width - 1), Mathf.Clamp(cell.Y, 0, draft.Height - 1));
        private static bool IsBatchTool(WorkshopTool tool) => tool == WorkshopTool.Line || tool == WorkshopTool.HollowRectangle || tool == WorkshopTool.FilledRectangle || tool == WorkshopTool.Fill;
        private static bool SameDocument(LevelDefinition a, LevelDefinition b) => a != null && b != null && a.Name == b.Name && a.Description == b.Description && LevelAuthoring.LayoutEquals(a, b);
        private static IEnumerable<GridPos> Cells(GridRect area)
        { for (int y = area.MinY; y <= area.MaxY; y++) for (int x = area.MinX; x <= area.MaxX; x++) yield return new GridPos(x, y); }

        private void RenderDraft()
        {
            if (board == null || !visible || draft == null) return;
            hoverVisible = false;
            board.ShowDraft(previewValid && preview != null ? preview : draft);
            if (pastePending && clipboard != null) board.Highlight(Cells(new GridRect(hoverCell.X, hoverCell.Y, clipboard.Width, clipboard.Height)), previewValid);
            else if (gesture == Gesture.MoveRegion && movingSelection.HasValue)
            {
                var area = movingSelection.Value;
                board.Highlight(Cells(new GridRect(area.MinX + lastCell.X - gestureStart.X, area.MinY + lastCell.Y - gestureStart.Y, area.Width, area.Height)), previewValid);
            }
            else if (gesture == Gesture.MoveActor) board.Highlight(new[] { lastCell }, previewValid);
            else if (selection.HasValue) board.Highlight(Cells(selection.Value));
            else if (issueHighlight.HasValue) board.Highlight(new[] { issueHighlight.Value }, false);
            Refresh();
        }

        private void BuildInterface()
        {
            toolButtons.Clear(); brushButtons.Clear();
            titleText = ui.Get<TMP_Text>("Document title");
            selectionText = ui.Get<TMP_Text>("Selection information");
            currentToolText = ui.Get<TMP_Text>("Current tool");
            statusText = ui.Get<TMP_Text>("Workshop status");
            var brushes = new[] { LevelBrush.Floor, LevelBrush.Wall, LevelBrush.Goal, LevelBrush.Player, LevelBrush.Box };
            var config = board.Visuals;
            var sprites = new[] { config.Floor, config.Wall, config.Goal, config.Player, config.Box };
            for (int i = 0; i < brushes.Length; i++)
            {
                var brush = brushes[i];
                var button = ui.Button("Element " + brush, () => SetBrush(brush));
                button.GetComponent<UiButtonVisual>().Icon.sprite = sprites[i]; brushButtons.Add(button);
            }
            foreach (WorkshopTool tool in Enum.GetValues(typeof(WorkshopTool)))
            {
                var button = ui.Button("Tool " + tool, () => SetTool(tool)); toolButtons.Add(button);
                var hint = button.GetComponent<WorkshopPointerHint>();
                string previous = "";
                hint.Entered = () => { previous = status; SetStatus(hint.Description); };
                hint.Exited = () => { if (status == hint.Description) SetStatus(previous); };
            }
            ui.Button("Workshop menu", ShowMenu); ui.Button("Workshop settings", ShowSettings);
            undoButton = ui.Button("Undo edit", UndoEdit); redoButton = ui.Button("Redo edit", RedoEdit);
            ui.Button("Fit canvas", () => { board.ResetView(); view = board.CaptureView(); });
            saveButton = ui.Button("Save level", () => Save()); playButton = ui.Button("Playtest level", () => StartPlaytest());
            ui.Button("Return to game", RequestReturn);
        }

        private void Refresh()
        {
            if (draft == null) return;
            if (titleText != null) titleText.text = (draft.Name ?? "未命名关卡") + (IsDirty ? " *" : "");
            if (statusText != null) statusText.text = status;
            if (currentToolText != null) currentToolText.text = ui.Get<WorkshopPointerHint>("Tool " + Tool).DisplayName;
            if (selectionText != null)
                selectionText.text = $"{draft.Width} × {draft.Height} 格    箱子 {draft.Boxes?.Length ?? 0} / 目标 {draft.Goals?.Length ?? 0}\n" +
                    (selection.HasValue ? $"选区 {selection.Value.Width} × {selection.Value.Height} · 拖动移动整个区域" : "选中框选工具可直接拖动玩家或箱子");
            if (undoButton != null) undoButton.interactable = CanUndo;
            if (redoButton != null) redoButton.interactable = CanRedo;
            if (saveButton != null) saveButton.interactable = LevelAuthoringServices.AssetWriter != null;
            if (playButton != null) playButton.interactable = draft != null;
            var brushes = new[] { LevelBrush.Floor, LevelBrush.Wall, LevelBrush.Goal, LevelBrush.Player, LevelBrush.Box };
            for (int i = 0; i < brushButtons.Count; i++) MarkButton(brushButtons[i], Brush == brushes[i] && Tool != WorkshopTool.Eraser);
            for (int i = 0; i < toolButtons.Count; i++)
            {
                toolButtons[i].interactable = Brush != LevelBrush.Player || !IsBatchTool((WorkshopTool)i);
                MarkButton(toolButtons[i], Tool == (WorkshopTool)i);

            }
        }

        private static void MarkButton(Button button, bool selected) => button.GetComponent<UiButtonVisual>().Select(selected);
        private void SetStatus(string message)
        {
            status = message ?? "";
            if (statusText != null) statusText.text = status;
            if (modalFeedback != null) modalFeedback.text = status;
        }

        private UiView NewModal(string name, string heading)
        {
            CancelGesture(); CloseModal();
            var panel = views.OpenDialog(name); modal = panel.Rect;
            panel.Text("Heading", heading); modalFeedback = panel.Text("Dialog feedback", "");
            return panel;
        }
        private void CloseModal()
        { if (views != null) views.CloseDialog(); modal = null; modalFeedback = null; modalCancel = null; }

        private void ShowMenu()
        {
            var panel = NewModal("Workshop menu dialog", "关卡菜单");
            panel.Button("New level", () => ConfirmDiscard(() => { SetDocument(LevelAuthoring.CreateBlank()); CloseModal(); RenderDraft(); board.ResetView(); }));
            panel.Button("Open level", () => ConfirmDiscard(() => ShowOpen()));
            panel.Button("Save as", () => { if (Save(LevelSaveIntent.SaveAs)) CloseModal(); });
            panel.Button("Add to catalog", () => { if (Save(LevelSaveIntent.SaveAndAddToCatalog)) CloseModal(); });
            panel.Button("Validate level", () => ShowIssues(LevelValidator.Validate(draft)));
            panel.Text("Asset explanation", "普通草稿可先保存。加入目录和试玩前会检查结构，关卡是否有解仍需亲自试玩。");
            panel.Button("Close menu", CloseModal);
        }

        private void ShowOpen(int page = 0)
        {
            var writer = LevelAuthoringServices.AssetWriter;
            var levels = writer != null ? writer.ListLevels().Where(item => item != null && item.Data != null).ToList() :
                (resources?.Catalog?.Levels ?? new List<LevelAsset>()).Where(item => item != null && item.Data != null).ToList();
            const int pageSize = 6;
            int pages = Mathf.Max(1, (levels.Count + pageSize - 1) / pageSize);
            page = Mathf.Clamp(page, 0, pages - 1);
            var panel = NewModal("Open level dialog", "打开项目关卡");
            var entries = panel.Get<UiList>("Open list"); entries.Clear();
            panel.Show("Empty", levels.Count == 0);
            for (int i = page * pageSize; i < Mathf.Min(levels.Count, (page + 1) * pageSize); i++)
            {
                var asset = levels[i];
                string label = asset.Data.Name + (writer != null && writer.IsInCatalog(asset) ? "  [目录]" : "  [草稿]");
                var button = entries.Add().Button("Open level item", () => { SetDocument(asset.ToDefinition(), asset); CloseModal(); RenderDraft(); board.ResetView(); }, label);
                button.GetComponentInChildren<TMP_Text>().richText = false;
            }
            if (levels.Count == 0) panel.Text("Empty", "项目中还没有关卡，请先新建。");
            panel.Text("Page", $"{page + 1} / {pages}");
            var previous = panel.Button("Previous", () => ShowOpen(page - 1));
            previous.interactable = page > 0;
            var next = panel.Button("Next", () => ShowOpen(page + 1));
            next.interactable = page + 1 < pages;
            panel.Button("Cancel open", CloseModal);
        }

        private void ShowSettings()
        { ShowSettings(draft.Name, draft.Description, draft.Width, draft.Height); }

        private void ShowSettings(string pendingName, string pendingDescription, int pendingWidth, int pendingHeight)
        {
            var panel = NewModal("Level settings dialog", "关卡设置");
            panel.Text("Name label", "关卡名称");
            var name = panel.Input("Level name", pendingName);
            panel.Text("Description label", "关卡说明 / 提示");
            var description = panel.Input("Level description", pendingDescription);
            name.richText = false; description.richText = false;
            name.textComponent.richText = false; description.textComponent.richText = false;
            panel.Text("Size label", "地图宽 / 高（2 至 32 格）");
            var width = panel.Input("Map width", pendingWidth.ToString());
            var height = panel.Input("Map height", pendingHeight.ToString());
            width.contentType = TMP_InputField.ContentType.IntegerNumber;
            height.contentType = TMP_InputField.ContentType.IntegerNumber;
            panel.Text("Resize help", "缩小地图会裁切右侧与上方内容；应用后可以撤销。");
            panel.Button("Cancel settings", CloseModal);
            panel.Button("Apply settings", () =>
            {
                if (!int.TryParse(width.text, out var w) || !int.TryParse(height.text, out var h) || w < 2 || h < 2 || w > 32 || h > 32)
                { SetStatus("地图宽高必须是 2 至 32 的整数。"); return; }
                var proposed = draft.DeepClone();
                proposed.Name = name.text; proposed.Description = description.text;
                LevelAuthoring.Resize(proposed, w, h);
                if (w < draft.Width || h < draft.Height)
                {
                    var confirm = NewModal("Resize confirmation dialog", "确认缩小地图");
                    modalCancel = () => ShowSettings(proposed.Name, proposed.Description, w, h);
                    confirm.Text("Resize warning", $"地图将从 {draft.Width} × {draft.Height} 缩小为 {w} × {h}。\n右侧和上方超出新边界的内容将被裁切。\n确认后可用一次撤销恢复。");
                    confirm.Button("Cancel resize", () => ShowSettings(proposed.Name, proposed.Description, w, h));
                    confirm.Button("Confirm resize", () => ApplySettings(proposed));
                }
                else ApplySettings(proposed);
            });
        }

        private void ApplySettings(LevelDefinition proposed)
        {
            var before = draft;
            draft = proposed.DeepClone();
            CommitChange(before); selection = null; issueHighlight = null;
            CloseModal(); RenderDraft();
            if (before.Width != draft.Width || before.Height != draft.Height) board.ResetView();
        }

        private void ConfirmDiscard(Action action)
        {
            if (!IsDirty) { action(); return; }
            var panel = NewModal("Unsaved changes dialog", "当前草稿尚未保存");
            panel.Text("Explanation", "保存后继续，或放弃本次修改。取消会回到当前草稿。");
            panel.Button("Save changes", () => { if (Save()) { CloseModal(); action(); } });
            panel.Button("Discard changes", () => { CloseModal(); action(); });
            panel.Button("Cancel leaving", CloseModal);
        }

        private void ShowIssues(IReadOnlyList<ValidationIssue> issues, int page = 0)
        {
            var panel = NewModal("Validation dialog", issues.Count == 0 ? "结构检查通过" : "请先修正关卡");
            panel.Show("Validation details", issues.Count == 0);
            panel.Show("Validation instruction", issues.Count > 0);
            panel.Show("Issue list", issues.Count > 0);
            panel.Show("Previous issues", issues.Count > 7); panel.Show("Next issues", issues.Count > 7); panel.Show("Issue page", issues.Count > 7);
            if (issues.Count == 0)
                panel.Text("Validation details", "玩家、箱子、目标点与地图结构正确。请继续试玩确认关卡可解。");
            else
            {
                const int perPage = 7;
                int pages = Mathf.Max(1, (issues.Count + perPage - 1) / perPage);
                page = Mathf.Clamp(page, 0, pages - 1);
                panel.Text("Validation instruction", "点击带坐标的问题，可返回画布定位。");
                var entries = panel.Get<UiList>("Issue list"); entries.Clear();
                for (int i = page * perPage; i < Mathf.Min(issues.Count, (page + 1) * perPage); i++)
                {
                    var issue = issues[i];
                    var button = entries.Add().Button("Validation issue", () =>
                    {
                        CloseModal();
                        if (issue.Position.HasValue)
                        {
                            selection = null;
                            issueHighlight = ClampCell(issue.Position.Value);
                            RenderDraft(); board.FocusCell(issue.Position.Value);
                        }
                        SetStatus(issue.ToString());
                    }, issue.ToString());

                }
                if (pages > 1)
                {
                    var previous = panel.Button("Previous issues", () => ShowIssues(issues, page - 1));
                    var next = panel.Button("Next issues", () => ShowIssues(issues, page + 1));
                    previous.interactable = page > 0; next.interactable = page + 1 < pages;
                    panel.Text("Issue page", $"{page + 1} / {pages}");
                }
            }
            panel.Button("Close validation", CloseModal);
        }
    }


}

#endif
