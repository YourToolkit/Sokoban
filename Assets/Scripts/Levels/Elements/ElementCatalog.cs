using System;
using System.Collections.Generic;
using System.Threading;
using Sokoban.Core;
using UnityEngine;

namespace Sokoban.Content
{
    [CreateAssetMenu(menuName = "推箱子/元素目录")]
    public sealed class ElementCatalog : ScriptableObject
    {
        [InspectorName("元素列表")] public List<ElementDefinition> Elements = new List<ElementDefinition>();
        public static event Action Changed;
        private static int revision;
        private static int refreshPending;
        public static int Revision => revision;

        public ElementDefinition Find(string id)
        {
            if (Elements == null) return null;
            return Elements.Find(element => element != null && element.Data != null && element.Data.Id == id);
        }

        /// <summary>Copies all rule values: a running session never observes mutable Unity assets.</summary>
        public ElementRegistry Snapshot()
        {
            var definitions = new List<ElementTypeSpec>();
            if (Elements != null)
                foreach (var element in Elements)
                    if (element != null && element.Data != null) definitions.Add(element.Data);
            return new ElementRegistry(definitions).Snapshot();
        }

        public static void NotifyChanged()
        {
            MarkChanged();
            PublishPendingChanges();
        }

        // OnValidate may run while Unity deserializes on a worker thread. UI callbacks are flushed by the Editor adapter.
        public static void MarkChanged()
        {
            Interlocked.Increment(ref revision);
            Interlocked.Exchange(ref refreshPending, 1);
        }

        public static void PublishPendingChanges()
        {
            if (Interlocked.Exchange(ref refreshPending, 0) == 0) return;
            if (Changed == null) return;
            foreach (Action listener in Changed.GetInvocationList())
            {
                try { listener(); }
                catch (Exception exception) { Debug.LogWarning("元素配置已更新，部分视图未能刷新：" + exception); }
            }
        }

#if UNITY_EDITOR
        private void OnValidate() => MarkChanged();
#endif
    }
}
