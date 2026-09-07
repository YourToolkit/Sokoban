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
        [Tooltip("验证解对应的有效玩法签名。机制参数变化后，旧验证解不再有效。")]
        public string VerifiedGameplaySignature = "";
        public LevelDefinition ToDefinition() => Data?.DeepClone();

        public string GetVerifiedSolution(ElementRegistry registry = null)
        {
            if (Data == null || string.IsNullOrEmpty(VerifiedSolution)) return "";
            registry = registry ?? ElementRegistry.BuiltIns();
            if (ElementValidation.Validate(Data, registry).Count > 0) return "";
            if (string.IsNullOrEmpty(VerifiedGameplaySignature))
                return GameplayFingerprint.IsLegacyCompatible(Data, registry) ? VerifiedSolution : "";
            return VerifiedGameplaySignature == GameplayFingerprint.Compute(Data, registry) ? VerifiedSolution : "";
        }
    }
}
