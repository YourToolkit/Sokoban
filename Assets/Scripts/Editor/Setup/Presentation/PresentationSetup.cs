using System;
using System.Collections.Generic;
using System.IO;
using Sokoban.Content;
using Sokoban.Runtime;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Sokoban.EditorTools
{
    /// <summary>Creates missing presentation assets once. Prefabs are the source of truth afterwards.</summary>
    public static partial class PresentationSetup
    {
        public const string RootPath = "Assets/Prefabs/GameRoot.prefab";
        public const string BoardPath = "Assets/Prefabs/Board/Board.prefab";
        private const string UI = "Assets/Prefabs/UI/";
        private const string Workshop = "Assets/Prefabs/Editor/Workshop/";
        private const string BoardLayerName = "Board";
        private static GameResources resources;

        [MenuItem("推箱子/补齐场景与界面资源", priority=52)]
        public static void Ensure()
        {
            resources = AssetDatabase.LoadAssetAtPath<GameResources>(ProjectSetup.ResourcesPath);
            if (!resources || !resources.Visuals) throw new InvalidOperationException("请先准备关卡资源。");
            PixelArtAssets.Ensure(resources.Visuals);
            Directory.CreateDirectory(UI); Directory.CreateDirectory(Workshop); Directory.CreateDirectory("Assets/Prefabs/Board"); AssetDatabase.Refresh();
            var play = Page("Play screen", BuildPlay); var selection = Page("Room selection", BuildSelection);
            var pause = Page("Pause overlay", BuildPause); var complete = Page("Room cleared overlay", BuildComplete);
            var error = Page("Content error", BuildError);
            BuildWorkshop();
            UpgradeToolTemplates();
            var board = BuildBoard();
            if (!AssetDatabase.LoadAssetAtPath<GameController>(RootPath))
            {
                var root = new GameObject("推箱子");
                var background = new GameObject("Background camera",typeof(Camera),typeof(AudioListener)); background.transform.SetParent(root.transform,false);
                var camera=background.GetComponent<Camera>(); camera.orthographic=true; camera.clearFlags=CameraClearFlags.SolidColor; camera.cullingMask=0; camera.depth=-100; camera.backgroundColor=UiFactory.Paper;
                var canvas = UiFactory.Canvas(root.transform);
                var events = new GameObject("UI input",typeof(EventSystem),typeof(StandaloneInputModule)); events.transform.SetParent(root.transform,false);
                var sound=root.AddComponent<AudioSource>(); sound.playOnAwake=false;
                var b=(BoardView)PrefabUtility.InstantiatePrefab(board,root.transform); b.name="Board presentation";
                var screens=canvas.gameObject.AddComponent<GameScreens>();
                var p=Instance(play,canvas.transform); var s=Instance(selection,canvas.transform); var a=Instance(pause,canvas.transform); var c=Instance(complete,canvas.transform); var e=Instance(error,canvas.transform);
                screens.Configure(p,s,a,c,e); screens.Show(p);
                var controlsObject = new GameObject("Player controls"); controlsObject.transform.SetParent(root.transform, false);
                var controls = controlsObject.AddComponent<PlayerController>();
                root.AddComponent<GameController>().Configure(resources,b,canvas,screens,sound,controls);
                PrefabUtility.SaveAsPrefabAsset(root,RootPath); Object.DestroyImmediate(root);
            }
            // Only the original empty template is migrated automatically. Never regenerate an authored Game scene.
            var scene=SceneManager.GetSceneByPath(ProjectSetup.GameScene);
            bool opened=!scene.IsValid() || !scene.isLoaded;
            if(opened)
            {
                // Unity cannot additively open next to an unsaved untitled scene. Do not discard it.
                for(int i=0;i<SceneManager.sceneCount;i++)
                    if(string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path) && SceneManager.GetSceneAt(i).isDirty)
                        throw new InvalidOperationException("资源已补齐。请先保存未命名场景，再补齐 Game 场景。");
                scene=EditorSceneManager.OpenScene(ProjectSetup.GameScene,OpenSceneMode.Additive);
            }
            try
            {
            bool hasApp=false; foreach(var root in scene.GetRootGameObjects()) if(root.GetComponentInChildren<GameController>(true)) hasApp=true;
            if (!hasApp)
            {
                if(scene.isDirty) throw new InvalidOperationException("请先保存 Game 场景的修改，再迁移场景结构。");
                var roots=scene.GetRootGameObjects();
                if (roots.Length>1 || roots.Length==1 && roots[0].GetComponent<Camera>()==null) throw new InvalidOperationException("Game 场景包含额外对象，迁移不会自动替换它们。");
                foreach(var root in roots) Object.DestroyImmediate(root);
                PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RootPath),scene);
                EditorSceneManager.SaveScene(scene);
            }
            }
            finally { if(opened) EditorSceneManager.CloseScene(scene,true); }
            AssetDatabase.SaveAssets(); WorkshopViewRegistration.Install();
            Debug.Log("场景和 Prefab 已就绪；后续布局修改请直接编辑 Prefab，不会被此工具重建。");
        }
        private static UiView Instance(UiView prefab,Transform parent) { var v=(UiView)PrefabUtility.InstantiatePrefab(prefab,parent); v.name=prefab.name; return v; }
        private static UiView Page(string name,Action<UiView> build,string folder=UI,float w=1600,float h=900)
        {
            string path=folder+name+".prefab"; var existing=AssetDatabase.LoadAssetAtPath<UiView>(path); if(existing) return existing;
            var root=UiFactory.Root(null,name); root.sizeDelta=new Vector2(w,h); var view=root.gameObject.AddComponent<UiView>();
            build(view); view.CaptureBindings();
            var saved=PrefabUtility.SaveAsPrefabAsset(root.gameObject,path).GetComponent<UiView>(); Object.DestroyImmediate(root.gameObject); return saved;
        }
        private static TMP_Text T(Transform root,string name,string value,float x,float y,float w,float h,float size=24,Color? color=null,TextAlignmentOptions align=TextAlignmentOptions.TopLeft)
            => UiFactory.Text(root,name,value,x,y,w,h,size,color,FontStyles.Normal,align);
        private static Button B(Transform root,string key,string text,float x,float y,float w=200,float h=56,bool primary=false)
        {
            var b=UiFactory.Button(root,key,text,x,y,w,h,()=>{},primary);
            b.gameObject.AddComponent<UiButtonVisual>().Configure(b.GetComponentInChildren<TMP_Text>()); return b;
        }
        private static RectTransform R(Transform root,string key,float x,float y,float w,float h)
        { var r=new GameObject(key,typeof(RectTransform)).GetComponent<RectTransform>(); r.SetParent(root,false); return UiFactory.Place(r,x,y,w,h); }
        private static void Viewport(Transform parent,float x,float y,float w,float h)
        {
            var area=R(parent,"Board viewport",x,y,w,h); var raw=new GameObject("Pixel surface",typeof(RectTransform),typeof(RawImage)).GetComponent<RawImage>();
            raw.transform.SetParent(area,false); raw.rectTransform.anchorMin=raw.rectTransform.anchorMax=new Vector2(.5f,.5f); raw.rectTransform.pivot=new Vector2(.5f,.5f); raw.raycastTarget=false;
            area.gameObject.AddComponent<BoardViewport>().Configure(raw);
            raw.gameObject.SetActive(false);
        }
        private static RectTransform Row(Transform parent,string name,float x,float y,float w,float h)
        {
            var row=R(parent,name,x,y,w,h); var layout=row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing=20; layout.childAlignment=TextAnchor.MiddleCenter; layout.childControlWidth=false; layout.childControlHeight=false; layout.childForceExpandWidth=false; layout.childForceExpandHeight=false; return row;
        }
        private static void Notice(Transform parent) => T(parent,"Notice","",590,810,930,54,20,UiFactory.Teal,TextAlignmentOptions.Center);
        private static void BuildPlay(UiView v)
        {
            var r=v.transform; T(r,"Room title","关卡名称",64,52,764,63,38); T(r,"Moves count","步数  00",884,65,188,44,27);
            T(r,"Pushes count","推动  00",1090,65,188,44,27); T(r,"Goals","目标  0 / 0",1296,65,252,44,27,UiFactory.Teal);
            UiFactory.Panel(r,"Header rule",64,126,1472,2,UiFactory.White,false); Viewport(r,224,153,1152,576);
            var row=Row(r,"Controls",64,778,1472,56); B(row,"Undo","撤销 [Z]",0,0); B(row,"Restart","重开 [R]",0,0); B(row,"Leave room","选关",0,0); B(row,"Pause","暂停 [ESC]",0,0);
            T(r,"Play feedback","方向键 / WASD 移动，箱子只能推动，不能拉回。",64,852,1472,30,20,UiFactory.Muted,TextAlignmentOptions.Center);
        }
        private static void BuildSelection(UiView v)
        {
            var r=v.transform; T(r,"Room selection heading","选择关卡",64,70,1200,70,48); T(r,"Selection introduction","所有关卡均可直接游玩。将每个箱子推到目标格即可通关。",68,154,1250,40,24,UiFactory.Muted);
            var template=Page("Level card", card=> {
                UiFactory.Panel(card.transform,"Background",0,0,480,228,UiFactory.White);
                T(card.transform,"Number","01",24,10,75,72,38,UiFactory.Gold); T(card.transform,"Status","未通关",124,23,324,44,20,UiFactory.Muted,TextAlignmentOptions.Right);
                T(card.transform,"Name","关卡名称",24,86,430,50,29); T(card.transform,"Best score","可直接游玩",24,154,243,46,21,UiFactory.Muted); B(card.transform,"Open room","开始",290,153,166,51,true);
            },UI,480,228);
            var clip=UiFactory.Panel(r,"Room list",64,240,1472,516,Color.clear,false); clip.raycastTarget=true; clip.gameObject.AddComponent<RectMask2D>();
            var content=R(clip.transform,"Cards",0,0,1472,516); var grid=content.gameObject.AddComponent<GridLayoutGroup>(); grid.cellSize=new Vector2(480,228); grid.spacing=new Vector2(16,18); grid.constraint=GridLayoutGroup.Constraint.FixedColumnCount;grid.constraintCount=3;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;
            var scroll=clip.gameObject.AddComponent<ScrollRect>(); scroll.content=content; scroll.viewport=clip.rectTransform;scroll.horizontal=false;scroll.movementType=ScrollRect.MovementType.Clamped; scroll.scrollSensitivity=38;
            clip.gameObject.AddComponent<UiList>().Configure(content,template);
            T(r,"No rooms","暂无可游玩的关卡。",100,320,1300,100,30); B(r,"Back to game","返回游戏",64,800,230); R(r,"Editor entry",318,800,260,56);
            T(r,"Selection progress","已通关 0 / 6",1176,808,354,32,22,UiFactory.Teal,TextAlignmentOptions.Right); Notice(r);
        }
        private static Transform Dialog(UiView v,float x,float y,float w,float h)
        {
            var shade=UiFactory.Panel(v.transform,"Shade",0,0,1600,900,new Color(.03f,.04f,.07f,.88f),false); shade.raycastTarget=true;
            return UiFactory.Panel(v.transform,"Dialog",x,y,w,h,UiFactory.White).transform;
        }
        private static void BuildPause(UiView v)
        {
            var r=Dialog(v,490,204,620,492); T(r,"Pause title","暂停",44,64,532,65,42,null,TextAlignmentOptions.Center);
            B(r,"Resume","继续游戏",44,173,532,58,true); B(r,"Pause restart","重新开始",44,247,258,58); B(r,"Pause leave","选择关卡",318,247,258,58);
            B(r,"Pause sound","音效：开",44,321,532,54); T(r,"Pause hint","按 ESC 继续游戏",44,413,532,32,21,UiFactory.Muted,TextAlignmentOptions.Center);
        }
        private static void BuildComplete(UiView v)
        {
            var r=Dialog(v,458,162,684,576); T(r,"Clear eyebrow","全部箱子已到位",42,37,600,32,22,UiFactory.Teal,TextAlignmentOptions.Center);
            T(r,"Clear title","通关",36,90,612,76,47,null,TextAlignmentOptions.Center); T(r,"Clear room","关卡名称",42,177,600,43,25,UiFactory.Muted,TextAlignmentOptions.Center);
            UiFactory.Panel(r,"Score background",42,234,600,128,UiFactory.Paper); T(r,"Final moves","0",64,236,260,78,43,null,TextAlignmentOptions.Center); T(r,"Final move label","步数",64,314,260,42,20,UiFactory.Muted,TextAlignmentOptions.Center);
            T(r,"Final pushes","0",360,236,260,78,43,null,TextAlignmentOptions.Center); T(r,"Final push label","推动",360,314,260,42,20,UiFactory.Muted,TextAlignmentOptions.Center);
            B(r,"Continue","下一关",42,379,600,61,true); var row=Row(r,"Replay controls",42,457,600,55);B(row,"Replay","重玩",0,0,292,55);B(row,"Clear leave","选择关卡",0,0,292,55);
            T(r,"Save status","成绩已保存。",24,534,636,32,19,UiFactory.Muted,TextAlignmentOptions.Center);
        }
        private static void BuildError(UiView v)
        { T(v.transform,"Error heading","关卡需要修复",180,252,1240,100,45);T(v.transform,"Error details","",184,387,1232,190,27,UiFactory.Muted); B(v.transform,"Error home","返回选关",184,638,330,64,true); Notice(v.transform); }
        private static BoardView BuildBoard()
        {
            int boardLayer = LayerMask.NameToLayer(BoardLayerName);
            if (boardLayer < 0) throw new InvalidOperationException("缺少 Board Layer，请检查 ProjectSettings/TagManager.asset。");
            var existing=AssetDatabase.LoadAssetAtPath<BoardView>(BoardPath); if(existing)return existing;
            var boxRoot=new GameObject("Box",typeof(SpriteRenderer)); boxRoot.layer=boardLayer; var box=boxRoot.GetComponent<SpriteRenderer>();box.sprite=resources.Visuals.Box;box.sortingOrder=4;
            var boxAsset=PrefabUtility.SaveAsPrefabAsset(boxRoot,"Assets/Prefabs/Board/Box.prefab").GetComponent<SpriteRenderer>(); Object.DestroyImmediate(boxRoot);
            var playerRoot=new GameObject("Player",typeof(SpriteRenderer));playerRoot.layer=boardLayer; var player=playerRoot.GetComponent<SpriteRenderer>();player.sprite=resources.Visuals.Player;player.sortingOrder=5;
            var playerAsset=PrefabUtility.SaveAsPrefabAsset(playerRoot,"Assets/Prefabs/Board/Player.prefab").GetComponent<SpriteRenderer>();Object.DestroyImmediate(playerRoot);
            var root=new GameObject("Board presentation"); var cameraObject=new GameObject("Board camera",typeof(Camera));cameraObject.transform.SetParent(root.transform,false);
            var camera=cameraObject.GetComponent<Camera>();camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.cullingMask=1<<boardLayer;camera.depth=0;camera.transform.position=new Vector3(0,0,-10);camera.backgroundColor=UiFactory.Paper;camera.enabled=false;
            var grid=new GameObject("Board",typeof(Grid));grid.layer=boardLayer;grid.transform.SetParent(root.transform,false);
            var terrain=Map(grid.transform,"Terrain",0,boardLayer);var goals=Map(grid.transform,"Goals",1,boardLayer);
            var actor=(SpriteRenderer)PrefabUtility.InstantiatePrefab(playerAsset,grid.transform); actor.gameObject.SetActive(false);
            var view=root.AddComponent<BoardView>();view.Configure(camera,grid.transform,terrain,goals,actor,boxAsset,resources.Visuals);
            var saved=PrefabUtility.SaveAsPrefabAsset(root,BoardPath).GetComponent<BoardView>();Object.DestroyImmediate(root);return saved;
        }
        private static Tilemap Map(Transform parent,string name,int order,int layer)
        { var go=new GameObject(name,typeof(Tilemap),typeof(TilemapRenderer));go.layer=layer;go.transform.SetParent(parent,false);go.GetComponent<TilemapRenderer>().sortingOrder=order;return go.GetComponent<Tilemap>(); }
    }
}
