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

**版本：v3.37.0**（2026-08-25；版本号升级由用户决定）

当前 Git 状态以实际 `git status` 为准；2026-08-24 P 键重启权限误报修复的源码、测试与文档已经收口。本文件此前记录的 `codex/paltimer-automation-tick-snapshot` 为旧会话状态；后续接手以实际 Git 状态和代码为准。

2026-09-03 比赛锁合同保持 `PAL98.TournamentLock.v1` 不升 schema；PalTimer 把锁定者合法长度从 1–4 扩为 1–8 个汉字、英文字母或数字，并在整个进程生命周期发布 `Local\PAL98.PalTimer.TournamentLock.v1` 版本化能力标记，供 PALDLL 在锁定游戏启动时识别支持比赛配置的计时器。四个 PAL98DX9 系内核在有效锁下仍只返回清单内 `competition_display_name`，即配置工具“比赛/补丁名称”规范化后的精确“xx比赛专用”，不追加普通版本号。PalTimer 继续只校验签名清单和三份不可变快照身份，不读取或解释 ConfigTool/PALDLL 的运行文件白名单；内存、IPC、轮询、节点和计时边界不变。Release|x64 构建和比赛锁专项回归通过，EXE SHA-256 `75FF6F1D7E0C6E46D4A2C81A821730D9D552FDADB31D50432ED1CBDDD1293A55`；已连同对应 PAL.dll、PAL.map 和配置工具部署到 `D:\SteamLibrary\steamapps\common\PAL\PAL98`，备份在 `_codex_backups\20260903-111027-tournament-timer-capability`。产品源码随本次 3.37.0 增量发布收口，`.agents/goal/*` 的既有生成式改动继续保留且不属于产品提交。

2026-09-02 当前工作区有未提交的比赛锁身份支持，并保留 `.agents/goal/*` 的既有生成式改动。`TournamentLockInfoReader` 从已附加 PAL.exe 所在目录校验 `PAL98.TournamentLock.v1` 的私有 Release HMAC、规范字段、固定三文件集合和快照哈希；当前清单要求 `display_line_overrides` 与 `configuration_code_marker` 成对出现，先前两字段均缺失的已签名 v1 清单继续兼容，半升级失败关闭。有效锁让 PAL98DX9、魂牵、Dream220 显血和不欢乐内核精确显示 `competition_display_name`（“xx比赛专用”），无锁沿用原身份，损坏锁不采用比赛身份。没有修改进程匹配、内存地址、RPM/WPM、70ms 循环、节点、计时或 `PAL98_IPC_v1`。产品版本保持 3.37.0。可信完整性密钥只存在于本机 Git exclude 文件 `Pal98Timer/TournamentIntegrityKey.txt` 并嵌入本地 Release；公开 Debug 使用明确测试密钥，公开 Release 缺少私钥时不会信任比赛锁。最终 Release|x64 SHA-256 `5B7F4FAD63552CD3479F1D8349DB29247B6DAF62A86BFB476F4C4C323D62C40E`；新旧清单、篡改与无锁回归通过，未提交、未推送、未部署。

2026-08-30 按用户截图调整 PAL98DX9 游戏内三行时间线：第三列由相对最佳线差值改为节点当前累计时间，当前节点实时刷新、已完成节点保留完成时刻、未来节点留空；快/慢颜色仍按原比较值计算。只改叠加快照和绘制，不改节点推进、主计时、暂停、反作弊、存读档或内存读取。产品、文件和程序集版本继续保持 `3.37.0` / `3.37.0.0`；Release|x64、静态契约和离屏交互/渲染通过，构建 SHA-256 `D861DCBA4AF286BC0CD9D06B60FE971D6D0A840F497680045F1D30935FCC9593`。完整 GPL 候选位于 `artifacts/paltimer-3.37.0-overlay-current-time-20260830-r1`，源码 ZIP SHA-256 `15C47118DC80424D43633E458B2D24DCA64F8530FAA4A7CD1F0ACAA6E3D6388C`，并已独立解压完成 Release|x64 重建。部署到 `D:\PAL98_v1.59\Tools\PalTimer-3.37.0` 时同步主 EXE、对应源码 ZIP、README、发布清单和校验和；用户配置、最佳线、布局、插件、计时数据及其余 27 个文件哈希不变。旧 EXE/README/清单/校验和备份在 `backup-before-overlay-current-time-20260830-134547`。

2026-08-25 当前 Dream 显血核心除基础 `pal98.dream220.compat@1.0.18` 外，只额外接受冻结派生
`pal98.dream220.compat.drawcard.16e143813df5@1.0.18`；任意其他 Dream 派生仍失败关闭。该派生包是显血 +
李逍遥专用扎麻神针 + 敌人物抗1/五人防御+888 + 全吴强，但继续共用 `DREAM220VISIBLE` 时间线和
`bestDREAM220VISIBLE.txt`。Release|x64、定向 profile/路线/no-new-memory-write 与关键 PAL98DX9 回归通过；GPL
r5 EXE SHA-256 `A4D98490C0F42DFACB44D7138008134A587214DF0475C9291636B835187D838C`，对应源码 ZIP SHA-256
`B429859C39894449AB398DB00CCDD6547AA3284971D7570C188E1C0CCDB67AFC`，已部署到
`D:\PAL98_v1.58\Tools\PalTimer-3.37.0`；误部署的 3.38.0 目录已移入可恢复备份，未启动 3.37.0 或修改计时数据。

2026-08-24 修复 PALDLL_DX9 游戏内按 P 重启时的管理员权限误报。实机只读证据确认重启后的 PAL 与 PalTimer 均为 `Elevated=0`，且相同 `OpenProcess(0x1F0FFF)` 随后成功；问题是三个 PAL98 内核把 PID 切换期单次 `ERROR_ACCESS_DENIED=5` 立即解释为管理员进程。新增共享 `PalProcessOpenRetryPolicy`：只对同一 PID 的错误5提供1.5秒单调时钟稳定期，成功、非错误5、换 PID 或清理状态都会复位；同一 PID 持续拒绝后仍发布既有权限提示。定向策略 harness、三内核权限/标题/叠加/暂停/云读档回归和 VS2026 `Release|x64` 构建通过；仅有既有 warning。新版主程序已部署到计时器3.37.0目录，SHA-256 `77D5FD838E4EC496B0D91199603A851F2F79AB41A993CF2099075FB022FCDDB2`；旧版备份位于 `backup-before-p-restart-access-denied-debounce-20260824-115943`。除主 EXE 和新增备份外，其余23个文件哈希未变；未启动计时器。

2026-08-23 本轮新增 PAL98DX9 专用、默认关闭的实验性游戏内信息叠加，并把界面、程序集与文件版本升级为 `3.37.0`。叠加层仅在开关启用时创建无激活、鼠标穿透的透明窗口和 100ms WinForms Timer；根据原开发者补充和历史 `LiveWindow` 的 `ShowPointCount=2` 语义，窗口改为 PAL98DX9 客户区右下角的紧凑面板，并显示路线首尾自动平移的3行节点窗口（上一、当前、下一；节点名、最佳线，已完成/当前项含差值）。后续视觉反馈要求移除补丁版本和预计通关文字、缩窄面板、把节点名右对齐靠近时间列，并通过缩短底部锚定面板一行高度让全部内容下移一行；简体/英文标题使用常规宋体 `SimSun`，繁体标题根据既有缓存窗口标题使用细明体 `MingLiU`（BIG5 风格字形，不改变 Unicode 解码链），亮绿/亮黄/亮红改为低饱和灰金/灰绿/灰红，文字渲染使用 `SingleBitPerPixelGridFit` 以避免洋红透明色键与抗锯齿混色产生紫边。战斗计时左侧新增 `暂停N`，只读复用 `GForm.HandPauseCount` 的公开 getter；手动从未暂停切到暂停时沿用既有逻辑加一，重置归零，窗口失焦、反作弊和云/接力读档的 `SetUIPause` 不计入。功能菜单最终收拢为单一顶层“游戏内信息叠加设置”，子菜单承载启用/关闭、开关快捷键、位置/比例、字体/字号/颜色和恢复默认。叠加快捷键默认未设置，只允许至少含一个 Ctrl/Shift/Alt 的组合键，拒绝 F1-F12、Ctrl+Enter 和当前节点音效快捷键；它复用主窗体既有全局键盘钩子，基准键按下后锁存到抬起以屏蔽系统连发，没有新增钩子、线程或轮询。显式编辑遮罩只有用户进入“调整叠加位置和比例”时才临时移除鼠标穿透并在原窗口绘制低饱和遮罩、提示栏和右下角缩放手柄；左键拖动位置、拖动手柄等比缩放、右键或再次点菜单完成，退出立即恢复 `WS_EX_TRANSPARENT`。系统字体对话框可选择字体、6–18号字号和常规/粗体/斜体，颜色对话框配置常规文字颜色；快/慢与反作弊语义色保留，拒绝透明色键所用的洋红色。归一化位置、0.5–2.0比例、字体、颜色和快捷键保存到独立 `dx9_overlay_layout`；旧目录缺少该文件时保持原布局、默认颜色且快捷键未设置。它只消费 DX9 内核已有的计时、资源、节点、暂停和窗口句柄快照，不抓屏、不联网、不接旧 OBS 插件、不增加 `OpenProcess` / `ReadProcessMemory` / `WriteProcessMemory`，也不进入计时、反作弊、读档或节点推进链。缺少 `dx9_overlay` 时默认关闭；关闭时只在 PAL98DX9 内核初始化读取一次小型布局文件取得快捷键，不创建叠加窗口、Timer、线程或周期轮询。`.ai/dx9_overlay_regression_check.py`、`.ai/dx9_overlay_interaction_regression_check.ps1`、构建/部署产物反射行为检查、离屏视觉预览及暂停、反作弊、云/接力读档、管理员提示、DX9 标题、水灵珠相关回归均通过；VS2026 / MSBuild 18 `Release|x64` 重建成功，只有既有警告。最终重建的 `Pal98Timer.exe` 程序集版本为 `3.37.0.0`，文件/产品版本为 `3.37.0`，SHA-256 为 `D0D2AD09BD664A60AD44BB547EAE86D6A2AD5B675060D70978B0E07D52ECA42B`。未启动 PalTimer，真实游戏中的快捷键输入、合并菜单、暂停次数、拖动、缩放、字体/颜色、焦点、性能和速通语义仍需实机验证。

2026-08-23 v3.37.0 本地部署：仅把最终 `Release|x64` 主程序覆盖到 `D:\SteamLibrary\steamapps\common\PAL\自动计时器\计时器3.37.0\Pal98Timer.exe`，未覆盖配置、成绩、插件、改键器或运行依赖。覆盖前目标为 v3.36.6，SHA-256 `773B808E3153BBF299E44E3BEB18E9D33897B50E92AA29ADD9B85A89FA533CBF`；已备份到 `backup-before-v3370-release-20260823-122731\Pal98Timer.exe`，备份哈希一致。部署后目标 SHA-256 为 `518E1BC28DC0E1DC3F2822DBEE5FE8E32AB95397D06131E0062BF0671758F602`，与来源一致。已从目标路径做隔离工作目录的 5 秒启动冒烟：主程序保持运行并取得有效主窗口句柄；隐藏环境下正常关闭请求未在 5 秒内结束，随后只终止本次测试启动的主程序及其自动拉起的 KeyChanger，未留进程。测试引起目标 `size` 同内容重写，确认与原文件 SHA-256 完全一致后恢复了原时间戳；其他用户状态未改。该冒烟不等于 PAL98DX9 实机、30 分钟性能或速通语义验收。

2026-08-23 v3.37.0 右下角/3行时间线纠正部署：仅重新覆盖同一目标的 `Pal98Timer.exe`，覆盖前 SHA-256 `518E1BC28DC0E1DC3F2822DBEE5FE8E32AB95397D06131E0062BF0671758F602`，备份到 `backup-before-v3370-bottom-right-timeline-20260823-124627\Pal98Timer.exe` 且哈希一致；新目标 SHA-256 `E1F600C9870006CEA3067EF77EBE1CE34F565D3FD54B6CA775041E390C8FCCF3`，与最终来源一致。部署后直接加载目标 EXE 验证5节点的时间线窗口：step 0 为0/1/2，step 2 为1/2/3，step 4 为2/3/4，step 5（路线完成）保留2/3/4且无当前标记。未启动 PalTimer/PAL.exe，未修改 `dx9_overlay`、配置、成绩、插件、改键器或其他用户状态；仍待真实 PAL98DX9 视觉、焦点和30分钟性能验收。

2026-08-23 v3.37.0 低饱和/微软雅黑视觉纠正部署：同一目标仅覆盖 `Pal98Timer.exe`；覆盖前 SHA-256 `E1F600C9870006CEA3067EF77EBE1CE34F565D3FD54B6CA775041E390C8FCCF3`，备份到 `backup-before-v3370-neutral-yahei-20260823-130421\Pal98Timer.exe` 且哈希一致；新目标 SHA-256 `D349D17FF382EAC6F440D5E4AEFB0BA16D36E41A6DB8678BE76639DE7FFCAC3C`，与最终来源一致。部署前后 `dx9_overlay` 均为用户已启用的 `1`，未覆盖配置、成绩、插件、改键器或其他状态。直接加载部署版离屏渲染虚拟计时/资源/三节点预览，确认无补丁版本文字、节点名贴近时间列、微软雅黑生效、低饱和颜色区分成立且没有洋红晕边；预览临时文件已删除。未启动 PalTimer/PAL.exe，仍待真实游戏画面和30分钟性能验收。

2026-08-23 v3.37.0 宋体/去预计通关/下移一行视觉纠正部署：同一目标仍只覆盖 `Pal98Timer.exe`；覆盖前 SHA-256 `D349D17FF382EAC6F440D5E4AEFB0BA16D36E41A6DB8678BE76639DE7FFCAC3C`，备份到 `backup-before-v3370-simsun-no-estimate-20260823-131104\Pal98Timer.exe` 且哈希一致；新目标 SHA-256 `BE75B5C7D62D25C1DDE0C0EF6931F44B8FCB80348A23D4DE9B6F0E26EE6FE3ED`，与最终来源一致。部署前后 `dx9_overlay` 保持用户已启用的 `1`，未覆盖配置、成绩、插件、改键器或其他状态。直接加载部署版离屏渲染虚拟计时/资源/三节点预览，确认常规宋体明显变细、预计通关行已移除、底部锚定面板缩短一行后整体下移，且没有洋红晕边；预览临时文件已删除。未启动 PalTimer/PAL.exe，仍待真实游戏画面和30分钟性能验收。

2026-08-23 v3.37.0 可移动/缩放/字体配置部署：同一目标仍只覆盖 `Pal98Timer.exe`；覆盖前 SHA-256 `BE75B5C7D62D25C1DDE0C0EF6931F44B8FCB80348A23D4DE9B6F0E26EE6FE3ED`，备份到 `backup-before-v3370-overlay-layout-20260823-132358\Pal98Timer.exe` 且哈希一致；新目标 SHA-256 `A2722A4DF15EDAD946B733D34B8176B29B71CCB428CCAAEAE4FF6318D02BD7E6`，与最终来源一致。部署前后 `dx9_overlay` 保持 `1`；目标原本不存在 `dx9_overlay_layout`，部署只替换 EXE，因此没有代替用户生成或覆盖布局设置。直接对部署版运行隔离 STA 窗口反射回归，确认默认右下角、正常态 `WS_EX_TRANSPARENT`、显式编辑态移动/等比缩放、字体/字号保存、离屏渲染、恢复默认和退出后恢复穿透均通过；测试配置只写入并清理临时目录。部署时 PalTimer、PAL.exe 和 KeyChanger 均未运行。真实 PAL98DX9 游戏输入、Alt-Tab、窗口缩放和30分钟性能仍待人工实机验收。

2026-08-23 v3.37.0 叠加暂停次数部署：同一目标仍只覆盖 `Pal98Timer.exe`；覆盖前 SHA-256 `A2722A4DF15EDAD946B733D34B8176B29B71CCB428CCAAEAE4FF6318D02BD7E6`，备份到 `backup-before-v3370-overlay-pause-count-20260823-140429\Pal98Timer.exe` 且哈希一致；新目标 SHA-256 `5E5CF02EA2F1984922E44D53BA7D56467DD5B8375EECFEA2BBB5D68404BD2FAA`，与最终来源一致。战斗计时行显示 `暂停N  战斗 0.00s`，只读传递主界面既有手动暂停计数，不写入或重新计算暂停状态。部署前后 `dx9_overlay` 保持 `1`，目标仍不存在 `dx9_overlay_layout`，因此没有覆盖用户布局；部署版隔离 STA 回归验证暂停数快照、渲染、移动/缩放、字体保存和穿透恢复均通过。部署时 PalTimer、PAL.exe 和 KeyChanger 均未运行；真实 F9/按钮暂停、反作弊、失焦和云/接力读档显示仍待 PAL98DX9 实机核对。

2026-08-23 v3.37.0 叠加菜单合并部署：同一目标仍只覆盖 `Pal98Timer.exe`；覆盖前 SHA-256 `5E5CF02EA2F1984922E44D53BA7D56467DD5B8375EECFEA2BBB5D68404BD2FAA`，备份到 `backup-before-v3370-overlay-menu-20260823-141208\Pal98Timer.exe` 且哈希一致；新目标 SHA-256 `F8794FED7A4221E72299C4971C45EF8895399D0E4064CE57631C3F0B89BA6DF7`，与最终来源一致。功能菜单原4个顶层叠加项合并为单一“游戏内信息叠加设置”，启用、调整位置/比例、字体/字号和恢复默认成为子项；原事件处理和配置键未改变。部署前后 `dx9_overlay=1`；用户已经生成的 `dx9_overlay_layout` SHA-256 `F50D24DDF07D5A8D4B70B6C7CF1E64A2FB4D8BB0B9E626C2858B137672A9D432` 保持一致。部署时 PalTimer 未运行，PAL.EXE 继续运行且未被停止或修改；仍待用户下次启动计时器后实机确认级联菜单交互。

2026-08-23 v3.37.0 叠加快捷键/字体颜色部署：同一目标仍只覆盖 `Pal98Timer.exe`；覆盖前 SHA-256 `F8794FED7A4221E72299C4971C45EF8895399D0E4064CE57631C3F0B89BA6DF7`，备份到 `backup-before-v3370-overlay-hotkey-color-20260823-142526\Pal98Timer.exe` 且哈希一致；新目标 SHA-256 `D0D2AD09BD664A60AD44BB547EAE86D6A2AD5B675060D70978B0E07D52ECA42B`，与来源一致。新增“配置叠加开关快捷键”和“调整叠加字体颜色”子项；快捷键默认未设置，只允许含 Ctrl/Shift/Alt 的非保留组合，复用现有全局钩子并锁存到基准键抬起。部署前后 `dx9_overlay=1` 的 SHA-256 `6B86B273FF34FCE19D6B804EFF5A3F5747ADA4EAA22F1D49C01E52DDB7875B4B`、用户 `dx9_overlay_layout` 的 SHA-256 `8210824992C66AEB12722EF8A1AB7D90C70928971B84BEF241B5BA31BB3B88B9` 均保持不变。部署版隔离 STA 反射回归通过；PalTimer 未运行，PAL.EXE PID 84812 在部署前后持续运行，未被停止或修改。

2026-08-23 只读核对 PALDLL_DX9 `dev` 的 `PAL98_IPC_v1`：现有共享内存包含事件 ring、地图/坐标/BGM/战斗状态、脚本轨迹/游标和黑屏状态，PAL98-Speedrun-Bot 已有只读 consumer，适合作为后续 TAS/诊断数据源。但其实时状态更新依附既有 `copymen` 路径，当前 ABI 也没有计时器蜂蜜、火虫草、血玲珑等完整资源统计；因此 v3.37.0 叠加层不接入 IPC、不修改 PALDLL_DX9。未来若接入，必须验证 magic/version/struct size、稳定序号、stale timeout、单 owner 与多实例行为，且不得反向控制计时器语义。

2026-07-27 本轮正式把界面当前版本、程序集版本和文件版本统一升级为 `3.36.6`，并把 README 中截至当前的未发布内容归档为 v3.36.6 更新说明。VS2026 / MSBuild 18 `Release|x64` 重建成功；生成的 `Pal98Timer.exe` 程序集版本为 `3.36.6.0`，文件版本与产品版本均为 `3.36.6`，SHA-256 为 `773B808E3153BBF299E44E3BEB18E9D33897B50E92AA29ADD9B85A89FA533CBF`。本轮只处理版本与发布文档，没有重新部署实机目录，也没有改变计时、节点、内存读取或存档语义。

2026-07-27 v3.36.6 正式版本部署：已把上述 `Release|x64` 主程序部署到 `D:\SteamLibrary\steamapps\common\PAL\自动计时器\计时器3.36.6\Pal98Timer.exe`。覆盖前目标版本为 `3.36.5`、SHA-256 为 `F505E6B092A99999C4B5527101D6B626D818439AD40B8E3C1DE99B401566142D`，备份位于 `backup-before-v3366-release-20260727-132018\Pal98Timer.exe` 且哈希一致；部署后程序集版本为 `3.36.6.0`，文件/产品版本为 `3.36.6`，目标 SHA-256 为 `773B808E3153BBF299E44E3BEB18E9D33897B50E92AA29ADD9B85A89FA533CBF`，与来源一致。仅覆盖主 EXE，未改配置、成绩、插件或存档；部署时 PAL.exe 和 Pal98Timer 均未运行，尚待玩家启动后完成大理水灵珠节点实机验证。

2026-07-27 本轮改动已通过提交 `4727096` 推送到 `origin/master`：不欢乐模式 `CoreName` 从历史中文值统一为 README 已公开的 `PAL98UNHAPPY`，旧最佳线保留式迁移。PAL98DX9/PAL98UNHAPPY 新 SRPG 现在携带 `PalDrawCard.FlyingFlagAll.v1.bin` 完整快照与 SHA-256；明确记录源 sidecar 不存在，旧 SRPG 不处理目标 sidecar，导入前保留时间戳备份并要求重启 PAL.exe。服务器继续不透明存取 `.bin`，无需修改。新增两项回归脚本和规则文档。

2026-07-27 本轮改动：PAL98、PAL98DX9、PAL98UNHAPPY 的接力/云 SRPG 改为恢复 `TimerStr.TotalMonsterCount` 完整快照；旧 SRPG 缺少字段时保留本地撞怪数。服务器和 `PALCloud.dll` 不变。新增 `.ai/srpg_monster_count_regression_check.ps1`，新/旧字段行为 harness、既有 sidecar 兼容检查和 VS2026 `Release|x64` 构建均通过。

2026-07-27 本轮改动：PAL98/PAL98DX9 的“水灵珠”节点改为双位置门闩，彻底移除脚本状态依赖。实机在场景267确认旧 `X/Y=(912,1512)` 是视口坐标、实际地图坐标 `rX/rY=(1072,1624)`，最近的小李逍遥对象位于 `(1088,1616)`、TriggerMode=2；正常路线现按实际坐标和队伍朝向复现该对象的手动触发搜索范围，并在范围内锁定水灵珠数量 `N`，后续轮询只有看到数量大于 `N` 才触发，用户已确认正常交换位置正确。回梦无痕实机连续五次快照稳定在 `MapID=175`、场景 `204`、实际回程落点 `(1168,760)`；重启前数量 `4` 会归一为 `1`，因此该分支要求精确固定位置且水灵珠数量至少为 `1`。70ms循环只复用既有场景、实际坐标、朝向和背包快照，不再解析 `SSS.MKF`/`M.MSG`，也不再读取当前脚本状态。不欢乐模式没有该节点，未修改。

2026-07-27 本轮测试部署：提交 `0893c0bf471c07f52b841580e061d3f93dbd5a86` 的 `Release|x64` 主程序已部署到 `D:\SteamLibrary\steamapps\common\PAL\自动计时器\计时器3.36.6\Pal98Timer.exe`，只覆盖主 EXE，未覆盖目标目录中的配置、成绩、插件或其他用户数据。覆盖前备份到 `backup-before-water-pearl-position-20260727-114503\Pal98Timer.exe`；新 EXE SHA-256 为 `EDF6C0BFFF65342FCB4391C3FF64BBEDF6A3608BE007AA77C7AD5A3ADCA50D6A`。该次部署时目录名为3.36.6，而当时源码和 EXE 内部版本仍为3.36.5；后续已正式升级到3.36.6。本轮未启动程序、未执行 PAL98/PAL98DX9 实机路线验证。

2026-07-27 坐标修正部署：提交 `4daf189e7e5e1ce86f9d652935a096357b48302d` 的 `Release|x64` 主程序已重新部署到同一计时器3.36.6目录。覆盖前把上一版主程序备份到 `backup-before-water-pearl-coordinate-frame-20260727-120149\Pal98Timer.exe`，旧 SHA-256 为 `EDF6C0BFFF65342FCB4391C3FF64BBEDF6A3608BE007AA77C7AD5A3ADCA50D6A`，新 SHA-256 为 `B1D1CEE0F00818BDD6AABB9DDB832866EC8F02CEFEE91D92DCEC2136FFC19251`，来源与目标一致。只覆盖主 EXE，配置、成绩、插件、`plugin_auth` 和其他依赖均保留；部署时 PAL.exe 保持运行，Pal98Timer 未运行，部署后未自动启动计时器。

2026-07-27 回梦固定落点/数量阈值部署：提交 `093a8ccd589279c9da41f7e0d358e84234357abd` 的 `Release|x64` 主程序已部署到同一计时器3.36.6目录。覆盖前备份到 `backup-before-water-pearl-dali-threshold-20260727-121031\Pal98Timer.exe`；旧 SHA-256 为 `B1D1CEE0F00818BDD6AABB9DDB832866EC8F02CEFEE91D92DCEC2136FFC19251`，新 SHA-256 为 `2BD0E914000956A69FC78900B49C2118A5B99F7E721F9ABC9FFE449E768CC23D`，来源与目标一致。仅覆盖主 EXE，PAL.exe 保持运行，Pal98Timer 未运行；待用户在当前 `Area=204 / rX=1168 / rY=760 / count=4` 状态启动计时器确认节点通过。

2026-07-27 重启数量归一修正部署：提交 `e635813cd7bad0067713516a19af1f5cef13b6db` 的 `Release|x64` 主程序已部署到同一计时器3.36.6目录。重启实机证明相同固定位置的水灵珠数量从临时 `4` 归一为 `1`，因此门闩改为 `count >= 1`。覆盖前备份到 `backup-before-water-pearl-restart-count-20260727-130727\Pal98Timer.exe`；旧 SHA-256 为 `2BD0E914000956A69FC78900B49C2118A5B99F7E721F9ABC9FFE449E768CC23D`，新 SHA-256 为 `F505E6B092A99999C4B5527101D6B626D818439AD40B8E3C1DE99B401566142D`，来源与目标一致。仅覆盖主 EXE，PAL.exe 保持运行，Pal98Timer 未运行。

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

### v3.37.0 增量（2026-09-03）

- PalTimer 进程生命周期发布 `Local\PAL98.PalTimer.TournamentLock.v1` 手动复位事件，作为 PALDLL 锁定启动时的 v1 兼容能力标记；发布失败只记调试信息，不影响计时器启动
- 四个 PAL98DX9 系内核的反射回归现在直接断言 `GetGameVersion()` 等于签名 `competition_display_name`，保证版本位置不追加普通版本号
- 比赛锁锁定者名称校验与 PALDLL_DX9、Pal98ConfigTool 同步扩为最多 8 个汉字、英文字母或数字；签名 schema、页脚、比赛显示名和旧 v1 清单兼容规则不变
- 回归样本改用 8 字符锁定者，同时继续覆盖当前/旧版显示合同、签名与快照篡改、无锁兼容和四内核接线

### 未发布（2026-09-02）

- PAL98DX9、魂牵、Dream220 显血和不欢乐内核统一读取同一份签名比赛锁身份；有效锁显示精确“xx比赛专用”，损坏/缺失快照或签名不采用该身份
- 私有 Release 完整性密钥以内嵌资源读取，本机源文件已 Git exclude；开发密钥仅供 Debug/回归，不作为正式比赛信任根
- 新增 `.ai/tournament_lock_identity_regression_check.ps1`，覆盖有效身份、清单篡改、快照篡改、无锁兼容和四个内核接线；版本保持 3.37.0

### v3.37.0（2026-08-23）
- PAL98DX9 专用实验性游戏内信息叠加已实现并部署；默认关闭、10Hz、透明鼠标穿透，无抓屏、网络或旧 OBS 路径
- 单一设置菜单现包含启用开关、开关快捷键、位置/比例、字体/字号/颜色和恢复默认；配置统一保存在 `dx9_overlay` / `dx9_overlay_layout`
- 叠加快捷键复用既有全局键盘钩子，拒绝 F1-F12、Ctrl+Enter、无修饰键和节点音效快捷键冲突，按住时不会连续切换
- 字体颜色可选；透明色键洋红被拒绝，快/慢差值和反作弊等语义色不被自定义常规色覆盖
- `Release|x64` 构建、静态契约、隔离交互/渲染、既有暂停/反作弊/读档/管理员提示/DX9 标题/水灵珠回归通过；已部署主 EXE且保留用户配置哈希
- PALDLL_DX9 按 P 重启的短暂访问拒绝不再立即误报管理员权限；三个 PAL98 内核共用按 PID 限定的1.5秒重试门，持续权限不匹配提示保持不变，已部署到计时器3.37.0目录
- PAL98DX9 游戏内三行时间线第三列改为节点当前累计时间；当前节点实时刷新，已完成节点保留完成时刻，未来节点留空，快/慢配色与 3.37.0 版本号保持不变

### 未发布（2026-07-27）
- 不欢乐模式内部身份统一为 `PAL98UNHAPPY`；旧本地最佳线仅在新文件不存在时复制迁移，旧文件保留
- `PAL98UNHAPPY.*.tpg` 成为不欢乐模式专用插件前缀，仍兼容 `PAL98.*.tpg`，不自动加载 `PAL98DX9.*.tpg`
- 新增 `.ai/pal98unhappy_identity_regression_check.py`；Release|x64 构建通过
- PAL98DX9/PAL98UNHAPPY 新 SRPG 携带飞行旗完整快照、存在标志和 SHA-256；旧 SRPG 通过 `OptionalField` 保持“不处理 sidecar”语义
- 导入新 SRPG 时先验证完整快照；目标 sidecar 存在则生成时间戳备份并原子替换，源快照明确不存在时把目标移到备份；服务器代码不变
- 新增 `docs/SRPG_FLYING_FLAG_SIDECAR_RULE.md` 和 `.ai/srpg_flying_flag_sidecar_regression_check.ps1`；新旧 BinaryFormatter 双向兼容、备份恢复行为及 Release|x64 构建通过
- PAL98/PAL98DX9/PAL98UNHAPPY 从接力或云 SRPG 恢复 `TotalMonsterCount`；旧 SRPG 无字段时不覆盖本地值，无需修改服务器或 `PALCloud.dll`
- 新增 `.ai/srpg_monster_count_regression_check.ps1`；字段存在、零值及旧包缺字段三种行为 harness 通过
- PAL98/PAL98DX9 的“水灵珠”节点不再由 `0x109` 物品单独触发；正常路线要求进入场景267的小李逍遥 TriggerMode=2 手动触发范围并在后续轮询看到数量增长，回梦无痕要求场景204精确固定位置 `(1168,760)` 且至少有1颗水灵珠
- 新增 `Pal98WaterSpiritPearlSplit.cs` 与 `.ai/water_spirit_pearl_split_regression_check.ps1`；实机确认旧 `X/Y` 是视口坐标，正常分支现读取 `rX/rY` 与朝向，匹配小李逍遥对象 `(1088,1616)`；大理回程使用实际固定坐标 `(1168,760)` 与 `count >= 1`；运行时无资源解析或脚本状态读取

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

- **比赛计时器能力握手**：源码和主机回归已覆盖能力事件的创建/发现/释放及四内核精确显示，并已部署到 Steam PAL98；仍需先启动新版 PalTimer、再启动锁定 PAL.exe，确认 PALDLL 不提示，并分别以旧版/未启动 PalTimer 验证更新提示。
- **8 字符比赛锁身份**：源码、签名清单回归和 Release|x64 构建已通过并已部署；仍需用更新后的配置工具与 PAL.dll 创建真实 5–8 字符锁，确认 PalTimer 显示精确比赛名。
- **比赛锁计时器身份**：静态/反射回归和 Release|x64 已通过并已部署；仍需用配置工具真实创建一个锁，启动 PAL.exe 后确认四类内核显示精确比赛名，并确认无锁/解锁后恢复各自普通身份。
- **P 键重启管理员权限误报**：代码、策略 harness、三内核结构回归和 `Release|x64` 构建通过。仍需部署后验证“普通 PAL + 普通 PalTimer，连续按 P 重启不弹提示且重新附加”；并验证“管理员 PAL + 普通 PalTimer”在约1.5秒后仍提示并退出。
- **PAL98DX9 游戏内信息叠加**：代码、构建、隔离窗口交互、配置往返和部署验证完成。仍需实机确认配置快捷键在游戏前台启用/关闭时不影响游戏输入，F6/F8/F9/F10/F11/F12、Ctrl+Enter 与节点音效快捷键保持原语义；并分别在关闭/开启状态完成30分钟 CPU、Private Bytes、GDI Handles 与游戏速度对照，覆盖暂停、反作弊、读档和节点推进。
- **PAL98DX9 三行时间线当前时间显示**：静态、构建和离屏渲染已通过；仍需实机确认当前节点秒数连续刷新、节点完成后时间冻结、未来节点留空，并检查默认字体和自定义缩放下两列 `HH:mm:ss` 不裁切。

- **PAL98/PAL98DX9 水灵珠双路线节点**：正常交换位置已由用户确认正确。回梦无痕重启验证证明数量会从临时的 `4` 归一为 `1`；修正后应在场景204固定实际坐标 `(1168,760)` 时数量 `0` 不跳、数量 `1` 及以上跳。仍需玩家复测，并补测错误位置、F10 reset 和 PAL98DX9 同路线。PAL98UNHAPPY 未改。
- **SRPG 携带 `PalDrawCard.FlyingFlagAll.v1.bin`**：代码、行为 harness 和构建验证完成。包含该代码的当前主程序已随水灵珠测试部署到计时器3.36.6目录，但仍需在真实 PAL98DX9/PAL98UNHAPPY 中分别验证 sidecar 存在/不存在的接力与云存读档，确认时间戳备份、重启提示、重启后飞行旗位置和 `1.RPG` 一致。

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

**比赛锁优先：** 本轮 PALDLL_DX9、Pal98ConfigTool、PalTimer 源码已同步新白名单、8 字符锁定者边界和计时器 v1 能力标记，并已部署到 Steam PAL98 测试目录。下一步用同一可信 Release 密钥做一次真实闭环：以 5–8 字符锁定者创建锁，先启动新版 PalTimer、再启动 PAL.exe，确认 PALDLL 不出现更新提示且四内核版本位置只显示精确“xx比赛专用”；随后以未启动/旧版 PalTimer 启动同一锁定游戏，确认只出现一次更新提示且游戏仍可继续。再验证四行逐行保持/覆盖、“锁定者XXX : 配置码末 6 位”、锁定后分辨率/窗口位置和两种语言可调且启动无保存提示，并人工改 `DefaultPatch`、其它 `config.ini`/`dxwrapper.ini` 键和 `mod.ini`，确认只恢复非白名单内容，最后解锁。不要提交或输出私钥；不要把自动回归写成实机验收。

**最高优先：** 在明确授权启动/写入后，用冻结派生 profile
`pal98.dream220.compat.drawcard.16e143813df5@1.0.18` 做自然剧情、真实战斗、结局、接力存档→退出→重启→读档和完整 `DREAM220VISIBLE` 时间线验收；再切到魂牵三版和 `仙剑98柔情DX9` 确认隔离与 Classic 176528 字节接力存档不变。资源/合成谓词通过不等于实机验收。

实机验证 PAL98DX9 叠加配置快捷键：游戏前台开/关各一次、按住不连发、游戏不收到基准键，并复测 F6/F8/F9/F10/F11/F12、Ctrl+Enter、暂停/反作弊/云与接力读档、路线节点推进；随后做关闭/开启各30分钟 CPU、Private Bytes、GDI Handles 与游戏速度对照。

实机核对三行时间线第三列：重置后当前节点显示 `00:00:00` 并随主计时按秒刷新，完成节点后冻结完成时刻，下一节点保持空白；同时确认最佳线中列不变、快慢颜色正确、默认/自定义字体和 0.5–2.0 缩放均无裁切。

在下一次明确授权部署 PalTimer 后，先验证普通权限 PAL/PalTimer 连续按 P 重启不会误报，再用管理员 PAL + 普通 PalTimer 确认持续拒绝提示仍存在；不要把计时器改成默认管理员运行。

1. 正常交换位置已经实机通过；下一步重新验证 PAL98/PAL98DX9 回梦无痕分支：场景204固定实际坐标 `(1168,760)` 下数量 `0` 不跳、重启归一后的数量 `1` 及以上跳，错误场景/相邻坐标不跳；然后补测 F10 reset 和 PAL98DX9。
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
| `Pal98Timer/Pal98WaterSpiritPearlSplit.cs` | 水灵珠节点正常交换基线增长与大理回程双位置门闩 |
| `Pal98Timer/TournamentLockInfoReader.cs` | 校验本地 PAL98 比赛锁签名、字段和快照身份，只提供可信计时器显示名 |
| `Pal98Timer/TournamentTimerCapability.cs` | 在计时器进程生命周期发布 PALDLL 可探测的比赛锁 v1 兼容能力事件 |
| `Pal98Timer/仙剑98柔情DX9.cs` | DX9 内核、进程检测、物品/战斗统计、节点定义 |
| `Pal98Timer/Hunqian167*.cs` | 魂牵三版精确 profile 门、独立时间线与路线纯谓词 |
| `Pal98Timer/Dream220Visible*.cs` | 梦幻2.2显血版精确 profile 门、独立时间线与路线纯谓词 |
| `docs/PAL98DX9_PROFILE_CORES.md` | 三条 PAL.exe DX9 核心的公开身份、存档长度和发布门 |
| `Pal98Timer/仙剑98柔情.cs` | 原 98 内核 |
| `Pal98Timer/仙剑98柔情不欢乐模式.cs` | 不欢乐模式内核 |
| `Pal98Timer/SoundConfig.cs` | 音效配置单例、MCI/WMP 播放逻辑 |
| `Pal98Timer/SoundConfigForm.cs` | 音效配置窗口 UI |
| `Pal98Timer/KeyChangerDel.cs` | 改键功能、F11 改键开关 |

---

## 7. 最近改动

### 2026-09-03 会话（比赛计时器能力握手与精确名称显示，版本保持 3.37.0）

- 新增 `TournamentTimerCapability`，PalTimer 在进入主 UI 循环前发布 `Local\PAL98.PalTimer.TournamentLock.v1` 手动复位事件，并在退出时释放；异常只降级为 PALDLL 的提示，不阻断计时器
- 四个 PAL98DX9 系内核继续从已验证清单读取 `competition_display_name`；新增可执行反射断言，要求 `GetGameVersion()` 与该值完全相等，不追加 3.37.0 或普通补丁身份
- Release|x64 与专项回归通过，EXE SHA-256 `75FF6F1D7E0C6E46D4A2C81A821730D9D552FDADB31D50432ED1CBDDD1293A55`；已部署到 Steam PAL98 `Tools\PalTimer`，同时补齐含对应产品源码的 GPL 源码 ZIP 并重建与现场 `KeyChanger1.exe` 布局一致的清单/校验和；未改变文件/产品版本、PAL98_IPC、进程内存读写、70ms 循环、节点、计时、暂停或网络行为，产品源码随本次 3.37.0 增量发布收口

### 2026-09-03 会话（比赛锁 8 字符锁定者兼容，版本保持 3.37.0）

- `TournamentLockInfoReader` 的锁定者规范由 1–4 扩为 1–8 个汉字、英文字母或数字，与 ConfigTool/PALDLL 同步；没有改变 schema、完整性密钥、三快照校验或比赛显示名
- 回归清单改用 8 字符当前/旧版清单，继续验证篡改失败关闭、无锁兼容、四内核身份接线以及无 IPC/内存合同变化
- README 与本交接已同步；Release|x64 构建通过，EXE SHA-256 `9637902349D6BDAD2B5899B571C7067F1E4336ACFF6C88F48D9B213532590FAA`；未提交、未推送、未部署

### 2026-09-02 会话（PAL98 Tournament Lock v1 计时器身份，版本保持 3.37.0）

- 新增签名清单只读器和私有/开发资源构建门；严格校验 schema、比赛名/锁定者/四行标题、固定页脚、三份 INI 快照大小与 SHA-256
- PAL98DX9 在成功附加 PAL.exe 时只读取一次可信比赛身份，Dream220 显血与魂牵派生核心优先返回该精确名称；不欢乐高相似内核同步接入
- README 和回归脚本已更新；未改变 PAL98_IPC、内存读取、进程权限、节点、路线、暂停、反作弊、叠加或计时行为
- Release|x64 重建零错误、27 个既有 warning；产品/文件版本仍为 3.37.0；未提交、未推送、未部署

### 2026-08-30 会话（PAL98DX9 叠加第三列改为当前时间，版本保持 3.37.0）

- `Dx9OverlayTimelineEntry` 第三列由格式化差值改为 `HH:mm:ss` 当前累计时间，并单独保留比较秒数供既有快/慢配色使用
- `CreateDx9OverlayTimeline()` 只读取现有 `CheckPoint.Current` / `GetCHA()`；当前节点即使为零也显示 `00:00:00`，已完成节点显示完成时刻，未来节点留空
- 两个时间列均使用 76 逻辑像素，面板总宽、高、10Hz 刷新、默认关闭、快捷键、位置、配置和其他内核隔离均不变
- 更新静态契约与离屏反射测试；Release|x64 构建零错误、27 个既有 warning，EXE 文件/产品/程序集版本仍为 `3.37.0` / `3.37.0.0`
- 完整 GPL 候选位于 `artifacts/paltimer-3.37.0-overlay-current-time-20260830-r1`；源码 ZIP SHA-256 `15C47118DC80424D43633E458B2D24DCA64F8530FAA4A7CD1F0ACAA6E3D6388C`，独立解压重建保持 3.37.0。部署目录同步 EXE、源码 ZIP、README、发布清单和校验和；旧 EXE SHA-256 `A4D98490C0F42DFACB44D7138008134A587214DF0475C9291636B835187D838C`，新 EXE SHA-256 `D861DCBA4AF286BC0CD9D06B60FE971D6D0A840F497680045F1D30935FCC9593`；备份目录 `backup-before-overlay-current-time-20260830-134547`，用户状态及其余 27 个文件哈希不变

### 2026-08-25 会话（3.37.0 魂牵 / 梦幻22显血独立 PAL.exe 时间线）

- 保留 `梦幻22 / DREAM22` 为 `sdlpal` 历史内核；新增玩家可见的 `仙剑98柔情DX9魂牵 / PAL98DX9HUNQIAN` 与 `仙剑98柔情DX9梦幻22显血 / DREAM220VISIBLE`，Classic 继续使用 `仙剑98柔情DX9 / PAL98DX9`
- 三条 PAL.exe DX9 线共用既有 `仙剑98柔情DX9.GameObject` 和 70ms 只读循环，但使用独立 CoreName、best 文件和节点线；没有新增线程、网络、进程枚举、RPM/WPM 调用点
- Dream 接受 `pal98.dream220.compat@1.0.18` 和本次冻结的 `pal98.dream220.compat.drawcard.16e143813df5@1.0.18`；魂牵只接受 v1.57 Easy 1.0.2、Hard 1.0.2、Nonhuman 1.0.3 基础 profile。Dream 其他派生包、魂牵抽卡派生包以及 pointer/descriptor/hash/path 异常均失败关闭
- 接力捕获按内容扩展：Classic 176528 字节不变，魂牵 184688，梦幻显血 185872；只在用户主动接力/云存档时触发，不改变每 tick 读取
- 内容继续在 3.37.0 下演进；版本号升级仅由用户决定。11 个 Python、原 6 个 PowerShell 回归入口与新增 Owner/GPL guard 全通过；VS2026 Release x64 构建零错误、28 个既有 warning
- About、README 和根 `LICENSE` 已统一为仓库 Owner/当前维护者 `othercat`、`GPL-2.0-only`，历史 `ihouou/PalTimer` 只保留 upstream 归属；正式 EXE SHA-256 `BC9CA2441C600DBE19DBE49D9FD1387ED0D529104C463A012B2180C11D621F98`
- 早先误标为 3.38.0 的 r2/r3/r4 候选已退休，不再作为部署输入；功能代码没有回退。
- 2026-08-25 派生内容增量：`DREAM220VISIBLE` 仍使用同一独立时间线与 `bestDREAM220VISIBLE.txt`，但额外精确接受“显血 + 李逍遥专用扎麻神针 + 物抗1 + 全吴强”的冻结 profile，不把任意 Dream draw-card 家族泛化为受支持计时身份。定向 profile/路线/no-new-memory-write 回归及关键 PAL98DX9 既有回归通过，VS2026 `Release|x64` 重建零错误、28 个既有 warning
- 正式本地候选改为 `artifacts/paltimer-3.37.0-v158-redeploy-20260825-r5`：EXE SHA-256 `A4D98490C0F42DFACB44D7138008134A587214DF0475C9291636B835187D838C`，源码 ZIP SHA-256 `B429859C39894449AB398DB00CCDD6547AA3284971D7570C188E1C0CCDB67AFC`；EXE 文件/产品/程序集版本、发布清单和源码 ZIP 名均为 3.37.0
- r5 已部署到 `D:\PAL98_v1.58\Tools\PalTimer-3.37.0`；误部署的 3.38.0 目录备份在 `D:\PAL98_v1.58\_codex_backups\20260825-214129-timer-version-correction`，聚合部署回滚位于 `D:\PAL98_v1.58\_codex_backups\20260825-214135-dream220-derived-addon`。未启动 3.37.0，也未改 best、叠加布局、配置或计时数据
- 当前路线证据仅为 profile/resource 对齐与合成谓词；自然剧情、真实战斗、结局、完整跑线和接力存读档仍需实机验收

### 2026-08-24 会话（P 键重启权限误报稳定门）

- 新增 `PalProcessOpenRetryPolicy.cs`，使用 `Stopwatch` 为同一 PID 的错误5提供1.5秒稳定期；非错误5立即沿用旧错误，成功/换 PID/清理均复位
- PAL98、PAL98DX9、PAL98UNHAPPY 的 `CanOpenPalProcess` 与 `TryOpenPalProcess` 统一接入；真正持续的管理员权限不匹配提示和确认后退出语义不变
- 新增策略行为 harness，并扩展三内核权限结构检查；没有修改内存地址、RPM/WPM、计时、暂停、反作弊、节点、云存读档或 OBS
- 新版 `Pal98Timer.exe` 已备份旧版后部署到计时器3.37.0目录；来源/目标哈希一致，其他23个文件未变，未启动 PAL/PalTimer

### 2026-08-23 会话（PAL98DX9 游戏内信息叠加 3.37.0）

- 新增默认关闭的 PAL98DX9 透明叠加窗口，显示主时间、资源、暂停次数、战斗/空闲时间和前/当前/后3行节点；支持显式移动/等比缩放、字体/字号/颜色配置
- 功能入口合并为一个级联菜单；叠加开关快捷键默认未设置，冲突验证覆盖 F1-F12、Ctrl+Enter、无修饰键和节点音效开关快捷键
- 快捷键转发只扩展现有 `GForm` 全局键盘钩子和 `TimerCore` 虚方法；无第二钩子、无新增线程/轮询，关闭时仅初始化读取一次布局文件
- 仅修改 PAL98DX9 内核；PAL98、PAL98UNHAPPY 无叠加引用，PALDLL_DX9 IPC 未接入
- 3.37.0 主 EXE 已部署到用户指定目录，备份和来源/目标 SHA-256 已核对，用户启用状态和布局文件哈希未变

### 2026-07-27 会话（水灵珠节点实际坐标与小李逍遥触发范围门闩）

- 实机快照确认 PAL98 `BaseAddr+0x262/+0x264` 的旧 `X/Y=(912,1512)` 是视口坐标，`BaseAddr+0x274/+0x276` 的 `rX/rY=(1072,1624)` 是当前实际地图坐标，队伍朝向 `BaseAddr+0x26E=2`
- 最近的小李逍遥对象实机位于 `(1088,1616)`、TriggerMode=2、触发脚本 `0x8861`；有效 SSS 资源的最终自动位置脚本 `0x87E8` 同样给出 `(1088,1616)`
- PAL98/PAL98DX9 正常路线按实际地图坐标和朝向复现该对象的手动触发搜索点，在触发范围内记录当前水灵珠数量；只有后续仍在范围内读到数量增长才完成节点
- 十年前之前由随机物品/特殊剧情形成的任意水灵珠数量 `N` 位于触发范围外，不触发节点；之后进入范围只记录基线 `N`，交换脚本再次给予后读到大于 `N`（通常 `N+1`）才触发。离开范围后获得的物品不会沿用旧基线误触发，重新进入会重建基线
- 回梦无痕触发前也允许已有任意 `N`；实机连续五次快照确认卡牌跳转后稳定停在 `MapID=175`、场景 `204`、实际坐标 `(1168,760)`。重启使数量从临时 `4` 归一为 `1`，计时器因此仅在该精确固定位置且水灵珠数量至少为 `1` 时触发，不再检测大理对话或使用扩大矩形
- 70ms OnTick 只复用既有 `Area/rX/rY/PartyDirection` 和背包快照；已删除运行时 `SSS.MKF/M.MSG` 解析、进程附加资源入口及 `p1+0x500` 当前脚本状态读取
- 行为 harness 覆盖有效资源、小李逍遥四方向手动触发点、TriggerMode=2 最远允许点/首个禁止点、十年前前任意 `N`、正常 `N→N+1`、离开/重进、大理精确实际坐标、回梦数量 `0`/`1` 边界、相邻坐标、错误场景和reset
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

2026-09-03 比赛计时器能力握手与精确名称显示验证：

```text
PASS: .ai/tournament_lock_identity_regression_check.ps1（能力事件发布顺序/可发现性/释放、四内核精确显示、现有签名与篡改边界）
PASS: VS2026 MSBuild 18 Release|x64，0 errors；文件/产品版本保持 3.37.0
PASS: Pal98Timer.exe SHA-256 75FF6F1D7E0C6E46D4A2C81A821730D9D552FDADB31D50432ED1CBDDD1293A55
PASS: 部署到 D:\SteamLibrary\steamapps\common\PAL\PAL98\Tools\PalTimer；目标 EXE 与 Release|x64 同哈希，15 项发布清单和 16 行 SHA256SUMS 全通过
PASS: GPL 源码 ZIP SHA-256 832533F95FA3869AE690F07F68E0004CAE5A3481E5807D3FA3648BA2852BED7A，包含能力实现和回归源码、不含私有密钥
NOT RUN: 新版/旧版/未启动 PalTimer × 锁定 PAL.exe 实机启动提示和版本显示
```

2026-09-03 比赛锁 8 字符锁定者兼容验证：

```text
PASS: .ai/tournament_lock_identity_regression_check.ps1（8 字符当前/旧版清单、清单/快照篡改、无锁兼容、四内核接线、无 IPC/内存合同变化）
PASS: VS2026 MSBuild 18 Release|x64，0 errors，只有既有 warning
PASS: 文件/产品版本保持 3.37.0；SHA-256 9637902349D6BDAD2B5899B571C7067F1E4336ACFF6C88F48D9B213532590FAA
NOT RUN: 真实 5–8 字符锁定者比赛包、PAL.exe 与四内核计时器显示闭环
```

2026-09-02 PAL98 Tournament Lock v1 计时器身份验证：

```text
PASS: .ai/tournament_lock_identity_regression_check.ps1（有效签名、清单/快照篡改、无锁兼容、四内核身份接线、无 IPC/内存合同变化）
PASS: pal98dx9_title_identity / pal98unhappy_identity / pal_open_process_permission / dx9_overlay / dream220_visible 回归
PASS: VS2026 MSBuild 18 Release|x64，0 errors，27 个既有 warning
PASS: 文件/产品版本保持 3.37.0；SHA-256 2FA37E55E1F3F77618E51A7FAB11AC3DBAC227A800077757A8E4332DC4F8D69E
NOT RUN: 真实配置工具创建锁、PAL.exe 标题、四内核计时器显示、人工 INI 篡改恢复和解锁闭环
```

2026-08-30 PAL98DX9 三行时间线当前时间显示验证：

```text
PASS: .ai/dx9_overlay_regression_check.py（第三列读取节点当前累计时间、版本 3.37.0、默认关闭/10Hz/隔离/无新增内存读取）
PASS: .ai/dx9_overlay_interaction_regression_check.ps1（第三列字段、快慢比较、布局往返、离屏渲染、移动/缩放、穿透恢复）
PASS: VS2026 MSBuild 18 Release|x64，0 errors，27 个既有 warning
PASS: 文件/产品版本 3.37.0，程序集版本 3.37.0.0，SHA-256 D861DCBA4AF286BC0CD9D06B60FE971D6D0A840F497680045F1D30935FCC9593
PASS: 完整 GPL 候选与源码 ZIP 已生成；源码 ZIP 独立解压 Release|x64 重建通过，版本仍为 3.37.0 / 3.37.0.0
PASS: 部署目标与来源 SHA-256 一致；部署版离屏反射回归通过，EXE/源码/README/清单/校验和之外的 27 个文件哈希不变
NOT RUN: PAL98DX9 实机当前时间连续刷新、节点完成冻结、未来节点留空、字体/缩放裁切与30分钟性能对照
```

2026-08-25 Dream220 冻结派生 profile 补充验证：

```text
PASS: dream220_visible_regression_check.ps1（基础/精确派生接受，未知派生拒绝，路线谓词与 no-new-memory-write）
PASS: owner/license、PAL98DX9 title、process permission/retry、overlay、water pearl、cloud pause 关键回归
PASS: VS2026 MSBuild 18 Release|x64，0 errors，28 个既有 warning
PASS: GPL r3 源码 ZIP 在独立解压目录完成 Release|x64 重建，验证目录已清理
PASS: 部署 EXE/源码 ZIP 与 r3 候选 SHA-256 一致；备份位于 D:\PAL98_v1.58\_codex_backups\20260825-120311-dream220-derived-addon
NOT RUN: PalTimer/PAL.exe GUI、真实 Dream 路线、接力存读档、30 分钟性能与 Classic/魂牵隔离
```

2026-08-24 P 键重启权限误报补充验证：

```text
PASS: .ai/pal_process_open_retry_policy_regression_check.ps1（同 PID 边界、复位、换 PID、单调时钟回退、fail-closed）
PASS: pal_open_process_permission / pal98dx9_title_identity / dx9_overlay / banana_pause_resume / cloud_save_load_pause 回归
PASS: VS2026 MSBuild 18 Release|x64，0 errors，只有27个既有 warning
PASS: 部署版反射行为测试确认1.5秒边界、持续拒绝、非错误5和换 PID 语义；目标 SHA-256 77D5FD838E4EC496B0D91199603A851F2F79AB41A993CF2099075FB022FCDDB2
KNOWN FAIL: automation_snapshot_export_regression_check.py 仍失败在未修改的 TimerCore AutoTest envelope / gated tick snapshot 旧字符串匹配，与本轮权限重试无关
NOT RUN: 部署版普通权限连续 P 重启、管理员 PAL + 普通 PalTimer 持续拒绝、Win7 SP1
```

2026-08-23 PAL98DX9 叠加快捷键/字体颜色补充验证：

```text
PASS: .ai/dx9_overlay_regression_check.py（默认关闭、10Hz、单一菜单、快捷键冲突门、无抓屏/网络/额外内存读取、其他内核隔离）
PASS: .ai/dx9_overlay_interaction_regression_check.ps1（布局/颜色/快捷键往返、冲突拒绝、移动/缩放、渲染、reset、鼠标穿透恢复）
PASS: banana_pause_resume / cloud_save_load_pause / reset_clears_ui_pause / pal_open_process_permission / pal98dx9_title_identity / water_spirit_pearl_split 回归
PASS: VS2026 MSBuild 18 Release|x64，0 errors，27个既有 warning
PASS: 部署版隔离 STA 反射回归；来源/目标 SHA-256 D0D2AD09BD664A60AD44BB547EAE86D6A2AD5B675060D70978B0E07D52ECA42B
PASS: 部署前后 dx9_overlay 与 dx9_overlay_layout SHA-256 不变；PAL.EXE 保持运行，未启动 PalTimer
NOT RUN: 真实 PAL98DX9 游戏输入/快捷键、暂停/反作弊/读档/节点推进和关闭/开启各30分钟性能/速度对照
```

2026-07-27 水灵珠节点测试部署：

```text
目标：D:\SteamLibrary\steamapps\common\PAL\自动计时器\计时器3.36.6\Pal98Timer.exe
来源：Pal98Timer\bin\x64\Release\Pal98Timer.exe（commit 0893c0bf471c07f52b841580e061d3f93dbd5a86）
备份：D:\SteamLibrary\steamapps\common\PAL\自动计时器\计时器3.36.6\backup-before-water-pearl-position-20260727-114503\Pal98Timer.exe
旧 SHA-256：45301CEEA08CB7EDF5EBB6F18DD94D1FDF50BB5CC54955AE00B50C686CE47604
新 SHA-256：EDF6C0BFFF65342FCB4391C3FF64BBEDF6A3608BE007AA77C7AD5A3ADCA50D6A（来源与目标一致）
部署范围：仅 Pal98Timer.exe；配置、成绩、插件、plugin_auth 和其他依赖未覆盖
未执行：程序启动烟测、PAL98/PAL98DX9 正常交换、随机物品和回梦无痕实机路线
```

2026-07-27 水灵珠节点补充验证：

```powershell
& .\.ai\water_spirit_pearl_split_regression_check.ps1 -GameDirectory 'D:\SteamLibrary\steamapps\common\PAL\PAL98'
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" Pal98Timer.sln /m /t:Build /p:Configuration=Release /p:Platform=x64 /nologo
git diff --check
```

```text
REAL: normal=area 267/child (1088,1616)/trigger mode 2, Dali=area 204/fixed (1168,760)/count >= 1
PASS: 任意 N、小李逍遥四方向手动触发点、TriggerMode=2 边界、离开/重进、大理精确实际坐标+重启归一数量1、reset，以及运行时零脚本/资源依赖。
PASS: 70ms Observe 只使用既有场景/实际地图坐标/朝向/背包快照，PAL98UNHAPPY 未接入。
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
