#if UNITY_EDITOR
using System;
using UnityEngine;

namespace Sokoban.Runtime
{
    public sealed class WorkshopViewCatalog : ScriptableObject
    {
        [Serializable] public struct Dialog { public string Key; public UiView Prefab; }
        public UiView Main;
        public UiView Entry;
        public Dialog[] Dialogs;
    }
}
#endif
