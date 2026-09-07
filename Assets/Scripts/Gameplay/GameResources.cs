using UnityEngine;

namespace Sokoban.Content
{
    [CreateAssetMenu(menuName = "推箱子/资源入口")]
    public sealed class GameResources : ScriptableObject
    {
        [InspectorName("关卡目录")] public LevelCatalog Catalog;
        [InspectorName("元素目录")] public ElementCatalog Elements;
        [InspectorName("操作配置")] public GameConfig Config;
        [InspectorName("视觉与音效")] public VisualConfig Visuals;
    }
}
