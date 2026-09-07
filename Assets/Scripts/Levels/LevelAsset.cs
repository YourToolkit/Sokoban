using Sokoban.Core;
using UnityEngine;

namespace Sokoban.Content
{
    [CreateAssetMenu(menuName = "推箱子/关卡")]
    public sealed class LevelAsset : ScriptableObject
    {
        public LevelDefinition Data = new LevelDefinition();
        [Tooltip("可选验证解，使用 U R D L 表示方向。修改布局后自动失效。")]
        public string VerifiedSolution = "";
        public LevelDefinition ToDefinition() => Data?.DeepClone();
    }
}
