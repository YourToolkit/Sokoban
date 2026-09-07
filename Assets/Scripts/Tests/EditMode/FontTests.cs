using NUnit.Framework;
using Sokoban.EditorTools;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using Object = UnityEngine.Object;

namespace Sokoban.Tests
{
    public sealed class FontTests
    {
        [Test]
        public void ChineseUiUsesAStaticAtlasAndAnIsolatedDynamicFontSupportsRareInput()
        {
            var font = Resources.Load<TMP_FontAsset>("Fonts/SokobanChinese");
            Assert.That(font, Is.Not.Null);
            Assert.That(font.atlasPopulationMode, Is.EqualTo(AtlasPopulationMode.Static));
            Assert.That(font.isMultiAtlasTexturesEnabled, Is.False);
            Assert.That(font.fallbackFontAssetTable, Is.Not.Empty);
            var fallback = font.fallbackFontAssetTable[0];
            Assert.That(fallback, Is.Not.Null);
            Assert.That(fallback.atlasPopulationMode, Is.EqualTo(AtlasPopulationMode.Dynamic));
            Assert.That(fallback.sourceFontFile, Is.Not.Null);
            Assert.That(fallback.isMultiAtlasTexturesEnabled, Is.True);
            var source = AssetDatabase.LoadAssetAtPath<Font>(ChineseFontSetup.SourcePath);
            Assert.That(fallback.sourceFontFile, Is.SameAs(source));
            const string fixedUi = "推箱子关卡选择编辑暂停继续游戏撤销重开步数推动目标保存试玩返回设置中文方向键滑行箱压力板关联门对象属性继承默认实例覆盖恢复默认切换同格对象完成关联 Z R ESC";
            foreach (char character in fixedUi)
                Assert.That(font.HasCharacter(character, false, false), Is.True, "Missing fixed UI character: " + character);

            // Keep this codepoint escaped so the source-text atlas collector does
            // not accidentally bake the deliberately uncommon test glyph.
            const uint rareCodepoint = 0x9F98;
            Assert.That(font.HasCharacter((int)rareCodepoint), Is.False,
                "This test needs an uncommon source-font glyph outside the static UI atlas.");
            int originalCharacterCount = fallback.characterTable.Count;
            TMP_FontAsset isolated = null;
            try
            {
                isolated = TMP_FontAsset.CreateFontAsset(source, 64, 8, GlyphRenderMode.SDFAA,
                    256, 256, AtlasPopulationMode.Dynamic, true);
                Assert.That(isolated, Is.Not.Null);
                Assert.That(isolated.TryAddCharacters(new[] { rareCodepoint }, out uint[] missing), Is.True);
                Assert.That(missing, Is.Null.Or.Empty);
                Assert.That(isolated.HasCharacter((int)rareCodepoint), Is.True);
                Assert.That(fallback.characterTable.Count, Is.EqualTo(originalCharacterCount),
                    "The test must not add characters to the persistent fallback asset.");
            }
            finally
            {
                if (isolated != null)
                {
                    var material = isolated.material;
                    var textures = isolated.atlasTextures;
                    Object.DestroyImmediate(isolated);
                    if (material != null) Object.DestroyImmediate(material);
                    foreach (var texture in textures) if (texture != null) Object.DestroyImmediate(texture);
                }
            }
        }
    }
}
