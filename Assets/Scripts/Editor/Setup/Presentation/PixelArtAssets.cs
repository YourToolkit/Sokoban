using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Sokoban.Content;
using Sokoban.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Sokoban.EditorTools
{
    /// <summary>Connects authored gameplay sprites and creates missing Editor-only tool icons.</summary>
    public static class PixelArtAssets
    {
        public static void Ensure(VisualConfig config)
        {
            config.Floor = Gameplay(config.Floor, "Floor");
            config.Wall = Gameplay(config.Wall, "Wall");
            config.Goal = Gameplay(config.Goal, "Goal");
            config.Player = Gameplay(config.Player, "Player");
            config.Box = Gameplay(config.Box, "Crate");
            config.BoxOnGoal = Gameplay(config.BoxOnGoal, "CrateDocked");
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssetIfDirty(config);
        }

        private static Sprite Gameplay(Sprite assigned, string name)
        {
            if (assigned) return assigned;
            string path = "Assets/Sprites/" + name + ".png";
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (!sprite) throw new FileNotFoundException("缺少棋盘美术资源：" + path, path);
            return sprite;
        }

        public static Sprite Tool(WorkshopTool tool)
        {
            string path = "Assets/Sprites/Editor/Tools/" + tool + ".png";
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing) return existing;
            var go = new GameObject("Icon authoring", typeof(RectTransform), typeof(WorkshopToolIcon));
            try
            {
                var icon = go.GetComponent<WorkshopToolIcon>();
                icon.Tool = tool;
                using (var mesh = new VertexHelper())
                    typeof(WorkshopToolIcon).GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic,
                        null, new[] { typeof(VertexHelper) }, null).Invoke(icon, new object[] { mesh });
                var mask = (HashSet<int>)typeof(WorkshopToolIcon)
                    .GetField("pixels", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(icon);
                var pixels = new Color32[400];
                foreach (int index in mask) pixels[index] = Color.white;
                return Write(path, pixels, 20, 20);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        private static Sprite Write(string path, Color32[] pixels, int size, int ppu)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            PixelArtImportSettings.Apply(importer, ppu);
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
