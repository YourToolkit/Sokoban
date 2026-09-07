using UnityEngine;

namespace Sokoban.Content
{
    [CreateAssetMenu(menuName = "推箱子/视觉与音效")]
    public sealed class VisualConfig : ScriptableObject
    {
        [InspectorName("地板")] public Sprite Floor;
        [InspectorName("墙壁")] public Sprite Wall;
        [InspectorName("目标点")] public Sprite Goal;
        [InspectorName("玩家")] public Sprite Player;
        [InspectorName("箱子")] public Sprite Box;
        [InspectorName("就位箱子")] public Sprite BoxOnGoal;
        [InspectorName("移动音效")] public AudioClip MoveSound;
        [InspectorName("推动音效")] public AudioClip PushSound;
        [InspectorName("受阻音效")] public AudioClip BlockedSound;
        [InspectorName("通关音效")] public AudioClip WinSound;
    }
}
