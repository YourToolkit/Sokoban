#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sokoban.Content;
using Sokoban.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Sokoban.Runtime
{
    /// <summary>
    /// Editor Play Mode capture hosted by the Runtime assembly so Unity can attach the component.
    /// The Diagnostics path keeps this opt-in helper separate from gameplay; player builds exclude it.
    /// </summary>
    public sealed class DevelopmentCapture : MonoBehaviour
    {
        public const string OutputEnvironmentVariable = "SOKOBAN_CAPTURE_OUTPUT";
        private string output;
        private int expectedWidth, expectedHeight;
        private readonly List<string> checks = new List<string>();
        private readonly List<string> overflows = new List<string>();
        private readonly List<string> errors = new List<string>();
        private bool finished, backgroundChecked;
        private float started;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ConfigureIsolatedProgress()
        {
            var directory = Environment.GetEnvironmentVariable(OutputEnvironmentVariable);
            if (!string.IsNullOrEmpty(directory))
                ProgressStore.DefaultDirectoryOverride = Path.Combine(Path.GetFullPath(directory), "isolated-progress");
        }

        public static void Begin(string directory, int width, int height)
        {
            if (FindObjectOfType<DevelopmentCapture>() != null) return;
            var probe = new GameObject("Editor rendered acceptance capture").AddComponent<DevelopmentCapture>();
            probe.output = Path.GetFullPath(directory);
            probe.expectedWidth = width; probe.expectedHeight = height;
        }

        private IEnumerator Start()
        {
            Directory.CreateDirectory(output);
            started = Time.realtimeSinceStartup;
            Application.logMessageReceived += ObserveLog;
            Application.runInBackground = true;
            yield return null;
            float readyDeadline = Time.realtimeSinceStartup + 12;
            while ((Screen.width != expectedWidth || Screen.height != expectedHeight) && Time.realtimeSinceStartup < readyDeadline)
                yield return null;
            if (Screen.width != expectedWidth || Screen.height != expectedHeight)
            { Finish("GameView resolution did not match the requested fixed size."); yield break; }
            var app = FindObjectOfType<GameController>();
            if (app == null) { Finish("Game did not bootstrap."); yield break; }
            if (ProgressStore.DefaultDirectoryOverride != Path.Combine(output, "isolated-progress"))
            { Finish("Progress was not isolated before GameController.Awake."); yield break; }
            var resources = Resources.Load<GameResources>("GameResources");
            if (resources == null || resources.Catalog == null || resources.Catalog.Levels.Count == 0)
            { Finish("The project has no prepared catalog."); yield break; }
            app.StartLevel(Mathf.Min(2, resources.Catalog.Levels.Count - 1));
            if (app.CurrentScreen == GameController.ScreenState.Paused) app.Resume();
            yield return Capture("01-playing");
            app.Pause();
            yield return Capture("02-pause");
            app.Resume(); app.OpenWorkshop();
            var workshop = app.Workshop;
            workshop.InputEnabled = false;
            workshop.SetDocument(CaptureLevel());
            workshop.Open();
            yield return Capture("03-editor-default");
            Click(app, "Workshop menu");
            yield return Capture("03c-editor-menu");
            Click(app, "Close menu");

            var selectHint = app.GetComponentsInChildren<WorkshopPointerHint>().Single(hint => hint.name == "Tool Select");
            selectHint.OnPointerEnter(null);
            yield return Capture("03a-editor-tool-hint");
            selectHint.OnPointerExit(null);
            workshop.SetBrush(LevelBrush.Player);
            yield return Capture("03b-editor-player-tools");

            workshop.SetBrush(LevelBrush.Wall);
            workshop.SetTool(WorkshopTool.HollowRectangle);
            var beforeShape = workshop.Draft;
            workshop.BeginGesture(new GridPos(2, 5));
            workshop.UpdateGesture(new GridPos(5, 8));
            if (!LevelAuthoring.LayoutEquals(beforeShape, workshop.Draft))
            { Finish("Shape preview mutated the document before release."); yield break; }
            yield return Capture("04-editor-shape-preview");
            workshop.CancelGesture();

            workshop.BeginGesture(new GridPos(2, 2), true);
            workshop.EndGesture(new GridPos(5, 4));
            workshop.CopySelection(); workshop.BeginPaste();
            workshop.UpdateGesture(new GridPos(8, 2));
            yield return Capture("05-editor-paste-preview");
            workshop.CancelGesture();
            Click(app, "Workshop settings");
            var nameInput = app.GetComponentsInChildren<TMP_InputField>().Single(input => input.name == "Level name");
            nameInput.text = "中文关卡 · 松林小径";
            yield return Capture("06-editor-settings");
            Click(app, "Cancel settings");

            workshop.SetTool(WorkshopTool.Brush); workshop.SetBrush(LevelBrush.Goal);
            workshop.BeginGesture(new GridPos(2, 2)); workshop.EndGesture(new GridPos(2, 2));
            workshop.UndoEdit();
            workshop.SetTool(WorkshopTool.Select);
            app.Board.ZoomAt(app.Board.ScreenRect.center, 1);
            app.Board.Pan(new Vector2(24, 12));
            var expectedView = app.Board.CaptureView();
            var expectedDraft = workshop.Draft;
            bool expectedRedo = workshop.CanRedo;
            if (!workshop.StartPlaytest()) { Finish("The valid draft could not enter playtest."); yield break; }
            if (!app.IsPlaytest) { Finish("Editor playtest used formal progress."); yield break; }
            yield return Capture("07-playtest");
            app.Move(Direction.Up);
            float animationDeadline = Time.realtimeSinceStartup + 5;
            while (app.Board.IsAnimating && Time.realtimeSinceStartup < animationDeadline) yield return null;
            if (app.Board.IsAnimating) { Finish("Playtest animation timed out."); yield break; }
            app.OpenWorkshop();
            if (app.CurrentScreen != GameController.ScreenState.Workshop ||
                !LevelAuthoring.LayoutEquals(expectedDraft, workshop.Draft) || workshop.CanRedo != expectedRedo ||
                workshop.Tool != WorkshopTool.Select || app.Board.CaptureView().Center != expectedView.Center ||
                !Mathf.Approximately(app.Board.CaptureView().Zoom, expectedView.Zoom))
            { Finish("Playtest return lost the draft, history, tool or view."); yield break; }
            yield return Capture("08-playtest-return");
            if (workshop.IsDirty) { Finish("Capture draft was unexpectedly dirty before returning."); yield break; }
            workshop.RequestReturn();
            app.ShowLevelSelect();
            yield return Capture("09-level-select");

            string progressPath = Path.Combine(ProgressStore.DefaultDirectoryOverride, "progress.json");
            byte[] progressBefore = File.Exists(progressPath) ? File.ReadAllBytes(progressPath) : null;
            var completionLevel = LevelAuthoring.CreateBlank(7, 5);
            completionLevel.Id = "completion-capture-only";
            completionLevel.Name = "一步到位";
            completionLevel.Description = "将箱子推到目标点。";
            LevelAuthoring.Paint(completionLevel, new GridPos(2, 2), LevelBrush.Player);
            LevelAuthoring.Paint(completionLevel, new GridPos(3, 2), LevelBrush.Box);
            LevelAuthoring.Paint(completionLevel, new GridPos(4, 2), LevelBrush.Goal);
            app.StartPlaytest(completionLevel, null);
            if (!app.IsPlaytest) { Finish("Completion capture was not an isolated playtest."); yield break; }
            if (app.CurrentScreen == GameController.ScreenState.Paused) app.Resume();
            app.Move(Direction.Right);
            animationDeadline = Time.realtimeSinceStartup + 5;
            while (app.Board.IsAnimating && Time.realtimeSinceStartup < animationDeadline) yield return null;
            if (app.CurrentScreen == GameController.ScreenState.Paused) app.Resume();
            if (app.Board.IsAnimating || app.CurrentScreen != GameController.ScreenState.Complete || !app.Session.IsWon)
            { Finish("The independent one-move playtest did not reach completion."); yield break; }
            yield return Capture("10-complete");
            byte[] progressAfter = File.Exists(progressPath) ? File.ReadAllBytes(progressPath) : null;
            if ((progressBefore == null) != (progressAfter == null) || progressBefore != null && !progressBefore.SequenceEqual(progressAfter))
            { Finish("The completion playtest changed formal progress."); yield break; }
            checks.Add("Independent one-move completion rendered without writing formal progress or using asset solution caches.");
            checks.Add("Shape and paste previews were rendered from detached drafts.");
            checks.Add("Playtest return preserved draft, redo history, selected tool, zoom and pan.");
            checks.Add("All progress access used the output directory's isolated-progress store.");
            Finish(errors.Count > 0 ? "Runtime logged errors." : overflows.Count > 0 ? "Visible UI overflow detected." : null);
        }

        private void Update()
        {
            if (finished || started == 0) return;
            if (errors.Count > 0) Finish("Runtime error interrupted the capture.");
            else if (Time.realtimeSinceStartup - started > 100) Finish("Capture timed out.");
        }

        private static LevelDefinition CaptureLevel()
        {
            var level = LevelAuthoring.CreateBlank(14, 10);
            level.Id = "editor-capture-only"; level.Name = "松林小径";
            level.Description = "绕过墙角，分别把两个箱子推到目标点。";
            LevelAuthoring.Paint(level, new GridPos(3, 3), LevelBrush.Player);
            LevelAuthoring.Paint(level, new GridPos(5, 3), LevelBrush.Box);
            LevelAuthoring.Paint(level, new GridPos(8, 6), LevelBrush.Box);
            LevelAuthoring.Paint(level, new GridPos(10, 3), LevelBrush.Goal);
            LevelAuthoring.Paint(level, new GridPos(10, 6), LevelBrush.Goal);
            LevelAuthoring.PaintLine(level, new GridPos(6, 5), new GridPos(6, 7), LevelBrush.Wall);
            LevelAuthoring.PaintLine(level, new GridPos(9, 7), new GridPos(11, 7), LevelBrush.Wall);
            return level;
        }

        private static void Click(GameController app, string name)
        { app.GetComponentsInChildren<Button>().Single(button => button.name == name).onClick.Invoke(); }

        private IEnumerator Capture(string name)
        {
            yield return new WaitForSecondsRealtime(.25f);
            Canvas.ForceUpdateCanvases();
            foreach (var label in FindObjectsOfType<TMP_Text>())
            {
                if (!label.gameObject.activeInHierarchy) continue;
                label.ForceMeshUpdate();
                if (label.isTextOverflowing && label.GetComponentInParent<TMP_InputField>() == null)
                    overflows.Add(name + ": text overflow: " + label.name + " = " + label.text);
            }
            var corners = new Vector3[4];
            foreach (var button in FindObjectsOfType<Button>())
            {
                var rect = (RectTransform)button.transform;
                rect.GetWorldCorners(corners);
                foreach (var corner in corners)
                    if (corner.x < -1 || corner.y < -1 || corner.x > Screen.width + 1 || corner.y > Screen.height + 1)
                    { overflows.Add(name + ": button outside viewport: " + button.name); break; }
            }
            RenderFrame(Path.Combine(output, name + ".png"));
            checks.Add(name + ".png: actual Camera and Canvas rendered at " + Screen.width + " x " + Screen.height);
            yield return null;
        }

        private void RenderFrame(string path)
        {
            int width = Screen.width, height = Screen.height;
            var composite = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            composite.Create();
            var previous = RenderTexture.active;
            var canvas = FindObjectOfType<Canvas>();
            if (canvas == null) throw new InvalidOperationException("No runtime Canvas to render.");
            var mode = canvas.renderMode;
            var oldCamera = canvas.worldCamera;
            float oldDistance = canvas.planeDistance;
            var iconRects = new List<Rect>();
            foreach (var icon in canvas.GetComponentsInChildren<WorkshopPointerHint>())
            {
                if (!icon.GetComponentInParent<Button>().interactable) continue;
                var corners = new Vector3[4];
                icon.GetComponent<UiButtonVisual>().Icon.rectTransform.GetWorldCorners(corners);
                var rect = Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
                var hits = new List<RaycastResult>();
                EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = rect.center }, hits);
                // Dialog shades deliberately obscure the toolbar; validate only unobstructed icons.
                if (hits.Count > 0 && hits[0].gameObject == icon.GetComponentInParent<Button>().gameObject)
                    iconRects.Add(rect);
            }
            var transforms = canvas.GetComponentsInChildren<Transform>(true);
            var layers = new int[transforms.Length];
            for (int i = 0; i < transforms.Length; i++) layers[i] = transforms[i].gameObject.layer;
            GameObject cameraObject = null;
            Texture2D image = null;
            try
            {
                RenderTexture.active = composite;
                var cameras = FindObjectsOfType<Camera>();
                var background = cameras.FirstOrDefault(camera => camera.enabled && camera.gameObject.activeInHierarchy && camera.name == "Background camera");
                if (background == null) throw new InvalidOperationException("No runtime Background camera to render.");
                var previousBackgroundTarget = background.targetTexture;
                var previousBackgroundRect = background.rect;
                try
                {
                    // Let the real camera perform its usual color-space conversion. GL.Clear with
                    // the UI's sRGB color would write it as linear and brighten the encoded PNG.
                    background.targetTexture = composite;
                    background.rect = new Rect(0, 0, 1, 1);
                    background.Render();
                }
                finally
                {
                    background.targetTexture = previousBackgroundTarget;
                    background.rect = previousBackgroundRect;
                }
                if (!backgroundChecked)
                {
                    RenderTexture.active = composite;
                    var sample = new Texture2D(1, 1, TextureFormat.RGB24, false);
                    try
                    {
                        sample.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); sample.Apply();
                        Color32 actual = sample.GetPixel(0, 0), expected = UiFactory.Paper;
                        string hex = $"#{actual.r:X2}{actual.g:X2}{actual.b:X2}";
                        checks.Add("Actual Background camera pixel before board/UI: " + hex);
                        if (Math.Abs(actual.r - expected.r) > 1 || Math.Abs(actual.g - expected.g) > 1 || Math.Abs(actual.b - expected.b) > 1)
                            throw new InvalidOperationException("Background camera readback was " + hex + ", expected #232735.");
                        backgroundChecked = true;
                    }
                    finally { Destroy(sample); }
                }
                foreach (var camera in cameras)
                    if (camera.enabled && camera.gameObject.activeInHierarchy && camera.name == "Board camera" && camera.targetTexture != null) camera.Render();
                cameraObject = new GameObject("Capture UI camera", typeof(Camera));
                var uiCamera = cameraObject.GetComponent<Camera>();
                uiCamera.enabled = false; uiCamera.orthographic = true;
                uiCamera.orthographicSize = height / 2f;
                uiCamera.transform.position = new Vector3(0, 0, -100);
                uiCamera.nearClipPlane = .1f; uiCamera.farClipPlane = 30;
                uiCamera.clearFlags = CameraClearFlags.Depth;
                uiCamera.cullingMask = 1 << 5;
                uiCamera.targetTexture = composite;
                for (int i = 0; i < transforms.Length; i++) transforms[i].gameObject.layer = 5;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = uiCamera; canvas.planeDistance = 10;
                Canvas.ForceUpdateCanvases();
                uiCamera.Render();
                RenderTexture.active = composite;
                image = new Texture2D(width, height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
                foreach (var rect in iconRects)
                {
                    int visiblePixels = 0;
                    for (int y = Mathf.Max(0, Mathf.CeilToInt(rect.yMin)); y < Mathf.Min(height, rect.yMax); y++)
                        for (int x = Mathf.Max(0, Mathf.CeilToInt(rect.xMin)); x < Mathf.Min(width, rect.xMax); x++)
                            if (image.GetPixel(x, y).r > .65f) visiblePixels++;
                    if (visiblePixels < 8) throw new InvalidOperationException("An enabled tool icon was blank in the rendered frame: " + path);
                }
            }
            finally
            {
                canvas.renderMode = mode; canvas.worldCamera = oldCamera; canvas.planeDistance = oldDistance;
                for (int i = 0; i < transforms.Length; i++) if (transforms[i] != null) transforms[i].gameObject.layer = layers[i];
                RenderTexture.active = previous;
                if (cameraObject != null) cameraObject.GetComponent<Camera>().targetTexture = null;
                composite.Release(); Destroy(composite);
                if (image != null) Destroy(image);
                if (cameraObject != null) Destroy(cameraObject);
                Canvas.ForceUpdateCanvases();
            }
        }

        private void ObserveLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert ||
                type == LogType.Warning && message.IndexOf("Unicode", StringComparison.OrdinalIgnoreCase) >= 0 && message.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0)
                errors.Add(message + "\n" + stack);
        }

        private void Finish(string failure)
        {
            if (finished) return;
            finished = true;
            StopAllCoroutines();
            var report = new List<string> { failure == null ? "PASS" : "FAIL: " + failure,
                "Resolution: " + Screen.width + " x " + Screen.height, "Execution: Unity Editor Play Mode (no player build)" };
            report.AddRange(checks);
            report.Add("UI overflows: " + overflows.Count); report.AddRange(overflows);
            report.Add("Runtime errors: " + errors.Count); report.AddRange(errors);
            File.WriteAllLines(Path.Combine(output, "report.txt"), report);
            Application.logMessageReceived -= ObserveLog;
            // The Editor wrapper owns leaving Play Mode and process exit after this report appears.
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= ObserveLog;
            ProgressStore.DefaultDirectoryOverride = null;
        }
    }
}
#endif
