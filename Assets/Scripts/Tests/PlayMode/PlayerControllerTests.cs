#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Sokoban.Core;
using Sokoban.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Sokoban.Tests
{
    public sealed class PlayerControllerTests
    {
        private GameController game;
        private PlayerController controls;
        private static readonly MethodInfo Process = typeof(PlayerController).GetMethod("ProcessInput", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo Focus = typeof(PlayerController).GetMethod("OnApplicationFocus", BindingFlags.Instance | BindingFlags.NonPublic);

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            var room = LevelAuthoring.CreateBlank(12, 12);
            room.Name = "角色控制验收";
            LevelAuthoring.Paint(room, new GridPos(2, 2), LevelBrush.Player);
            LevelAuthoring.Paint(room, new GridPos(9, 9), LevelBrush.Box);
            LevelAuthoring.Paint(room, new GridPos(10, 9), LevelBrush.Goal);
            PlaytestRequest.Pending = room;
            game = PresentationFixture.CreateGame();
            yield return null;
            controls = game.PlayerController;
            Focus.Invoke(controls, new object[] { true });
            if (game.CurrentScreen == GameController.ScreenState.Paused) game.Resume();
            // Feed deterministic keyboard frames while exercising the real scene flow and rules.
            controls.enabled = false;
            Assert.That(game.IsPlaytest, Is.True);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (game != null) Object.Destroy(game.gameObject);
            PlaytestRequest.Pending = null;
            yield return null;
        }

        [Test]
        public void HoldUsesFirstDelayThenRepeatIntervalAndDoesNotQueueDuringAnimation()
        {
            Frame(10, KeyCode.D, KeyCode.D);
            Assert.That(game.Session.State.Steps, Is.EqualTo(1));
            Frame(20, KeyCode.None, KeyCode.D);
            Assert.That(game.Session.State.Steps, Is.EqualTo(1), "An animating move must not queue another step.");
            Settle();
            float first = 10 + controls.Settings.RepeatDelay;
            Frame(first - .01f, KeyCode.None, KeyCode.D);
            Assert.That(game.Session.State.Steps, Is.EqualTo(1));
            Frame(first + .001f, KeyCode.None, KeyCode.D);
            Assert.That(game.Session.State.Steps, Is.EqualTo(2));
            Settle();
            Frame(first + .001f + controls.Settings.RepeatInterval - .01f, KeyCode.None, KeyCode.D);
            Assert.That(game.Session.State.Steps, Is.EqualTo(2));
            Frame(first + .002f + controls.Settings.RepeatInterval, KeyCode.None, KeyCode.D);
            Assert.That(game.Session.State.Player, Is.EqualTo(new GridPos(5, 2)));
        }

        [Test]
        public void FreshDirectionBeatsAnotherHeldDirectionAndInspectorBindingsAreUsed()
        {
            Frame(1, KeyCode.W, KeyCode.D, extraHeld: KeyCode.W);
            Assert.That(game.Session.State.Player, Is.EqualTo(new GridPos(2, 3)));
            Settle();
            var serialized = new SerializedObject(controls);
            var right = serialized.FindProperty("rightKeys");
            right.arraySize = 1;
            right.GetArrayElementAtIndex(0).intValue = (int)KeyCode.J;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Frame(2, KeyCode.D, KeyCode.D);
            Assert.That(game.Session.State.Steps, Is.EqualTo(1));
            Frame(3, KeyCode.J, KeyCode.J);
            Assert.That(game.Session.State.Player, Is.EqualTo(new GridPos(3, 3)));
        }

        [Test]
        public void TextInputBlocksMovementAndEscapeAndClearsHeldState()
        {
            Frame(10, KeyCode.D, KeyCode.D); Settle();
            Frame(10.01f, KeyCode.Escape, KeyCode.D, textInput: true);
            Assert.That(game.CurrentScreen, Is.EqualTo(GameController.ScreenState.Playing));
            Assert.That(game.Session.State.Steps, Is.EqualTo(1));
            Frame(10.02f, KeyCode.None, KeyCode.D);
            Assert.That(game.Session.State.Steps, Is.EqualTo(2));
        }

        [Test]
        public void PauseUndoAndRestartResetTheHeldDirection()
        {
            Frame(10, KeyCode.D, KeyCode.D); Settle();
            Frame(10.01f, KeyCode.Escape);
            Assert.That(game.CurrentScreen, Is.EqualTo(GameController.ScreenState.Paused));
            Frame(10.02f, KeyCode.Escape);
            Frame(10.03f, KeyCode.None, KeyCode.D);
            Assert.That(game.Session.State.Steps, Is.EqualTo(2)); Settle();
            Frame(10.04f, KeyCode.Z);
            Assert.That(game.Session.State.Steps, Is.EqualTo(1));
            Frame(10.05f, KeyCode.None, KeyCode.D);
            Assert.That(game.Session.State.Steps, Is.EqualTo(2)); Settle();
            Frame(10.06f, KeyCode.R);
            Assert.That(game.Session.State.Steps, Is.Zero);
            Frame(10.07f, KeyCode.None, KeyCode.D);
            Assert.That(game.Session.State.Player, Is.EqualTo(new GridPos(3, 2)));
        }

        [Test]
        public void FocusLossPausesAndRequiresReturnBeforeKeyboardCommands()
        {
            Focus.Invoke(controls, new object[] { false });
            Frame(1, KeyCode.Escape, KeyCode.D);
            Assert.That(game.CurrentScreen, Is.EqualTo(GameController.ScreenState.Paused));
            Assert.That(game.Session.State.Steps, Is.Zero);
            Focus.Invoke(controls, new object[] { true });
            Frame(2, KeyCode.Escape);
            Frame(2.01f, KeyCode.None, KeyCode.D);
            Assert.That(game.Session.State.Steps, Is.EqualTo(1));
        }

        [Test]
        public void SelectionAndWorkshopBlockDirectionsThenReturnWithFreshInput()
        {
            game.ShowLevelSelect();
            Frame(1, KeyCode.D, KeyCode.D);
            Assert.That(game.Session.State.Steps, Is.Zero);
            // Return to our isolated playtest, never replace a formal level or its progress.
            var room = game.WorkshopSourceDefinition;
            game.StartPlaytest(room, null);
            game.OpenWorkshop();
            Frame(2, KeyCode.Escape, KeyCode.D);
            Assert.That(game.CurrentScreen, Is.EqualTo(GameController.ScreenState.Workshop));
            Assert.That(game.Session.State.Steps, Is.Zero);
            game.ReturnFromWorkshop();
            Frame(2.01f, KeyCode.None, KeyCode.D);
            Assert.That(game.Session.State.Steps, Is.EqualTo(1));
        }

        private void Frame(float time, KeyCode down, KeyCode held = KeyCode.None, bool textInput = false, KeyCode extraHeld = KeyCode.None)
        {
            Func<KeyCode, bool> pressed = key => key == down;
            Func<KeyCode, bool> holding = key => key == held || key == extraHeld;
            Process.Invoke(controls, new object[] { time, pressed, holding, textInput });
        }
        private void Settle() => game.Board.Sync(game.Session.State);
    }
}
#endif
