using System;
using Sokoban.Content;
using Sokoban.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Sokoban.EditorTools
{
    [CustomEditor(typeof(BoardView))]
    public sealed class BoardPreviewInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            Field("boardCamera", "棋盘相机"); Field("boardRoot", "网格根节点");
            Field("terrainMap", "地形 Tilemap"); Field("goalMap", "目标 Tilemap");
            Field("player", "玩家节点"); Field("boxPrefab", "箱子 Prefab");
            Field("visuals", "素材配置"); Field("pixelsPerCell", "每格原始像素");
            Field("elementCatalog", "元素目录");
            Field("backgroundColor", "棋盘背景色");
            serializedObject.ApplyModifiedProperties();
            var board=(BoardView)target;
            if(EditorUtility.IsPersistent(board) || Application.isPlaying) return;
            EditorGUILayout.Space();EditorGUILayout.LabelField("关卡预览（不会修改关卡）",EditorStyles.boldLabel);
            var current=BoardScenePreview.Selected(board);
            var selected=(LevelAsset)EditorGUILayout.ObjectField("预览关卡",current,typeof(LevelAsset),false);
            if(selected!=current) BoardScenePreview.Select(board,selected);
            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button("刷新预览")) BoardScenePreview.Show(board,selected);
                if(GUILayout.Button("清除预览")) BoardScenePreview.Clear();
                if(GUILayout.Button("定位预览") && selected && selected.Data!=null)
                    SceneView.lastActiveSceneView?.Frame(new Bounds(new Vector3(selected.Data.Width/2f,selected.Data.Height/2f),new Vector3(selected.Data.Width+2,selected.Data.Height+2,1)),false);
            }
            using(new EditorGUI.DisabledScope(!selected))
                if(GUILayout.Button("试玩预览关卡")) EditorPlaytestBridge.Start(selected);
            EditorGUILayout.HelpBox("普通 Play 仍按游戏进度启动。这里的试玩不记录正式成绩；修改布局请打开关卡编辑器。",MessageType.Info);
        }
        private void Field(string key,string label)
        { EditorGUILayout.PropertyField(serializedObject.FindProperty(key),new GUIContent(label)); }
    }

    [InitializeOnLoad]
    public static class BoardScenePreview
    {
        private static BoardView preview;
        private static bool queued;
        public static BoardView Current => preview;
        static BoardScenePreview()
        {
            EditorSceneManager.sceneOpened += (scene,mode)=>Queue();
            EditorSceneManager.sceneSaving += (scene,path)=>Clear();
            EditorSceneManager.sceneSaved += scene=>Queue();
            EditorSceneManager.sceneClosing += (scene,removing)=>Clear();
            AssemblyReloadEvents.beforeAssemblyReload += Clear;
            EditorApplication.playModeStateChanged += state=> {
                if(state==PlayModeStateChange.ExitingEditMode) Clear();
                if(state==PlayModeStateChange.EnteredEditMode) Queue();
            };
            EditorApplication.projectChanged += Queue;
            ElementCatalog.Changed += Queue;
            Undo.undoRedoPerformed += Queue;
            Undo.postprocessModifications += modifications=> { Queue(); return modifications; };
            EditorApplication.delayCall += Queue;
        }
        private static string Key(BoardView source) => "Sokoban.Preview."+Application.dataPath+"."+source.gameObject.scene.path;
        public static LevelAsset Selected(BoardView source)
        {
            var guid=SessionState.GetString(Key(source),"");
            if(!string.IsNullOrEmpty(guid)) return AssetDatabase.LoadAssetAtPath<LevelAsset>(AssetDatabase.GUIDToAssetPath(guid));
            var resources=source.GetComponentInParent<GameController>()?.ResourcesConfig;
            return resources && resources.Catalog && resources.Catalog.Levels.Count>0 ? resources.Catalog.Levels[0] : null;
        }
        public static void Select(BoardView source,LevelAsset level)
        {
            SessionState.SetString(Key(source),level ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(level)) : "none");
            Show(source,level);
        }
        public static void Clear()
        { if(preview) Object.DestroyImmediate(preview.gameObject); preview=null; SceneView.RepaintAll(); }
        public static void Show(BoardView source,LevelAsset level)
        {
            Clear();
            if(!source || !level || level.Data==null || EditorApplication.isPlayingOrWillChangePlaymode || !source.gameObject.scene.IsValid()) return;
            preview=Object.Instantiate(source); preview.name="临时关卡预览（不保存）";
            preview.ConfigureElements(source.Elements != null ? source.Elements : source.GetComponentInParent<GameController>()?.ResourcesConfig?.Elements);
            SceneManager.MoveGameObjectToScene(preview.gameObject,source.gameObject.scene);
            foreach(var item in preview.GetComponentsInChildren<Transform>(true)) item.gameObject.hideFlags=HideFlags.HideAndDontSave;
            preview.PrepareScenePreview(level.ToDefinition());
            foreach(var item in preview.GetComponentsInChildren<Transform>(true)) item.gameObject.hideFlags=HideFlags.HideAndDontSave;
            SceneView.RepaintAll();
        }
        private static void Queue()
        {
            if(Application.isBatchMode || queued || EditorApplication.isPlayingOrWillChangePlaymode) return;
            queued=true;EditorApplication.delayCall+=()=> {
                queued=false;
                if(EditorApplication.isPlayingOrWillChangePlaymode) return;
                foreach(var app in Object.FindObjectsOfType<GameController>())
                    if(app.gameObject.scene.path==ProjectSetup.GameScene) { Show(app.Board,Selected(app.Board)); break; }
            };
        }
    }
}
