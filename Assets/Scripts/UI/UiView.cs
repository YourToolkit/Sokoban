using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Sokoban.Runtime
{
    /// <summary>Serialized semantic bindings survive hierarchy moves and object renames.</summary>
    public sealed class UiView : MonoBehaviour
    {
        [Serializable] public struct Binding { public string Key; public GameObject Target; }
        [SerializeField] private Binding[] bindings = Array.Empty<Binding>();
        private readonly Dictionary<Button, UnityAction> handlers = new Dictionary<Button, UnityAction>();
        public RectTransform Rect => (RectTransform)transform;
        public T Get<T>(string key) where T : Component
        {
            foreach (var entry in bindings)
                if (entry.Key == key && entry.Target != null)
                {
                    var result = entry.Target.GetComponent<T>();
                    if (result != null) return result;
                    break;
                }
            throw new InvalidOperationException(name + " 缺少界面引用：" + key + " / " + typeof(T).Name);
        }
        public TMP_Text Text(string key, string value)
        { var label = Get<TMP_Text>(key); label.text = value ?? ""; return label; }
        public TMP_InputField Input(string key, string value)
        { var input = Get<TMP_InputField>(key); input.SetTextWithoutNotify(value ?? ""); return input; }
        public Button Button(string key, UnityAction action, string label = null)
        {
            var button = Get<Button>(key);
            if (handlers.TryGetValue(button, out var previous)) button.onClick.RemoveListener(previous);
            if (action != null) { button.onClick.AddListener(action); handlers[button] = action; }
            else handlers.Remove(button);
            if (label != null) button.GetComponent<UiButtonVisual>().Label.text = label;
            return button;
        }
        public void Show(string key, bool visible) => Get<Transform>(key).gameObject.SetActive(visible);
        public void ReleaseBindings()
        {
            foreach (var pair in handlers) if (pair.Key != null) pair.Key.onClick.RemoveListener(pair.Value);
            handlers.Clear();
        }
        private void OnDestroy() => ReleaseBindings();
#if UNITY_EDITOR
        public void ReplaceBindingTarget(GameObject oldTarget, GameObject newTarget)
        {
            for (int i = 0; i < bindings.Length; i++)
                if (bindings[i].Target == oldTarget) bindings[i].Target = newTarget;
        }
        public void CaptureBindings()
        {
            var entries = new List<Binding>();
            var keys = new HashSet<string>();
            foreach (var child in GetComponentsInChildren<Transform>(true))
                if (child != transform && keys.Add(child.name)) entries.Add(new Binding { Key = child.name, Target = child.gameObject });
            bindings = entries.ToArray();
        }
#endif
    }
}
