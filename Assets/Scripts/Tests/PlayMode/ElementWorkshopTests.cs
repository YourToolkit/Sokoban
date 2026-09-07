#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sokoban.Content;
using Sokoban.Core;
using Sokoban.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Sokoban.Tests
{
    public sealed class ElementWorkshopTests
    {
        private readonly List<Object> cleanup = new List<Object>();
        private RuntimeWorkshop workshop;
        private GameResources resources;
        private ElementCatalog catalog;

        [SetUp]
        public void CreateIsolatedAuthoringContext()
        {
            resources = ScriptableObject.CreateInstance<GameResources>(); cleanup.Add(resources);
            catalog = ScriptableObject.CreateInstance<ElementCatalog>(); cleanup.Add(catalog); resources.Elements = catalog;
            foreach (var spec in ElementRegistry.BuiltIns().Types)
            {
                var definition = ScriptableObject.CreateInstance<ElementDefinition>(); cleanup.Add(definition);
                definition.Data = spec; catalog.Elements.Add(definition);
            }
            var custom = ScriptableObject.CreateInstance<ElementDefinition>(); cleanup.Add(custom);
            custom.Data = new ElementTypeSpec
            {
                Id = "configured-box", Name = "自定义箱子", Role = ElementRole.Box, Mechanics = new[] { MechanicKind.Push },
                Properties = new[] { new PropertyDescriptor { Key = "weight", Name = "重量", Kind = ParameterKind.Integer, Min = 1, Max = 9, DefaultValue = new ParameterValue { Key = "weight", Kind = ParameterKind.Integer, IntValue = 2 } } }
            };
            catalog.Elements.Add(custom);
            var root = new GameObject("Element workshop tests"); cleanup.Add(root);
            workshop = root.AddComponent<RuntimeWorkshop>(); workshop.Initialize(null, null, null, resources);
            workshop.SetDocument(LevelAuthoring.CreateBlank(10, 8));
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            PlaytestRequest.Pending = null;
            foreach (var value in cleanup) if (value != null) Object.Destroy(value);
            cleanup.Clear(); yield return null;
        }

        private void Paint(string type, int x, int y)
        {
            workshop.SetElement(type); var p = new GridPos(x, y);
            workshop.BeginGesture(p); workshop.EndGesture(p);
        }

        [Test]
        public void InstancePropertiesParticipateInDirtyUndoAndResetWithoutMutatingDefaults()
        {
            Paint("configured-box", 2, 2);
            var level = workshop.Draft; workshop.SetDocument(level);
            Assert.That(workshop.SelectElementAt(new GridPos(2, 2)), Is.True);
            Assert.That(workshop.SetSelectedParameter(new ParameterValue { Key = "weight", Kind = ParameterKind.Integer, IntValue = 7 }), Is.True);
            Assert.That(workshop.IsDirty, Is.True);
            Assert.That(workshop.Draft.Elements.Single(element => element.TypeId == "configured-box").Overrides.Single().IntValue, Is.EqualTo(7));
            Assert.That(catalog.Find("configured-box").Data.Properties[0].DefaultValue.IntValue, Is.EqualTo(2));
            workshop.UndoEdit();
            Assert.That(workshop.IsDirty, Is.False);
            Assert.That(workshop.Draft.Elements.Single(element => element.TypeId == "configured-box").Overrides, Is.Empty);
            workshop.RedoEdit();
            Assert.That(workshop.Draft.Elements.Single(element => element.TypeId == "configured-box").Overrides.Single().IntValue, Is.EqualTo(7));
            workshop.SelectElementAt(new GridPos(2, 2));
            Assert.That(workshop.ResetSelectedParameter("weight"), Is.True);
            Assert.That(workshop.Draft.Elements.Single(element => element.TypeId == "configured-box").Overrides, Is.Empty);
        }

        [Test]
        public void PickingTwoDoorsCommitsOneTransactionAndCancelPreservesTheOriginalLinks()
        {
            Paint("pressure-plate", 2, 2); Paint("door", 5, 2); Paint("door", 7, 2);
            workshop.SetDocument(workshop.Draft);
            workshop.SelectElementAt(new GridPos(2, 2));
            Assert.That(workshop.BeginReferencePicking("targetDoors"), Is.True);
            Assert.That(workshop.PickReferenceAt(new GridPos(5, 2)), Is.True);
            Assert.That(workshop.PickReferenceAt(new GridPos(7, 2)), Is.True);
            Assert.That(workshop.IsDirty, Is.False, "Reference preview must not mutate the document.");
            Assert.That(workshop.PickReferenceAt(new GridPos(4, 4)), Is.False);
            Assert.That(workshop.CommitReferencePicking(), Is.True);
            var plate = workshop.Draft.Elements.Single(element => element.TypeId == "pressure-plate");
            Assert.That(plate.Overrides.Single(value => value.Key == "targetDoors").StringValues, Has.Length.EqualTo(2));
            workshop.UndoEdit(); Assert.That(workshop.IsDirty, Is.False); Assert.That(workshop.CanUndo, Is.False);
            workshop.RedoEdit(); workshop.SelectElementAt(new GridPos(2, 2));
            workshop.BeginReferencePicking("targetDoors"); workshop.PickReferenceAt(new GridPos(5, 2)); workshop.CancelReferencePicking();
            Assert.That(workshop.Draft.Elements.Single(element => element.TypeId == "pressure-plate").Overrides.Single().StringValues, Has.Length.EqualTo(2));
        }

        [Test]
        public void RepeatedClickSelectsThePressurePlateUnderABoxWithoutMovingEither()
        {
            Paint("pressure-plate", 2, 2); Paint("configured-box", 2, 2);
            workshop.SetDocument(workshop.Draft); workshop.SetTool(WorkshopTool.Select);
            workshop.BeginGesture(new GridPos(2, 2)); workshop.EndGesture(new GridPos(2, 2));
            Assert.That(workshop.Draft.Elements.Single(element => element.Id == workshop.SelectedInstanceId).TypeId, Is.EqualTo("configured-box"));
            workshop.BeginGesture(new GridPos(2, 2)); workshop.EndGesture(new GridPos(2, 2));
            Assert.That(workshop.Draft.Elements.Single(element => element.Id == workshop.SelectedInstanceId).TypeId, Is.EqualTo("pressure-plate"));
            Assert.That(workshop.BeginReferencePicking("targetDoors"), Is.True);
            Assert.That(workshop.IsDirty, Is.False); Assert.That(workshop.CanUndo, Is.False);
        }

        [Test]
        public void ACustomTypeStrokeAndItsInstanceParametersSurviveDraggingAndUndo()
        {
            Paint("configured-box", 2, 2);
            workshop.SelectElementAt(new GridPos(2, 2));
            workshop.SetSelectedParameter(new ParameterValue { Key = "weight", Kind = ParameterKind.Integer, IntValue = 6 });
            var before = workshop.Draft.Elements.Single(element => element.TypeId == "configured-box");
            workshop.SetTool(WorkshopTool.Select); workshop.BeginGesture(new GridPos(2, 2)); workshop.EndGesture(new GridPos(4, 3));
            var after = workshop.Draft.Elements.Single(element => element.Id == before.Id);
            Assert.That(after.Position, Is.EqualTo(new GridPos(4, 3))); Assert.That(after.TypeId, Is.EqualTo(before.TypeId));
            Assert.That(after.Overrides.Single().IntValue, Is.EqualTo(6));
            workshop.UndoEdit(); Assert.That(workshop.Draft.Elements.Single(element => element.Id == before.Id).Position, Is.EqualTo(new GridPos(2, 2)));
        }

        [UnityTest]
        public IEnumerator CatalogRefreshWaitsForAStrokeAndPreservesRemovedTypeData()
        {
            Paint("configured-box", 2, 2);
            workshop.SetDocument(workshop.Draft);
            workshop.SetElement("configured-box"); workshop.BeginGesture(new GridPos(3, 3));
            catalog.Elements.Remove(catalog.Find("configured-box")); ElementCatalog.NotifyChanged();
            yield return null;
            workshop.UpdateGesture(new GridPos(5, 3)); workshop.EndGesture(new GridPos(5, 3));
            yield return null;
            Assert.That(workshop.Draft.Elements.Count(element => element.TypeId == "configured-box"), Is.EqualTo(4));
            Assert.That(LevelValidator.Validate(workshop.Draft, catalog.Snapshot()).Any(issue => issue.Message.Contains("未知") || issue.Message.Contains("未注册")), Is.True);
            workshop.UndoEdit();
            Assert.That(workshop.Draft.Elements.Single(element => element.TypeId == "configured-box").Position, Is.EqualTo(new GridPos(2, 2)));
        }

        [UnityTest]
        public IEnumerator RealPrefabBuildsItsElementListFromTheCatalogAndOpensInstanceProperties()
        {
            var app = PresentationFixture.CreateGame(); cleanup.Add(app.gameObject);
            yield return null;
            if (app.CurrentScreen == GameController.ScreenState.Paused) app.Resume();
            app.OpenWorkshop();
            var editor = app.Workshop;
            Assert.That(editor.View.Get<UiList>("Element list").Items.Count, Is.GreaterThanOrEqualTo(8));
            editor.SetDocument(LevelAuthoring.CreateBlank(8, 8));
            editor.SetElement("pressure-plate"); editor.BeginGesture(new GridPos(2, 2)); editor.EndGesture(new GridPos(2, 2));
            Assert.That(editor.SelectElementAt(new GridPos(2, 2)), Is.True);
            editor.OpenObjectProperties();
            yield return null;
            Assert.That(editor.View.GetComponentsInChildren<TMP_Text>().Any(label => label.text == "关联门"), Is.True);
            Assert.That(editor.BeginReferencePicking("targetDoors"), Is.True);
            Assert.That(editor.View.Get<UnityEngine.UI.Button>("Object properties").GetComponent<UiButtonVisual>().Label.text, Is.EqualTo("完成关联"));
            editor.CancelReferencePicking();
        }
    }
}
#endif
