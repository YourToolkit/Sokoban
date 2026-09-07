using System;
using Sokoban.Core;
using UnityEngine;

namespace Sokoban.Content
{
    /// <summary>Presentation for a registered state value; no gameplay rule depends on this record.</summary>
    [Serializable]
    public sealed class ElementStatePresentation
    {
        [InspectorName("状态字段 ID")] public string Key = "";
        [InspectorName("匹配值")]
        [Tooltip("布尔值填写 true 或 false；数字使用小数点；文本与枚举填写原值。")]
        public string Value = "";
        [InspectorName("状态素材")] public Sprite Sprite;
        [InspectorName("进入状态音效")] public AudioClip Sound;
    }

    /// <summary>Unity presentation and authorable defaults for one stable element type.</summary>
    [CreateAssetMenu(menuName = "推箱子/元素类型")]
    public sealed class ElementDefinition : ScriptableObject
    {
        [InspectorName("元素定义")] public ElementTypeSpec Data = new ElementTypeSpec();
        [InspectorName("元素图标")] public Sprite Icon;
        [InspectorName("默认素材")] public Sprite Sprite;
        [InspectorName("激活或就位素材")] public Sprite ActiveSprite;
        [InspectorName("表现预制体")] public SpriteRenderer Prefab;
        [InspectorName("移动音效")] public AudioClip MoveSound;
        [InspectorName("激活音效")] public AudioClip ActivateSound;
        [InspectorName("复原音效")] public AudioClip DeactivateSound;
        [InspectorName("状态表现规则")] public ElementStatePresentation[] StatePresentations = Array.Empty<ElementStatePresentation>();

#if UNITY_EDITOR
        private void OnValidate() => ElementCatalog.MarkChanged();
#endif
    }
}
