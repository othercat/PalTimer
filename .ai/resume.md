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
   - `CLAUDE.md`
   - `TODO.md`
   - `.ai/decisions.md`
   - `.ai/env.md`
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

当前维护分支：`codex/paltimer-opacity-net472`。

task-111 / task-112 已完成代码层和构建验证，等待或已经进入 checkpoint commit / push。主要工作集中在：
- 音效系统（节点音效配置、音效开关快捷键）
- 稳定性修复（窗口句柄刷屏、MP3 播放兼容）
- 三个高相似内核（PAL98/PAL98DX9/PAL98UNHAPPY）的功能同步
- UI 可读性：透明度功能当前语义为“背景图透明度”，文字、按钮和计时数字保持不透明。
- 工具链：项目目标框架已从 .NET Framework 4.0 / 4.5 升级到 .NET Framework 4.7.2，便于 VS2026/MSBuild 18 编译，并保留 Win7 SP1 运行时目标。

---

## 3. 已完成内容

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

1. 读取 `docs/TODO-pal98-dx9-updates.md` 了解完整开发计划
2. task-111 需要 Human 打开计时器实测：背景图透明度变化时，文字、按钮和计时数字保持不透明；OBS 截取框可按直播需求另行调透明度
3. task-112 需要发布前确认 Win7 SP1 目标机已安装 .NET Framework 4.7.2 runtime
4. task-014 和 task-015 代码层修复已完成，仍需实机验证
5. 实机测试 task-015：云存档、云读档、接力存档、接力接盘操作完成后均保持暂停，用户手动恢复计时
6. 实机测试 task-014：站到香蕉树→F9暂停→拿香蕉→F9恢复，确认计时器能恢复
7. 补测普通路径：站到香蕉树→拿香蕉自动恢复；普通 F9 暂停不应被误启动
8. 如需发布版本，再决定是否更新 README.md 版本记录

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
| `Pal98Timer/仙剑98柔情DX9.cs` | DX9 内核、进程检测、物品/战斗统计、节点定义 |
| `Pal98Timer/仙剑98柔情.cs` | 原 98 内核 |
| `Pal98Timer/仙剑98柔情不欢乐模式.cs` | 不欢乐模式内核 |
| `Pal98Timer/SoundConfig.cs` | 音效配置单例、MCI/WMP 播放逻辑 |
| `Pal98Timer/SoundConfigForm.cs` | 音效配置窗口 UI |
| `Pal98Timer/KeyChangerDel.cs` | 改键功能、F11 改键开关 |

---

## 7. 最近改动

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
- `仙剑98柔情.cs`/`仙剑98柔情DX9.cs`/`仙剑98柔情不欢乐模式.cs`：删除 `EnableNonSequentialCheck = true` 硬编码；`LoadGame()` 中保存/恢复 `TotalMonsterCount`（云读档不清零撞怪）；`UI_SaveGameEx`/接力-接盘/云读档暂停状态保持修复
- `GEX.cs`：删除 `BuildCenteredMainTimer()` 和 `MeasureTextSize()`，恢复固定偏移布局
- `KeyChangerDel.cs`：F11 改键兼容"改建器"和"改键器"两种窗口标题
- `README.md`：合并 v3.36.2/3/4 为统一的 v3.36.4 更新说明
- `docs/TODO-pal98-dx9-updates.md`：添加 DONE 段落

---

## 8. 测试状态

本轮编译命令：

```bash
"/c/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/MSBuild.exe" Pal98Timer.sln -p:Configuration=Release -p:Platform=x64 -verbosity:minimal
```

本轮编译结果：

```text
失败 — MSB3644，当前机器缺少 .NETFramework,Version=v4.0 的引用程序集 / targeting pack；项目尚未进入 C# 编译阶段。
```

本轮已通过的静态回归检查：

```bash
C:\Users\other\miniconda3\envs\paltools\python.exe .ai\banana_pause_resume_regression_check.py
C:\Users\other\miniconda3\envs\paltools\python.exe .ai\cloud_save_load_pause_regression_check.py
```

```text
PASS: all PAL98 kernels clear existing anti-cheat pause before HasStartGame.
PASS: PAL98 cloud/relay save-load paths leave UI pause enabled.
```

上一轮成功编译结果：

```text
通过 — Build succeeded, 0 errors, 只有不相关 warning
```

实机测试需手动进行（需要仙剑98游戏环境）：
- 音效试听：选择 MP3/WAV 文件点击试听，确认能播放或有错误提示
- 音效开关快捷键：配置后运行中按快捷键，确认切换生效
- 主计时布局：确认毫秒紧跟主时间，不出现过大间距
- 详见 `docs/TODO-pal98-dx9-updates.md` 末尾的实机验证清单

---

## 9. 多电脑环境差异

### 当前开发环境

- 项目路径：`C:\SourceCodes\GithubRepos\PalTimer`
- 主要环境：Windows 11 Pro，VS2026 (v18)
- 编译目标：Release|x64
- .NET Framework 4.0 目标

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
