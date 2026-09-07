using System;
using UnityEditor;
using UnityEngine;

namespace Sokoban.EditorTools
{
    /// <summary>Keeps the six authored gameplay sprites on one Unity import convention.</summary>
    internal sealed class PixelArtImportSettings : AssetPostprocessor
    {
        private static readonly string[] GameplaySprites =
        {
            "Assets/Sprites/Floor.png",
            "Assets/Sprites/Wall.png",
            "Assets/Sprites/Goal.png",
            "Assets/Sprites/Player.png",
            "Assets/Sprites/Crate.png",
            "Assets/Sprites/CrateDocked.png"
        };

        private void OnPreprocessTexture()
        {
            if (Array.IndexOf(GameplaySprites, assetPath) < 0) return;
            Apply((TextureImporter)assetImporter, 32);
        }

        internal static void Apply(TextureImporter importer, int pixelsPerUnit)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteGenerateFallbackPhysicsShape = false;
            importer.SetTextureSettings(settings);
        }
    }
}
