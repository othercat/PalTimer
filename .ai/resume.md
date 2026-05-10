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

技术栈：C# / .NET Framework 4.0 / WinForms / GDI+，编译目标 x64，最低系统 Win7 SP1。

---

## 2. 当前工作状态

**版本：v3.36.3**（2026-05-10）

当前 master 分支干净，所有近期改动已提交。主要工作集中在：
- 音效系统（节点音效配置、音效开关快捷键）
- 稳定性修复（窗口句柄刷屏、MP3 播放兼容）
- 三个高相似内核（PAL98/PAL98DX9/PAL98UNHAPPY）的功能同步

---

## 3. 已完成内容

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

### 已知问题
- **主计时居中**：曾在 v3.36.2 中尝试用 `Graphics.MeasureString` 实现居中，但因文字居中对齐（`sfCC`）导致 `*` 号切换时毫秒位置跳动，v3.36.3 已回退。当前使用固定偏移+左对齐布局，部分用户电脑上主时间视觉不完全居中。需要后续重新设计居中方案。

### 后续优化方向（P2）
- 抽取 PAL98/PAL98DX9/PAL98UNHAPPY 共用模块，降低三处同步成本
- 借鉴 LiveSplit / livesplit-core 的架构扩展（Split 状态模型、Layout/Rendering 分离、Auto Splitter 抽象、多对比线）

---

## 5. 当前最重要的下一步

新的 AI 接手后，优先做以下事情：

1. 读取 `docs/TODO-pal98-dx9-updates.md` 了解完整开发计划
2. 如需修 bug，先确认问题是否可复现
3. 如需编译，使用 VS2026 的 Release|x64 配置（见下方编译命令）
4. 修改后需在 README.md 更新版本记录，在 TODO 更新状态

---

## 6. 关键文件说明

| 文件 | 作用 |
|---|---|
| `README.md` | 项目说明、快捷键、版本更新记录 |
| `docs/TODO-pal98-dx9-updates.md` | 详细开发计划、验收清单 |
| `.ai/resume.md` | 当前交接文件（本文件） |
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

### 2026-05-10 会话

- `SoundConfig.cs`：新增 `MciOpenFile()` 尝试多种 MCI 设备类型；新增 `PlayMp3WithWmp()` WMP COM 回退；`TestPlay()`/`PlaySound()`/`PlayToggleSound()` 统一使用 MCI→WMP 链式播放
- `SoundConfigForm.cs`：快捷键提示标签从内联改为独立一行，修复文字截断
- `GEX.cs`：删除 `BuildCenteredMainTimer()` 和 `MeasureTextSize()`，恢复 commit `769df93` 的固定偏移+左对齐布局
- `GForm.cs`：版本号更新为 `3.36.3`
- `README.md`：添加 v3.36.3 更新说明，标记 v3.36.2 居中为已回退
- `docs/TODO-pal98-dx9-updates.md`：添加 v3.36.3 DONE 段落，更新居中为已回退并记录原因

---

## 8. 测试状态

最近一次编译命令：

```bash
"/c/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/MSBuild.exe" Pal98Timer.sln -p:Configuration=Release -p:Platform=x64 -verbosity:minimal
```

编译结果：

```text
通过 — Build succeeded, 0 errors, 0 warnings (或少量不相关 warning)
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
