using System;
using System.Collections.Generic;
using System.IO;
using Sokoban.Content;
using Sokoban.Core;
using Sokoban.Runtime;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace Sokoban.EditorTools
{
    public sealed partial class LevelEditorWindow : EditorWindow
    {
        private enum Brush { Floor, Wall, Goal, Player, Box, Erase }
        private const string ResourcesPath = "Assets/Resources/GameResources.asset";
        private const string LevelFolder = "Assets/Resources/Levels";

        [SerializeField] private LevelAsset source;
        [SerializeField] private LevelEditorDraft draft;
        [SerializeField] private string savedJson;
        [SerializeField] private string sourceJson;
        [SerializeField] private bool externalConflict;
        [SerializeField] private Brush brush = Brush.Wall;
        [SerializeField] private int desiredWidth = 8;
        [SerializeField] private int desiredHeight = 8;
        [SerializeField] private float cellSize = 38f;
        [SerializeField] private Vector2 gridScroll;
        [SerializeField] private Vector2 catalogScroll;
        [SerializeField] private Vector2 issuesScroll;
        private List<ValidationIssue> issues = new List<ValidationIssue>();
        private GridPos? highlightedCell;
        private GridPos? lastPaintedCell;
        private int paintUndoGroup = -1;
        private string notice = "";
        private GUIStyle centeredCell;
        private bool saving;

        [MenuItem("推箱子/关卡编辑器", priority = 10)]
        public static void Open()
        {
            var window = GetWindow<LevelEditorWindow>();
            window.titleContent = new GUIContent("关卡工坊");
            window.minSize = new Vector2(800, 570);
            window.Show();
        }

        public static void OpenLevel(LevelAsset level)
        {
            Open();
            var window = GetWindow<LevelEditorWindow>();
            if (window.source == level && window.draft != null) { window.Focus(); return; }
            if (window.ConfirmSwitch()) window.LoadLevel(level);
        }

        [OnOpenAsset]
        private static bool OnOpenAsset(int instanceId, int line)
        {
            var level = EditorUtility.InstanceIDToObject(instanceId) as LevelAsset;
            if (level == null) return false;
            OpenLevel(level);
            return true;
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("关卡工坊");
            minSize = new Vector2(800, 570);
            saveChangesMessage = "关卡还有未保存的修改，关闭前是否保存？";
            Undo.undoRedoPerformed -= OnUndoRedo;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorLevelAssetWriter.AssetSaved -= OnAssetSaved;
            EditorLevelAssetWriter.AssetSaved += OnAssetSaved;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            ElementCatalog.Changed -= OnElementCatalogChanged;
            ElementCatalog.Changed += OnElementCatalogChanged;
            if (draft == null) CreateBlank();
            RefreshValidation();
            RefreshDirty();
            RefreshSourceIfChanged();
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorLevelAssetWriter.AssetSaved -= OnAssetSaved;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            ElementCatalog.Changed -= OnElementCatalogChanged;
        }

        private void OnDestroy()
        {
            if (draft != null) { Undo.ClearUndo(draft); DestroyImmediate(draft); }
        }

        private void OnFocus() => RefreshSourceIfChanged();
        private void OnAssetSaved(LevelAsset asset)
        {
            if (!saving && asset == source) RefreshSourceIfChanged();
            Repaint();
        }
        private void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredEditMode) RefreshSourceIfChanged();
        }

        private void RefreshSourceIfChanged()
        {
            if (source == null || draft == null || saving) return;
            string current = JsonUtility.ToJson(source);
            if (current == sourceJson) return;
            RefreshDirty();
            if (!hasUnsavedChanges) LoadLevel(source);
            else
            {
                externalConflict = true;
                notice = "源关卡已被其他工具保存。当前草稿已保留，请重新加载或另存为，避免覆盖其他修改。";
                Repaint();
            }
        }

        public override void SaveChanges()
        {
            if (!Save(false)) throw new OperationCanceledException("关卡尚未保存。请继续编辑、另存草稿，或放弃修改后关闭。");
            base.SaveChanges();
        }

        public override void DiscardChanges()
        {
            if (source != null) LoadLevel(source);
            else CreateBlank();
            base.DiscardChanges();
        }

        private void OnUndoRedo()
        {
            if (draft == null) return;
            desiredWidth = draft.Data.Width;
            desiredHeight = draft.Data.Height;
            RefreshValidation();
            RefreshDirty();
            Repaint();
        }

        private void OnGUI()
        {
            RefreshElementRegistry();
            if (draft == null || draft.Data == null) CreateBlank();
            if (centeredCell == null)
            {
                centeredCell = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter };
                centeredCell.normal.textColor = Color.white;
            }

            DrawToolbar();
            if (externalConflict)
            {
                EditorGUILayout.HelpBox("源关卡已更新，当前未保存草稿不会自动覆盖它。", MessageType.Warning);
                using (new EditorGUILayout.HorizontalScope())
                using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                {
                    if (GUILayout.Button("放弃当前草稿并重新加载") && EditorUtility.DisplayDialog("重新加载关卡", "当前未保存的草稿将被放弃，是否读取源关卡？", "重新加载", "取消")) LoadLevel(source);
                    if (GUILayout.Button("将当前草稿另存为新关卡")) Save(true);
                }
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                EditorGUILayout.HelpBox("正在运行游戏。可以在游戏视图中编辑关卡；停止运行后可继续使用此窗口。", MessageType.Info);

            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(250)))
                    {
                        DrawProperties();
                        DrawInstanceProperties();
                        DrawCatalog();
                    }
                    using (new EditorGUILayout.VerticalScope())
                    {
                        DrawPalette();
                        DrawGrid();
                        DrawValidation();
                    }
                }
            }
            if (!string.IsNullOrEmpty(notice)) EditorGUILayout.HelpBox(notice, MessageType.None);
            HandleShortcuts();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("新建", EditorStyles.toolbarButton, GUILayout.Width(45)) && ConfirmSwitch()) CreateBlank();
                if (GUILayout.Button("打开…", EditorStyles.toolbarButton, GUILayout.Width(62))) ShowOpenMenu();
                GUILayout.Space(6);
                if (GUILayout.Button("保存", EditorStyles.toolbarButton, GUILayout.Width(45))) Save(false);
                if (GUILayout.Button("另存为…", EditorStyles.toolbarButton, GUILayout.Width(75))) Save(true);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("检查", EditorStyles.toolbarButton, GUILayout.Width(70)))
                { RefreshValidation(); notice = issues.Count == 0 ? "结构检查通过，请试玩确认关卡能够通关。" : "点击检查结果可以定位问题格子。"; }
                var previous = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.45f, 0.85f, 0.75f);
                if (GUILayout.Button("保存并试玩", EditorStyles.toolbarButton, GUILayout.Width(125))) SaveAndPlaytest();
                GUI.backgroundColor = previous;
            }
        }

        private void DrawProperties()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("关卡编辑器", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(source == null ? "新建草稿" : IsPublished(source) ? "正式关卡" : "已保存草稿", EditorStyles.miniLabel);
            EditorGUI.BeginChangeCheck();
            var selected = (LevelAsset)EditorGUILayout.ObjectField(source, typeof(LevelAsset), false);
            if (EditorGUI.EndChangeCheck() && selected != source && ConfirmSwitch())
            {
                if (selected == null) CreateBlank(); else LoadLevel(selected);
                GUIUtility.ExitGUI();
            }

            EditorGUILayout.Space(7);
            EditorGUI.BeginChangeCheck();
            string roomName = EditorGUILayout.TextField("名称", draft.Data.Name);
            EditorGUILayout.LabelField("关卡说明", EditorStyles.miniBoldLabel);
            string description = EditorGUILayout.TextArea(draft.Data.Description ?? "", GUILayout.Height(48));
            if (EditorGUI.EndChangeCheck())
            {
                RecordEdit("修改关卡说明", false);
                draft.Data.Name = roomName;
                draft.Data.Description = description;
                FinishEdit();
            }
            EditorGUILayout.LabelField("固定 ID", EditorStyles.miniBoldLabel);
            EditorGUILayout.SelectableLabel(draft.Data.Id ?? "", EditorStyles.miniLabel, GUILayout.Height(18));

            using (new EditorGUILayout.HorizontalScope())
            {
                float previousLabelWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 43;
                desiredWidth = Mathf.Clamp(EditorGUILayout.IntField("宽度", desiredWidth), 2, 32);
                desiredHeight = Mathf.Clamp(EditorGUILayout.IntField("高度", desiredHeight), 2, 32);
                EditorGUIUtility.labelWidth = previousLabelWidth;
            }
            using (new EditorGUI.DisabledScope(desiredWidth == draft.Data.Width && desiredHeight == draft.Data.Height))
                if (GUILayout.Button("应用尺寸")) ResizeRoom();
            EditorGUILayout.LabelField($"{draft.Data.Width} x {draft.Data.Height}   |   {(draft.Data.Boxes ?? Array.Empty<GridPos>()).Length} 个箱子   |   {(draft.Data.Goals ?? Array.Empty<GridPos>()).Length} 个目标", EditorStyles.miniLabel);
            EditorGUILayout.Space(5);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("撤销")) Undo.PerformUndo();
                if (GUILayout.Button("重做")) Undo.PerformRedo();
            }
            EditorGUILayout.HelpBox("左键拖动绘制，右键拖动擦除。目标可与箱子或玩家叠加；墙会清除对象，地板保留对象。", MessageType.None);
        }

        private void DrawPalette()
        {
            DrawElementPalette();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(selectElements ? "点选对象查看属性；拖动对象修改位置" : "拖动连续绘制，右键擦除", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                GUILayout.Label("缩放", GUILayout.Width(35));
                cellSize = GUILayout.HorizontalSlider(cellSize, 22f, 58f, GUILayout.Width(95));
            }
        }

        private void DrawGrid()
        {
            gridScroll = EditorGUILayout.BeginScrollView(gridScroll, GUILayout.MinHeight(180), GUILayout.ExpandHeight(true));
            int visibleWidth = Mathf.Clamp(draft.Data.Width, 2, 32);
            int visibleHeight = Mathf.Clamp(draft.Data.Height, 2, 32);
            float width = visibleWidth * cellSize;
            float height = visibleHeight * cellSize;
            Rect board = GUILayoutUtility.GetRect(width, height, GUILayout.ExpandWidth(false));
            Event evt = Event.current;
            for (int y = 0; y < visibleHeight; y++)
            for (int x = 0; x < visibleWidth; x++)
            {
                var p = new GridPos(x, y);
                var rect = CellRect(board, p);
                bool wall = draft.Data.CellAt(p) == CellType.Wall;
                bool goal = draft.Data.IsGoal(p);
                bool box = Array.IndexOf(draft.Data.Boxes ?? Array.Empty<GridPos>(), p) >= 0;
                bool player = draft.Data.HasPlayer && draft.Data.PlayerStart == p;
                EditorGUI.DrawRect(rect, new Color(0.055f, 0.08f, 0.11f));
                var inset = new Rect(rect.x + 1, rect.y + 1, rect.width - 2, rect.height - 2);
                EditorGUI.DrawRect(inset, wall ? new Color(0.22f, 0.30f, 0.38f) : new Color(0.12f, 0.17f, 0.22f));
                if (goal)
                {
                    var center = rect.center;
                    float r = cellSize * 0.30f;
                    Handles.BeginGUI();
                    Handles.color = new Color(0.35f, 0.9f, 0.73f);
                    Handles.DrawAAPolyLine(2.5f, new Vector3(center.x, center.y - r), new Vector3(center.x + r, center.y), new Vector3(center.x, center.y + r), new Vector3(center.x - r, center.y), new Vector3(center.x, center.y - r));
                    Handles.EndGUI();
                }
                if (box)
                {
                    float padding = cellSize * 0.22f;
                    EditorGUI.DrawRect(new Rect(rect.x + padding, rect.y + padding, cellSize - 2 * padding, cellSize - 2 * padding), goal ? new Color(0.2f, 0.64f, 0.5f) : new Color(0.73f, 0.46f, 0.21f));
                    GUI.Label(rect, "箱", centeredCell);
                }
                if (player) GUI.Label(rect, "人", centeredCell);
                DrawElementCell(p, rect);
                if (highlightedCell.HasValue && highlightedCell.Value == p) DrawOutline(rect, new Color(1f, 0.42f, 0.3f));
                if (rect.Contains(evt.mousePosition))
                {
                    DrawOutline(rect, Color.white);
                    if (evt.type == EventType.Repaint)
                    { var labelRect = new Rect(board.x, board.yMax + 2, width, 18); GUI.Label(labelRect, $"格子 ({x}, {y})", EditorStyles.miniLabel); }
                }
            }
            GUILayout.Space(18);
            ProcessPaint(board, evt);
            EditorGUILayout.EndScrollView();
        }

        private Rect CellRect(Rect board, GridPos p) => new Rect(board.x + p.X * cellSize, board.y + (Mathf.Clamp(draft.Data.Height, 2, 32) - 1 - p.Y) * cellSize, cellSize, cellSize);

        private static void DrawOutline(Rect rect, Color color)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 2), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 2, rect.width, 2), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 2, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - 2, rect.y, 2, rect.height), color);
        }

        private void ProcessPaint(Rect board, Event evt)
        {
            if (!GUI.enabled || draft.Data.Width < 2 || draft.Data.Width > 32 || draft.Data.Height < 2 || draft.Data.Height > 32) return;
            if (ProcessElementSelection(board, evt)) return;
            if (evt.type == EventType.MouseUp && paintUndoGroup >= 0)
            {
                Undo.CollapseUndoOperations(paintUndoGroup);
                paintUndoGroup = -1;
                lastPaintedCell = null;
            }
            if (!board.Contains(evt.mousePosition) || (evt.button != 0 && evt.button != 1)) return;
            if (evt.type == EventType.MouseMove) Repaint();
            if (evt.type != EventType.MouseDown && evt.type != EventType.MouseDrag) return;
            var p = new GridPos(Mathf.FloorToInt((evt.mousePosition.x - board.x) / cellSize), draft.Data.Height - 1 - Mathf.FloorToInt((evt.mousePosition.y - board.y) / cellSize));
            if (!draft.Data.IsInside(p)) return;
            if (evt.type == EventType.MouseDown)
            {
                GUI.FocusControl(null);
                Undo.IncrementCurrentGroup();
                paintUndoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("绘制关卡");
                lastPaintedCell = null;
            }
            if (!lastPaintedCell.HasValue || lastPaintedCell.Value != p)
            {
                PaintElement(p, evt.button == 1 || brush == Brush.Erase);
                lastPaintedCell = p;
            }
            evt.Use();
            Repaint();
        }

        private void Paint(GridPos p, Brush chosen)
        {
            RecordEdit("绘制关卡", true);
            if (!LevelAuthoring.Paint(draft.Data, p, (LevelBrush)chosen)) return;
            draft.VerifiedSolution = "";
            FinishEdit();
        }

        private void DrawValidation()
        {
            EditorGUILayout.LabelField(issues.Count == 0 ? "结构检查通过" : $"结构检查 / {issues.Count} 个问题", EditorStyles.boldLabel);
            if (issues.Count == 0)
            {
                EditorGUILayout.HelpBox("可以开始试玩。结构检查只检查布局是否合法，不保证存在解法。", MessageType.Info);
                return;
            }
            issuesScroll = EditorGUILayout.BeginScrollView(issuesScroll, GUILayout.Height(110));
            foreach (var issue in issues)
            {
                string text = issue.Position.HasValue ? $"{issue.Position.Value}  {issue.Message}" : issue.Message;
                if (GUILayout.Button(text, EditorStyles.wordWrappedMiniLabel))
                {
                    highlightedCell = issue.Position;
                    if (issue.Position.HasValue)
                    {
                        var p = issue.Position.Value;
                        gridScroll = new Vector2(Mathf.Max(0, p.X * cellSize - 100), Mathf.Max(0, (draft.Data.Height - 1 - p.Y) * cellSize - 100));
                    }
                    Repaint();
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawCatalog()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("正式关卡目录", EditorStyles.boldLabel);
            LevelCatalog catalog = GetCatalog();
            if (catalog == null)
            {
                EditorGUILayout.HelpBox("入口资源或关卡目录缺失，请先准备工程资源。", MessageType.Warning);
                return;
            }
            bool published = IsPublished(source);
            if (GUILayout.Button(published ? "从正式目录移除当前关卡" : "保存并加入正式目录"))
            {
                if (published)
                {
                    Undo.RecordObject(catalog, "从正式目录移除关卡");
                    catalog.Levels.Remove(source);
                    EditorUtility.SetDirty(catalog);
                    AssetDatabase.SaveAssetIfDirty(catalog);
                    notice = "已从正式目录移除，关卡资产仍保留为草稿。";
                }
                else AddToCatalog();
            }
            catalogScroll = EditorGUILayout.BeginScrollView(catalogScroll, GUILayout.MinHeight(80), GUILayout.ExpandHeight(true));
            for (int i = 0; i < catalog.Levels.Count; i++)
            {
                LevelAsset level = catalog.Levels[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    string label = $"{i + 1:00}  " + (level == null ? "资源缺失" : level.Data?.Name ?? level.name);
                    if (GUILayout.Button(label, level == source ? EditorStyles.miniButton : EditorStyles.label, GUILayout.ExpandWidth(true)) && level != null && level != source && ConfirmSwitch())
                    { LoadLevel(level); GUIUtility.ExitGUI(); }
                    using (new EditorGUI.DisabledScope(i == 0))
                        if (GUILayout.Button("^", GUILayout.Width(23))) { MoveCatalogEntry(catalog, i, i - 1); GUIUtility.ExitGUI(); }
                    using (new EditorGUI.DisabledScope(i == catalog.Levels.Count - 1))
                        if (GUILayout.Button("v", GUILayout.Width(23))) { MoveCatalogEntry(catalog, i, i + 1); GUIUtility.ExitGUI(); }
                }
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.LabelField("目录顺序决定关卡列表与下一关的顺序。", EditorStyles.wordWrappedMiniLabel);
        }

        private void AddToCatalog()
        {
            SaveWithIntent(LevelSaveIntent.SaveAndAddToCatalog, source == null);
        }

        private static void MoveCatalogEntry(LevelCatalog catalog, int from, int to)
        {
            Undo.RecordObject(catalog, "调整关卡顺序");
            LevelAsset value = catalog.Levels[from];
            catalog.Levels.RemoveAt(from);
            catalog.Levels.Insert(to, value);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);
        }

        private void ResizeRoom()
        {
            if ((desiredWidth < draft.Data.Width || desiredHeight < draft.Data.Height) &&
                !EditorUtility.DisplayDialog("调整地图尺寸", "新边界以外的格子和对象会被移除，本操作可以撤销。", "调整尺寸", "取消")) return;
            RecordEdit("调整地图尺寸", true);
            if (!LevelAuthoring.Resize(draft.Data, desiredWidth, desiredHeight, Registry)) return;
            draft.VerifiedSolution = "";
            FinishEdit();
        }

        private void RecordEdit(string action, bool layoutChanged)
        {
            Undo.RecordObject(draft, action);
            highlightedCell = null;
            notice = "";
        }

        private void FinishEdit()
        {
            EditorUtility.SetDirty(draft);
            RefreshValidation();
            RefreshDirty();
        }

        private void RefreshValidation() => issues = LevelValidator.Validate(draft == null ? null : draft.Data, Registry);

        private void RefreshDirty()
        {
            hasUnsavedChanges = draft != null && JsonUtility.ToJson(draft) != savedJson;
        }

        private bool ConfirmSwitch()
        {
            if (!hasUnsavedChanges) return true;
            int result = EditorUtility.DisplayDialogComplex("关卡尚未保存", "切换关卡前是否保存当前修改？", "保存", "取消", "放弃修改");
            if (result == 1) return false;
            return result == 2 || Save(false);
        }

        private void CreateBlank()
        {
            ReplaceDraft();
            source = null;
            draft.Data = LevelAuthoring.CreateBlank();
            draft.VerifiedSolution = "";
            FinishLoad();
        }

        private void LoadLevel(LevelAsset level)
        {
            ReplaceDraft();
            source = level;
            draft.Data = level.ToDefinition() ?? new LevelDefinition();
            draft.VerifiedSolution = level.GetVerifiedSolution(Registry);
            FinishLoad();
        }

        private void ReplaceDraft()
        {
            if (draft != null) { Undo.ClearUndo(draft); DestroyImmediate(draft); }
            draft = CreateInstance<LevelEditorDraft>();
            draft.hideFlags = HideFlags.HideAndDontSave;
        }

        private void FinishLoad()
        {
            LevelMigration.Upgrade(draft.Data, Registry);
            selectedElementId = null; referenceProperty = null; elementDragStart = null;
            desiredWidth = Mathf.Clamp(draft.Data.Width, 2, 32);
            desiredHeight = Mathf.Clamp(draft.Data.Height, 2, 32);
            savedJson = JsonUtility.ToJson(draft);
            sourceJson = source == null ? "" : JsonUtility.ToJson(source);
            externalConflict = false;
            hasUnsavedChanges = false;
            highlightedCell = null;
            gridScroll = Vector2.zero;
            notice = "";
            RefreshValidation();
            Repaint();
        }

        private bool Save(bool saveAs)
            => SaveWithIntent(saveAs ? LevelSaveIntent.SaveAs : LevelSaveIntent.Save, saveAs || source == null);

        private bool SaveWithIntent(LevelSaveIntent intent, bool choosePath)
        {
            RefreshSourceIfChanged();
            if (externalConflict && intent != LevelSaveIntent.SaveAs)
            { notice = "源关卡已发生变化，请先重新加载，或将当前草稿另存为。"; return false; }
            RefreshValidation();
            string path = null;
            bool makeNew = source == null || intent == LevelSaveIntent.SaveAs;
            if (choosePath)
            {
                string initialName = string.IsNullOrWhiteSpace(draft.Data.Name) ? "新关卡" : draft.Data.Name;
                foreach (char invalid in Path.GetInvalidFileNameChars()) initialName = initialName.Replace(invalid, '_');
                string folder = source != null ? Path.GetDirectoryName(AssetDatabase.GetAssetPath(source))?.Replace('\\', '/') : LevelFolder;
                path = EditorUtility.SaveFilePanelInProject(intent == LevelSaveIntent.SaveAs ? "另存为新关卡" : "保存关卡", initialName, "asset", "关卡将保存为工程资产；草稿不会自动加入正式目录。", folder);
                if (string.IsNullOrEmpty(path)) return false;
            }
            LevelSaveResult result;
            saving = true;
            try { result = new EditorLevelAssetWriter().SaveAtPath(source, draft.Data, draft.VerifiedSolution, intent, path); }
            finally { saving = false; }
            notice = result.Message;
            if (!result.Success) return false;
            source = result.Asset;
            draft.Data = source.ToDefinition();
            draft.VerifiedSolution = source.GetVerifiedSolution(Registry);
            if (makeNew) Undo.ClearUndo(draft);
            savedJson = JsonUtility.ToJson(draft);
            sourceJson = JsonUtility.ToJson(source);
            externalConflict = false;
            hasUnsavedChanges = false;
            RefreshValidation();
            EditorGUIUtility.PingObject(source);
            return true;
        }

        private LevelAsset CreateAssetCopy(bool assignNewIdentity)
        {
            var copy = CreateInstance<LevelAsset>();
            copy.Data = assignNewIdentity ? LevelAuthoring.Duplicate(draft.Data) : draft.Data.DeepClone();
            copy.VerifiedSolution = draft.VerifiedSolution;
            if (!string.IsNullOrEmpty(copy.VerifiedSolution)) copy.VerifiedGameplaySignature = GameplayFingerprint.Compute(copy.Data, Registry);
            return copy;
        }

        private void SaveAndPlaytest()
        {
            RefreshValidation();
            if (issues.Count > 0) { notice = "请先修复检查结果中的问题，再开始试玩。"; return; }
            if (Save(false)) EditorPlaytestBridge.Start(source);
        }

        private void ShowOpenMenu()
        {
            var menu = new GenericMenu();
            string[] guids = AssetDatabase.FindAssets("t:LevelAsset");
            Array.Sort(guids, (left, right) => string.Compare(AssetDatabase.GUIDToAssetPath(left), AssetDatabase.GUIDToAssetPath(right), StringComparison.Ordinal));
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var level = AssetDatabase.LoadAssetAtPath<LevelAsset>(path);
                string label = (IsPublished(level) ? "正式关卡/" : "草稿/") + Path.GetFileNameWithoutExtension(path);
                menu.AddItem(new GUIContent(label), level == source, () => { if (ConfirmSwitch()) LoadLevel(level); });
            }
            if (guids.Length == 0) menu.AddDisabledItem(new GUIContent("暂无已保存关卡"));
            menu.ShowAsContext();
        }

        private void HandleShortcuts()
        {
            Event evt = Event.current;
            if (evt.type != EventType.KeyDown || EditorApplication.isPlayingOrWillChangePlaymode) return;
            bool actionKey = evt.control || evt.command;
            if (actionKey && evt.keyCode == KeyCode.S)
            { Save(evt.shift); evt.Use(); return; }
            if (EditorGUIUtility.editingTextField || actionKey || evt.alt) return;
            if (evt.keyCode >= KeyCode.Alpha1 && evt.keyCode <= KeyCode.Alpha6)
            { brush = (Brush)(evt.keyCode - KeyCode.Alpha1); selectedTypeId = brush == Brush.Erase ? "floor" : brush.ToString().ToLowerInvariant(); selectElements = false; evt.Use(); Repaint(); }
        }

        private static LevelCatalog GetCatalog()
        {
            var resources = AssetDatabase.LoadAssetAtPath<GameResources>(ResourcesPath);
            return resources == null ? null : resources.Catalog;
        }

        private static bool IsPublished(LevelAsset level)
        {
            var catalog = GetCatalog();
            return level != null && catalog != null && catalog.Levels.Contains(level);
        }
    }
}
