using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Sokoban.EditorTools
{
    /// <summary>Creates portable Chinese fonts from the pinned, licensed project source.</summary>
    public static class ChineseFontSetup
    {
        public const string FontPath = "Assets/Resources/Fonts/SokobanChinese.asset";
        public const string FallbackPath = "Assets/Fonts/SokobanChineseDynamic.asset";
        public const string SourcePath = "Assets/Fonts/NotoSansSC-Regular.otf";

        [MenuItem("推箱子/更新中文字体", priority = 52)]
        public static void Generate()
        {
            Directory.CreateDirectory("Assets/Resources/Fonts");
            AssetDatabase.Refresh();
            var source = AssetDatabase.LoadAssetAtPath<Font>(SourcePath);
            if (source == null) throw new InvalidOperationException("缺少 Noto Sans SC 字体源文件。");
            var fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FallbackPath);
            if (fallback == null)
            {
                fallback = TMP_FontAsset.CreateFontAsset(source, 64, 8, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
                fallback.name = "SokobanChineseDynamic";
                // Persist a real initial atlas; subsequent characters are added at runtime.
                fallback.TryAddCharacters("中");
                PersistFont(fallback, FallbackPath);
            }
            fallback.isMultiAtlasTexturesEnabled = true;
            fallback.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            EditorUtility.SetDirty(fallback);

            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            bool create = font == null;
            if (create)
                font = TMP_FontAsset.CreateFontAsset(source, 64, 8, GlyphRenderMode.SDFAA, 4096, 4096, AtlasPopulationMode.Dynamic, false);
            font.name = "SokobanChinese";
            font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            font.isMultiAtlasTexturesEnabled = false;
            font.ClearFontAssetData();
            string characters = CollectCharacters();
            if (!font.TryAddCharacters(characters, out string missing))
                throw new InvalidOperationException("中文字体生成失败，缺少字符：" + missing);
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            font.fallbackFontAssetTable = new List<TMP_FontAsset> { fallback };
            if (create) PersistFont(font, FontPath);
            EditorUtility.SetDirty(font);
            foreach (var texture in font.atlasTextures) EditorUtility.SetDirty(texture);
            EditorUtility.SetDirty(font.material);
            var settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>("Assets/TextMesh Pro/Resources/TMP Settings.asset");
            if (settings != null)
            {
                var serialized = new SerializedObject(settings);
                serialized.FindProperty("m_defaultFontAsset").objectReferenceValue = font;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(settings);
            }
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/chinese-font-report.txt", $"Static characters: {characters.Length}\nMissing characters: 0\nAtlas: {font.atlasWidth} x {font.atlasHeight}\nDynamic fallback: Noto Sans SC, multi atlas\n");
            Debug.Log("中文字体已更新，预生成 " + characters.Length + " 个字符。");
        }

        public static string CollectCharacters()
        {
            var characters = new SortedSet<char>();
            for (char c = ' '; c <= '~'; c++) characters.Add(c);
            foreach (char c in "…—·•×→←↑↓□■✓") characters.Add(c);
            foreach (var path in Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories))
                foreach (char c in File.ReadAllText(path))
                    if ((c >= '\u4e00' && c <= '\u9fff') || (c >= '\u3000' && c <= '\u303f') || (c >= '\uff01' && c <= '\uff60')) characters.Add(c);
            return new string(characters.ToArray());
        }

        private static void PersistFont(TMP_FontAsset font, string path)
        {
            AssetDatabase.CreateAsset(font, path);
            font.material.name = font.name + " Material";
            AssetDatabase.AddObjectToAsset(font.material, font);
            for (int i = 0; i < font.atlasTextures.Length; i++)
            {
                font.atlasTextures[i].name = font.name + " Atlas " + i;
                AssetDatabase.AddObjectToAsset(font.atlasTextures[i], font);
            }
            EditorUtility.SetDirty(font);
        }
    }
}
