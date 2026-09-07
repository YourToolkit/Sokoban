using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Sokoban.Content;
using Sokoban.Core;
using Sokoban.EditorTools;
using Sokoban.Runtime;
using UnityEditor;
using UnityEngine;

namespace Sokoban.Tests
{
    public sealed class MechanismAssetTests
    {
        private string folder;
        private string progressFolder;
        private ElementCatalog elements;
        private LevelCatalog levels;
        private EditorLevelAssetWriter writer;

        [SetUp]
        public void PrepareIsolatedAssets()
        {
            string name = "__MechanismAssetTests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", name);
            folder = "Assets/" + name;
            AssetDatabase.CreateFolder(folder, "Levels");
            elements = ScriptableObject.CreateInstance<ElementCatalog>();
            foreach (var spec in ElementRegistry.BuiltIns().Types)
            {
                var definition = ScriptableObject.CreateInstance<ElementDefinition>();
                definition.Data = spec.DeepClone();
                AssetDatabase.CreateAsset(definition, folder + "/" + spec.Id + ".asset");
                elements.Elements.Add(definition);
            }
            AssetDatabase.CreateAsset(elements, folder + "/Elements.asset");
            levels = ScriptableObject.CreateInstance<LevelCatalog>();
            AssetDatabase.CreateAsset(levels, folder + "/Levels.asset");
            var resources = ScriptableObject.CreateInstance<GameResources>();
            resources.Elements = elements; resources.Catalog = levels;
            AssetDatabase.CreateAsset(resources, folder + "/Resources.asset");
            writer = new EditorLevelAssetWriter(folder + "/Resources.asset", folder + "/Levels");
            progressFolder = Path.Combine(Application.temporaryCachePath, name);
            Directory.CreateDirectory(progressFolder);
        }

        [TearDown]
        public void RemoveIsolatedAssets()
        {
            string assetTarget = Path.GetFullPath(folder);
            string assetRoot = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string assetPrefix = Path.GetFullPath(Path.Combine(assetRoot, "__MechanismAssetTests_"));
            string progressTarget = Path.GetFullPath(progressFolder);
            string progressRoot = Path.GetFullPath(Application.temporaryCachePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string progressPrefix = Path.GetFullPath(Path.Combine(progressRoot, "__MechanismAssetTests_"));
            Assert.That(assetTarget.StartsWith(assetPrefix, StringComparison.OrdinalIgnoreCase), Is.True);
            Assert.That(string.Equals(Path.GetDirectoryName(assetTarget), assetRoot, StringComparison.OrdinalIgnoreCase), Is.True);
            Assert.That(progressTarget.StartsWith(progressPrefix, StringComparison.OrdinalIgnoreCase), Is.True);
            Assert.That(string.Equals(Path.GetDirectoryName(progressTarget), progressRoot, StringComparison.OrdinalIgnoreCase), Is.True);
            Assert.That(Path.GetFileName(progressTarget), Is.EqualTo(Path.GetFileName(assetTarget)));
            AssetDatabase.DeleteAsset(folder);
            if (Directory.Exists(progressTarget)) Directory.Delete(progressTarget, true);
        }

        [Test]
        public void MigrationIsIdempotentAndKeepsGuidIdentityVersionAndEquivalentSignature()
        {
            var asset = CreateAsset(BuiltInLevels.Create()[0]);
            asset.Data.LayoutVersion = 7;
            asset.VerifiedSolution = "RR";
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset));
            string signature = GameplayFingerprint.Compute(asset.Data, elements.Snapshot());
            Assert.That(MechanicsContentSetup.UpgradeAsset(asset, elements.Snapshot()), Is.True);
            string once = JsonUtility.ToJson(asset.Data);
            Assert.That(MechanicsContentSetup.UpgradeAsset(asset, elements.Snapshot()), Is.False);
            Assert.That(JsonUtility.ToJson(asset.Data), Is.EqualTo(once));
            Assert.That(asset.Data.Id, Is.EqualTo("level-01"));
            Assert.That(asset.Data.LayoutVersion, Is.EqualTo(7));
            Assert.That(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset)), Is.EqualTo(guid));
            Assert.That(GameplayFingerprint.Compute(asset.Data, elements.Snapshot()), Is.EqualTo(signature));
            Assert.That(asset.GetVerifiedSolution(elements.Snapshot()), Is.EqualTo("RR"));
        }

        [Test]
        public void UnityCatalogSnapshotsFreezeNestedDefaultsAndKeepPresentationSeparate()
        {
            var plate = elements.Find("pressure-plate");
            var before = elements.Snapshot();
            plate.Data.Properties[0].Name = "更新名称";
            plate.Data.Properties[0].DefaultValue.StringValues = new[] { "edited" };
            Assert.That(before.Find("pressure-plate").Properties[0].Name, Is.EqualTo("关联门"));
            Assert.That(before.Find("pressure-plate").Properties[0].DefaultValue.StringValues, Is.Empty);
            Assert.That(elements.Snapshot().Find("pressure-plate").Properties[0].Name, Is.EqualTo("更新名称"));
        }

        [Test]
        public void EveryOriginalLevelKeepsItsSavedLayoutAndDeterministicInstanceIdentities()
        {
            foreach (var original in BuiltInLevels.Create())
            {
                var migrated = LevelMigration.Snapshot(original, elements.Snapshot());
                Assert.That(migrated.Id, Is.EqualTo(original.Id));
                Assert.That(migrated.LayoutVersion, Is.EqualTo(original.LayoutVersion));
                Assert.That(migrated.Width, Is.EqualTo(original.Width));
                Assert.That(migrated.Height, Is.EqualTo(original.Height));
                CollectionAssert.AreEqual(original.Cells, migrated.Cells);
                CollectionAssert.AreEquivalent(original.Boxes, migrated.Elements.Where(value => value.TypeId == "box").Select(value => value.Position));
                CollectionAssert.AreEquivalent(original.Goals, migrated.Elements.Where(value => value.TypeId == "goal").Select(value => value.Position));
                Assert.That(migrated.Elements.Single(value => value.TypeId == "player").Position, Is.EqualTo(original.PlayerStart));
                Assert.That(JsonUtility.ToJson(migrated), Is.EqualTo(JsonUtility.ToJson(LevelMigration.Snapshot(original, elements.Snapshot()))));
            }
        }

        [Test]
        public void ArbitraryStatePresentationRulesRoundTripWithoutChangingGameplaySignature()
        {
            var definition = elements.Find("box");
            string path = AssetDatabase.GetAssetPath(definition);
            var level = LevelMigration.Snapshot(BuiltInLevels.Create()[0], elements.Snapshot());
            string signature = GameplayFingerprint.Compute(level, elements.Snapshot());
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/CrateDocked.png");
            var sound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Push.wav");
            Assert.That(sprite, Is.Not.Null);
            Assert.That(sound, Is.Not.Null);
            definition.StatePresentations = new[]
            {
                new ElementStatePresentation { Key = "future-state", Value = "充能完毕", Sprite = sprite, Sound = sound },
                new ElementStatePresentation { Key = "future-count", Value = "3.25" },
                new ElementStatePresentation { Key = "active", Value = "true", Sprite = sprite }
            };
            EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssetIfDirty(definition);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var restored = AssetDatabase.LoadAssetAtPath<ElementDefinition>(path);
            Assert.That(restored.StatePresentations, Has.Length.EqualTo(3));
            Assert.That(restored.StatePresentations[0].Key, Is.EqualTo("future-state"));
            Assert.That(restored.StatePresentations[0].Value, Is.EqualTo("充能完毕"));
            Assert.That(restored.StatePresentations[0].Sprite, Is.EqualTo(sprite));
            Assert.That(restored.StatePresentations[0].Sound, Is.EqualTo(sound));
            Assert.That(restored.StatePresentations[1].Value, Is.EqualTo("3.25"));
            Assert.That(restored.StatePresentations[2].Value, Is.EqualTo("true"));
            Assert.That(GameplayFingerprint.Compute(level, elements.Snapshot()), Is.EqualTo(signature));
        }

        [Test]
        public void EveryNewElementImageImportsAsA32PpuPointSampledTwoDimensionalSprite()
        {
            string[] paths = Directory.GetFiles("Assets/Sprites/Elements", "*.png", SearchOption.AllDirectories);
            Assert.That(paths.Length, Is.GreaterThanOrEqualTo(6), "Six shipped mechanism images must be present.");
            foreach (string file in paths)
            {
                string path = file.Replace('\\', '/');
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.That(importer, Is.Not.Null, path);
                Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite), path);
                Assert.That(importer.textureShape, Is.EqualTo(TextureImporterShape.Texture2D), path);
                Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Single), path);
                Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(32), path);
                Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point), path);
                Assert.That(importer.mipmapEnabled, Is.False, path);
                Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed), path);
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                Assert.That(sprite, Is.Not.Null, "Importer settings must actually produce a Sprite subasset: " + path);
                Assert.That(sprite.pixelsPerUnit, Is.EqualTo(32), path);
            }
        }

        [Test]
        public void SampleSlidesThroughLinkedDoorAndDoesNotJoinTheFormalCatalogWhenSaved()
        {
            var sample = MechanicsContentSetup.CreateSample();
            Assert.That(LevelValidator.Validate(sample, elements.Snapshot()), Is.Empty);
            var game = new GameSession(sample, elements.Snapshot());
            Assert.That(game.TryMove(Direction.Right).Succeeded, Is.True);
            Assert.That(game.IsWon, Is.True);
            var result = writer.Save(null, sample, "R", LevelSaveIntent.Save);
            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(levels.Levels, Is.Empty);
            Assert.That(result.Asset.GetVerifiedSolution(elements.Snapshot()), Is.EqualTo("R"));
        }

        [Test]
        public void UnknownTypesAndOpaqueParametersRoundTripAsDraftAndCannotEnterCatalog()
        {
            var sample = MechanicsContentSetup.CreateSample();
            var unknown = new ElementInstance("unknown-instance", "future-element", new GridPos(2, 1), new[]
            {
                new ParameterValue { Key = "future-property", Kind = (ParameterKind)999, RawValue = "{\"answer\":42,\"label\":\"保留\"}", BoolValue = true, IntValue = 42, FloatValue = 3.25f, StringValue = "<b>普通文本</b>", StringValues = new[] { "future-a", "future-b" } },
                new ParameterValue { Key = "future-links", Kind = ParameterKind.References, StringValues = new[] { "example-door", "missing-but-retained" } }
            });
            sample.Elements = sample.Elements.Concat(new[] { unknown }).ToArray();
            var result = writer.Save(null, sample, "", LevelSaveIntent.Save);
            Assert.That(result.Success, Is.True, result.Message);
            string path = AssetDatabase.GetAssetPath(result.Asset);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var restored = AssetDatabase.LoadAssetAtPath<LevelAsset>(path).Data.Elements.Single(value => value.Id == unknown.Id);
            Assert.That(JsonUtility.ToJson(restored), Is.EqualTo(JsonUtility.ToJson(unknown)));
            Assert.That(writer.Save(result.Asset, result.Asset.ToDefinition(), "", LevelSaveIntent.SaveAndAddToCatalog).Success, Is.False);
            Assert.That(levels.Levels, Is.Empty);
        }

        [Test]
        public void NewTypeDefinitionWorksThroughSaveAndLoadWithoutHardcodedEnumEntry()
        {
            var variant = ScriptableObject.CreateInstance<ElementDefinition>();
            variant.Data = elements.Find("sliding-box").Data.DeepClone();
            variant.Data.Id = "blue-sliding-box"; variant.Data.Name = "蓝色滑行箱";
            AssetDatabase.CreateAsset(variant, folder + "/blue-sliding-box.asset");
            elements.Elements.Add(variant);
            var sample = MechanicsContentSetup.CreateSample();
            sample.Elements.Single(value => value.TypeId == "sliding-box").TypeId = variant.Data.Id;
            var result = writer.Save(null, sample, "R", LevelSaveIntent.SaveAndAddToCatalog);
            Assert.That(result.Success, Is.True, result.Message);
            var game = new GameSession(result.Asset.ToDefinition(), elements.Snapshot());
            Assert.That(game.TryMove(Direction.Right).Succeeded, Is.True);
            Assert.That(game.IsWon, Is.True);
        }

        [Test]
        public void SharedRuleChangesInvalidateTheSolutionWithoutChangingLevelFileOrLayoutVersion()
        {
            var result = writer.Save(null, MechanicsContentSetup.CreateSample(), "R", LevelSaveIntent.Save);
            Assert.That(result.Success, Is.True, result.Message);
            var asset = result.Asset;
            string before = JsonUtility.ToJson(asset.Data);
            elements.Find("door").Data.RulesVersion++;
            Assert.That(asset.GetVerifiedSolution(elements.Snapshot()), Is.Empty);
            Assert.That(JsonUtility.ToJson(asset.Data), Is.EqualTo(before));
            var save = writer.Save(asset, asset.ToDefinition(), asset.VerifiedSolution, LevelSaveIntent.Save);
            Assert.That(save.Success, Is.True, save.Message);
            Assert.That(asset.VerifiedSolution, Is.Empty);
            Assert.That(asset.Data.LayoutVersion, Is.Zero);
        }

        [Test]
        public void MetadataAndArtworkEditsPreserveGameplayScores()
        {
            var level = MechanicsContentSetup.CreateSample();
            var store = new ProgressStore(progressFolder);
            Assert.That(store.RecordWin(level, 1, 1, elements.Snapshot()), Is.True);
            level.Name = "仅改名称"; level.Description = "仅改说明";
            elements.Find("sliding-box").Data.Name = "只翻译名称";
            elements.Find("sliding-box").Icon = null;
            var record = new ProgressStore(progressFolder).Get(level, elements.Snapshot());
            Assert.That(record, Is.Not.Null);
            Assert.That(record.BestSteps, Is.EqualTo(1));
        }

        [Test]
        public void RuleRevisionInvalidatesOldScoresAndNewWinStartsItsOwnBestRun()
        {
            var level = MechanicsContentSetup.CreateSample();
            var store = new ProgressStore(progressFolder);
            Assert.That(store.RecordWin(level, 1, 1, elements.Snapshot()), Is.True);
            elements.Find("sliding-box").Data.RulesVersion++;
            Assert.That(store.Get(level, elements.Snapshot()), Is.Null);
            Assert.That(store.RecordWin(level, 4, 1, elements.Snapshot()), Is.True);
            Assert.That(new ProgressStore(progressFolder).Get(level, elements.Snapshot()).BestSteps, Is.EqualTo(4));
        }

        [Test]
        public void LegacyScoresAreClaimedOnlyByEquivalentBaselineRulesAndPersistTheirSignature()
        {
            var level = BuiltInLevels.Create()[0];
            var store = new ProgressStore(progressFolder);
            Assert.That(store.RecordWin(level.Id, 2, 2, level.LayoutVersion), Is.True);
            var migrated = LevelMigration.Snapshot(level, elements.Snapshot());
            Assert.That(store.Get(migrated, elements.Snapshot()), Is.Not.Null);
            string signature = new ProgressStore(progressFolder).Get(migrated, elements.Snapshot()).GameplaySignature;
            Assert.That(signature, Is.Not.Empty);
            elements.Find("box").Data.RulesVersion++;
            Assert.That(store.Get(migrated, elements.Snapshot()), Is.Null);
        }

        [Test]
        public void LegacyRecordCannotBeClaimedByNewMechanismsEvenWhenIdAndLayoutVersionMatch()
        {
            var level = MechanicsContentSetup.CreateSample();
            var store = new ProgressStore(progressFolder);
            Assert.That(store.RecordWin(level.Id, 1, 1, level.LayoutVersion), Is.True);
            Assert.That(store.Get(level, elements.Snapshot()), Is.Null);
        }

        [Test]
        public void TerrainDefaultsParticipateInScoresEvenWithoutALayoutOrRulesVersionChange()
        {
            var floor = elements.Find("floor");
            floor.Data.Properties = new[] { new PropertyDescriptor
            {
                Key = "future-friction", Name = "摩擦参数", Kind = ParameterKind.Float, Min = 0, Max = 10,
                DefaultValue = new ParameterValue { Key = "future-friction", Kind = ParameterKind.Float, FloatValue = 1 }
            } };
            var level = LevelMigration.Snapshot(BuiltInLevels.Create()[0], elements.Snapshot());
            var store = new ProgressStore(progressFolder);
            Assert.That(store.RecordWin(level, 2, 2, elements.Snapshot()), Is.True);
            floor.Data.Properties[0].DefaultValue.FloatValue = 2;
            Assert.That(store.Get(level, elements.Snapshot()), Is.Null);
        }

        [Test]
        public void OverriddenParametersKeepTheirEffectiveScoreWhenTheUnusedTemplateDefaultChanges()
        {
            var box = elements.Find("box");
            box.Data.Properties = new[] { new PropertyDescriptor
            {
                Key = "future-weight", Name = "重量参数", Kind = ParameterKind.Integer, Min = 1, Max = 10,
                DefaultValue = new ParameterValue { Key = "future-weight", Kind = ParameterKind.Integer, IntValue = 1 }
            } };
            var level = LevelMigration.Snapshot(BuiltInLevels.Create()[0], elements.Snapshot());
            level.Elements.Single(value => value.TypeId == "box").Overrides = new[]
            {
                new ParameterValue { Key = "future-weight", Kind = ParameterKind.Integer, IntValue = 3 }
            };
            var store = new ProgressStore(progressFolder);
            Assert.That(store.RecordWin(level, 2, 2, elements.Snapshot()), Is.True);
            box.Data.Properties[0].DefaultValue.IntValue = 2;
            Assert.That(store.Get(level, elements.Snapshot()), Is.Not.Null);
            level.Elements.Single(value => value.TypeId == "box").Overrides = Array.Empty<ParameterValue>();
            Assert.That(store.Get(level, elements.Snapshot()), Is.Null);
        }

        [Test]
        public void LegacyScoresAreNotClaimedAfterATypeGainsParameters()
        {
            var level = BuiltInLevels.Create()[0];
            var store = new ProgressStore(progressFolder);
            Assert.That(store.RecordWin(level.Id, 2, 2, level.LayoutVersion), Is.True);
            elements.Find("box").Data.Properties = new[] { new PropertyDescriptor
            {
                Key = "new-behaviour", Name = "新增特性", Kind = ParameterKind.Boolean,
                DefaultValue = new ParameterValue { Key = "new-behaviour", Kind = ParameterKind.Boolean }
            } };
            Assert.That(store.Get(level, elements.Snapshot()), Is.Null);
            Assert.That(store.Get(level.Id, level.LayoutVersion).GameplaySignature, Is.Empty);
        }

        [Test]
        public void InvalidConfigurationCannotExposeAnOldSolutionOrScoreEvenWhenItsFingerprintMatches()
        {
            var result = writer.Save(null, MechanicsContentSetup.CreateSample(), "R", LevelSaveIntent.Save);
            Assert.That(result.Success, Is.True, result.Message);
            var store = new ProgressStore(progressFolder);
            Assert.That(store.RecordWin(result.Asset.Data, 1, 1, elements.Snapshot()), Is.True);
            string signature = GameplayFingerprint.Compute(result.Asset.Data, elements.Snapshot());
            // Unknown parameter type is rejected even if all instances override its default value.
            elements.Find("pressure-plate").Data.Properties[0].Kind = (ParameterKind)999;
            Assert.That(GameplayFingerprint.Compute(result.Asset.Data, elements.Snapshot()), Is.EqualTo(signature));
            Assert.That(result.Asset.GetVerifiedSolution(elements.Snapshot()), Is.Empty);
            Assert.That(store.Get(result.Asset.Data, elements.Snapshot()), Is.Null);
            Assert.Throws<ArgumentException>(() => store.RecordWin(result.Asset.Data, 1, 1, elements.Snapshot()));
        }

        private LevelAsset CreateAsset(LevelDefinition level)
        {
            var asset = ScriptableObject.CreateInstance<LevelAsset>(); asset.Data = level;
            AssetDatabase.CreateAsset(asset, folder + "/Levels/Existing.asset");
            return asset;
        }
    }
}
