using System;
using System.Collections.Generic;
using Sokoban.Core;
using Sokoban.Runtime;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Sokoban.EditorTools
{
    public static partial class PresentationSetup
    {
        private static void BuildWorkshop()
        {
            if(AssetDatabase.LoadAssetAtPath<WorkshopViewCatalog>(WorkshopViewRegistration.Path)) return;
            var main=Page("Runtime workshop", v=> {
                var r=v.transform; UiFactory.Panel(r,"Header",24,20,1552,112,UiFactory.White);
                T(r,"Document title","关卡名称",44,35,295,45,30);T(r,"Document mode","项目关卡 · 运行时编辑",44,87,300,32,18,UiFactory.Muted);
                string[] labels={"地板","墙","目标","玩家","箱子"}; var brushes=new[]{LevelBrush.Floor,LevelBrush.Wall,LevelBrush.Goal,LevelBrush.Player,LevelBrush.Box};
                var visual=resources.Visuals;var sprites=new[]{visual.Floor,visual.Wall,visual.Goal,visual.Player,visual.Box};
                for(int i=0;i<labels.Length;i++)
                {
                    var b=B(r,"Element "+brushes[i],"",366+i*128,37,116,78);
                    var icon=UiFactory.Panel(b.transform,"Icon",14,18,42,42,Color.white,false);icon.sprite=sprites[i];icon.preserveAspect=true;
                    T(b.transform,"Name",labels[i],60,8,52,64,21,null,TextAlignmentOptions.Center);
                    b.GetComponent<UiButtonVisual>().Configure(b.GetComponentInChildren<TMP_Text>(),icon);
                }
                T(r,"Selection information","地图信息",1034,38,510,72,21,UiFactory.Muted);
                Viewport(r,40,150,1380,600);UiFactory.Panel(r,"Tools panel",1444,150,132,600,UiFactory.White);
                T(r,"Current tool","画笔",1448,159,124,48,21,UiFactory.Teal,TextAlignmentOptions.Center);
                string[] names={"画笔","直线","空心矩形","实心矩形","框选","填充","橡皮"};
                string[] tips={"画笔：按住左键连续绘制，一整笔只产生一次撤销；离开地图会取消本笔。","直线：拖动预览，松开提交；Esc 取消。玩家不能批量绘制。","空心矩形：拖动绘制边框，内部内容保持不变；松开提交，Esc 取消。","实心矩形：拖动覆盖矩形内所有格子；松开提交，Esc 取消。","框选：拖动玩家或箱子；Shift 拖动框选，拖选区移动。Ctrl+C/V 复制粘贴，Delete 删除。","填充：只替换四方向相连、地形和对象组合完全相同的格子。玩家不能批量填充。","橡皮：清除格内对象和目标点并恢复地板；按住拖动可连续清除。"};
                for(int i=0;i<names.Length;i++)
                {
                    var tool=(WorkshopTool)i;var b=B(r,"Tool "+tool,"",1480,216+i*74,60,60);
                    var icon=UiFactory.Panel(b.transform,"Tool icon",10,10,40,40,UiFactory.Ink,false);icon.sprite=PixelArtAssets.Tool(tool);icon.preserveAspect=true;
                    b.GetComponent<UiButtonVisual>().Configure(b.GetComponentInChildren<TMP_Text>(),icon);
                    var hint=b.gameObject.AddComponent<WorkshopPointerHint>();hint.Tool=tool;hint.DisplayName=names[i];hint.Description=tips[i];
                }
                B(r,"Workshop menu","菜单",40,780,130,58);B(r,"Workshop settings","设置",188,780,130,58);B(r,"Undo edit","撤销",336,780,130,58);B(r,"Redo edit","重做",484,780,130,58);
                B(r,"Fit canvas","恢复全图",632,780,170,58);B(r,"Save level","保存",820,780,158,58,true);B(r,"Playtest level","试玩",996,780,158,58);B(r,"Return to game","返回游戏",1172,780,198,58);
                T(r,"Editor hotkeys","Ctrl+Z 撤销\nCtrl+S 保存",1394,778,160,64,19,UiFactory.Muted);T(r,"Workshop status","左键绘制 · 中键平移 · 滚轮缩放 · Shift 框选",40,852,1512,30,20,UiFactory.Muted);
            },Workshop);
            var entry=Page("Editor entry", v=>B(v.transform,"Open editor","编辑",0,0,200,56),Workshop,200,56);
            var dialogs=new List<WorkshopViewCatalog.Dialog>();
            Action<string,string,float,Action<Transform>> add=(key,title,height,build)=> {
                var prefab=Page(key,v=> {
                    var r=Dialog(v,410,(900-height)/2,780,height);T(r,"Heading",title,30,24,720,48,30);
                    T(v.transform,"Dialog feedback","",410,(900+height)/2+12,780,68,21,UiFactory.Gold);build(r);
                },Workshop); dialogs.Add(new WorkshopViewCatalog.Dialog{Key=key,Prefab=prefab});
            };
            add("Workshop menu dialog","关卡菜单",538,r=> {
                B(r,"New level","新建关卡",32,100,342);B(r,"Open level","打开关卡",404,100,342);B(r,"Save as","另存为新关卡",32,180,342);B(r,"Add to catalog","保存并加入关卡目录",404,180,342);
                B(r,"Validate level","检查关卡",32,260,716);
                T(r,"Asset explanation","普通草稿可先保存。加入目录和试玩前会检查结构，关卡是否有解仍需亲自试玩。",32,348,712,68,21,UiFactory.Muted);B(r,"Close menu","继续编辑",244,450,292);
            });
            var openItem=Page("Open item",v=>B(v.transform,"Open level item","关卡名称",0,0,716,54),Workshop,716,54);
            add("Open level dialog","打开项目关卡",650,r=> {
                List(r,"Open list",openItem,32,96,716,400,14);T(r,"Empty","项目中还没有关卡，请先新建。",32,110,716,100,24);
                T(r,"Page","1 / 1",337,526,106,40,22,UiFactory.Muted,TextAlignmentOptions.Center);B(r,"Previous","上一页",32,518,232,52);B(r,"Next","下一页",516,518,232,52);B(r,"Cancel open","取消",244,584,292,48);
            });
            add("Level settings dialog","关卡设置",650,r=> {
                T(r,"Name label","关卡名称",32,90,180,32,22);UiFactory.InputField(r,"Level name","关卡名称",32,128,716,56,null);
                T(r,"Description label","关卡说明 / 提示",32,210,350,32,22);UiFactory.InputField(r,"Level description","",32,248,716,110,null,true);
                T(r,"Size label","地图宽 / 高（2 至 32 格）",32,386,440,32,22);
                UiFactory.InputField(r,"Map width","8",32,428,170,56,null).contentType=TMP_InputField.ContentType.IntegerNumber;
                UiFactory.InputField(r,"Map height","8",230,428,170,56,null).contentType=TMP_InputField.ContentType.IntegerNumber;
                T(r,"Resize help","缩小地图会裁切右侧与上方内容；应用后可以撤销。",32,504,716,48,20,UiFactory.Muted);B(r,"Cancel settings","取消",32,570,342);B(r,"Apply settings","应用设置",404,570,342,56,true);
            });
            add("Resize confirmation dialog","确认缩小地图",390,r=> {
                T(r,"Resize warning","地图边缘将被裁切，确认后可撤销。",32,102,716,146,23,UiFactory.Muted);B(r,"Cancel resize","返回设置",32,292,342,58);B(r,"Confirm resize","确认裁切",404,292,342,58,true);
            });
            add("Unsaved changes dialog","当前草稿尚未保存",350,r=> {
                T(r,"Explanation","保存后继续，或放弃本次修改。取消会回到当前草稿。",32,106,716,82,24,UiFactory.Muted);B(r,"Save changes","保存",32,246,220,60,true);B(r,"Discard changes","放弃修改",280,246,220,60);B(r,"Cancel leaving","取消",528,246,220,60);
            });
            var issueItem=Page("Issue item",v=> {var b=B(v.transform,"Validation issue","需要修复的问题",0,0,716,39);var t=b.GetComponent<UiButtonVisual>().Label;t.fontSize=20;t.alignment=TextAlignmentOptions.MidlineLeft;},Workshop,716,39);
            add("Validation dialog","请先修正关卡",620,r=> {
                T(r,"Validation details","结构检查通过。",32,96,716,280,24,UiFactory.Muted);T(r,"Validation instruction","点击带坐标的问题，可返回画布定位。",32,90,716,34,21,UiFactory.Muted);
                List(r,"Issue list",issueItem,32,135,716,329,8);B(r,"Previous issues","上一页",32,482,220,40);B(r,"Next issues","下一页",528,482,220,40);T(r,"Issue page","1 / 1",274,482,232,40,20,UiFactory.Muted,TextAlignmentOptions.Center);
                B(r,"Close validation","返回编辑",244,534,292);
            });
            var catalog=ScriptableObject.CreateInstance<WorkshopViewCatalog>();catalog.Main=main;catalog.Entry=entry;catalog.Dialogs=dialogs.ToArray();AssetDatabase.CreateAsset(catalog,WorkshopViewRegistration.Path);
        }
        private static void List(Transform parent,string name,UiView item,float x,float y,float w,float h,float gap)
        {
            var root=R(parent,name,x,y,w,h);var layout=root.gameObject.AddComponent<VerticalLayoutGroup>();layout.spacing=gap;layout.childControlHeight=false;layout.childControlWidth=false;layout.childForceExpandHeight=false;layout.childForceExpandWidth=false;
            root.gameObject.AddComponent<UiList>().Configure(root,item);
        }
    }
}
