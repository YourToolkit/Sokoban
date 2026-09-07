using Sokoban.Content;
using Sokoban.Core;
using UnityEditor;
using UnityEngine;

namespace Sokoban.EditorTools
{
    [CustomEditor(typeof(ElementDefinition))]
    public sealed class ElementDefinitionInspector : UnityEditor.Editor
    {
        private static readonly string[] RoleNames = { "地板", "墙", "目标", "玩家", "箱子", "机关" };
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var data = serializedObject.FindProperty("Data");
            EditorGUILayout.LabelField("元素类型", EditorStyles.boldLabel);
            Field(data, "Id", "固定类型 ID");
            EditorGUILayout.HelpBox("关卡通过固定类型 ID 引用元素。复制类型后请设置新的 ID；修改中文名称不会影响已有布局。", MessageType.None);
            Field(data, "Name", "中文名称");
            var role = data.FindPropertyRelative("Role");
            if (role.intValue >= 0 && role.intValue < RoleNames.Length)
                role.intValue = EditorGUILayout.Popup("占用层级", role.intValue, RoleNames);
            else role.intValue = EditorGUILayout.IntField("未知占用层级（原值）", role.intValue);
            Field(data, "MechanicIds", "注册机制 ID");
            if (data.FindPropertyRelative("MechanicIds").arraySize == 0)
                Field(data, "Mechanics", "兼容机制列表");
            EditorGUILayout.HelpBox("已注册机制：push（可推动）、slide（持续滑行）、pressure-plate（压力板）、door（门）。机制是否能够组合由规则注册表校验。", MessageType.None);
            Field(data, "RulesVersion", "规则版本");
            Field(data, "Properties", "属性描述");
            Field(data, "Defaults", "继承默认值");
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("表现资源", EditorStyles.boldLabel);
            foreach (string field in new[] { "Icon", "Sprite", "ActiveSprite", "Prefab", "MoveSound", "ActivateSound", "DeactivateSound" })
                EditorGUILayout.PropertyField(serializedObject.FindProperty(field));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("StatePresentations"), true);
            EditorGUILayout.HelpBox("状态表现只影响画面与声音。例如状态字段 active、匹配值 true 表示机关激活；onGoal、true 表示箱子就位。布尔值使用 true/false，数字使用小数点，文本与枚举填写原值。", MessageType.None);
            serializedObject.ApplyModifiedProperties();
            var element = (ElementDefinition)target;
            var issues = new ElementRegistry(new[] { element.Data }).ValidateTypes();
            foreach (var issue in issues) EditorGUILayout.HelpBox(issue.Message, MessageType.Warning);
        }

        private static void Field(SerializedProperty parent, string name, string label) =>
            EditorGUILayout.PropertyField(parent.FindPropertyRelative(name), new GUIContent(label), true);
    }

    [CustomPropertyDrawer(typeof(ParameterValue))]
    public sealed class ElementParameterDrawer : PropertyDrawer
    {
        private static readonly string[] KindNames = { "布尔", "整数", "小数", "文本", "枚举", "单个对象引用", "多个对象引用" };
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!property.isExpanded) return EditorGUIUtility.singleLineHeight;
            var value = ValueProperty(property);
            return (EditorGUIUtility.singleLineHeight + 2) * 3 + EditorGUI.GetPropertyHeight(value, true);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            var row = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            string key = property.FindPropertyRelative("Key").stringValue;
            property.isExpanded = EditorGUI.Foldout(row, property.isExpanded, string.IsNullOrEmpty(key) ? label.text : key, true);
            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;
                row.y += row.height + 2;
                EditorGUI.PropertyField(row, property.FindPropertyRelative("Key"), new GUIContent("字段 ID"));
                row.y += row.height + 2;
                var kind = property.FindPropertyRelative("Kind");
                if (kind.intValue >= 0 && kind.intValue < KindNames.Length)
                    kind.intValue = EditorGUI.Popup(row, "数值类型", kind.intValue, KindNames);
                else EditorGUI.LabelField(row, "未知类型（保留原始值）", kind.intValue.ToString());
                row.y += row.height + 2;
                var value = ValueProperty(property);
                row.height = EditorGUI.GetPropertyHeight(value, true);
                EditorGUI.PropertyField(row, value, new GUIContent("值"), true);
                EditorGUI.indentLevel--;
            }
            EditorGUI.EndProperty();
        }

        private static SerializedProperty ValueProperty(SerializedProperty property)
        {
            switch ((ParameterKind)property.FindPropertyRelative("Kind").intValue)
            {
                case ParameterKind.Boolean: return property.FindPropertyRelative("BoolValue");
                case ParameterKind.Integer: return property.FindPropertyRelative("IntValue");
                case ParameterKind.Float: return property.FindPropertyRelative("FloatValue");
                case ParameterKind.References: return property.FindPropertyRelative("StringValues");
                case ParameterKind.String: case ParameterKind.Enum: case ParameterKind.Reference: return property.FindPropertyRelative("StringValue");
                default: return property.FindPropertyRelative("RawValue");
            }
        }
    }
}
