#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using NUnit.Framework;
using Sokoban.Content;
using Sokoban.Core;
using Sokoban.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sokoban.Tests
{
    public sealed class MechanismPresentationTests
    {
        private GameController game;
        private LevelDefinition room;
        [UnitySetUp]
        public IEnumerator Create()
        {
            var asset = Resources.Load<LevelAsset>("Levels/Examples/SlidingAndDoors");
            Assert.That(asset, Is.Not.Null);
            room = asset.ToDefinition();
            // Keep the slide playable after settling, so the test can exercise undo through real controls.
            room.Elements.First(e => e.TypeId == "goal").Position = new GridPos(7, 3);
            LevelMigration.SyncLegacy(room);
            PlaytestRequest.Pending = room;
            game = PresentationFixture.CreateGame();
            yield return null;
            Assert.That(game.CurrentScreen, Is.EqualTo(GameController.ScreenState.Playing));
        }
        [UnityTearDown]
        public IEnumerator Destroy()
        { if (game != null) Object.Destroy(game.gameObject); PlaytestRequest.Pending = null; yield return null; }

        [UnityTest]
        public IEnumerator SlidingFramesPauseAndResumeWithoutAdvancingPresentation()
        {
            game.Move(Direction.Right);
            Assert.That(game.LastAction.Frames.Count, Is.GreaterThan(1));
            game.Pause();
            var crate = game.Board.RendererFor(room.Elements.First(e => e.TypeId == "sliding-box").Id);
            Vector3 pausedPosition = crate.transform.position;
            yield return new WaitForSecondsRealtime(.25f);
            Assert.That(crate.transform.position, Is.EqualTo(pausedPosition));
            Assert.That(game.Board.IsAnimating, Is.True);
            game.Resume();
            yield return Settle();
            Assert.That(crate.transform.position, Is.EqualTo(new Vector3(7.5f, 2.5f, 0)));
            Assert.That(game.Session.State.Steps, Is.EqualTo(1));
            Assert.That(game.Session.State.Pushes, Is.EqualTo(1));
            game.Undo();
            Assert.That(crate.transform.position, Is.EqualTo(new Vector3(2.5f, 2.5f, 0)));
            Assert.That(game.Session.State.Steps, Is.Zero);
        }

        [UnityTest]
        public IEnumerator RestartCancelsEveryRemainingSlideFrame()
        {
            game.Move(Direction.Right);
            game.Restart();
            yield return new WaitForSecondsRealtime(1);
            Assert.That(game.Board.IsAnimating, Is.False);
            Assert.That(game.CurrentScreen, Is.EqualTo(GameController.ScreenState.Playing));
            Assert.That(game.Session.State.Steps, Is.Zero);
            Assert.That(game.Board.BoxRenderers.Single().transform.position, Is.EqualTo(new Vector3(2.5f, 2.5f, 0)));
        }

        [UnityTest]
        public IEnumerator LeavingDuringSlideCannotCompleteOrMoveTheNextRoom()
        {
            game.Move(Direction.Right);
            game.StartLevel(0);
            var expected = game.Session.State.Player;
            yield return new WaitForSecondsRealtime(1);
            Assert.That(game.CurrentLevelIndex, Is.Zero);
            Assert.That(game.Session.State.Steps, Is.Zero);
            Assert.That(game.Board.PlayerRenderer.transform.position, Is.EqualTo(new Vector3(expected.X + .5f, expected.Y + .5f, 0)));
            Assert.That(game.CurrentScreen, Is.EqualTo(GameController.ScreenState.Playing));
        }

        [UnityTest]
        public IEnumerator InitialMechanismAppearanceAndIncompleteDraftUseStableInstances()
        {
            var door = game.Session.State.Elements.First(e => e.Id == "example-door");
            Assert.That(door.Active, Is.True);
            Assert.That(game.Board.RendererFor(door.Id).sprite, Is.EqualTo(game.ResourcesConfig.Elements.Find("door").ActiveSprite));
            room.Elements = room.Elements.Where(e => e.TypeId != "player").Concat(new[] { new ElementInstance("lost", "missing-type", new GridPos(3, 1)) }).ToArray();
            game.Board.ShowDraft(room);
            Assert.That(game.Board.PlayerRenderer.gameObject.activeSelf, Is.False);
            Assert.That(game.Board.RendererFor("lost"), Is.Not.Null);
            Assert.That(game.Board.RendererFor("lost").color, Is.EqualTo(Color.magenta));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ConfiguredStateAppearanceOverridesTheFallbackWithoutARendererBranch()
        {
            var definition = game.ResourcesConfig.Elements.Find("door");
            var before = definition.StatePresentations;
            try
            {
                definition.StatePresentations = new[] { new ElementStatePresentation
                    { Key = "active", Value = "true", Sprite = game.ResourcesConfig.Elements.Find("player").Sprite } };
                game.Board.Sync(game.Session.State);
                Assert.That(game.Board.RendererFor("example-door").sprite, Is.EqualTo(definition.StatePresentations[0].Sprite));
                yield return null;
            }
            finally { definition.StatePresentations = before; }
        }

        private IEnumerator Settle()
        {
            float end = Time.realtimeSinceStartup + 8;
            while (game.Board.IsAnimating && Time.realtimeSinceStartup < end) yield return null;
            Assert.That(game.Board.IsAnimating, Is.False);
        }
    }
}
#endif
