#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Sokoban.Runtime
{
    public sealed class WorkshopPointerHint : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public WorkshopTool Tool;
        public string DisplayName;
        [TextArea] public string Description;
        public Action Entered, Exited;
        public void OnPointerEnter(PointerEventData eventData) => Entered?.Invoke();
        public void OnPointerExit(PointerEventData eventData) => Exited?.Invoke();
    }
}
#endif
