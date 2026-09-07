# ADR 0003：Scene、Prefab 与整数像素棋盘显示

状态：已实施。日期：2026-09-06。

## 问题

此前 Scene 只有相机，游戏启动后由代码创建棋盘和所有界面。关卡数据可维护，但调整字体、控件位置和表现需要改脚本，且不按 Play 无法查看关卡。一次性生成代码不应成为日常 UI 的维护位置。

## 决策

保留单 Scene 和独立网格规则。Game Scene 保存 GameRoot Prefab 实例，包含 GameController、两个职责不同的相机、一个 AudioListener、Canvas、EventSystem、音频、Grid 和 Tilemap。背景相机只清屏，棋盘相机仅绘制棋盘层到纹理。取消按场景名隐藏创建应用及禁用所有相机的启动方式，缺少引用时报告配置错误。

固定页面保存在 Prefab，GameScreens 负责显隐；UiView 使用序列化语义绑定提供文本、按钮事件和可见状态。GameController 仍负责会话、进度和流程。UiList 只实例化条目 Prefab。布局、TMP、Image、Button 状态样式保存在 Prefab；初始化脚本只补齐缺失资源。工具按钮使用同一个模板，各实例覆写图标、中文提示和工具类型。

RuntimeWorkshop 保留草稿、事务历史和手势，WorkshopViews 持有独立视图及弹窗实例。专用视图目录和工具精灵位于 Editor 子目录，只由 Editor 适配器通过 AssetDatabase 注册；Game Scene、GameRoot 和 GameResources 不引用这些资产。专用代码使用 UNITY_EDITOR 保护，Development 配置也不例外。

BoardView 引用已存在的相机、Tilemap、玩家和箱子 Prefab。关卡资产只提供初始布局，视觉资源来自 VisualConfig。运行时创建的 Tile、纹理与实例有明确所有权，销毁视图不影响持久资产。

BoardView Inspector 提供只读预览和单独的试玩入口。预览选择存在 SessionState；临时节点不参与场景序列化。保存、关闭、程序集重载和进入 Play 前清理，回到编辑模式或资产变更后恢复。普通 Play 入口不消费预览选择；只有明确的试玩按钮进入不记成绩的会话。

棋盘每格 32 个原始像素、1 个世界单位。BoardViewport 的 RectTransform 给出物理显示范围，棋盘相机按 32 像素/单位渲染，RawImage 用 Point 采样并整数倍显示。画面边界与移动位置均对齐像素；不足整倍的空间留边。缩放和平移改变相机与整数倍率，鼠标拾取反算同一 RawImage 区域。TMP UI 在高分辨率 Canvas 上独立显示。六张棋盘精灵统一使用 Full Rect、Point、无压缩、无 Mipmap 的导入设置；棋盘对象位于具名 `Board` Layer。

## 取舍

非 Play 预览只渲染已保存内容；不会把 Scene 内拖动预览箱子当作编辑关卡的方法。大地图在较小窗口中最低保持 1 倍源像素，必要时平移查看。整数倍缩放有离散档位，避免非整数像素模糊。预览选择只在当前本机 Unity 会话中记忆，不写工程配置。

本次保留现有关卡资产、布局版本、ID 和 GUID。关卡版本可以被策划独立编辑，测试不能用原教学模板覆盖其新版本；结构合法与可解性分别报告。

## 检查

使用实际 GameRoot/Board Prefab 进行 PlayMode 测试，验证样式保持、像素边界、拾取和工作坊流程。EditMode 检查场景依赖、预览保存隔离和精灵导入设置。两种 Domain Reload 通过实际进入/退出 Play 验证保存；Release 和 Development 均只编译脚本并检查依赖，不生成游戏包。

Unity 的临时对象规则参考 [HideFlags.DontSave](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/HideFlags.DontSave.html)；场景生命周期通过 [EditorSceneManager](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/SceneManagement.EditorSceneManager.html) 管理。实际检查结果见 [验证记录](../verification.md)。
