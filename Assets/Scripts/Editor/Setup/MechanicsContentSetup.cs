using System;
using System.Collections.Generic;
using System.IO;
using Sokoban.Content;
using Sokoban.Core;
using Sokoban.Runtime;
using UnityEditor;
using UnityEngine;

namespace Sokoban.EditorTools
{
    /// <summary>Explicit, idempotent upgrade. Never replaces existing level content or element tuning.</summary>
    public static class MechanicsContentSetup
    {
        public const string CatalogPath = "Assets/Resources/Configs/ElementCatalog.asset";
        public const string ElementFolder = "Assets/Resources/Configs/Elements";
        public const string SamplePath = "Assets/Resources/Levels/Examples/SlidingAndDoors.asset";

        [MenuItem("推箱子/准备元素目录与升级关卡", priority = 52)]
        public static void Install()
        {
            var resources = AssetDatabase.LoadAssetAtPath<GameResources>(ProjectSetup.ResourcesPath);
            if (resources == null) throw new InvalidOperationException("请先准备游戏资源入口。");
            ImportElementSprites();
            EnsureFolder(ElementFolder);
            var catalog = resources.Elements;
            if (catalog == null)
            {
                catalog = AssetDatabase.LoadAssetAtPath<ElementCatalog>(CatalogPath);
                if (catalog == null)
                {
                    catalog = ScriptableObject.CreateInstance<ElementCatalog>();
                    AssetDatabase.CreateAsset(catalog, CatalogPath);
                }
                resources.Elements = catalog;
                EditorUtility.SetDirty(resources);
            }
            if (catalog.Elements == null) catalog.Elements = new List<ElementDefinition>();
            foreach (var type in ElementRegistry.BuiltIns().Types)
            {
                var element = catalog.Find(type.Id);
                if (element == null)
                {
                    string path = ElementFolder + "/" + type.Id + ".asset";
                    element = AssetDatabase.LoadAssetAtPath<ElementDefinition>(path);
                    if (element == null)
                    {
                        element = ScriptableObject.CreateInstance<ElementDefinition>();
                        element.Data = type.DeepClone();
                        ConnectPresentation(element);
                        AssetDatabase.CreateAsset(element, path);
                    }
                    catalog.Elements.Add(element);
                    EditorUtility.SetDirty(catalog);
                }
            }
            var registry = catalog.Snapshot();
            var issues = registry.ValidateTypes();
            if (issues.Count > 0) throw new InvalidOperationException("元素配置有误：" + issues[0].Message);
            int upgraded = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:LevelAsset", new[] { "Assets" }))
            {
                var asset = AssetDatabase.LoadAssetAtPath<LevelAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (UpgradeAsset(asset, registry)) upgraded++;
            }
            EnsureSample(registry);
            AssetDatabase.SaveAssets();
            BindBoardPrefab("Assets/Prefabs/Board/Board.prefab", catalog);
            BindBoardPrefab("Assets/Prefabs/GameRoot.prefab", catalog);
            ElementCatalog.NotifyChanged();
            Debug.Log("元素目录已准备，升级 " + upgraded + " 个关卡。原关卡编号、布局版本与目录顺序保留；示例关卡未加入选关目录。");
        }

        public static bool UpgradeAsset(LevelAsset asset, ElementRegistry registry)
        {
            if (asset == null || asset.Data == null || asset.Data.SchemaVersion != 0) return false;
            // Upgrade creates stable instance identities, preserving the original definition's semantic layout.
            var upgraded = LevelMigration.Snapshot(asset.Data, registry);
            string oldFingerprint = GameplayFingerprint.Compute(asset.Data, registry);
            if (oldFingerprint != GameplayFingerprint.Compute(upgraded, registry))
                throw new InvalidOperationException("关卡升级改变了玩法，已中止：" + asset.name);
            asset.Data = upgraded;
            if (!string.IsNullOrEmpty(asset.VerifiedSolution))
                asset.VerifiedGameplaySignature = GameplayFingerprint.IsLegacyCompatible(upgraded, registry) ? oldFingerprint : "";
            EditorUtility.SetDirty(asset);
            return true;
        }

        public static LevelDefinition CreateSample()
        {
            var level = new LevelDefinition
            {
                Id = "example-sliding-and-doors", Name = "滑行与门", Width = 9, Height = 5,
                Description = "向右推动滑行箱。玩家接替箱子压住压力板，门保持开启；箱子会一直滑到障碍前。选中压力板可查看它关联的门。",
                Cells = new CellType[45], HasPlayer = true, PlayerStart = new GridPos(1, 2),
                Boxes = new[] { new GridPos(2, 2) }, Goals = new[] { new GridPos(7, 2) }
            };
            for (int y = 0; y < level.Height; y++)
                for (int x = 0; x < level.Width; x++)
                    if (x == 0 || y == 0 || x == level.Width - 1 || y == level.Height - 1)
                        level.Cells[y * level.Width + x] = CellType.Wall;
            LevelMigration.Upgrade(level);
            var elements = new List<ElementInstance>(level.Elements);
            foreach (var element in elements) if (element.TypeId == "box") element.TypeId = "sliding-box";
            elements.Add(new ElementInstance("example-door", "door", new GridPos(4, 2)));
            elements.Add(new ElementInstance("example-pressure-plate", "pressure-plate", new GridPos(2, 2), new[]
            {
                new ParameterValue { Key = "targetDoors", Kind = ParameterKind.References, StringValues = new[] { "example-door" } }
            }));
            level.Elements = elements.ToArray();
            LevelMigration.SyncLegacy(level);
            return level;
        }

        private static void EnsureSample(ElementRegistry registry)
        {
            if (AssetDatabase.LoadAssetAtPath<LevelAsset>(SamplePath) != null) return;
            EnsureFolder("Assets/Resources/Levels/Examples");
            var asset = ScriptableObject.CreateInstance<LevelAsset>();
            asset.Data = CreateSample();
            var issues = LevelValidator.Validate(asset.Data, registry);
            if (issues.Count > 0) throw new InvalidOperationException("机制示例配置有误：" + issues[0].Message);
            asset.VerifiedSolution = "R";
            asset.VerifiedGameplaySignature = GameplayFingerprint.Compute(asset.Data, registry);
            AssetDatabase.CreateAsset(asset, SamplePath);
        }

        private static void ConnectPresentation(ElementDefinition definition)
        {
            string ordinary;
            string active = null;
            switch (definition.Data.Id)
            {
                case "floor": ordinary = "Floor"; break;
                case "wall": ordinary = "Wall"; break;
                case "goal": ordinary = "Goal"; break;
                case "player": ordinary = "Player"; break;
                case "box": ordinary = "Crate"; active = "CrateDocked"; break;
                case "sliding-box": ordinary = "Elements/SlidingCrate"; active = "Elements/SlidingCrateDocked"; break;
                case "pressure-plate": ordinary = "Elements/PressurePlate"; active = "Elements/PressurePlateActive"; break;
                case "door": ordinary = "Elements/Door"; active = "Elements/DoorOpen"; break;
                default: return;
            }
            definition.Sprite = RequireSprite(ordinary);
            definition.Icon = definition.Sprite;
            definition.ActiveSprite = active == null ? null : RequireSprite(active);
            if (definition.Data.Role == ElementRole.Player)
                definition.MoveSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Move.wav");
            else if (definition.Data.Role == ElementRole.Box)
                definition.MoveSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Push.wav");
            else if (definition.Data.Role == ElementRole.Fixture)
            {
                definition.ActivateSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Move.wav");
                definition.DeactivateSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Push.wav");
            }
            if (definition.Data.Role == ElementRole.Box || definition.Data.Role == ElementRole.Player)
            {
                string prefabPath = definition.Data.Role == ElementRole.Player ? "Assets/Prefabs/Board/Player.prefab" : "Assets/Prefabs/Board/Box.prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                definition.Prefab = prefab != null ? prefab.GetComponent<SpriteRenderer>() : null;
            }
        }

        private static Sprite LoadSprite(string name) => AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/" + name + ".png");

        private static Sprite RequireSprite(string name)
        {
            var sprite = LoadSprite(name);
            if (sprite != null) return sprite;
            string path = "Assets/Sprites/" + name + ".png";
            string importer = AssetImporter.GetAtPath(path)?.GetType().Name ?? "尚未导入";
            throw new InvalidOperationException("无法读取元素 Sprite：" + path + "（" + importer + "）。请确认文件存在并完成 Sprite 导入后重试；本次不会用其他元素图片代替。");
        }

        private static void ImportElementSprites()
        {
            const string folder = "Assets/Sprites/Elements";
            if (!Directory.Exists(folder)) return;
            foreach (string file in Directory.GetFiles(folder, "*.png", SearchOption.AllDirectories))
            {
                string path = file.Replace('\\', '/');
                // The first import may still be queued when assets were created by an external tool.
                // Complete the texture import and materialize its Sprite subasset before creating any definition.
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                if (AssetDatabase.LoadAssetAtPath<Sprite>(path) == null)
                    throw new InvalidOperationException("元素图片导入后没有生成 Sprite：" + path + "。请检查图片格式及 Sprite 导入设置；元素目录尚未更新。");
            }
        }

        /// <summary>One-time repair for the three definitions produced by the initial asynchronous import.
        /// Deliberately separate from Install so later user presentation choices are never reset.</summary>
        public static void RepairGeneratedPresentations()
        {
            ImportElementSprites();
            int repaired = 0;
            foreach (string id in new[] { "sliding-box", "pressure-plate", "door" })
            {
                var definition = AssetDatabase.LoadAssetAtPath<ElementDefinition>(ElementFolder + "/" + id + ".asset");
                if (definition == null || definition.Data == null || definition.Data.Id != id || definition.ActiveSprite != null ||
                    (definition.StatePresentations?.Length ?? 0) != 0) continue;
                var incorrectFallback = RequireSprite(id == "sliding-box" ? "Crate" : "Goal");
                if (definition.Sprite != incorrectFallback || definition.Icon != incorrectFallback) continue;
                string ordinary = id == "sliding-box" ? "Elements/SlidingCrate" : id == "pressure-plate" ? "Elements/PressurePlate" : "Elements/Door";
                string active = id == "sliding-box" ? "Elements/SlidingCrateDocked" : id == "pressure-plate" ? "Elements/PressurePlateActive" : "Elements/DoorOpen";
                var ordinarySprite = RequireSprite(ordinary);
                var activeSprite = RequireSprite(active);
                definition.Sprite = ordinarySprite;
                definition.Icon = ordinarySprite;
                definition.ActiveSprite = activeSprite;
                EditorUtility.SetDirty(definition);
                AssetDatabase.SaveAssetIfDirty(definition);
                repaired++;
            }
            ElementCatalog.NotifyChanged();
            Debug.Log("已修复 " + repaired + " 个首次导入时生成的错误素材引用；其他配置与关卡内容保持原值。");
        }

        private static void BindBoardPrefab(string path, ElementCatalog catalog)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) return;
            bool required = false;
            foreach (var board in asset.GetComponentsInChildren<BoardView>(true))
                if (board.Elements != catalog) { required = true; break; }
            if (!required) return;
            // Editing prefab contents retains all authored transforms, visuals and nested-prefab connections.
            // GameRoot normally inherits the first prefab, so it is saved only if it contains a separate board or an override.
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (var board in contents.GetComponentsInChildren<BoardView>(true))
                {
                    if (board.Elements == catalog) continue;
                    board.ConfigureElements(catalog);
                    EditorUtility.SetDirty(board);
                }
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            if (slash < 0) throw new InvalidOperationException("资源目录必须位于 Assets 内。");
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
    }

    /// <summary>Asset changes notify views; each view defers applying the refresh until its current gesture ends.</summary>
    [InitializeOnLoad]
    internal sealed class ElementAssetChanges : AssetPostprocessor
    {
        static ElementAssetChanges()
        {
            EditorApplication.update -= ElementCatalog.PublishPendingChanges;
            EditorApplication.update += ElementCatalog.PublishPendingChanges;
        }

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            // A deleted asset no longer has a queryable type; notify conservatively so custom-folder definitions also disappear.
            if (deleted.Length > 0 || ContainsElementAsset(imported) || ContainsElementAsset(moved) || ContainsElementAsset(movedFrom))
                EditorApplication.delayCall += ElementCatalog.NotifyChanged;
        }

        private static bool ContainsElementAsset(string[] paths)
        {
            foreach (string path in paths)
            {
                if (path.StartsWith(MechanicsContentSetup.ElementFolder + "/", StringComparison.Ordinal) || path == MechanicsContentSetup.CatalogPath ||
                    path.StartsWith("Assets/Sprites/", StringComparison.Ordinal)) return true;
                if (AssetDatabase.GetMainAssetTypeAtPath(path) == typeof(ElementDefinition) || AssetDatabase.GetMainAssetTypeAtPath(path) == typeof(ElementCatalog)) return true;
            }
            return false;
        }
    }
}
