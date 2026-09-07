using System.Collections.Generic;
using UnityEngine;

namespace Sokoban.Runtime
{
    public sealed class UiList : MonoBehaviour
    {
        [SerializeField] private RectTransform content;
        [SerializeField] private UiView itemPrefab;
        private readonly List<UiView> items = new List<UiView>();
        public void Clear() { foreach (var item in items) if (item != null) { item.gameObject.SetActive(false); Destroy(item.gameObject); } items.Clear(); }
        public UiView Add() { var item = Instantiate(itemPrefab, content, false); item.gameObject.SetActive(true); items.Add(item); return item; }
        public IReadOnlyList<UiView> Items => items;
#if UNITY_EDITOR
        public void Configure(RectTransform parent, UiView prefab) { content = parent; itemPrefab = prefab; }
#endif
    }
}
