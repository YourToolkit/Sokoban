using System.Collections.Generic;
using UnityEngine;

namespace Sokoban.Content
{
    [CreateAssetMenu(menuName = "推箱子/关卡目录")]
    public sealed class LevelCatalog : ScriptableObject
    {
        [InspectorName("关卡列表")] public List<LevelAsset> Levels = new List<LevelAsset>();
    }
}
