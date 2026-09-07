# 按游戏功能查找脚本

已采用：按游戏功能组织目录，并独立角色控制入口。关卡菜单移除复制、粘贴和清空选区，Ctrl+C/V 与 Delete 快捷操作保留。

## 从角色控制开始

在 Scene 中展开 **GameRoot > Player controls**，选中后即可看到 PlayerController 的移动按键、快捷键与操作配置。按键可以包含多个绑定，默认支持 WASD 和方向键；操作配置指向 GameConfig.asset，包含长按首次间隔、长按重复间隔和移动时长。

一次方向操作的调用顺序：

1. `Gameplay/Player/PlayerController.cs` 读取输入，处理长按、输入屏蔽，发出移动意图。
2. `Gameplay/GameController.cs` 把操作交给当前游戏会话，协调声音、计数和结算。
3. `Gameplay/Rules/GameSession.cs` 判定墙体、箱子、目标，提交合法的格子位置，并记录撤销。
4. `Gameplay/Board/BoardView.cs` 显示玩家和箱子的移动；动画不决定能否推动。

## 当前目录

```text
Scripts/
  Gameplay/
    GameController.cs          游戏流程
    Player/             PlayerController：按键、长按、输入屏蔽
    Board/              棋盘显示、像素视口
    Rules/              推箱规则、有效步、撤销
  Levels/               关卡资产、目录；Model 中为关卡定义与校验
  UI/                   页面、按钮、列表
  Save/                 正式成绩
  LevelEditor/          草稿、工具、编辑与试玩；Rules 中为共享编辑算法
  Editor/
    LevelEditor/        Unity 编辑窗口、资产保存与试玩桥
    Scene/              非 Play 棋盘预览
    Setup/              资源与 Prefab 准备工具
    Validation/         开发验收工具
  Tests/
```

过去把 Core / Runtime 作为一级目录，强调编译边界，导致角色控制难找；现在第一层导航回答“要改哪个游戏功能”。程序集仍保留 Sokoban.Core / Runtime / Editor 的名称和原有命名空间，已有代码与资产引用保持兼容。

## 维护约定

- 移动文件时必须连同 .meta 保留 GUID；功能导航不改变正式游戏与开发编辑器的隔离。
- 保留 GameController 的公共移动入口，以及新方向优先、长按间隔、中文输入保护、暂停／失焦／动画期间的移动屏蔽。
- Player controls 是 GameRoot Prefab 的真实子节点；切关、暂停、重开、撤销等状态变化统一重置长按状态。
- 同步迁移工具和测试中的路径，并更新 README 的实际脚本入口。
- Gameplay/Rules 中的程序集定义保持独立 C# 规则。Levels/Model 和 LevelEditor/Rules 使用 .asmref 加入同一个规则程序集，不能在其中引用 Unity 对象。
- Scripts 根目录的程序集定义承载游戏显示与控制；Editor 和 Tests 有自己的程序集边界。LevelEditor 中运行于 Game 视图的专用代码继续通过 UNITY_EDITOR 隔离。
- 验证副本复制工具会先同步 Scripts 目录，避免文件移动后遗留旧路径、产生重复类或重复 GUID。
