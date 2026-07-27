# Project Resume / AI Handoff

> 这是本项目的 AI 交接文件。
> 任何新的 AI Agent / Codex / Claude Code / ChatGPT 接手本项目时，应先阅读本文件，再根据本文件指引继续工作。
> 不要依赖上一轮聊天上下文，所有判断以仓库文件、Git 状态和本文件为准。

---

## 0. 接手规则

新的 AI 接手本项目时，请按以下顺序执行：

1. 读取本文件：`.ai/resume.md`
2. 如存在以下文件，也需要阅读：
   - `README.md`
   - `AGENTS.md`
   - `TODO.md`
   - `.ai/decisions.md`
   - `.ai/env.md`
   - `CLAUDE.md`：仅在审计 Claude Code 入口、迁移 agent-setting 或排查 Claude/Codex 指令冲突时阅读；普通 Codex 开发不需要读取。
3. 执行 `git status`，确认当前工作区状态。
4. 如果本文件与代码状态冲突，以代码和 Git 状态为准，并更新本文件。
5. 不要假设本机路径、Python 环境、Node 环境、游戏路径和其他电脑一致。
6. 不要提交密钥、Cookie、Token、账号密码、本机私有配置。
7. 继续工作前，先总结当前项目状态，再提出下一步计划。
8. 修改代码或文档后，需要在本文件的"最近改动"和"下一步建议"中更新交接信息。

---

## 1. 项目目标

PalTimer（仙剑98自动计时器）是一个 Windows 桌面应用，用于仙剑奇侠传系列游戏的速通自动计时。通过读取游戏进程内存，自动判断时间节点完成，支持多种仙剑/古剑游戏内核。

当前主要维护方向：
- 仙剑98柔情版（PAL98）及其 DX9 移植版（PAL98DX9）、不欢乐模式（PAL98UNHAPPY）
- 其他内核（仙剑98Steam、仙剑2Steam、仙剑3、仙剑5前传、梦幻2.2、古剑2、自定义）

技术栈：C# / .NET Framework 4.7.2 / WinForms / GDI+，编译目标以解决方案配置为准，最低系统目标仍需兼容 Win7 SP1。

---

## 2. 当前工作状态

**版本：v3.36.5**（2026-05-15）

当前 Git 状态：`master...origin/master`。本文件此前记录的 `codex/paltimer-automation-tick-snapshot` 为旧会话状态；后续接手以实际 Git 状态和代码为准。

2026-07-27 本轮改动已通过提交 `4727096` 推送到 `origin/master`：不欢乐模式 `CoreName` 从历史中文值统一为 README 已公开的 `PAL98UNHAPPY`，旧最佳线保留式迁移。PAL98DX9/PAL98UNHAPPY 新 SRPG 现在携带 `PalDrawCard.FlyingFlagAll.v1.bin` 完整快照与 SHA-256；明确记录源 sidecar 不存在，旧 SRPG 不处理目标 sidecar，导入前保留时间戳备份并要求重启 PAL.exe。服务器继续不透明存取 `.bin`，无需修改。新增两项回归脚本和规则文档。

2026-07-27 本轮改动：PAL98、PAL98DX9、PAL98UNHAPPY 的接力/云 SRPG 改为恢复 `TimerStr.TotalMonsterCount` 完整快照；旧 SRPG 缺少字段时保留本地撞怪数。服务器和 `PALCloud.dll` 不变。新增 `.ai/srpg_monster_count_regression_check.ps1`，新/旧字段行为 harness、既有 sidecar 兼容检查和 VS2026 `Release|x64` 构建均通过。

2026-07-27 本轮改动：PAL98/PAL98DX9 的“水灵珠”节点改为剧情标记门闩与物品联合判定。连接新 PAL.exe 时从最终 `SSS.MKF`/`M.MSG` 一次解析“得到水灵珠”和大理祭坛返回对话，70ms 循环只读当前脚本状态与既有背包状态；开局随机水灵珠不再提前触发，正常十年前路线和回梦无痕等跳过路线均有独立入口。不欢乐模式没有该节点，未修改。资源/状态机回归和 VS2026 `Release|x64` 构建通过，待 PAL98/PAL98DX9 双路线实机确认。

2026-07-09 本轮未发布改动：PAL98DX9 标题识别补充繁体与英文兼容。简体仍要求并识别旧基本盘 `仙剑奇侠传...` 标题格式；繁体同等识别 `仙劍奇俠傳...`；英文识别 `PAL98DX9 (v...)` 并沿用版本号提取。同步更新 `仙剑98柔情DX9.cs` 与 `仙剑98柔情不欢乐模式.cs`，新增 `.ai/pal98dx9_title_identity_regression_check.py` 结构检查。

2026-07-03 本轮未发布改动：修改富甲插件源码 `PAL98.FujiaCaishen`，右下角输出改为在“钱/道具”前显示“四大神器：已收集/未收集”。插件在计时开始、节点初始化和重置后重新统计；在上船节点坐标触发时冻结状态。目标物品为紫金丹 `0x111`、土灵珠 `0x10B`、六神丹 `0x11E`、布包 `0x10F`，读取链路沿用富甲插件既有背包槽读取。新增 `.ai/fujia_yuhang_artifacts_regression_check.py`。

2026-07-03 本轮未发布改动：最新计时器新增隐藏插件授权调试开关。默认仍要求 `.tpg` 签名有效；仅当 `Pal98Timer.exe` 同目录存在 `plugin_auth` 文件且包含精确行 `allow_unsigned_plugins=1` 时，加载器允许未签名插件，插件管理器签名列显示“调试放行”。新增 `.ai/plugin_authorization_bypass_regression_check.py`。该开关只用于本地调试/验证，不等同于 3.36.4 公开包可只换未签名插件发布。

2026-07-03 本轮测试部署：已部署到 `D:\SteamLibrary\steamapps\common\PAL\自动计时器\计时器3.36.5`。覆盖 `Pal98Timer.exe` 与 `plugins\PAL98.FujiaShouji.tpg`，新增隐藏文件 `plugin_auth`。覆盖前备份到 `backup-before-yuhang-plugin-auth-20260703-135000`。新 `.tpg` 为本地调试未签名包，内嵌 DLL MD5 `2D5744D39ACA9E1490FB2E7860CC77CC`。

2026-07-03 本轮测试部署补丁：为避免右下角换行，富甲插件输出从“余杭四大神器：状态  钱：N  道具：N”压缩为“四大神器：状态 钱：N 道具：N”。已重新部署 `D:\SteamLibrary\steamapps\common\PAL\自动计时器\计时器3.36.5\plugins\PAL98.FujiaShouji.tpg`，覆盖前备份到 `backup-before-fujia-layout-fix-20260703-142010`。新 `.tpg` MD5 `4238F9E555614370C14168832736E2EB`，内嵌 DLL MD5 `1F4E6DC002A309E941583E239A6E39B9`。

2026-07-03 本轮测试部署补丁：用户明确不能更新 exe 布局后，已回滚测试目录 `Pal98Timer.exe` 到 MD5 `F373BAE2D9FF2B8BFC99C4C1893A3ABD`，并撤销源码 `GEX.cs` 布局/no-wrap 改动。仅保留插件包文本紧凑方案，输出为“神器:状态 钱N 道具N”并在末尾加全角空格尝试把右对齐文本视觉上向中间移动。已重新部署 `D:\SteamLibrary\steamapps\common\PAL\自动计时器\计时器3.36.5\plugins\PAL98.FujiaShouji.tpg`，覆盖前备份到 `backup-before-fujia-compact-text-20260703-144502`。新 `.tpg` MD5 `34DEC6BFD3561E6B1FF802965F83A849`，内嵌 DLL MD5 `930A9F19F9F863E7AF4779AC1923A67D`。

2026-06-24 本轮未发布改动：修复暂停状态下重置计时器后，重开游戏仍默认暂停的问题；`TimerCore.Reset()` 现在清除 `IsUIPause`，`PTimer.Reset()` 同步把内部 `_Status` 复位为 stopped，确保 reset 后下一次 `MT.Start()` 可重新启动。新增 `.ai/reset_clears_ui_pause_regression_check.py` 覆盖该语义。

2026-06-23 本轮未发布改动：节点音效配置支持每条音频独立音量，`sound_config.txt` 旧 `启用|文件路径` 格式保持可读，保存后升级为 `启用|音量|文件路径`；另在 `agent-setting` 新增 `pal98-paltimer-plugin-development` Skill，用于沉淀 PalTimer `.tpg` 插件/API/包格式知识。

task-114 继续推进：gated automation snapshot file export、no-BOM fix、automation-only `--automation-non-sequential-splits` 与 `--automation-accept-pal98-base-title` 已合并到 `master`。真实 same-run gate v4 证明 route outcome / real 1x validation / same-run provenance / source gate 均通过，且两个 automation flag 已到达 PalTimer，但最后写出的 snapshot 仍停在 `core_loaded`，没有后续 `checkpoint` / `run_end` 证据。当前分支新增 automation-only 低频 tick snapshot：后台 tick 后每 500ms 最多刷新一次 automation snapshot，用于区分“线程未启动 / tick 未执行”和“tick 执行但 attach 或 split 未达成”；默认普通用户路径仍不变，不启动 HTTP/socket，不读取或输出 cloud ID，不使用 OBS socket，不改变 `OpenProcess` / `ReadProcessMemory` / `WriteProcessMemory` / 节点判定。

task-113 已在当前分支继续推进：主程序 manifest 已从 `requireAdministrator` 改为 `asInvoker` 并推送；随后新增 PAL98/PAL98DX9/PAL98UNHAPPY 三内核 `OpenProcess` 失败时的权限处理。当前语义是：只有 PAL.exe 以管理员权限运行、而 PalTimer 非管理员导致错误码 5 时，弹出短提示“PAL.exe是管理员权限运行，计时器需要重启用管理员权限才能运行”，用户确认后 PalTimer 直接关闭；PAL.exe 和 PalTimer 同为管理员、或 PAL.exe 普通而 PalTimer 管理员时不额外提示。

task-111 / task-112 已完成代码层和构建验证，等待或已经进入 checkpoint commit / push。主要工作集中在：
- 音效系统（节点音效配置、音效开关快捷键）
- 稳定性修复（窗口句柄刷屏、MP3 播放兼容）
- 三个高相似内核（PAL98/PAL98DX9/PAL98UNHAPPY）的功能同步
- UI 可读性：透明度功能当前语义为“背景图透明度”，文字、按钮和计时数字保持不透明。
- 工具链：项目目标框架已从 .NET Framework 4.0 / 4.5 升级到 .NET Framework 4.7.2，便于 VS2026/MSBuild 18 编译，并保留 Win7 SP1 运行时目标。

---

## 3. 已完成内容

### 未发布（2026-07-27）
- 不欢乐模式内部身份统一为 `PAL98UNHAPPY`；旧本地最佳线仅在新文件不存在时复制迁移，旧文件保留
- `PAL98UNHAPPY.*.tpg` 成为不欢乐模式专用插件前缀，仍兼容 `PAL98.*.tpg`，不自动加载 `PAL98DX9.*.tpg`
- 新增 `.ai/pal98unhappy_identity_regression_check.py`；Release|x64 构建通过
- PAL98DX9/PAL98UNHAPPY 新 SRPG 携带飞行旗完整快照、存在标志和 SHA-256；旧 SRPG 通过 `OptionalField` 保持“不处理 sidecar”语义
- 导入新 SRPG 时先验证完整快照；目标 sidecar 存在则生成时间戳备份并原子替换，源快照明确不存在时把目标移到备份；服务器代码不变
- 新增 `docs/SRPG_FLYING_FLAG_SIDECAR_RULE.md` 和 `.ai/srpg_flying_flag_sidecar_regression_check.ps1`；新旧 BinaryFormatter 双向兼容、备份恢复行为及 Release|x64 构建通过
- PAL98/PAL98DX9/PAL98UNHAPPY 从接力或云 SRPG 恢复 `TotalMonsterCount`；旧 SRPG 无字段时不覆盖本地值，无需修改服务器或 `PALCloud.dll`
- 新增 `.ai/srpg_monster_count_regression_check.ps1`；字段存在、零值及旧包缺字段三种行为 harness 通过
- PAL98/PAL98DX9 的“水灵珠”节点不再由 `0x109` 物品单独触发；必须先观察到动态解析的正常获取或大理祭坛返回对话并离开该对话，再结合背包水灵珠完成节点
- 新增 `Pal98WaterSpiritPearlSplit.cs` 与 `.ai/water_spirit_pearl_split_regression_check.ps1`；当前补丁资源解析为 `FFFF 2EE4`/脚本 `886E` 和 `FFFF 2AB1`/脚本 `773F`，重复/损坏资源按 fail-closed 处理，70ms Observe 路径无文件 I/O

### 未发布（2026-07-03）
- 富甲插件右下角显示新增“四大神器/神器”状态，测试包当前紧凑输出顺序为“神器:状态 钱N 道具N”
- 计时开始、节点初始化和重置后清空四大神器观察状态；上船节点坐标触发时冻结为已收集或未收集
- 目标物品 ID：紫金丹 `0x111`、土灵珠 `0x10B`、六神丹 `0x11E`、布包 `0x10F`
- 新增结构性回归脚本 `.ai/fujia_yuhang_artifacts_regression_check.py`
- 最新计时器新增隐藏本地调试开关：exe 同目录 `plugin_auth` 文件包含 `allow_unsigned_plugins=1` 时允许未签名插件；默认无文件或内容不匹配时仍按原签名授权规则拒绝
- 插件管理器对被隐藏开关放行的未签名插件显示“调试放行”，用于区分真实签名有效和本地调试放行
- 新增结构性回归脚本 `.ai/plugin_authorization_bypass_regression_check.py`

### 未发布（2026-06-24）
- 修复暂停状态下重置计时器后，下一次游戏开始仍默认暂停的问题
- `TimerCore.Reset()` 清除 `IsUIPause`，`PTimer.Reset()` 将 `_Status` 复位为 stopped，避免从暂停或运行状态 reset 后下一次 `Start()` 被旧状态阻挡
- 新增结构性回归脚本 `.ai/reset_clears_ui_pause_regression_check.py`

### 未发布（2026-06-23）
- 节点音效配置支持每条音频独立音量：5 类节点/通关提示音和音效开关的打开/关闭提示音均可设置 0-100 音量
- `SoundConfig` 播放路径在 MCI alias 打开后通过 `setaudio ... volume` 设置音量；MP3 的 WMP COM 回退路径同步设置 `settings.volume`
- 配置文件向后兼容：旧 `启用|文件路径` 读取为 100 音量，新保存格式为 `启用|音量|文件路径`
- 新增结构性回归脚本 `.ai/sound_config_volume_regression_check.py`
- 在 `D:\Workspace\agent-setting` 新增 `pal98-paltimer-plugin-development` Skill，记录 PalTimer 插件 API、`.tpg` 包格式、加载规则和两个 PAL98 插件样例

### v3.36.5 (2026-05-15)
- 修复 PAL98 / PAL98DX9 / PAL98UNHAPPY 香蕉树反作弊暂停叠加 F9 手动暂停后，拿到香蕉仍无法恢复计时的问题
- 调整云存档、云读档、接力-存档、接力-接盘暂停语义：无论操作前是否暂停，操作完成后都保持暂停，由用户手动恢复计时

### v3.36.4 (2026-05-10)
- 跳图路线（非顺序节点推进）改为功能开关，默认关闭，功能菜单中可手动开启
- 新增"最终通关"音效配置，通关时自动播放

### v3.36.3 (2026-05-10)
- 修复节点音效试听无反馈（添加 MCI 错误检查和提示）
- 修复 MP3 播放兼容性（MCI mpegvideo 不可用时自动回退到 Windows Media Player COM）
- 修复音效配置窗口快捷键提示文字被截断
- 回退主计时居中改动，恢复为固定偏移+左对齐布局（居中方案毫秒位置异常）

### v3.36.2 (2026-05-10)
- 节点完成快慢提示音效配置（`SoundConfig.cs` + `SoundConfigForm.cs`）
- 音效开关快捷键及开关提示音配置
- 土灵符实时战斗检测
- 修复游戏关闭后反复弹出"窗口句柄无效"
- 修复 F11 改键功能在部分旧版改键器上失效
- 非顺序节点推进（跳图路线支持）
- 解决方案移除 x86 平台，统一 Release|x64

### 更早（见 docs/TODO-pal98-dx9-updates.md）
- 对象浏览器检测修复
- 夜行衣实时检测
- 暂停后战斗时间不暂停
- 怪物统计不准确
- DX9 版本自动复制记录线
- 不欢乐模式支持
- 2025 新补丁版本适配
- 火/血/剑实时读取

---

## 4. 未完成内容

### 本轮已修复，待实机验证

- **PAL98/PAL98DX9 水灵珠双路线节点**：资源动态解析、状态机 harness 与构建均通过。仍需分别实机验证：开局已有水灵珠不跳节点；正常进入十年前在“得到水灵珠”对话结束且物品存在后跳；回梦无痕跳过十年前后在大理祭坛“小李子”对话结束且物品存在后跳；F10 reset 后旧门闩不残留。PAL98UNHAPPY 未改。
- **SRPG 携带 `PalDrawCard.FlyingFlagAll.v1.bin`**：代码、行为 harness 和构建验证完成。仍需在真实 PAL98DX9/PAL98UNHAPPY 中分别验证 sidecar 存在/不存在的接力与云存读档，确认时间戳备份、重启提示、重启后飞行旗位置和 `1.RPG` 一致；本轮没有部署到游戏目录。

### 高优先级 - 已修复，待实机验证

- **节点音效每条音频独立音量 — 已完成代码层和构建验证，待 Human 听音确认**
  - 修复内容：`SoundConfig.cs` 为每个 `SoundTriggerType` 和开关提示音保存 0-100 音量，MCI/WMP COM 播放和试听均使用对应音量。
  - 配置兼容：旧 `sound_config.txt` 的 `启用|文件路径` 和无分隔旧路径格式仍可读取，默认音量 100；保存后写为 `启用|音量|文件路径`。
  - UI：`SoundConfigForm.cs` 增加每行音量数字框，窗口加宽避免路径、音量、浏览、试听控件重叠。
  - 安全边界：未修改节点判定、计时开始/停止、暂停、反作弊、保存/读档、云存读档、路线节点推进、`OpenProcess` / `ReadProcessMemory` / `WriteProcessMemory`。
  - 验证：`.ai/sound_config_volume_regression_check.py`、`git diff --check`、`Release|x64` 构建通过。
  - 待实机：分别用 wav/mp3 验证节点提示音和开关提示音在音量 0、30、100 时试听和运行中播放音量符合预期。

- **task-114：PalTimer automation snapshot file export v0 — 已完成代码层和构建验证，待 AutoTest 联调**
  - 背景：Pal98AutoTest `route-bootstrap-paltimer-snapshot-gate` 已能消费结构化 `pal98.paltimer.snapshot`，但 PalTimer 没有稳定外部导出接口；fixture snapshot 已被 AutoTest block，不能再作为 same-run evidence。
  - 修复内容：`Program.cs` 解析 `--automation-snapshot-export <path>` 与 `--automation-snapshot-run-id <RUN_ID>`；`GForm.cs` 在 flag 启用时写 automation snapshot；`TimerCore.cs` 新增 `BuildAutomationSnapshotJson()` 输出 AutoTest envelope，并保留 `GetTimerJson()` 为 `paltimer_internal`。
  - 触发点：flag 启用后，加载 core 写 `core_loaded` snapshot；节点推进写 `checkpoint` snapshot；最终通关写 `run_end` snapshot。无 flag 时不写文件。
  - same-run route 说明：如果 PalTimer 在 route 中段才启动，顺序 split 模式会停在首个 split；本分支增加 `--automation-non-sequential-splits`，只在 automation snapshot export 启用时临时开启非顺序 split 捕捉，避免修改用户持久化 `skip_node`。
  - attach/title 说明：真实 same-run gate v3 证明非顺序 split flag 已生效，但 PalTimer 未 attach / 未读到 PAL.exe 状态。当前分支增加 `--automation-accept-pal98-base-title`，只在 automation snapshot export 启用时允许 PAL98DX9 / PAL98UNHAPPY 接受基础 PAL98 窗口标题，并写出 `pal_process_attach` 诊断；无 flag 时仍要求 DX9 标题确认。
  - 输出边界：`source=paltimer_automation_export`；不读/写 cloud ID，不接 OBS socket，不开 HTTP listener，不修改云上传逻辑，不改变 `ReadProcessMemory` / `WriteProcessMemory` / OpenProcess / 节点判定。
  - 验证：`Release|x64` 构建通过；`git diff --check`；`.ai/automation_snapshot_export_regression_check.py`、`.ai/banana_pause_resume_regression_check.py`、`.ai/cloud_save_load_pause_regression_check.py`、`.ai/pal_open_process_permission_regression_check.py` 均通过。
  - 待验证：AutoTest 侧 source whitelist 接受 `paltimer_automation_export`；真实同跑时确认导出的 split 名称、timer_status、timer_time 与 route-bootstrap gate 对齐。

- **task-113：取消 PalTimer 主程序默认 UAC 提权并提示权限不匹配 — 已完成代码层和构建验证**
  - 背景：`pal98autotest` 从普通进程启动部署版 `Pal98Timer.exe` 时遇到 `WinError 740`，说明当前主程序要求提升权限。
  - 修复内容：当前分支 `codex/paltimer-uac-asinvoker` 已修改 `Pal98Timer/Properties/app.manifest`，将主程序有效 `requestedExecutionLevel` 从 `requireAdministrator` 改为 `asInvoker`。
  - 后续修复：`Kernel32.OpenProcess` 开启 `SetLastError=true`；PAL98 / PAL98DX9 / PAL98UNHAPPY 三内核在 `OpenProcess` 返回 0 时只提示一次权限失败信息，`ERROR_ACCESS_DENIED=5` 时显示短提示“PAL.exe是管理员权限运行，计时器需要重启用管理员权限才能运行”。
  - 权限约束：普通速通和 pal98autotest 自动化测试推荐 PAL.exe / PalTimer 都普通权限运行；如果 PAL.exe 因其他补丁必须管理员运行，则 PalTimer 也需要人工以管理员权限启动。
  - 退出语义：确认短提示后 PalTimer 直接关闭，不再弹出二次退出确认；再次普通权限启动且 PAL.exe 仍为管理员权限时会继续提示并退出。
  - 安全边界：未修改 `.csproj`、KeyChanger 启动逻辑、`ReadProcessMemory` / `WriteProcessMemory`、内存地址、节点判定、暂停语义、x64 发布配置或真实部署目录。
  - 验证：`Pal98Timer.sln` `Release|x64` 构建通过；用 Windows SDK `mt.exe` 抽取 `Pal98Timer/bin/x64/Release/Pal98Timer.exe` manifest，确认有效节点为 `level="asInvoker"`；从普通 PowerShell 启动产物不再出现 `WinError 740`。
  - 结构检查：新增 `.ai/pal_open_process_permission_regression_check.py`，确认三套 PAL98 内核都有权限失败提示 guard，且不再直接把 `OpenProcess` 结果塞进 `PalHandle`。
  - 注意：本次烟测关闭 PalTimer 时 `CloseMainWindow()` 未让进程自行退出，已强制结束本次启动的进程；未发现残留 `Pal98Timer` / `KeyChanger` 进程。
  - 待实机：普通权限 PAL98DX9/PAL98/PAL98UNHAPPY 读内存、F9/F10/F11、KeyChanger、云验证/云存读档、关闭游戏后不刷屏；管理员权限 PAL.exe + 普通权限 PalTimer 时确认短提示后直接退出；管理员权限 PAL.exe + 管理员权限 PalTimer 时确认不提示。
  - 调查文档：`docs/paltimer-uac-keychanger-investigation-20260612.md`

### 高优先级 - 已修复，待构建环境和实机验证

- **task-112：计时器项目升级到 .NET Framework 4.7.2 — 已完成构建验证**
  - 背景：本机卸载 VS2019 后，VS2026/MSBuild 18 无法找到 `.NETFramework,Version=v4.0` 引用程序集；用户因 Win7 SP1 兼容目标确认选择 4.7.2。
  - 修复内容：主程序、插件和辅助项目 `.csproj` 统一到 `v4.7.2`；已有 `app.config` 的 supportedRuntime SKU 同步为 `.NETFramework,Version=v4.7.2`。
  - 验证：升级前 Release 编译因 `MSB3644` 失败；升级后 `Pal98Timer.sln` Release `Any CPU` 编译通过，`Pal98TimerOBSPlugin.csproj` Release `AnyCPU` 单独编译通过。
  - 已知警告：仍有既有架构不匹配、未使用变量和 `AppDomain.GetCurrentThreadId()` 过时警告；不是本次升级阻塞。
  - Task Context：`PAL98_AI_WORKSPACE/.ai/task_contexts/task-112-paltimer-net472-upgrade.md`

- **task-111：透明度功能改为背景图透明度 — 已完成构建验证，待 Human 视觉/OBS 验收**
  - 背景：用户要求不新增“背景图透明度”，而是直接把现有“透明度”功能改成背景图透明度；整体窗口透明度可由 OBS 截取框处理。
  - 修复内容：菜单文案改为“背景透明度”；`UpdateTransparency()` 固定 `Form.Opacity=1.0`；背景图绘制通过 alpha 处理，文字、按钮和计时数字保持不透明。
  - 验证：随 task-112 使用 VS2026/MSBuild 18 完成 Release 编译。
  - 待验证：Human 打开计时器确认背景透明度滑动效果、文字可读性和 OBS 截取效果。
  - Task Context：`PAL98_AI_WORKSPACE/.ai/task_contexts/task-111-paltimer-background-image-opacity.md`

- **task-015：云存档 / 云读档 / 接力存读档完成后永远保持暂停 — 已修复，待验证**
  - 新要求：无论云存档或云读档之前是否暂停，操作完成后都保持 `IsUIPause == true`，由用户手动恢复计时。
  - 用户确认：`接力-存档` / `接力-接盘` 也采用同一语义，操作完成后保持暂停。
  - 修复内容：PAL98 / PAL98DX9 / PAL98UNHAPPY 三个内核已移除云/接力存读档路径中的 `wasPausedBefore` 自动恢复逻辑，保留操作开始时 `SetUIPause(true)`。
  - 回归检查：新增 `.ai/cloud_save_load_pause_regression_check.py`；修复前失败，修复后通过。
  - 构建状态：task-112 后本机 VS2026/MSBuild 18 可完成 Release `Any CPU` 编译；仍需按发布目标决定是否补测 Release|x64。
  - 实机状态：仍需人工验证云存档、云读档、接力存档、接力接盘操作完成后保持暂停。
  - Task Context：`PAL98_AI_WORKSPACE/.ai/task_contexts/task-015-paltimer-cloud-save-load-always-pause.md`

- **task-014：香蕉树反作弊暂停/恢复 Bug 修复**
  - 修复内容：PAL98 / PAL98DX9 / PAL98UNHAPPY 三个内核在 `HasStartGame()` 之前增加 `IsInUnCheat` guard，已进入反作弊暂停时先调用 `CheckCheatEnd()`，确保 F9 暂停期间拿到香蕉也能清除反作弊暂停状态。
  - 安全边界：未修改 `GForm.cs`、`TimerCore.cs`、README、docs、工程文件；未在 F9 暂停期间直接调用 `MT.Start()`。
  - 回归检查：新增 `.ai/banana_pause_resume_regression_check.py`；修复前失败，修复后通过。
  - 构建状态：task-112 后本机 VS2026/MSBuild 18 可完成 Release `Any CPU` 编译；仍需按发布目标决定是否补测 Release|x64。
  - 实机状态：仍需人工验证“站到香蕉树 -> F9 暂停 -> 拿香蕉 -> F9 恢复”路径。

- **task-013：香蕉树反作弊暂停/恢复 Bug — LIKELY_BUG**
  - 诊断结论：确认存在 Bug。当玩家在反作弊窗口期内手动暂停（F9）再拿香蕉，`CheckCheatEnd()` 因 `HasStartGame()` 返回 false 被跳过，`IsInUnCheat` 永远无法清除，计时器永久停止。
  - 影响范围：PAL98 / PAL98DX9 / PAL98UNHAPPY 三个内核均受影响
  - 根因：`HasStartGame()` 在 `IsPause==true` 时返回 false，导致反作弊检查块被整体跳过，`CheckCheatEnd()` 无法执行
  - 推荐修复方案：将 `CheckCheatEnd()` 从 `HasStartGame()` 块中移出，确保拿到香蕉时无论暂停状态都能清除 `IsInUnCheat`
  - 报告位置：`.ai/BANANA_PAUSE_RESUME_BUG_REVIEW.md`
  - 已进入 task-014 修复。

### 已知问题
- **主计时居中**：曾在 v3.36.2 中尝试用 `Graphics.MeasureString` 实现居中，但因文字居中对齐（`sfCC`）导致 `*` 号切换时毫秒位置跳动，v3.36.3 已回退。当前使用固定偏移+左对齐布局，部分用户电脑上主时间视觉不完全居中。需要后续重新设计居中方案。

### 后续优化方向（P2）
- 抽取 PAL98/PAL98DX9/PAL98UNHAPPY 共用模块，降低三处同步成本
- 借鉴 LiveSplit / livesplit-core 的架构扩展（Split 状态模型、Layout/Rendering 分离、Auto Splitter 抽象、多对比线）

---

## 5. 当前最重要的下一步

新的 AI 接手后，优先做以下事情：

1. 实机验证 PAL98/PAL98DX9 水灵珠节点三场景：开局已有水灵珠不跳；正常十年前路线在“得到水灵珠”对话结束后跳；回梦无痕路线在大理祭坛“小李子”对话结束后跳，并补测 F10 reset 清门闩。
2. 实机验证 PAL98/PAL98DX9/PAL98UNHAPPY 新 SRPG：先制造可辨识的撞怪数并分别做一次接力、云存读档，确认导入后恢复源快照计数；再用缺少 `TotalMonsterCount` 的旧 SRPG 确认本地计数不变。PAL98DX9/PAL98UNHAPPY 同时覆盖源 sidecar 存在与不存在，确认目标时间戳备份、重启提示，并在重启 PAL.exe 后核对飞行旗与 RPG 属于同一快照。
3. 实机打开不欢乐模式，确认旧 `best仙剑98DX9不欢乐模式.txt` 会复制为 `bestPAL98UNHAPPY.txt`，专用插件前缀和成绩导出文件名均使用 `PAL98UNHAPPY`。
4. 音效独立音量：Human 打开“节点音效配置”，分别为节点提示音、最终通关音效、音效打开/关闭提示音设置不同音量，测试 wav/mp3 的试听和真实节点触发播放。
5. PalTimer 插件 Skill：如果继续插件开发，用 `D:\Workspace\agent-setting\projects\Pal98Works\skills\pal98-paltimer-plugin-development` 作为唯一事实来源；实机 `.tpg` 包默认只读检查，不要绕过签名。
6. task-114：让 Kimi/Codex 复核 automation tick snapshot PR；合并后用真实 route-bootstrap + PalTimer 导出文件重跑 same-run snapshot gate v5，并在 compact review 中检查最后 snapshot 的 `export_trigger`、`pal_process_attach`、`split_reached`、`single_run_evidence_chain_confirmed`。
7. task-113：在普通权限 PAL98DX9/PAL98/PAL98UNHAPPY 实机环境验证读内存、F9/F10/F11、KeyChanger、云功能和关闭游戏生命周期；补测 PAL.exe 管理员 + PalTimer 普通时短提示后退出、PAL.exe 管理员 + PalTimer 管理员时不提示、PAL.exe 普通 + PalTimer 管理员时不提示。
8. task-111 需要 Human 打开计时器实测：背景图透明度变化时，文字、按钮和计时数字保持不透明；OBS 截取框可按直播需求另行调透明度。
9. task-112 需要发布前确认 Win7 SP1 目标机已安装 .NET Framework 4.7.2 runtime。
10. task-014 和 task-015 代码层修复已完成，仍需实机验证。
11. 实机测试 task-015：云存档、云读档、接力存档、接力接盘操作完成后均保持暂停，用户手动恢复计时。
12. 实机测试 task-014：站到香蕉树→F9暂停→拿香蕉→F9恢复，确认计时器能恢复。
13. 补测普通路径：站到香蕉树→拿香蕉自动恢复；普通 F9 暂停不应被误启动。
14. 如需发布版本，再决定是否更新 README.md 版本记录。

---

## 6. 关键文件说明

| 文件 | 作用 |
|---|---|
| `README.md` | 项目说明、快捷键、版本更新记录 |
| `docs/TODO-pal98-dx9-updates.md` | 详细开发计划、验收清单 |
| `.ai/resume.md` | 当前交接文件（本文件） |
| `.ai/DEFENDER_FALSE_POSITIVE_DEPLOYMENT_NOTE.md` | Windows Defender 拦截 / 误报部署知识 |
| `.ai/decisions.md` | 长期技术决策 |
| `.ai/env.md` | 多电脑环境差异 |
| `Pal98Timer/GForm.cs` | 主窗体、快捷键处理、版本号 `CurrentVersion` |
| `Pal98Timer/GEX.cs` | GDI 主界面绘制、布局（BuildRects/DrawMainTimer） |
| `Pal98Timer/TimerCore.cs` | 通用节点推进、CurrentStep、跳节点、成绩导出 |
| `Pal98Timer/Pal98WaterSpiritPearlSplit.cs` | 水灵珠节点资源解析、脚本状态只读与双路线门闩 |
| `Pal98Timer/仙剑98柔情DX9.cs` | DX9 内核、进程检测、物品/战斗统计、节点定义 |
| `Pal98Timer/仙剑98柔情.cs` | 原 98 内核 |
| `Pal98Timer/仙剑98柔情不欢乐模式.cs` | 不欢乐模式内核 |
| `Pal98Timer/SoundConfig.cs` | 音效配置单例、MCI/WMP 播放逻辑 |
| `Pal98Timer/SoundConfigForm.cs` | 音效配置窗口 UI |
| `Pal98Timer/KeyChangerDel.cs` | 改键功能、F11 改键开关 |

---

## 7. 最近改动

### 2026-07-27 会话（水灵珠节点双路线剧情门闩）

- PAL98/PAL98DX9 将“背包有水灵珠”改为“先观察到目标剧情对话、离开对话、背包有水灵珠”三段判定；开局随机物品不再触发
- 正常路线匹配“得到水灵珠”，跳过十年前路线匹配回到大理祭坛后的“糟．．希望灵儿不会有事才好”；不依赖固定对话 ID，而是从最终 `M.MSG` 文本和 `SSS.MKF` 唯一 `FFFF` 引用解析
- 每次连接新 PAL.exe 只解析一次约 0.8MB 资源；70ms OnTick 只沿 `0x428000 -> p1 + 0x500 -> p2` 读取 8 字节脚本状态，并沿用既有物品栏读取
- 资源缺失、越界、重复文本或重复脚本引用时 fail closed，节点保持未触发并显示一次解析错误；F10 reset 和游戏进程断开均清门闩
- 新增行为 harness，覆盖动态 ID、当前实机资源、同 PID 缓存、换 PID 重解析、开局已有物品、正常路线、回梦无痕路线、无关对话、reset、重复/损坏资源
- 未修改 PAL98UNHAPPY、PALDLL_DX9、CunCunExpress、云存读档、sidecar、暂停、反作弊、OBS、写内存或服务器协议

### 2026-07-27 会话（SRPG 飞行旗完整快照 + 不欢乐模式身份统一）

- 新增 `Pal98Timer/SRPGSidecarTransport.cs`：捕获 sidecar 存在/不存在状态，存在时携带原始字节和 SHA-256；导入时验证版本、长度和哈希
- 扩展共享 `SRPGobj`，新增字段全部使用 `OptionalField`；实测新程序可读旧 SRPG，旧程序也可忽略新字段读取新 SRPG
- PAL98DX9/PAL98UNHAPPY 的接力与云存档共用相同客户端封装；服务器仍只上传/下载原 `.bin`，没有协议或服务器改动
- 导入完整快照要求 PAL.exe 已运行以确认游戏目录；覆盖前生成 `.paltimer-backup-YYYYMMDD-HHmmssfff`，源无 sidecar 时把目标移到备份
- PALDLL 不做热重载；新 SRPG 导入后提示保持计时器开启、重启 PAL.exe、再读取进度一，旧 SRPG 保持原提示和本地 sidecar
- 新增规则文档与行为回归 harness，覆盖存在、明确不存在、旧包、哈希损坏、覆盖备份和空快照备份
- 修改 `Pal98Timer/仙剑98柔情不欢乐模式.cs`：`CoreName` 统一为 `PAL98UNHAPPY`，使最佳线、成绩导出、插件前缀及后续云标识与 README 的公开英文名一致
- 兼容旧本地最佳线：仅在新文件不存在时复制 `best仙剑98DX9不欢乐模式.txt` 为 `bestPAL98UNHAPPY.txt`，不删除旧文件、不覆盖新文件
- 仍先加载 `PAL98UNHAPPY.*.tpg`，再兼容加载 `PAL98.*.tpg`；未把 `PAL98DX9.*.tpg` 自动注入不欢乐内核
- 新增 `.ai/pal98unhappy_identity_regression_check.py`，结构性检查规范身份、旧最佳线迁移和插件回退链路
- 未修改计时、暂停、反作弊、存读档、节点判定、OBS、进程匹配或任何内存地址；不欢乐修改器只在进程内注入脚本且没有稳定文件标志，本轮不增加猜测性自动检测

### 2026-07-03 会话（富甲插件增加四大神器状态 + 插件授权本地调试开关）

- 修改 `PAL98.FujiaCaishen/Main.cs`：`GetResult()` 在钱/道具前增加“四大神器：已收集/未收集”
- 沿用富甲插件既有 `0x428000 -> BaseAddr + 0x768` 背包槽读取链路，检测紫金丹、土灵珠、六神丹、布包任意一个是否曾在上船前出现
- 复刻 PAL98/PAL98DX9 的上船节点坐标条件：`Area=6`，`X=1072±32`，`Y=1080±16`，命中时冻结显示状态
- 在 `OnLoad`、`Start`、`InitCheckPoints` 时清空状态；计时器重置路径会重载插件并重新初始化
- 新增 `.ai/fujia_yuhang_artifacts_regression_check.py`，结构性检查输出前缀、目标物品 ID、上船冻结和重新统计语义
- 修改 `Pal98Timer/TimerCore.cs`：`.tpg` 加载条件保留默认签名检查；仅当 exe 同目录隐藏文件 `plugin_auth` 包含 `allow_unsigned_plugins=1` 时，允许本地调试加载未签名插件
- 修改 `Pal98Timer/PluginMgrForm.cs`：未签名插件被隐藏开关放行时，签名列显示“调试放行”，行色按可加载状态显示
- 新增 `.ai/plugin_authorization_bypass_regression_check.py`，结构性检查隐藏文件名、精确开关行、默认拒绝和插件管理器提示
- 测试部署到 `D:\SteamLibrary\steamapps\common\PAL\自动计时器\计时器3.36.5`：覆盖前备份 `Pal98Timer.exe` 和 `plugins\PAL98.FujiaShouji.tpg` 到 `backup-before-yuhang-plugin-auth-20260703-135000`，部署最新 `Pal98Timer.exe`、隐藏 `plugin_auth`、未签名调试版富甲 `.tpg`
- 右下角排版补丁：输出文案压缩为“四大神器：状态 钱：N 道具：N”，重新部署前备份旧调试 `.tpg` 到 `backup-before-fujia-layout-fix-20260703-142010`
- exe 布局改动已按用户要求回滚：不得改 `GEX.cs` 底部 `BL/BR` 布局；测试目录 exe 回滚为 MD5 `F373BAE2D9FF2B8BFC99C4C1893A3ABD`。当前只用插件输出“神器:状态 钱N 道具N”解决一行显示
- 安全边界：未改 PAL98/PAL98DX9/PAL98UNHAPPY 节点判定、计时暂停语义、云/接力存读档或 OBS 展示；插件授权放行默认关闭，只对最新计时器 exe 生效，未修改 3.36.4 现有发布包

### 2026-06-24 会话（reset 清除 UI 暂停状态）

- 修改 `Pal98Timer/TimerCore.cs`：`TimerCore.Reset()` 清除 `IsUIPause`，避免 F9/UI 暂停后重置计时器仍把下一局判成 UI 暂停
- 修改 `Pal98Timer/TimerCore.cs`：`PTimer.Reset()` 把 `_Status` 复位为 `0`，确保从运行或暂停状态 reset 后下一次 `Start()` 都能重新启动 stopwatch
- 新增 `.ai/reset_clears_ui_pause_regression_check.py`，结构性检查 reset 会清 UI 暂停，并让秒表处于可重启状态
- 更新 `README.md`，在未发布说明中记录 reset 暂停残留修复
- 安全边界：未修改 PAL98/PAL98DX9/PAL98UNHAPPY 的内存地址、`OpenProcess` / `ReadProcessMemory` / `WriteProcessMemory`、节点判定、云/接力存读档后保持暂停语义或 OBS 展示
- 验证：新增 reset 回归、云/接力暂停回归、香蕉树暂停回归、权限提示回归、`git diff --check`、`Pal98Timer.sln Release|x64` 构建通过；构建仅有既有 warning。`.ai/automation_snapshot_export_regression_check.py` 当前仍会因 automation tick interval 已改为可配置、脚本仍匹配旧固定 500ms 字符串而失败，本轮未改 automation 脚本。

### 2026-06-23 会话（节点音效独立音量 + PalTimer 插件 Skill）

- 修改 `Pal98Timer/SoundConfig.cs`：新增每个节点音效触发类型的音量配置，打开/关闭提示音也各自有音量；MCI 播放调用 `setaudio` 设置 0-1000 音量，WMP COM 回退设置 `settings.volume`
- 修改 `Pal98Timer/SoundConfigForm.cs`：每条音频行新增 0-100 音量数字框；试听使用当前行音量；窗口加宽，避免路径、音量、浏览、试听控件重叠
- 新增 `.ai/sound_config_volume_regression_check.py`，检查配置解析、保存格式、MCI/WMP 音量路径和 UI 音量控件
- 更新 `README.md` 与 `docs/TODO-pal98-dx9-updates.md`，记录未发布音效音量功能和手动听音验证点
- 新增 `D:\Workspace\agent-setting\projects\Pal98Works\skills\pal98-paltimer-plugin-development`，包含 `SKILL.md`、`agents/openai.yaml` 和 `references/paltimer-plugin-api.md`
- 插件调查结论：实机 `PAL98.FujiaShouji.tpg` 启用，类名 `PAL98.FujiaCaishen`，右下角显示钱和道具；`PAL98.BestResShow.tpg` 禁用；两者都占 `BR`，同位置只会加载最先成功的插件
- 安全边界：未修改节点判定、计时/暂停、云存读档、OBS、`OpenProcess` / `ReadProcessMemory` / `WriteProcessMemory`，未修改真实游戏目录 `.tpg` 包
- 验证：`.ai/sound_config_volume_regression_check.py`、Skill `quick_validate.py`、PalTimer `git diff --check`、agent-setting `git diff --check`、`Pal98Timer.sln Release|x64` 构建通过；构建仅有既有 warning

### 2026-06-14 会话（task-114 automation tick snapshot）

- 真实 same-run gate v4 结果：route outcome / real 1x validation / same-run provenance / source gate 均通过；`--automation-non-sequential-splits` 与 `--automation-accept-pal98-base-title` 均到达 PalTimer，但 snapshot 仍停留在 `core_loaded`，`pal_process_attach.status=not_checked`
- 诊断校正：`not_checked` 只证明最后持久化的 snapshot 是 `core_loaded`，不能单独证明 `OnTick()` / `GetPalHandle()` 从未执行；因为 PalTimer 此前只在 `core_loaded` / `checkpoint` / `run_end` 写 snapshot
- 本分支新增 automation-only tick snapshot：`TimerCore.Start()` 在每次 `OnTick()` 后调用 `WriteAutomationTickSnapshotIfDue()`，仅当 `AutomationArgs.Current.Enabled` 时生效，每 500ms 最多写一次 `export_trigger=automation_tick`
- envelope 新增 `automation_tick_snapshot_interval_ms`，便于 AutoTest/Kimi 判断当前导出是否包含低频 tick 刷新能力
- 安全边界：未修改具体 PAL98 内核、OpenProcess/RPM/WPM、节点判定、云/OBS/计时核心；普通无 automation export flag 路径零副作用

### 2026-06-14 会话（task-114 automation PAL98 base-title fallback）

- 真实 same-run gate v3 结果：route outcome / real 1x validation / same-run provenance / source gate 均通过；`--automation-non-sequential-splits` 已到达 PalTimer，snapshot 中 `non_sequential_check_enabled=true`；但 snapshot 只有 `core_loaded`，`timer_status=not_started`，未写 `checkpoint` / `run_end`
- 源码分析结论：PAL98DX9 / PAL98UNHAPPY 的 `GetPalHandle()` 在普通路径必须等待窗口标题带 DX9 标识；same-run gate 使用的 v1.14 基线可能只暴露基础 PAL98 标题，导致 PalTimer 能启动但不 attach / 不读状态
- 本分支新增 automation-only flag：`--automation-accept-pal98-base-title`；只有同时启用 `--automation-snapshot-export` 时才允许 PAL98DX9 / PAL98UNHAPPY 接受基础 PAL98 标题，普通用户路径仍不变
- automation snapshot envelope 新增 `automation_pal98_base_title_fallback`，PAL98DX9 / PAL98UNHAPPY 新增 `pal_process_attach` 诊断字段，记录 attach status、Pal 进程数量、窗口标题、title match、automation acceptance、OpenProcess error code
- 验证：`Release|x64` 构建通过；`git diff --check`；automation snapshot / banana pause / cloud pause / OpenProcess permission 结构性回归脚本均通过
- 待验证：AutoTest 侧透传新 flag 后，重跑真实 same-run gate，期望 either `split_reached=true` for `上船`，或 compact snapshot 明确显示 attach gate 失败原因

### 2026-06-14 会话（task-114 same-run 中段 route split 捕捉）

- 真实 same-run gate v2 结果：PalTimer automation snapshot 已无 BOM，route-bootstrap 使用 `--route-kind speedrun` 后 route outcome / real 1x validation 均通过，same-run provenance 和 source gate 通过；但 PalTimer snapshot 只有 `core_loaded`，停在首个 split `见石碑`，未确认 `上船`
- 源码分析结论：`PAL98DX9` 默认顺序 split 模式会从 `见石碑` 开始；如果 PalTimer 在中段 route 启动，已经错过早期 split，就无法推进到后续 `上船`
- 本分支新增 automation-only flag：`--automation-non-sequential-splits`；只有同时启用 `--automation-snapshot-export` 时才临时打开非顺序 split 捕捉，不写回 `skip_node`
- automation snapshot envelope 新增 `non_sequential_check_enabled` 与 `automation_non_sequential_splits` 字段，方便 AutoTest/Kimi compact report 确认 split tracking mode
- 验证：`Release|x64` 构建通过；`git diff --check`；automation snapshot / banana pause / cloud pause / OpenProcess permission 结构性回归脚本均通过
- 待验证：AutoTest 侧透传新 flag 后，重跑真实 same-run gate，期望 `paltimer_official_split_evidence` 到达 `上船`，snapshot gate 返回 `single_run_evidence_chain_confirmed`，且 `can_treat_as_official_timing` 仍为 false

### 2026-06-14 会话（task-114 automation snapshot BOM follow-up）

- 修复真实 same-run gate 发现的 BOM 兼容问题：`GForm.WriteAutomationSnapshot()` 的 automation snapshot writer 从 `Encoding.UTF8` 改为 `new UTF8Encoding(false)`，后续导出的 `pal98.paltimer.snapshot` JSON 不再带 UTF-8 BOM
- 更新 `.ai/automation_snapshot_export_regression_check.py`，检查 gated writer 继续使用 no-BOM UTF-8
- 验证：`Release|x64` 构建通过；`git diff --check`；automation snapshot / banana pause / cloud pause / OpenProcess permission 结构性回归脚本均通过
- 仍需 AutoTest 侧兼容旧 BOM snapshot reader，并在 route-bootstrap gate 用正确 `--route-kind speedrun` 重跑真实 same-run gate

### 2026-06-14 会话（task-114 automation snapshot export v0）

- 新建并切换到 `codex/paltimer-automation-snapshot-export`
- 修改 `Pal98Timer/Program.cs`：解析 `--automation-snapshot-export <path>` 与 `--automation-snapshot-run-id <RUN_ID>`；无 export path 时 automation export 关闭
- 修改 `Pal98Timer/GForm.cs`：新增 `WriteAutomationSnapshot(trigger)`；只在 automation flag 启用时写文件；加载 core、节点推进、最终通关时分别写 `core_loaded`、`checkpoint`、`run_end`
- 修改 `Pal98Timer/TimerCore.cs`：新增 `BuildAutomationSnapshotJson()`，输出 AutoTest `pal98.paltimer.snapshot` envelope，`source=paltimer_automation_export`，保留 `GetTimerJson()` 为 `paltimer_internal`
- 修改 `PTimer`：新增只读 `IsRunning` 属性，用于 snapshot 的 `timer_status`
- 新增 `.ai/automation_snapshot_export_regression_check.py`：结构性检查 automation args、gated writer 和 AutoTest envelope
- 验证：`Release|x64` 构建通过；`git diff --check`；automation snapshot / banana pause / cloud pause / OpenProcess permission 四个结构性回归脚本均通过
- 未做：未启动 PalTimer、PAL.exe、Speedrun-Bot；未连接 OBS socket；未读取或输出 cloud ID；未做真实同跑 gate

### 2026-06-12 会话（task-113 权限提示后续）

- 修复实测问题：PalTimer 常驻、PAL.exe 后续以管理员权限启动时，旧逻辑可能在 `Process.HasExited` 检查异常后把 Pal.exe 当成不存在，导致没有权限提示
- 修改 PAL98 / PAL98DX9 / PAL98UNHAPPY 三内核进程过滤逻辑：`HasExited` 无法检查时仍保留 Pal.exe 候选，并先用 `CanOpenPalProcess()` 探测权限；打不开则走一次性权限提示
- 调整 `.ai/resume.md` 接手规则：Codex 普通开发不再要求读取 `CLAUDE.md`；只有审计 Claude Code 入口、迁移 agent-setting 或排查 Claude/Codex 指令冲突时才读
- 修改 `Pal98Timer/GForm.cs`：删除 PalTimer 自身管理员启动提示；当三内核返回短权限提示后，用户确认即关闭 PalTimer，并跳过二次退出确认
- 修改 `Pal98Timer/TimerCore.cs`：新增共享短提示 `PAL.exe是管理员权限运行，计时器需要重启用管理员权限才能运行`
- 修改 `Pal98Timer/Kernel32.cs`：`OpenProcess` 增加 `SetLastError=true`，新增 `ERROR_ACCESS_DENIED` 和 `GetLastWin32Error()`
- 修改 `Pal98Timer/仙剑98柔情.cs`、`Pal98Timer/仙剑98柔情DX9.cs`、`Pal98Timer/仙剑98柔情不欢乐模式.cs`：三内核打开 Pal.exe 进程失败时只提示一次；错误码 5 时使用短提示，覆盖 PalTimer 常驻后 PAL.exe 再以管理员权限启动/重启的场景
- 新增 `.ai/pal_open_process_permission_regression_check.py`：结构性检查三内核的权限提示 guard
- 验证：`Release|x64` 构建通过；`git diff --check`、`.ai/pal_open_process_permission_regression_check.py`、`.ai/banana_pause_resume_regression_check.py`、`.ai/cloud_save_load_pause_regression_check.py` 均通过
- 部署：已覆盖部署到 `D:\SteamLibrary\steamapps\common\PAL\自动计时器\计时器3.36.5`；备份目录为 `D:\SteamLibrary\steamapps\common\PAL\自动计时器\计时器3.36.5\.deploy-backup-20260612-204548`；按用户要求未部署/恢复 `ModuleAddrX86Delegate.exe` 和 `ModuleAddrX64Delegate.exe`
- 待验证：管理员权限 PAL.exe + 普通权限 PalTimer 时短提示后直接退出；管理员权限 PAL.exe + 管理员权限 PalTimer 不提示；普通权限 PAL.exe + 管理员权限 PalTimer 不提示；普通权限 PAL.exe + 普通权限 PalTimer 路径不受影响

### 2026-06-12 会话（task-113 实施）

- 新建并切换到 `codex/paltimer-uac-asinvoker`
- 修改 `Pal98Timer/Properties/app.manifest`：主程序有效 `requestedExecutionLevel` 从 `requireAdministrator` 改为 `asInvoker`
- 保持 KeyChanger、C# 运行时代码、工程文件、发布平台和真实部署目录不变
- 验证：`Release|x64` 构建通过；产物 manifest 抽取确认有效节点为 `asInvoker`；普通 PowerShell 启动 `Pal98Timer/bin/x64/Release/Pal98Timer.exe` 成功，不再出现 `WinError 740`
- 验证：`git diff --check`、`.ai/banana_pause_resume_regression_check.py`、`.ai/cloud_save_load_pause_regression_check.py` 均通过
- 待验证：PAL98DX9/PAL98/PAL98UNHAPPY 实机读内存、F9/F10/F11、KeyChanger、云功能，以及管理员权限游戏进程的边界表现

### 2026-06-12 会话

- 只读调查 PalTimer 默认 UAC 提权来源，并新增 `docs/paltimer-uac-keychanger-investigation-20260612.md`
- 结论：当前 `Pal98Timer.exe` 要求 UAC 的直接来源是 `Pal98Timer/Properties/app.manifest` 的 `requireAdministrator`；`KeyChanger.exe` 在 3.34.1 后已独立，源码中未发现 KeyChanger 自己强制 UAC 或主程序用 `runas` 启动 KeyChanger
- 建议后续新分支最小验证：主程序 manifest 改为 `asInvoker`，保持 Release|x64 和 KeyChanger 现状不变，再做普通权限启动、PAL98DX9 读内存、F9/F10/F11、KeyChanger 和云功能回归
- 本轮未修改 `.cs`、`.csproj`、manifest、发布包或真实部署目录

### 2026-05-18 会话

- 记录 Windows Defender 拦截 / 误报部署知识
- 新增 `.ai/DEFENDER_FALSE_POSITIVE_DEPLOYMENT_NOTE.md`
- 结论：`BadImageFormatException` 与 Defender `Behavior:Win32/DefenseEvasion.A!ml` 是两类问题；前者多为误用 `Any CPU` 输出导致 32/64 位不匹配，后者更可能来自未签名 exe + 低级键盘 hook + OpenProcess / ReadProcessMemory / WriteProcessMemory 等敏感行为组合
- 短期建议：测试包使用完整 `Release|x64` 目录，不裸发来源不明 exe；确认 hash 和来源可信后再临时允许
- 长期建议：代码签名、完整发布包、降低启动期可疑行为面，需单独开任务评估

- task-112 计时器项目升级到 .NET Framework 4.7.2
- 修改主程序、插件和辅助项目 `.csproj`：`TargetFrameworkVersion` 统一为 `v4.7.2`
- 修改已有 `app.config`：supportedRuntime SKU 从 `.NETFramework,Version=v4.0` 改为 `.NETFramework,Version=v4.7.2`
- 验证：升级前 VS2026/MSBuild 18 因缺 `.NETFramework,Version=v4.0` targeting pack 报 `MSB3644`；升级后 `Pal98Timer.sln` Release `Any CPU` 编译通过，`Pal98TimerOBSPlugin.csproj` Release `AnyCPU` 单独编译通过
- 备注：保留 Win7 SP1 兼容目标，但目标机需安装 .NET Framework 4.7.2 runtime；未升级到 4.8 / 4.8.1

- task-111 透明度功能改为背景图透明度
- 修改 `GForm.cs` / `GForm.Designer.cs`：菜单和输入框文案改为“背景透明度”，`UpdateTransparency()` 不再降低整个 Form 透明度
- 修改 `GEX.cs`：背景图绘制支持 alpha；背景图透明时文字、按钮和计时数字保持不透明
- 验证：随 task-112 编译通过
- 待验证：Human 视觉确认和 OBS 场景效果确认

### 2026-05-15 会话

- task-015 云存档 / 云读档完成后永远保持暂停任务已插入
- 新要求：无论云存档或云读档之前是否暂停，操作完成后都保持暂停，用户手动恢复计时
- 用户确认：接力存档 / 接力接盘也采用同一语义，操作完成后保持暂停
- 修复：三个 PAL98 主内核已移除 `wasPausedBefore` + `if (!wasPausedBefore) SetUIPause(false)` 自动恢复模式，云/接力存读档操作后保持暂停
- 新增 `.ai/cloud_save_load_pause_regression_check.py`：结构性回归检查，确认三套内核不再残留云/接力操作后的自动恢复暂停逻辑
- 验证：云/接力暂停回归检查通过；task-014 香蕉树回归检查仍通过；MSBuild Release|x64 因本机缺 .NET Framework 4.0 targeting pack 报 `MSB3644`，未完成编译验证
- 输出：`PAL98_AI_WORKSPACE/.ai/task_contexts/task-015-paltimer-cloud-save-load-always-pause.md`

- 版本号更新为 v3.36.5
- 修改 `GForm.CurrentVersion`、`AssemblyVersion`、`AssemblyFileVersion` 为 `3.36.5`
- README 新增 v3.36.5 更新说明，记录香蕉树/F9 恢复修复和云/接力操作后保持暂停

- task-014 香蕉树反作弊暂停/恢复 Bug 修复完成（代码层）
- 修改 `Pal98Timer/仙剑98柔情.cs`、`Pal98Timer/仙剑98柔情DX9.cs`、`Pal98Timer/仙剑98柔情不欢乐模式.cs`：在 `HasStartGame()` 之前增加 `IsInUnCheat` 检查并调用 `CheckCheatEnd()`，让手动暂停期间拿到香蕉也能清除反作弊暂停状态
- 新增 `.ai/banana_pause_resume_regression_check.py`：结构性回归检查，确认三套内核都在 `HasStartGame()` 之前处理已进入的反作弊暂停结束，且未在该区域调用 `MT.Start()`
- 验证：回归检查通过；MSBuild Release|x64 因本机缺 .NET Framework 4.0 targeting pack 报 `MSB3644`，未完成编译验证
- 待验证：真实游戏中执行“站到香蕉树→F9暂停→拿香蕉→F9恢复”

- task-013 香蕉树反作弊暂停/恢复 Bug 只读诊断完成
- 结论：LIKELY_BUG — `HasStartGame()` 在 `IsPause==true` 时跳过整个反作弊检查块，导致 `CheckCheatEnd()` 无法清除 `IsInUnCheat`
- 影响：三个内核（PAL98/PAL98DX9/PAL98UNHAPPY）均受影响
- 输出：`.ai/BANANA_PAUSE_RESUME_BUG_REVIEW.md`（完整诊断报告）
- 更新：`.ai/resume.md`（task-013 状态和下一步建议）
- 未修改任何 .cs 文件、README、docs、工程文件或真实游戏数据

### 2026-05-10 会话

- `SoundConfig.cs`：新增 `GameComplete` 枚举值和中文描述；新增 `MciOpenFile()` + `PlayMp3WithWmp()` 播放链；音效优先级机制（通关=30 > 总时间=20 > 分段=10）；优先级中断播放
- `SoundConfigForm.cs`：快捷键提示标签独立一行；初始高度增加容纳"最终通关"行
- `TimerCore.cs`：`Checking()` 改为读取 `form.IsNonSequentialCheck`；`OnCheckPointEnd()` 新增通关音效播放
- `GForm.cs`：版本号更新为 `3.36.4`；新增 `IsNonSequentialCheck` 字段、点击处理、`skip_node` 文件加载/保存
- `GForm.Designer.cs`：功能菜单新增"跳图路线(非顺序节点)"勾选项
- `仙剑98柔情.cs`/`仙剑98柔情DX9.cs`/`仙剑98柔情不欢乐模式.cs`：删除 `EnableNonSequentialCheck = true` 硬编码；当时在 `LoadGame()` 中保存/恢复本地 `TotalMonsterCount`（该历史语义已于 2026-07-27 被“新 SRPG 恢复快照、旧 SRPG 保留本地值”取代）；`UI_SaveGameEx`/接力-接盘/云读档暂停状态保持修复
- `GEX.cs`：删除 `BuildCenteredMainTimer()` 和 `MeasureTextSize()`，恢复固定偏移布局
- `KeyChangerDel.cs`：F11 改键兼容"改建器"和"改键器"两种窗口标题
- `README.md`：合并 v3.36.2/3/4 为统一的 v3.36.4 更新说明
- `docs/TODO-pal98-dx9-updates.md`：添加 DONE 段落

---

## 8. 测试状态

2026-07-27 水灵珠节点补充验证：

```powershell
& .\.ai\water_spirit_pearl_split_regression_check.ps1 -GameDirectory 'D:\SteamLibrary\steamapps\common\PAL\PAL98'
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" Pal98Timer.sln /m /t:Build /p:Configuration=Release /p:Platform=x64 /nologo
git diff --check
```

```text
REAL: canonical=0x2EE4/script=0x886E, Dali=0x2AB1/script=0x773F
PASS: 动态资源 ID、同 PID 一次解析缓存、换 PID 重解析、损坏/重复拒绝、开局已有物品保护、正常路线、跳过路线、reset、无关对话保护。
PASS: 70ms Observe 路径没有文件/资源访问，PAL98UNHAPPY 未接入。
Pal98Timer.sln Release|x64 build succeeded；0 errors，27 个既有 warning。
git diff --check passed（仅生成式 goal pointer 有既有 CRLF/LF 警告）。
```

2026-07-27 本轮补充验证：

```powershell
& .\.ai\srpg_flying_flag_sidecar_regression_check.ps1
C:\Users\other\miniconda3\envs\paltools-hermes\python.exe .ai\pal98unhappy_identity_regression_check.py
C:\Users\other\miniconda3\envs\paltools-hermes\python.exe .ai\pal98dx9_title_identity_regression_check.py
C:\Users\other\miniconda3\envs\paltools-hermes\python.exe .ai\cloud_save_load_pause_regression_check.py
git diff --check
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" Pal98Timer.sln -m -p:Configuration=Release -p:Platform=x64 -verbosity:minimal
```

```text
PASS: SRPG sidecar capture, old-package compatibility, hash rejection, replace backup, and absent snapshot behavior.
PASS: new PalTimer deserializes an old SRPG with no sidecar action requested.
PASS: legacy PalTimer can deserialize a new SRPG while ignoring sidecar fields.
PASS: PAL98UNHAPPY uses the canonical identity and preserves legacy local best data.
PASS: PAL98DX9 timer title matching supports Simplified, Traditional, and English PAL98DX9 identities.
PASS: PAL98 cloud/relay save-load paths leave UI pause enabled.
git diff --check passed（仅生成式 goal pointer 有既有 CRLF/LF 警告）。
Pal98Timer.sln Release|x64 build succeeded；0 errors，只有既有 warning。
```

2026-07-03 本轮补充验证：

```bash
C:\Users\other\miniconda3\envs\paltools-hermes\python.exe .ai\plugin_authorization_bypass_regression_check.py
C:\Users\other\miniconda3\envs\paltools-hermes\python.exe .ai\fujia_yuhang_artifacts_regression_check.py
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" PAL98.FujiaCaishen\PAL98.FujiaCaishen.csproj -p:Configuration=Release -p:Platform=AnyCPU -verbosity:minimal
git diff --check
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" Pal98Timer.sln -p:Configuration=Release -p:Platform=x64 -verbosity:minimal
```

```text
PASS: plugin authorization bypass remains hidden and explicit.
PASS: Fujia plugin tracks and freezes Yuhang artifact collection.
PAL98.FujiaCaishen Release|AnyCPU build succeeded.
git diff --check passed.
Pal98Timer.sln Release|x64 build succeeded; warnings are existing unused-variable / obsolete API warnings.
```

插件包签名状态：

```text
3.36.4 运行包原插件：D:\SteamLibrary\steamapps\common\PAL\自动计时器\计时器3.36.4\plugins\PAL98.FujiaShouji.tpg
原包 ClassName=PAL98.FujiaCaishen, Version=1, EnableByte=100, SignatureValid=True, EmbeddedDllMD5=A02E941A38166DB7BFB9D7C7E236108A
新 DLL：PAL98.FujiaCaishen\bin\Release\PAL98.FujiaCaishen.dll
新 DLL MD5=2D5744D39ACA9E1490FB2E7860CC77CC
结论：未修改的 3.36.4 要求签名有效 `.tpg`，不能直接用未签名 DLL 替换发布；需要原作者/授权签名工具重新生成同名 `PAL98.FujiaShouji.tpg`。
```

3.36.5 测试目录部署状态：

```text
部署目录：D:\SteamLibrary\steamapps\common\PAL\自动计时器\计时器3.36.5
备份目录：D:\SteamLibrary\steamapps\common\PAL\自动计时器\计时器3.36.5\backup-before-yuhang-plugin-auth-20260703-135000
新 Pal98Timer.exe MD5=F373BAE2D9FF2B8BFC99C4C1893A3ABD
首次调试 PAL98.FujiaShouji.tpg MD5=D14F41279DC152AFA76CBDF4EB25F8D4
排版修正版 PAL98.FujiaShouji.tpg MD5=4238F9E555614370C14168832736E2EB
紧凑插件文本修正版 PAL98.FujiaShouji.tpg MD5=34DEC6BFD3561E6B1FF802965F83A849
原 Pal98Timer.exe 备份 MD5=272BD78B112C6A5E2C6A5F788C21130C
原 PAL98.FujiaShouji.tpg 备份 MD5=0175E530188EA072DD19463269CC3300
plugin_auth 已设为 Hidden，内容为 allow_unsigned_plugins=1
首次调试 PAL98.FujiaShouji.tpg：EnableByte=100, ClassName=PAL98.FujiaCaishen, Version=1, Description=富甲收集规则-右下-四大神器调试, Sign=debug-unsigned, EmbeddedDllMD5=2D5744D39ACA9E1490FB2E7860CC77CC
排版修正版 PAL98.FujiaShouji.tpg：EnableByte=100, ClassName=PAL98.FujiaCaishen, Version=1, Description=富甲收集规则-右下-四大神器调试, Sign=debug-unsigned, EmbeddedDllMD5=1F4E6DC002A309E941583E239A6E39B9
紧凑插件文本修正版 PAL98.FujiaShouji.tpg：EnableByte=100, ClassName=PAL98.FujiaCaishen, Version=1, Description=富甲收集规则-右下-神器紧凑调试, Sign=debug-unsigned, EmbeddedDllMD5=930A9F19F9F863E7AF4779AC1923A67D
```

待补充实机验证：

```text
实机 PAL98DX9/PAL98 富甲插件显示：开局“四大神器：未收集 钱：0 道具：0”；上船前拿到目标物品后，上船冻结“已收集”；未拿到则冻结“未收集”；重置后重新显示未收集并重新统计。
```

2026-06-24 本轮补充验证：

```bash
C:\Users\other\miniconda3\envs\paltools-hermes\python.exe .ai\reset_clears_ui_pause_regression_check.py
C:\Users\other\miniconda3\envs\paltools-hermes\python.exe .ai\cloud_save_load_pause_regression_check.py
C:\Users\other\miniconda3\envs\paltools-hermes\python.exe .ai\banana_pause_resume_regression_check.py
C:\Users\other\miniconda3\envs\paltools-hermes\python.exe .ai\pal_open_process_permission_regression_check.py
git diff --check
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" Pal98Timer.sln -p:Configuration=Release -p:Platform=x64 -verbosity:minimal
```

```text
PASS: reset clears UI pause and leaves the timer restartable.
PASS: PAL98 cloud/relay save-load paths leave UI pause enabled.
PASS: all PAL98 kernels clear existing anti-cheat pause before HasStartGame.
PASS: PAL98 kernels show the short elevated Pal.exe message and close PalTimer after acknowledgement.
git diff --check passed.
Release|x64 build succeeded; warnings are existing unused-variable / obsolete API warnings.
```

本轮未作为通过项的既有脚本：

```text
.ai/automation_snapshot_export_regression_check.py 当前失败在 TimerCore AutoTest envelope / gated tick snapshot 字符串匹配；源码已使用 AutomationArgs.Current.SnapshotIntervalMilliseconds 可配置 interval，而脚本仍匹配旧固定 500ms 形态。该失败与本轮 reset 修复无关，若继续 automation snapshot 工作应单独更新脚本。
```

2026-06-23 本轮补充验证：

```bash
C:\Users\other\miniconda3\envs\paltools-hermes\python.exe .ai\sound_config_volume_regression_check.py
C:\Users\other\miniconda3\envs\paltools-hermes\python.exe C:\Users\other\.codex\skills\.system\skill-creator\scripts\quick_validate.py D:\Workspace\agent-setting\projects\Pal98Works\skills\pal98-paltimer-plugin-development
git diff --check
git -C D:\Workspace\agent-setting diff --check
"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" Pal98Timer.sln -p:Configuration=Release -p:Platform=x64 -verbosity:minimal
```

```text
PASS: PalTimer sound config supports per-audio volume with backward-compatible config parsing.
Skill is valid.
PalTimer git diff --check passed.
agent-setting git diff --check passed with one existing CRLF/LF warning for pal98-dx9-hooking/SKILL.md.
Release|x64 build succeeded; warnings are existing unused-variable / obsolete API warnings.
```

本轮编译命令：

```bash
"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" Pal98Timer.sln -p:Configuration=Release -p:Platform=x64 -verbosity:minimal
```

本轮编译结果：

```text
通过 — Build succeeded, 0 errors；仅有既有 warning（未使用变量、AppDomain.GetCurrentThreadId() 过时等）。
产物：Pal98Timer/bin/x64/Release/Pal98Timer.exe
```

本轮已通过的静态 / 结构性回归检查：

```bash
git diff --check
C:\Users\other\miniconda3\envs\paltools-hermes\python.exe .ai\automation_snapshot_export_regression_check.py
C:\Users\other\miniconda3\envs\paltools-hermes\python.exe .ai\banana_pause_resume_regression_check.py
C:\Users\other\miniconda3\envs\paltools-hermes\python.exe .ai\cloud_save_load_pause_regression_check.py
C:\Users\other\miniconda3\envs\paltools-hermes\python.exe .ai\pal_open_process_permission_regression_check.py
```

```text
PASS: PalTimer automation snapshot export is gated and emits the AutoTest envelope.
PASS: all PAL98 kernels clear existing anti-cheat pause before HasStartGame.
PASS: PAL98 cloud/relay save-load paths leave UI pause enabled.
PASS: PAL98 kernels show the short elevated Pal.exe message and close PalTimer after acknowledgement.
```

本轮产物 manifest 检查：

```text
Windows SDK mt.exe 抽取 Pal98Timer/bin/x64/Release/Pal98Timer.exe manifest，确认有效 requestedExecutionLevel 为 level="asInvoker"。
注释示例中仍包含 requireAdministrator，不是生效节点。
```

本轮普通权限启动烟测：

```text
从普通 PowerShell 启动 Pal98Timer/bin/x64/Release/Pal98Timer.exe 成功，进程保持运行，不再出现 WinError 740。
关闭烟测：CloseMainWindow() 未让本次进程自行退出，已强制结束；未发现残留 Pal98Timer / KeyChanger 进程。
```

实机测试需手动进行（需要仙剑98游戏环境）：
- task-113：普通权限 PAL98DX9/PAL98/PAL98UNHAPPY 读内存、F9/F10/F11、KeyChanger、云验证/云存读档、关闭游戏后不刷屏。
- task-113：如果 PAL98DX9/PAL98/PAL98UNHAPPY 被管理员方式启动、PalTimer 普通权限运行，确认短提示出现一次，点击确定后 PalTimer 直接关闭，不再弹二次退出确认；下次同样条件启动仍会提示并退出。
- task-113：管理员权限 PAL.exe + 管理员权限 PalTimer 时不提示；普通权限 PAL.exe + 管理员权限 PalTimer 时不提示；普通权限 PAL.exe + 普通权限 PalTimer 路径不受影响。
- task-111：背景图透明度变化时，文字、按钮和计时数字保持不透明；OBS 截取效果符合直播需求。
- task-014：站到香蕉树 -> F9 暂停 -> 拿香蕉 -> F9 恢复，确认计时器能恢复。
- task-015：云存档、云读档、接力存档、接力接盘操作完成后均保持暂停。

---

## 9. 多电脑环境差异

### 当前开发环境

- 项目路径：`D:\Workspace\KnowledgeRoots\PAL\othercat\PalTimer`
- 主要环境：Windows 11 Pro，VS2026 (v18)
- 编译目标：Release|x64
- .NET Framework 4.7.2 目标

### 其他环境

参见 `.ai/env.md`（如存在）获取多电脑差异信息。

---

## 10. 长期技术决策

- 编译目标统一为 Release|x64，不恢复 x86（因 PalCloudLib 仅有 64 位版本）
- 最低系统要求 Win7 SP1
- 使用 VS2026 编译，不再依赖 VS2019
- 三个高相似内核（PAL98/PAL98DX9/PAL98UNHAPPY）应逐步抽取共用模块
- 音效播放使用 MCI + WMP COM 回退，保证 Win7 SP1 兼容

---

## 11. 风险与注意事项

- 不要把聊天记录当作项目事实。
- 不要只根据本文件写代码，必须结合实际代码和 Git 状态。
- 不要覆盖用户本机配置。
- 不要自动删除不理解的文件。
- 不要提交密钥、Cookie、Token、账号密码、本机私有配置。
- 不要假设 macOS 和 Windows 的路径、Shell、Python、Node、Conda 环境一致。
- 主计时居中方案曾失败两次（`*` 号导致毫秒跳动、额外缓冲导致间距过大），如需重新尝试需要先研究 `*` 号切换时的文字宽度变化机制。
- 如果发现本文件过时，需要先更新本文件，再继续后续任务。

---

## 12. 结束本轮工作前必须更新

每次 AI 或人工结束本轮工作前，应更新以下部分：

1. `2. 当前工作状态`
2. `3. 已完成内容`
3. `4. 未完成内容`
4. `5. 当前最重要的下一步`
5. `7. 最近改动`
6. `8. 测试状态`
7. 如有必要，更新 `9. 多电脑环境差异` 和 `10. 长期技术决策`

推荐结束口令：

```text
请根据本轮工作结果更新 .ai/resume.md，只保留下一个 AI 接手必须知道的信息，不要写聊天流水账。
```

推荐开始口令：

```text
请读取 .ai/resume.md，判断当前项目进度，继续上次任务。
```
