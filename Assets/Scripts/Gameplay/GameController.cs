using System;
using System.Collections.Generic;
using Sokoban.Content;
using Sokoban.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Sokoban.Runtime
{
    /// <summary>Owns the game flow; the same session and board are used for editor playtests.</summary>
    public sealed class GameController : MonoBehaviour
    {
        public enum ScreenState { Menu, LevelSelect, Playing, Paused, Complete, Workshop, Error }
        public ScreenState CurrentScreen { get; private set; }
        public GameSession Session { get; private set; }
        public bool IsPlaytest { get; private set; }
        public int CurrentLevelIndex { get; private set; }
        public BoardView Board => board;
#if UNITY_EDITOR
        public RuntimeWorkshop Workshop => workshop;
        public LevelDefinition WorkshopSourceDefinition => workshopReturn?.Definition.DeepClone() ?? playingDefinition?.DeepClone();
        public LevelAsset WorkshopSourceAsset => workshopReturn?.Source;
#endif

        [SerializeField] private GameResources resources;
        private ProgressStore progress;
        [SerializeField] private BoardView board;
        [SerializeField] private Canvas canvas;
        [SerializeField] private GameScreens screens;
        [SerializeField] private PlayerController playerController;
        public PlayerController PlayerController => playerController;
        public bool CanControlPlayer => isActiveAndEnabled && CanOperate();
        public GameScreens Screens => screens;
        public Canvas Canvas => canvas;
        public GameResources ResourcesConfig => resources;
        private RectTransform screen;

#if UNITY_EDITOR
        private RuntimeWorkshop workshop;
        private Action returnFromPlaytest;
        private SavedSession workshopReturn;
#endif
        private TMP_Text stepsText;
        private TMP_Text pushesText;
        private TMP_Text goalsText;
        private TMP_Text messageText;
        private Button undoButton;
        private Button restartButton;
        [SerializeField] private AudioSource audioSource;
        private bool soundEnabled;
        private bool pendingWin;
        private bool winRecorded;
        private bool winSaved;
        private float messageUntil;
        private string defaultMessage;
        private string transientMessage;
        private LevelDefinition playingDefinition;
        private ElementRegistry playingElements;
        public MoveResult LastAction { get; private set; }
        private readonly List<AudioClip> generatedClips = new List<AudioClip>();
        private AudioClip moveTone, pushTone, blockedTone, winTone;

        private GameConfig Config => resources.Config;
        private List<LevelAsset> Levels => resources.Catalog.Levels;
        private bool Ready => resources != null && resources.Catalog != null && resources.Catalog.Levels != null && resources.Config != null && board != null;

#if UNITY_EDITOR
        private sealed class SavedSession
        {
            public GameSession Session;
            public LevelDefinition Definition;
            public ElementRegistry Elements;
            public LevelAsset Source;
            public int Index;
            public bool IsPlaytest, WinRecorded, WinSaved;
            public Action PlaytestReturn;
        }
#endif

        private void Awake()
        {
            Application.targetFrameRate = 60;
            soundEnabled = PlayerPrefs.GetInt("Sokoban.Sound", 1) == 1;
            try
            {
                if (!resources || !resources.Catalog || !resources.Config || !resources.Visuals || !board || !canvas || !screens || !audioSource || !playerController || playerController.Game != this || !playerController.Settings)
                    throw new InvalidOperationException("Game 场景的应用入口缺少引用，请在 Inspector 中补齐资源、棋盘、Canvas、页面、音频和角色控制。");
                screens.Validate();
                board.ConfigureElements(resources.Elements);
                board.ConfigureAudio(Play);
                board.Initialize();
            }
            catch (Exception error) { Debug.LogError(error.Message, this); enabled = false; return; }
            progress = new ProgressStore();
            BuildSounds();
            ElementCatalog.Changed += OnElementsChanged;
#if UNITY_EDITOR
            if (PlaytestRequest.TryConsume(out var playtest)) StartSession(playtest, true, -1);
            else
#endif
                ShowMenu();
        }

#if UNITY_EDITOR
        public void Configure(GameResources assets, BoardView b, Canvas c, GameScreens ui, AudioSource sound, PlayerController controls)
        { resources = assets; board = b; canvas = c; screens = ui; audioSource = sound; playerController = controls; controls.Configure(this, assets.Config); }
#endif

        public void ShowMenu()
        {
            if (!Ready) return;
            if (FirstUnfinished() < 0) { ShowLevelSelect(); return; }
            StartLevel(FirstUnfinished());
            if (!string.IsNullOrEmpty(progress.LastError)) ShowToast(progress.LastError, 12);
        }

        public void ShowLevelSelect()
        {
            if (!Ready) return;
            CurrentScreen = ScreenState.LevelSelect;
            IsPlaytest = false; pendingWin = false; board.Hide();
            ResetScreen("Room selection");
            var list = screens.Selection.Get<UiList>("Room list");
            list.Clear();
            for (int i = 0; i < Levels.Count; i++) BuildLevelCard(list.Add(), i);
            screens.Selection.Show("No rooms", Levels.Count == 0);
            screens.Selection.Button("Back to game", ContinueGame);
            screens.Selection.Text("Selection progress", "已通关 " + CompletedCount() + " / " + Levels.Count);
#if UNITY_EDITOR
            WorkshopViews.AttachEntry(screens.Selection.Get<Transform>("Editor entry"), OpenWorkshop);
#endif
        }

        public void StartLevel(int index)
        {
            if (!Ready) return;
            if (index < 0 || index >= Levels.Count)
            {
                ShowToast("关卡目录中暂无可游玩的关卡。", 6);
                return;
            }
            if (Levels[index] == null)
            {
                ShowError("无法加载关卡", "关卡资源已丢失，请在关卡编辑器中修复目录。");
                return;
            }
#if UNITY_EDITOR
            returnFromPlaytest = null;
#endif
            StartSession(Levels[index].ToDefinition(), false, index);
        }

#if UNITY_EDITOR
        public void OpenWorkshop()
        {
            if (!Ready || CurrentScreen == ScreenState.Workshop) return;
            if (IsPlaytest && returnFromPlaytest != null) { ExitPlaytest(); return; }
            if (Session != null)
            {
                workshopReturn = new SavedSession
                {
                    Session = Session, Definition = playingDefinition.DeepClone(), Elements = playingElements, Index = CurrentLevelIndex,
                    Source = IsPlaytest ? null : Levels.Find(level => level != null && level.Data != null && level.Data.Id == playingDefinition.Id),
                    IsPlaytest = IsPlaytest, WinRecorded = winRecorded, WinSaved = winSaved, PlaytestReturn = returnFromPlaytest
                };
            }
            else workshopReturn = null;
            if (workshop == null)
            {
                workshop = gameObject.AddComponent<RuntimeWorkshop>();
                workshop.Initialize(this, canvas, board, resources);
            }
            workshop.Open();
        }

        public RectTransform BeginWorkshopView()
        {
            playerController.ResetInput();
            CurrentScreen = ScreenState.Workshop;
            IsPlaytest = false;
            pendingWin = false;
            if (board != null) board.Hide();
            screens.HideAll();
            RemoveModal();
            return null; // RuntimeWorkshop owns its serialized Editor-only view.
        }

        public void StartPlaytest(LevelDefinition definition, Action onReturn)
        {
            if (!Ready) return;
            returnFromPlaytest = onReturn;
            StartSession(definition, true, -1);
        }

        public void ReturnFromWorkshop()
        {
            if (!Ready) return;
            var saved = workshopReturn;
            workshopReturn = null;
            if (saved == null) { ShowMenu(); return; }
            int sourceIndex = saved.Source != null ? Levels.IndexOf(saved.Source) : -1;
            if (!saved.IsPlaytest && (sourceIndex < 0 || saved.Source.Data == null)) { ShowMenu(); return; }
            if (saved.Source != null && (saved.Source.Data.LayoutVersion != saved.Definition.LayoutVersion ||
                GameplayFingerprint.Compute(saved.Source.Data, CurrentElements()) != saved.Session.GameplaySignature))
            {
                StartLevel(sourceIndex);
                ShowToast("关卡布局或机制配置已更新，已重新开始。", 4);
                return;
            }
            Session = saved.Session;
            playingElements = saved.Elements;
            playingDefinition = saved.Source != null ? saved.Source.ToDefinition() : saved.Definition;
            IsPlaytest = saved.IsPlaytest;
            CurrentLevelIndex = saved.IsPlaytest ? saved.Index : sourceIndex;
            winRecorded = saved.WinRecorded;
            winSaved = saved.WinSaved;
            returnFromPlaytest = saved.PlaytestReturn;
            ShowCurrentSession();
        }
#endif

        private void ContinueGame()
        {
            int index = playingDefinition != null ? Levels.FindIndex(level => level != null && level.Data != null && level.Data.Id == playingDefinition.Id) : -1;
            if (Session == null || index < 0) { ShowMenu(); return; }
            if (Levels[index].Data.LayoutVersion != playingDefinition.LayoutVersion || GameplayFingerprint.Compute(Levels[index].Data, CurrentElements()) != Session.GameplaySignature)
            { StartLevel(index); return; }
            CurrentLevelIndex = index;
            IsPlaytest = false;
            playingDefinition = Levels[index].ToDefinition();
            ShowCurrentSession();
        }

        private void ShowCurrentSession()
        {
            CurrentScreen = ScreenState.Playing;
            pendingWin = false;
            playerController.ResetInput();
            BuildPlayScreen();
            board.SetViewport(screens.Play.Get<BoardViewport>("Board viewport"));
            board.Show(playingDefinition, Session.State, playingElements);
            RefreshCounters();
            if (Session.IsWon) CompleteLevel();
        }

        private void StartSession(LevelDefinition level, bool playtest, int index)
        {
            playingElements = CurrentElements();
            LastAction = null;
            try { Session = new GameSession(level, playingElements); }
            catch (Exception error)
            {
                IsPlaytest = playtest;
                Debug.LogWarning("创建推箱子游戏状态失败：" + error);
                var issues = LevelValidator.Validate(level, playingElements);
                ShowError("关卡需要修复", issues.Count > 0 ? issues[0].Message : "无法开始关卡，请检查关卡数据。");
                return;
            }
            playingDefinition = Session.Definition;
            IsPlaytest = playtest;
            CurrentLevelIndex = index;
            pendingWin = false;
            winRecorded = false;
            winSaved = false;
            playerController.ResetInput();
            ShowCurrentSession();
        }

        private void BuildPlayScreen()
        {
            ResetScreen("Play screen");
            var ui = screens.Play;
            ui.Text("Room title", (IsPlaytest ? "试玩  /  " : "") + playingDefinition.Name);
            stepsText = ui.Get<TMP_Text>("Moves count"); pushesText = ui.Get<TMP_Text>("Pushes count"); goalsText = ui.Get<TMP_Text>("Goals");
            undoButton = ui.Button("Undo", Undo); restartButton = ui.Button("Restart", Restart);
            ui.Button("Leave room", ShowLevelSelect).interactable = !IsPlaytest;
            ui.Button("Pause", Pause);
#if UNITY_EDITOR
            WorkshopViews.AttachEntry(ui.Get<Transform>("Controls"), () => { if (IsPlaytest) ExitPlaytest(); else OpenWorkshop(); }, IsPlaytest ? "返回编辑" : "编辑");
#endif
            defaultMessage = IsPlaytest ? "试玩不会保存正式成绩。返回编辑可继续修改当前草稿。" : "方向键 / WASD 移动，箱子只能推动，不能拉回。";
            messageText = ui.Text("Play feedback", defaultMessage);
        }

        private void BuildLevelCard(UiView card, int index)
        {
            var asset = Levels[index];
            var record = asset != null && asset.Data != null ? progress.Get(asset.Data, CurrentElements()) : null;
            bool completed = record?.Completed == true;
            card.Text("Number", (index + 1).ToString("00"));
            card.Text("Status", completed ? "已通关" : "未通关");
            card.Text("Name", asset != null && asset.Data != null ? asset.Data.Name : "关卡丢失");
            card.Text("Best score", completed ? "最佳 " + record.BestSteps + " 步" : "可直接游玩");
            card.Button("Open room", () => StartLevel(index), completed ? "再次游玩" : "开始").interactable = asset != null;
        }

        public void Undo()
        {
            if (!CanOperate() || !Session.Undo()) return;
            board.Sync(Session.State);
            RefreshCounters();
            playerController.ResetInput();
            ShowToast("已撤销上一步。", 2);
            Play(moveTone);
        }

        public void Restart()
        {
            if (Session == null) return;
            if (CurrentScreen != ScreenState.Playing && CurrentScreen != ScreenState.Paused && CurrentScreen != ScreenState.Complete) return;
            board.CancelAnimation();
            var source = !IsPlaytest && CurrentLevelIndex >= 0 && CurrentLevelIndex < Levels.Count ? Levels[CurrentLevelIndex] : null;
            StartSession(source != null ? source.ToDefinition() : playingDefinition, IsPlaytest, CurrentLevelIndex);
            if (CurrentScreen == ScreenState.Playing) ShowToast("关卡已重开。", 2);
        }

        public void Pause()
        {
            if (CurrentScreen != ScreenState.Playing) return;
            CurrentScreen = ScreenState.Paused; playerController.ResetInput();
            board.SetPlaybackPaused(true);
            var ui = screens.ShowModal(screens.Pause);
            ui.Button("Resume", Resume); ui.Button("Pause restart", Restart);
            ui.Button("Pause leave", LeaveSession, IsPlaytest ? "返回编辑" : "选择关卡");
            ui.Button("Pause sound", () => { ToggleSound(); CurrentScreen = ScreenState.Playing; Pause(); }, soundEnabled ? "音效：开" : "音效：关");
        }

        public void Resume()
        {
            if (CurrentScreen != ScreenState.Paused) return;
            RemoveModal();
            CurrentScreen = ScreenState.Playing;
            board.SetPlaybackPaused(false);
            playerController.ResetInput();
            if (pendingWin) CompleteLevel();
        }

        /// <summary>Entry point shared by keyboard input and integration checks.</summary>
        public void Move(Direction direction)
        {
            if (!CanOperate()) return;
            var result = Session.TryMove(direction);
            LastAction = result;
            if (!result.Succeeded)
            {
                if (!string.IsNullOrEmpty(result.Trace)) Debug.LogWarning("推箱子行动已回滚：" + result.Trace, this);
                board.Blocked(direction);
                ShowToast(result.Reason, 2.5f);
                Play(blockedTone);
                return;
            }
            RefreshCounters();
            // A solved room is earned when the rule transaction succeeds, even if
            // the player leaves before its presentation animation has finished.
            if (result.Won) CommitWin();
            if (!board.HasConfiguredMoveSound(result)) Play(result.Pushed ? pushTone : moveTone);
            board.Animate(result, Session.State, Config.MoveDuration, () =>
            {
                if (result.Won)
                {
                    if (CurrentScreen == ScreenState.Playing) CompleteLevel();
                    else pendingWin = true;
                }
            });
        }

        private void CompleteLevel()
        {
            pendingWin = false; CurrentScreen = ScreenState.Complete; playerController.ResetInput();
            var state = Session.State; CommitWin(); Play(winTone);
            var ui = screens.ShowModal(screens.Complete);
            ui.Text("Clear eyebrow", IsPlaytest ? "试玩完成" : "全部箱子已到位");
            ui.Text("Clear room", playingDefinition.Name);
            ui.Text("Final moves", state.Steps.ToString()); ui.Text("Final pushes", state.Pushes.ToString());
            bool hasNext = !IsPlaytest && CurrentLevelIndex + 1 < Levels.Count;
            ui.Button("Continue", () => { if (hasNext) StartLevel(CurrentLevelIndex + 1); else LeaveSession(); }, IsPlaytest ? "返回编辑" : hasNext ? "下一关" : "返回选关");
            ui.Button("Replay", Restart); ui.Button("Clear leave", ShowLevelSelect);
            ui.Show("Clear leave", !IsPlaytest);
            ui.Text("Save status", IsPlaytest ? "试玩不计入正式成绩。" : winSaved ? "成绩已保存。" : "成绩保存失败，本次游玩仍可继续。");
        }

        private void CommitWin()
        {
            if (winRecorded || Session == null || !Session.IsWon) return;
            winRecorded = true;
            var state = Session.State;
            winSaved = IsPlaytest || progress.RecordWin(playingDefinition, state.Steps, state.Pushes, playingElements);
        }

        private void Update()
        {
            if (messageText != null && Time.unscaledTime > messageUntil && transientMessage != null)
            {
                messageText.text = defaultMessage;
                messageText.color = screens.NormalColor;
                transientMessage = null;
            }
            if (undoButton != null) undoButton.interactable = CanOperate() && Session.CanUndo;
            if (restartButton != null) restartButton.interactable = Session != null && CurrentScreen == ScreenState.Playing;
        }

        public void NavigateBack()
        {
            if (CurrentScreen == ScreenState.Playing) Pause();
            else if (CurrentScreen == ScreenState.Paused) Resume();
            else if (CurrentScreen == ScreenState.LevelSelect) ContinueGame();
        }

        private bool CanOperate() => CurrentScreen == ScreenState.Playing && Session != null && board != null && !board.IsAnimating && !Session.IsWon;

        private void RefreshCounters()
        {
            var state = Session.State;
            stepsText.text = "步数  " + state.Steps.ToString("00");
            pushesText.text = "推动  " + state.Pushes.ToString("00");
            goalsText.text = "目标  " + state.GoalsPlaced + " / " + state.GoalsTotal;
        }

        private void ResetScreen(string name)
        {
            RemoveModal();
#if UNITY_EDITOR
            if (workshop != null) workshop.HideView();
#endif
            var page = name == "Play screen" ? screens.Play : name == "Room selection" ? screens.Selection : screens.Error;
            screen = screens.Show(page).Rect;
            stepsText = pushesText = goalsText = messageText = null;
            undoButton = restartButton = null; transientMessage = null; playerController.ResetInput();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }



        private void RemoveModal() { if (screens != null) screens.HideModals(); }

        private void ShowToast(string value, float seconds)
        {
            if (screen == null) return;
            if (messageText == null) { defaultMessage = ""; messageText = screens.Current.Get<TMP_Text>("Notice"); }
            transientMessage = value; messageText.text = value;
            messageText.color = screens.InfoColor; messageUntil = Time.unscaledTime + seconds;
        }

        private void ShowError(string title, string details)
        {
            CurrentScreen = ScreenState.Error;
            if (board != null) board.Hide();
            ResetScreen("Content error");
            screens.Error.Text("Error heading", title); screens.Error.Text("Error details", details);
            screens.Error.Button("Error home", LeaveSession, IsPlaytest ? "返回编辑" : "返回选关");
        }

        private int FirstUnfinished()
        {
            for (int i = 0; i < Levels.Count; i++)
                if (Levels[i] != null && Levels[i].Data != null && progress.Get(Levels[i].Data, CurrentElements())?.Completed != true) return i;
            return Levels.Count > 0 ? 0 : -1;
        }

        private int CompletedCount()
        {
            int count = 0;
            foreach (var level in Levels)
                if (level != null && level.Data != null && progress.Get(level.Data, CurrentElements())?.Completed == true) count++;
            return count;
        }

        private void LeaveSession()
        {
#if UNITY_EDITOR
            if (IsPlaytest) { ExitPlaytest(); return; }
#endif
            ShowLevelSelect();
        }

#if UNITY_EDITOR
        private void ExitPlaytest()
        {
            if (board != null) board.CancelAnimation();
            if (returnFromPlaytest != null)
            {
                var callback = returnFromPlaytest;
                returnFromPlaytest = null;
                callback();
                return;
            }
            PlaytestRequest.RequestExit();
        }
#endif

        private void ToggleSound()
        {
            soundEnabled = !soundEnabled;
            PlayerPrefs.SetInt("Sokoban.Sound", soundEnabled ? 1 : 0);
            PlayerPrefs.Save();
            if (soundEnabled) Play(moveTone);
        }

        private void BuildSounds()
        {
            var v = resources.Visuals;
            moveTone = v != null && v.MoveSound != null ? v.MoveSound : Tone("Move", 410, .07f);
            pushTone = v != null && v.PushSound != null ? v.PushSound : Tone("Push", 215, .105f);
            blockedTone = v != null && v.BlockedSound != null ? v.BlockedSound : Tone("Blocked", 125, .055f);
            winTone = v != null && v.WinSound != null ? v.WinSound : Tone("Complete", 640, .42f, true);
        }

        private AudioClip Tone(string label, float pitch, float duration, bool chord = false)
        {
            const int rate = 22050;
            var samples = new float[Mathf.CeilToInt(rate * duration)];
            for (int i = 0; i < samples.Length; i++)
            {
                float t = (float)i / rate;
                float envelope = Mathf.Min(1, t / .006f) * Mathf.Pow(1 - t / duration, 2);
                float wave = Mathf.Sin(2 * Mathf.PI * pitch * t);
                if (chord) wave = (wave + Mathf.Sin(2 * Mathf.PI * pitch * 1.25f * t) + Mathf.Sin(2 * Mathf.PI * pitch * 1.5f * t)) / 3;
                samples[i] = wave * envelope * .22f;
            }
            var clip = AudioClip.Create(label + " feedback", samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            generatedClips.Add(clip);
            return clip;
        }

        private void Play(AudioClip clip)
        {
            if (soundEnabled && clip != null && audioSource != null && resources != null)
                audioSource.PlayOneShot(clip, Mathf.Clamp01(Config.AudioVolume));
        }

        private ElementRegistry CurrentElements() => resources != null && resources.Elements != null ? resources.Elements.Snapshot() : ElementRegistry.BuiltIns();

        private void OnElementsChanged()
        {
            if (CurrentScreen == ScreenState.Playing || CurrentScreen == ScreenState.Paused)
                ShowToast("元素配置已更新，本局继续使用原规则；重新开始后生效。", 8);
        }

        private void OnDestroy()
        {
            ElementCatalog.Changed -= OnElementsChanged;
            foreach (var clip in generatedClips) if (clip != null) Destroy(clip);
        }
    }
}
