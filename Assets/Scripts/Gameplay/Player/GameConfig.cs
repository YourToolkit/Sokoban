using UnityEngine;

namespace Sokoban.Content
{
    [CreateAssetMenu(menuName = "推箱子/操作配置")]
    public sealed class GameConfig : ScriptableObject
    {
        [InspectorName("移动动画时长"), Range(0.04f, 0.4f)] public float MoveDuration = 0.12f;
        [InspectorName("长按首次间隔"), Range(0.1f, 0.8f)] public float RepeatDelay = 0.28f;
        [InspectorName("长按重复间隔"), Range(0.06f, 0.4f)] public float RepeatInterval = 0.13f;
        [InspectorName("音效音量"), Range(0f, 1f)] public float AudioVolume = 0.4f;
    }
}
