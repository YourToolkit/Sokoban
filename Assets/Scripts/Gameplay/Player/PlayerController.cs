using System;
using Sokoban.Content;
using Sokoban.Core;
using UnityEngine;

namespace Sokoban.Runtime
{
    /// <summary>Reads player controls and sends commands; GameSession owns grid movement rules.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("推箱子/角色控制")]
    public sealed class PlayerController : MonoBehaviour
    {
        [Header("游戏与操作配置")]
        [SerializeField, InspectorName("游戏流程")] private GameController game;
        [SerializeField, InspectorName("操作配置")] private GameConfig settings;
        [Header("移动按键（支持多个绑定）")]
        [SerializeField, InspectorName("向上")] private KeyCode[] upKeys = { KeyCode.W, KeyCode.UpArrow };
        [SerializeField, InspectorName("向右")] private KeyCode[] rightKeys = { KeyCode.D, KeyCode.RightArrow };
        [SerializeField, InspectorName("向下")] private KeyCode[] downKeys = { KeyCode.S, KeyCode.DownArrow };
        [SerializeField, InspectorName("向左")] private KeyCode[] leftKeys = { KeyCode.A, KeyCode.LeftArrow };
        [Header("游戏快捷键")]
        [SerializeField, InspectorName("撤销")] private KeyCode undoKey = KeyCode.Z;
        [SerializeField, InspectorName("重开")] private KeyCode restartKey = KeyCode.R;
        [SerializeField, InspectorName("暂停／返回")] private KeyCode backKey = KeyCode.Escape;

        private Direction? heldDirection;
        private float repeatAt;
        private bool focused = true;
        private static readonly Func<KeyCode, bool> KeyDown = Input.GetKeyDown;
        private static readonly Func<KeyCode, bool> KeyHeld = Input.GetKey;
        public GameController Game => game;
        public GameConfig Settings => settings;

        private void Awake()
        {
            if (game != null && settings != null) return;
            Debug.LogError("角色控制缺少游戏流程或操作配置，请在 Inspector 中补齐引用。", this);
            enabled = false;
        }

        private void Update() => ProcessInput(Time.unscaledTime, KeyDown, KeyHeld, UiFactory.IsTextInputFocused());

        // The frame is read once here; a fresh direction takes priority over another held key.
        private void ProcessInput(float now, Func<KeyCode, bool> keyDown, Func<KeyCode, bool> keyHeld, bool textInputFocused)
        {
            if (game == null || !game.isActiveAndEnabled || settings == null) { ResetInput(); return; }
            if (!focused || textInputFocused) { ResetInput(); return; }
            if (Pressed(backKey, keyDown)) { ResetInput(); game.NavigateBack(); return; }
            if (game.CurrentScreen != GameController.ScreenState.Playing) { ResetInput(); return; }
            if (!game.CanControlPlayer) return;
            if (Pressed(undoKey, keyDown)) { ResetInput(); game.Undo(); return; }
            if (Pressed(restartKey, keyDown)) { ResetInput(); game.Restart(); return; }
            var direction = ReadDirection(keyDown) ?? ReadDirection(keyHeld);
            if (!direction.HasValue) { ResetInput(); return; }
            if (direction != heldDirection)
            {
                heldDirection = direction;
                repeatAt = now + Mathf.Max(.08f, settings.RepeatDelay);
                game.Move(direction.Value);
            }
            else if (now >= repeatAt)
            {
                repeatAt = now + Mathf.Max(.06f, settings.RepeatInterval);
                game.Move(direction.Value);
            }
        }

        private Direction? ReadDirection(Func<KeyCode, bool> pressed)
        {
            if (AnyPressed(upKeys, pressed)) return Direction.Up;
            if (AnyPressed(rightKeys, pressed)) return Direction.Right;
            if (AnyPressed(downKeys, pressed)) return Direction.Down;
            if (AnyPressed(leftKeys, pressed)) return Direction.Left;
            return null;
        }

        private static bool AnyPressed(KeyCode[] keys, Func<KeyCode, bool> pressed)
        {
            if (keys != null) foreach (var key in keys) if (Pressed(key, pressed)) return true;
            return false;
        }
        private static bool Pressed(KeyCode key, Func<KeyCode, bool> pressed) => key != KeyCode.None && pressed(key);

        public void ResetInput() { heldDirection = null; repeatAt = 0; }
        private void OnDisable() => ResetInput();
        private void OnApplicationFocus(bool hasFocus)
        {
            focused = hasFocus;
            ResetInput();
            if (!hasFocus && game != null) game.Pause();
        }

#if UNITY_EDITOR
        public void Configure(GameController app, GameConfig config) { game = app; settings = config; }
#endif
    }
}
