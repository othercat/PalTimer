# SDLPAL 隔世追凶（简单）独立核心

Issue #18，PalTimer 3.39.0 / PalTimerOnline 1.0.0.5，Windows 10 及以后。核心菜单、标题玩法名称和版本行使用 **隔世追凶-简单**；整合包可用 `Pal98Timer.exe --core sdlpal-geshi-easy` 启动。没有指定参数时继续采用原有“上次核心 / 默认自动玩法核心”逻辑。

新核心不使用原版 PAL98、DX9、UNHAPPY 的进程内存地址，也不改变这三个核心的计时规则。游戏须发布 `Local\SDLPAL.LostEdit.v1.<PID>` v1 快照，且资源 profile 为 `geshi-easy-v1`。主仓 `docs/runtime-bridge.md` 是二进制布局的协议来源；本仓只实现客户端，不携带 SDLPAL 源码或资源。

## 使用

- 新游戏开始计时；F9 暂停/继续，F10 保留确认重置。游戏失去焦点、进程退出或快照过期时停表。读档保留累计时间并暂停，需 F9 继续；它不会把接力或中途连接声称为完整新跑次。
- F11 和“按键配置”打开已连接游戏 EXE 所在目录里的 `LostEditKeyConfig.exe`，工作目录也设为该处，支持资源另放于 `PAL` 子目录。新核心不启用旧全局 KeyChanger；切换到其他核心后仍采用其原行为。
- 默认节点为“八头蛇胜利”（队伍 315、原始敌人物件 547、结果 3）和“通关”（游戏真实结局脚本的 completed 标志）。逃跑、战败和关闭游戏均不算通关。没有用未经确认的剧情顺序拼接长路线。
- “编辑自动节点 / 捕获当前战斗”提供可排序表格。规则类型为 `battle`、`scene` 或 `completion`；战斗可限定队伍、敌人物件、场景，全部限定共同成立，且必须胜利。场景使用该 DOS 资源的实际 `scene`，不套用 Win95 MapIDFix。末行且仅一行须为 completion。保存规则会确认重置计时。
- 现有节点昵称、最佳线编辑、手动跳转、非顺序模式、节点音效继续使用 PalTimer 的公共功能。自动规则的稳定 ID 与昵称分离；完整引擎/资源/速度身份和规则变化都会隔离本地最佳线。
- 导出/导入时间线保留累计时间、节点状态、资源哈希、引擎哈希、速度身份。导入后暂停且记录为辅助跑次；本地参考线仍可编辑和保存。
- OBS 独立遮罩复用现有渲染器，支持位置/缩放、字体、文字颜色、快捷键、恢复默认和插件/云 ID；新核心用独立配置。主窗口与榜单 OBS 功能也保留。简体/繁體切换调整本核心菜单、节点和状态显示。

事件环丢失、读档 epoch 变化、过程中改变引擎/资源/速度都会保留诊断。丢失事件不补造胜利；中途连接不会把历史战斗当作刚完成的节点。

## 实时物品检测与富甲（Issue #19）

“实时物品检测 / 富甲设置…”打开独立实时表，支持按名称或编号筛选、仅持有、仅监测、勾选物品、清空监测与重建观察基线。表内列出背包数量、战斗占用、当前队伍装备、合计、本段净变化、资源价格和装备角色；在计时暂停或游戏失去焦点时仍读取最新快照。关闭表格不会关闭已选物品输出。

- 富甲显示实际现金、道具种类和总件数。**种类**为背包和当前队伍六个装备槽的物品 ID 去重数；**件数**为背包数量加当前队伍装备件数。战斗中已安排但尚未消耗的 `inUse` 仍属于背包，不再重复加一。尚未入队或离队角色的装备不计入；重复装备同一物品增加件数、不增加种类。
- 名称和资源价格来自当前 `catalog`。价格采用原生 `OBJECT_ITEM.wPrice` 的 `data[1]`，只作为资源信息显示；富甲现金不包含物品折价，也不使用原版物价、六颗灵珠例外或余杭神器判定。
- 列表采用引擎核验过的 `itemIds`，另加实际背包和队伍装备中观察到的 ID。当前审计目录含 242 项；571“隔世追凶”和575“魂息”属于初始法术，不从对象联合体 flags 推测为物品。目录不承诺所有物品都能取得。
- “本段净变化”是相对于本次观察基线的合计变化，不声称是获得/消耗事件；队伍成员加入或离开也可能影响它。首次连接、读档 epoch 变化、进程更换、资源/速度身份变化、断线重连和计时器重置都会建立新基线，历史背包不会被误报为刚获得。手动重建观察基线只影响物品比较，不重置计时或节点。
- 断线、过期/撕裂快照或无效物品数据时，表格数量显示“—”，插件显示数据不可用；不会把旧值当作实时数据，也不把缺失字段变成零件物品。缺少完整物品字段的早期桥接版本仍可提供其原有计时功能，物品窗口会说明需更新游戏。

富甲和监测输出是随计时器 EXE 编译的专用内置插件，分别使用现有 `BR` 与 `BL` 输出位置，默认启用；监测清单初始为空。两个开关既可在该表格修改，也可在原“插件管理”中修改，立即生效。传统 PAL98/DX9/UNHAPPY 核心、原 `PAL98.FujiaCaishen.tpg`、插件签名检查和插件协议版本保持不变；SDL 不加载这些原版固定内存插件。内置插件没有独立 TPG 需要复制，其源码及启用状态不构成授权绕过，发行仍由计时器宿主哈希覆盖。

开关、筛选和监测清单保存在 `sdlpal_geshi_items.json`，沿用工具目录优先、权限不足回退的设置存储。本地写入保留 `.previous`，用户目录回退采用共享状态备份。内容/版本/编号无效时提示并暂用默认值，原文件保留；只有显式更改配置才保存替换。计时器重置保留清单。

主窗口 `BL/BR`、OBS 独立遮罩和旧云步骤 `Plugins.BL/BR` 使用同一批缓存输出，不额外读取进程或触发网络请求。OBS 两种插件文字都进入可换行底栏。停用插件会清空本地显示和客户端插件缓存。这些显示值不参与计时、榜单身份或签封成绩计算。

旧 `PalCloudLib` 只在发送非空成绩正文（BigData）的 POST 中附带插件缓存；仅有插件值或空成绩的步骤不会发送插件。有效个人云会话中，符合成绩条件的真实跑次可随该 POST 更新或清空云插件。读档、接力、中途连接等不具成绩资格的状态仍可使用本地/OBS 显示，但不能保证更新或清除云端旧显示。诊断模式不发布竞赛观察、物品值或成绩；不会为触发插件请求构造虚假成绩正文。

联机排名、候选登记、签封与正式上传继续走既有 v2 流程；本功能不授予生产服务或构建批准。实际云服务需接受 `SDLPAL_GESHI_EASY` 核心，真实往返仍属于整包验收。OBS 软件采集结果见下节。

### OBS 实际采集（2026-10-18）

在计时器“功能”中启用“OBS 独立遮罩”，再在 OBS 的“窗口采集”选择 **隔世追凶-简单 - OBS 独立遮罩**，采集方式显式选择 **Windows 10（1903 或更新版本）**。本机 OBS 32.1.2 的“自动”方式捕获了品红底色，显式选择 Windows 10 方式后背景透明；在遮罩下添加浅灰色源，已确认底层颜色透出，文字没有黑色矩形背景。此次没有添加色键滤镜。

使用 r3 中实际发布的 3.39.0.0 EXE（SHA-256 `0333a8aec8a5904edb6eb929efad16bf148b55f2f114c6f42a86fa93ba032a46`）及配套 DLL 的隔离副本。OBS 能枚举该遮罩并持续显示时间、暂停/失焦状态、八头蛇与通关节点；富甲输出现金 9817、107 种/458 件。在真实物品表勾选 61 号“观音符”后，OBS 底栏同步出现“监测 观音符×1”，换行后仍完整可见。关闭物品表不会清除该输出。

OBS 使用已安装程序的便携副本，未修改原 OBS 配置；只看预览，没有开播、录制或上传成绩。数据来自此前隔离候选游戏的真实快照，属于中途连接，计时器明确显示不具完整跑次资格。此结果只确认本机 OBS 的窗口枚举、透明合成和插件显示，不代替联机榜单、个人云、音频录制、多显示器或完整路线验收。正式 EXE 未重新构建，r3 压缩包未改写。

## 接力与个人云档

“接力－保存/载入”和“云存档/云读档”采用 `SDLPAL.GeshiRelay.v1` JSON 包，包含明确身份、时间线、RPG 字节和 SHA256。它不使用 BinaryFormatter，也不会接收旧 PAL98 `.bin` 存档。

客户端只在用户发起操作时打开独立控制 MMF，并用命名 mutex 串行处理。仅支持 SAVE/LOAD 两个命令和固定 `250.rpg`；不发送路径、其他槽号、按键或任意内存写入。普通菜单档与快捷档不受影响。

SAVE 必须收到对应 request ID 的成功确认，才复制已经关闭的 `250.rpg`。LOAD 先完整核验接力身份和时间线，再原子写入保留槽（已有文件留 `.backup-时间戳`），等待成功确认和相同的新 epoch 才恢复计时。载入淡入期间 canControl=false 仍可核对成功确认。超时不自动重试 LOAD；取消、重置、切换核心或游戏重连后不恢复过期的计时状态。

云文件会话复用原 PalCloudLib 和 CloudSessionRunner，名称 `SDLPAL_GESHI_EASY`。云编码形如 `geshi-<云ID>-<32位随机ID>`。云文件功能与成绩资格分开；接力导入不取得榜单资格。上传/下载失败保留暂停状态，原错误会显示。真实云往返仍需对应云服务接受该核心、有效云会话和成套发布包。

旧个人云初始化按核心名和计时器 EXE 的 MD5 验证版本，与 pallab.top 的 v2 构建审批相互独立。2026-10-18 实机收到“版本不匹配”，云存档/云读档尚不可用；需由旧云维护者登记该核心及实际发布构建，再验证真实往返。新榜单部署或批准不会自动解除这项限制。

## 联机与榜单契约

新核心沿用“联机与排名”设置、OBS 榜单、实时节点比较、签封、离线待上传队列、重试和回执。生产运行仍必须通过配套 `PalCompetitionAuth.dll`、`PalCompetitionRegistration.public.json`、EXE/Online DLL 哈希和服务端批准；没有认证旁路。诊断运行 `diagnostic=true` **禁止发布竞赛观察和旧云成绩流**，不能作为测试成绩上传。

服务地址及启用权限仍从已验证游戏的 `gamePath/palmod/common-tools.v1.json` 读取，遵循现有配置锁规则；不会继承另一游戏或计时器目录的上传权限。可参考 [配置示例](sdlpal-common-tools.example.json)。默认游戏路径识别新增了有效 SDL MMF 分支，原 PAL.exe 配置读取方式保留。

| 字段 | SDLPAL 的值 / 语义 |
| --- | --- |
| gameplay.family | `sdlpal-geshi-easy`，独立于 standard / drawcard |
| gameplay.content_id | `geshi-easy-v1` |
| gameplay.content_sha256 | 四个 resourceHashes 按 Ordinal 文件名排序，`KEY=HASH` 用 LF 连接，无末尾 LF，再 SHA256 |
| gameplay.fade_ms | 实际 fadeCycleMs；0 表示尚未实现，不能形成有效榜单身份 |
| gameplay.map_speed_ticks | 0，明确不套用 PALDLL 9/10 tick |
| gameplay.walk_frame_ms | 新增可选字段，仅新 family 使用，等于实际 walkFrameMs |
| pal_dll_sha256 | 旧传输字段在此 family 下承载实际游戏 EXE SHA256 |
| game_version | `1.0.0.0`，桥接契约版本；实际引擎 git revision 在规则中保留 |

`ranking.rules` 含 `family`、`content`、`profile`、`engine_sha256`、`engine_build`、`fade_ms`、`map_speed_ticks="0"`、`walk_frame_ms`、`speed`、`battle_speed`、`route_rules_sha256`，以及 `resource.SSS.MKF`、`resource.DATA.MKF`、`resource.WORD.DAT`、`resource.M.MSG`。`configuration_id` 和 `gameplay.rules_sha256` 都用现有 `RankingConfiguration.Digest(rules)` 计算。`route_rules_sha256` 是 `SDLPAL.GeshiSplits.v1\n` 加顺序规则行的 SHA256（每行为 JSON 转义的 ID、Kind、Team、Scene、Enemy，以冒号分隔；空筛选为 `-`，每行含末尾 LF）。显示名称不进入身份；节点顺序/筛选条件属于身份。原核心的签封 JSON 不增加 walk_frame_ms=null 字段。

服务端需接受新 family、独立 walk_frame_ms、上述规则与实际资源/引擎身份，决定成绩准入及榜单发布。线上状态必须以真实批准与回执为准，离线模拟成功不代表已激活。中途连接、读档、手动跳过节点等观察会保留给服务器判定，不伪造反作弊证明。

## 构建与验证

VS 2026 / MSBuild 18，Release|x64，.NET Framework 4.7.2。普通仓库布局继续从相邻 PALDLL_DX9/shared 读取 PalUserData.cs；worktree 用可覆盖属性，不提交本机绝对路径：

```powershell
& $msbuild .\Pal98Timer.sln /p:Configuration=Release /p:Platform=x64 "/p:PalSharedSourceRoot=$shared" /m /nodeReuse:false
& .\tests\run_sdlpal_tests.ps1 -PalSharedSourceRoot $shared -NativeSnapshotFile $initialSnapshot,$loadedSnapshot
& .\tests\run_competition_tests.ps1 -PalSharedSourceRoot $shared
```

SDL 定向测试使用真实命名 MMF/控制确认、生产核心时钟及原生 JSON 快照，覆盖身份错误、过期/撕裂、事件环/逃跑、暂停/失焦、接力备份/epoch 和导入。物品检查验证实际窗口勾选、配置备份、生产 `OnTick` 到插件、OBS、云 payload 与 `CloudSessionRunner` 的路径，并验证两份原生存档的独立已核对总数：现金31265、80种/355件；现金9817、107种/458件。窗口截图由不激活的屏幕外测试宿主生成，不代表已完成整条游玩路线。竞赛测试只使用本机 Fake transport/auth，验证新配置榜单查询、未批准阻断、批准后的既有 challenge/upload 流程，并回归原有身份和上传行为；测试替身不进入产品程序集。

发布验收还须成套签发 EXE/Online/Auth/公开登记文件，实测新游戏至结局、菜单 F9/F10/F11、OBS、双方接力与个人云往返。正式榜单的测试通过服务端预演/隔离测试环境完成，不上传合成跑次污染正式榜单。
