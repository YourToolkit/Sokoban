#if UNITY_EDITOR
using System.Collections.Generic;
using Sokoban.Content;
using Sokoban.Core;

namespace Sokoban.Runtime
{
    public enum LevelSaveIntent { Save, SaveAs, SaveAndAddToCatalog }

    public sealed class LevelSaveResult
    {
        public bool Success;
        public LevelAsset Asset;
        public string Message;
        public LevelSaveResult() { }
        public LevelSaveResult(bool success, LevelAsset asset, string message)
        { Success = success; Asset = asset; Message = message; }
    }

    public interface ILevelAssetWriter
    {
        LevelSaveResult Save(LevelAsset target, LevelDefinition snapshot, string verifiedSolution, LevelSaveIntent intent);
        IReadOnlyList<LevelAsset> ListLevels();
        bool IsInCatalog(LevelAsset asset);
    }

    /// <summary>The Editor installs the single project-asset writer. Runtime UI never references UnityEditor.</summary>
    public static class LevelAuthoringServices
    {
        public static ILevelAssetWriter AssetWriter { get; set; }
    }
}

#endif
