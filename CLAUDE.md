# InnFactory — 客栈经营 + 自动化（给 Claude Code 的交接说明）

仙剑客栈式古风客栈经营 + Factorio 式自动化。设计文档：`Docs/GDD.md`（用户已认可 v0.1）。
用户是团队里负责技能光效（VFX）的人，用中文沟通，回复也用中文。

Unity 6000.3.25f1 / URP 17.3 / DX11。2.5D 等距 = 3D 场景 + 正交相机（俯角 30°、水平 45°）。Q 版古风，原型阶段全部是代码拼的几何体占位。

## 约定
- **每次交付前跑 Smoke Test**，`Logs/smoke.txt` 第一行 `RESULT: PASS` 且 Console 0 报错才能说“好了”；说明哪些验证过、哪些要用户亲自试玩（手感、节奏）。
- 数值 / 内容全部由 `Assets/Game/Editor/InnDataFactory.cs` 生成（`Data/**` 每次整体重建 + `Resources/GameDatabase.asset`），**不要手改 .asset**。数值有歧义按合理理解实现并用表格告诉用户。
- 场景 `Scenes/Inn.unity` 只有相机 / 灯光 / Inn 物体，地图和初始建筑在运行时按 `GameDatabase.startLayout` 生成（布局在 `InnDataFactory.BuildLayout`）。
- 用户在自己的 Unity 里点 `InnFactory → Setup → Run All` 重新生成。
- 还不是 git 仓库；要初始化 / 提交前先问用户。

## 构建与测试
1. 改代码在 `C:\Users\Administrator\Projects\InnFactory`。
2. 同步副本：`robocopy C:\Users\Administrator\Projects\InnFactory C:\Users\Administrator\Projects\InnFactory_batch /MIR /XD Library Temp Logs UserSettings`
3. 后台跑：`Unity.exe -batchmode -projectPath C:/Users/Administrator/Projects/InnFactory_batch -executeMethod Inn.EditorTools.SmokeTest.BuildAndRun -logFile Logs/smoke.log`
   （编译错误搜 `error CS`；只改测试可用 `SmokeTest.Run`）
4. 结果 `InnFactory_batch/Logs/smoke.txt`，截图 `Logs/shots/play_*.png`（相机直出，不含 IMGUI 界面）。

## 代码结构（命名空间 `Inn`）
- `Scripts/Data/`：`ItemDef` `CropDef` `RecipeDef` `BuildableDef` `CustomerDef` `GameDatabase`（每个 ScriptableObject 一个同名文件）、`Defs.cs`（枚举）
- `Scripts/Core/`：`InnGame`（总控：世界、经济、时间、菜谱发现、顾客刷新、员工、厨师规划 `PlanChefStep`）、`GridMap`（一张网格分三区）、
  `Item`/`Bag`/`IItemSource`/`IItemSink`、`BuildController`（B 建造）、`IsoCamera`、`InnInput`（测试用 `Press` 模拟按键）、`Vis`（占位几何体）
- `Scripts/World/`：`Building` 基类 + `Plot` `Producer`(畜舍) `Container` `Stove` `PassWindow` `Chair` `Furniture` `Belt` `Inserter` `Sprinkler` `Harvester` `Seeder`
- `Scripts/Actors/`：`Player` `Customer` `Staff`(厨师/跑堂/园丁) `StaffData` `Walker`
- `Scripts/UI/InnHUD`：IMGUI
- `Editor/`：`InnSetup`（URP、DX11、Run All）`InnDataFactory` `InnSceneFactory` `SmokeTest`

## 联机（最多 5 人合作，见 GDD 10.5）
- `Scripts/Net/`：`ITransport`（传输接口，以后 Steam 实现它）、`LanTransport`（TCP + UDP 广播 `LanDiscovery`，端口 7790/7791）、
  `NetProto`（协议版本 + `Wire` 序列化）、`InnNet`（主机权威：快照 15Hz、客户端指令执行）、`NetBot`（自动测试用客户端，`-nettest` 启动）。
- **会改共享世界的操作一律走 `InnGame.net.Xxx()`**（Interact / Place / Remove / Hire / Fire / SetJob / BuySeed / ToggleDish / NextDay / Pause / Speed），
  单机和主机直接执行，客户端发给主机。新加操作要在 `NetProto.Cmd` + `InnNet.Execute` 里加。
- 新建筑类型要实现 `WriteState / ReadState`，并在 `Update` 里 `if (Replica)` 只做表现不做模拟。
- `InnGame.Toast` 在主机上默认广播；执行某个掌柜的操作时（`toastTarget`）只发给他。只给本机看的用 `LocalToast`。
- 注意：`BinaryWriter.Write(unityObject)` 会被编译成写 bool（Unity 对象隐式转 bool），所以 ItemDef/RecipeDef/CropDef 用 `WriteDef/WriteRecipe/WriteCrop`。
- 命令行：`-name 名字` `-host` `-join IP` `-port N`。
- SmokeTest 的联机项会先打包 `Builds/InnFactory/InnFactory.exe`，再启动 5 个无窗口客户端（第 5 个应被拒），日志 `Logs/net_bot*.txt`。
- 给朋友测试的压缩包：打包后把 `Builds/InnFactory` + `Docs/如何联机.txt` 压成 zip（不要带 `InnFactory_BurstDebugInformation_DoNotShip`）。

## Telegram 群聊助手（`Tools/TelegramBot`）
- 用户和朋友在 Telegram 讨论开发；这是给群里用的问答机器人（C# / .NET 10 控制台，Anthropic C# SDK，模型 claude-sonnet-5-5，effort low）。
- 只回答 @它 / 回复它 / 私聊；背景知识 = 本文件 + `Docs/GDD.md`（启动时读）+ 内存里最近 80 条群聊。不写聊天记录到磁盘。
- `config.json`（含密钥，已 gitignore）里的 `allowedChatIds` 为白名单；为空时只回复 chat id。说明见该目录 README.md。
- 改动后跑 `dotnet run -- --selftest`（离线，不需要密钥）。Unity 不会导入 `Tools/`。

## 原型的已知简化
- NPC 直线移动不寻路（会穿过桌子）；玩家只被实心建筑挡住，区间墙只是装饰。
- 名望等级不锁内容（全部解锁）；畜舍不需要饲料；没有存档。
- 传送带走到尽头可以直接塞进箱子 / 出菜口 / 自动灶台（Factorio 里要机关手）。
