# 共享编辑事务、工程资产保存与布局版本

状态：已采用。日期：2026-09-05。

## 问题与范围

关卡制作需要在 Unity Play 时直接操作 Game 视图中的棋盘，并能随时试玩、返回修改。原 EditorWindow 继续使用，两种界面必须保存兼容的工程关卡。用户确认采用顶部元素、右侧工具、底部操作的布局，支持直线、矩形、填充、框选移动和复制粘贴。本次在 Unity 内使用和验收。

这延续 [数据驱动与独立网格规则](0001-data-driven-grid.md) 的决策：ScriptableObject 保存内容，独立 C# 模型处理规则。不引入 ECS、规则脚本语言或多个关卡存储后端。

## 模块边界

2026-09-06 明确使用对象为在 Unity Play 内跑测的关卡策划；“运行时编辑”描述操作时机，不代表面向独立游戏玩家。`RuntimeWorkshop`、工具图标与提示组件、资产保存合同、试玩请求及 `GameController` 编辑入口均由 `UNITY_EDITOR` 限定。保留 Runtime 程序集中的组件以支持 Game 视图挂载，不引用 `UnityEditor`；普通及 Development Player 均排除专用编辑代码。共享数据、规则和棋盘显示继续供游戏使用，不增加第二套关卡文件存储。

| 模块 | 责任 |
|---|---|
| `Core/LevelDefinition`、`LevelValidator` | 唯一关卡数据含义与结构校验 |
| `Core/LevelAuthoring` | 无 Unity 引用的单格编辑、批量算法、尺寸修改、布局比较和区域快照 |
| `Runtime/RuntimeWorkshop` | 输入、预览、编辑事务、草稿历史、中文界面、试玩与返回 |
| `Runtime/Authoring/LevelAuthoringServices` | `ILevelAssetWriter` 保存合同，不引用 `UnityEditor` |
| `Editor/EditorLevelAssetWriter` | 唯一工程资产保存实现，负责 ID、版本、校验、磁盘写入和目录引用 |
| `Editor/LevelEditorWindow` | 原窗口的交互与 Unity Undo，复用共享编辑和保存实现 |
| `Runtime/GameController`、`GameSession`、`BoardView` | 正式游戏与试玩共用的流程、规则和棋盘表现 |

上述代码均位于 `Assets/Scripts`。入口资产为 `Assets/Resources/GameResources.asset`，关卡保存到 `Assets/Resources/Levels`，目录和配置位于 `Assets/Resources/Configs`；场景、精灵、音频和字体按 Unity 资源类型分目录。

## 编辑事务与格子语义

界面先选择元素，再选择工具。编辑器操作深拷贝草稿，形状与拖动的预览不写资产。一条连续笔画、一个形状、一次填充或一次区域操作是一个可撤销事务。区域操作先验证边界再提交，拒绝操作没有部分修改。

- 单格绘制：地板保留目标和对象；目标转换为地板并叠加目标标记；墙和橡皮清除目标和对象。玩家画笔迁移唯一玩家，箱子与玩家相互替换。
- 直线：Bresenham 单格线，包含两个端点。
- 矩形：以两个角确定含边界区域，空心矩形只写周界，实心矩形写全部区域。
- 填充：在操作前快照上，以地形、目标、箱子、玩家的完整签名做四连通搜索。先确定全部受影响格子，再应用元素。
- 玩家：只允许单点放置和拖动，批量工具不可选择玩家。
- 区域移动：先读取完整快照，原区域清为空地，再整块替换目标；移动的区域包含玩家时一并移动。重叠区域不读取自身写入结果。
- 复制粘贴：剪贴板与后续源编辑独立，复制跳过玩家但保留其脚下目标。空地会覆盖目标内容；目标包含现有玩家时拒绝。目标越界整单拒绝。
- 对象拖动：只移动玩家或箱子，保留两端目标点；目标为墙或已有对象时拒绝。

运行时历史存储布局快照；Undo 恢复布局时保留当前资产身份与已提交版本，避免另存后撤销回旧 ID。旧窗口使用临时 ScriptableObject 接入 Unity Undo。两种历史机制都把空间语义交给 `LevelAuthoring`。

## 保存合同与目录合法性

`ILevelAssetWriter.Save(target, snapshot, verifiedSolution, intent)` 返回成功状态、权威 `LevelAsset` 和说明。`intent` 包含普通保存、另存为和保存并加入目录；`ListLevels()` 和 `IsInCatalog()` 支持打开与显示状态。调用者成功后从返回资产重新读取 ID、版本和布局。

运行时新建与另存使用 `Assets/Resources/Levels` 中的唯一路径；旧窗口可以用文件选择框传入目标路径，但仍复用同一保存实现。不存在 JSON 关卡后端。

保存前深拷贝输入并检查尺寸与地形数组。未收录资产允许保存结构不完整的草稿；正式关卡更新和加入目录共用 `LevelValidator`，非法布局不能覆盖正式资产。加入目录前先创建关卡资产，再保存它的持久化引用。删除目录引用不会删除关卡文件。

保存器对目标文件与目录的只读状态做前置检查；写入失败保留调用者草稿，并恢复本次触及的源数据和目录。新文件路径不得覆盖已有资产。只保存本次触及的资产，使用 `EditorUtility.SetDirty` 与 `AssetDatabase.SaveAssetIfDirty` 显式写盘；后者用于写入指定脏资产，避免把整个工程的其他脏资产一起保存。[Unity 2022.3 API](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/AssetDatabase.SaveAssetIfDirty.html)

保存器不承诺文件系统级的多文件原子事务。测试覆盖可预知的只读失败、校验拒绝、路径冲突，以及目录重新导入后的引用；实际保存与停止 Play 后重载由端到端烟测检查。

## Play 生命周期与原窗口协作

Editor 层通过 `[InitializeOnLoad]` 注册无状态保存器，并在 Play 状态变化时幂等安装。实现每次保存都重新查找项目目录，不保留场景对象。Runtime 不在 `SubsystemRegistration` 清空这个 Editor 提供的服务。

正常 Domain Reload 会重建静态状态；禁用 Domain Reload 时静态状态和事件不会自动重置，因此注册采用覆盖赋值，事件使用先取消再订阅的方式，避免多次 Play 累积处理器。两种配置均保留默认 Scene Reload。[Unity 2022.3：Domain Reloading](https://docs.unity3d.com/2022.3/Documentation/Manual/DomainReloading.html)

运行时草稿、历史、剪贴板和试玩会话只存在于本次 Play。显式保存写入 `.asset` 和目录资产，停止 Play 后保留；未保存的运行时数据在 Stop 后丢弃。界面内切换和返回处理未保存修改，Unity 工具栏 Stop 不会转换为隐式保存。

保存成功后发布 Editor 内通知。旧窗口草稿干净时重新加载同一资产；有未保存修改时保留草稿、显示冲突，并阻止直接覆盖，用户可以重新加载或另存。窗口重新获得焦点和退出 Play 时也核对源资产快照，处理跨 Domain Reload 的外部保存。

原窗口的“保存并试玩”保留 `EditorPlaytestBridge`：校验并保存后传递独立快照，临时设置 Play 起始场景，通过 SessionState 跨域重载恢复请求，退出时恢复原设置。Game 视图的“试玩”只需验证当前草稿，在现有游戏流程中传入副本；返回编辑恢复草稿和视角。试玩状态显式传递，不以 `Application.isEditor` 推断。

## 关卡身份与成绩隔离

普通保存保持 `Id`。保存器比较源资产与草稿的实际布局：尺寸、地形、目标点、玩家起点和箱子起点；忽略名称、说明、ID、版本值及目标/箱子的数组排序。布局变化令版本等于源版本加一，并清除旧验证解。新建和另存生成新 ID，版本为 0；复制完全相同布局可以保留验证解。

成绩读取按 `Id` 和 `LayoutVersion` 匹配，关卡目录重排与改名不影响成绩。旧版本成绩不会显示为当前布局已通关；新的正式通关会更新该 ID 的当前版本记录，不维护永久的多版本成绩历史。旧存档没有布局版本时按版本 0 读取。试玩从不记录正式成绩。

编辑结束返回原游戏时，如果源关卡的布局版本未变，恢复原游玩状态；若版本已更新，从新布局开始。这样不能把保存前的棋盘状态算作保存后关卡的解。

## 验证边界

EditMode 覆盖共享算法、快照隔离、Undo、身份与版本、草稿和正式关卡的校验差异、资产写入失败及旧窗口冲突。PlayMode 覆盖编辑事务和试玩返回；Editor 烟测在正常与禁用 Domain Reload 两种配置下检查资产持久化、目录引用和原场景恢复。实际运行记录以 [验证记录](../verification.md) 为准。
