# PalTimer UAC / KeyChanger 调查记录

日期：2026-06-12

## 背景

`pal98autotest` 在尝试以普通进程启动当前实机部署的 `Pal98Timer.exe` 时，`subprocess.Popen` 返回 `WinError 740`，错误文本为“请求的操作需要提升”。这说明部署包里的主计时器可执行文件当前会触发 UAC 提权。

用户提出的假设是：3.34.1 以前改键功能内置在主计时器里，可能需要管理员权限；3.34.1 以后 `KeyChanger.exe` 已独立，那么 `Pal98Timer.exe` 是否还能改成不需要 UAC 提权。

本轮只做源码调查和设计记录，不改 runtime 代码。

## 结论

初步判断：可以把 `Pal98Timer.exe` 主程序改为默认不需要 UAC 提权，最小候选改法是把 `Pal98Timer/Properties/app.manifest` 中的：

```xml
<requestedExecutionLevel level="requireAdministrator" uiAccess="false" />
```

改成：

```xml
<requestedExecutionLevel level="asInvoker" uiAccess="false" />
```

这个判断的关键依据是：当前 UAC 提示直接来自主程序 manifest，而不是 `KeyChanger.exe` 的独立 manifest 或 `KeyChangerDel.Open()` 的 `runas` 启动逻辑。

但这还不是可直接发布结论。改成 `asInvoker` 后必须做实机回归，确认 PAL98 / PAL98DX9 / PAL98UNHAPPY 的读内存、快捷键、F11 改键开关、云功能和发布部署场景没有被破坏。

## 源码证据

### 主计时器明确要求管理员权限

`Pal98Timer/Properties/app.manifest` 当前包含：

```xml
<requestedExecutionLevel level="requireAdministrator" uiAccess="false" />
```

`Pal98Timer/Pal98Timer.csproj` 当前包含：

```xml
<ApplicationManifest>Properties\app.manifest</ApplicationManifest>
```

因此从当前源码构建的主程序会嵌入该 manifest，并在普通启动时触发 UAC。`pal98autotest` 观察到的 `WinError 740` 与这个 manifest 行为一致。

历史检查：

- `git log -S "requireAdministrator" -- Pal98Timer/Properties/app.manifest Pal98Timer/Pal98Timer.csproj` 显示该要求从初始提交 `6a93509` 就存在。
- README 的 v0.22 记录“程序运行时会自动以管理员身份运行了”，对应早期设计。
- README 的 v3.34.1 记录“改键功能现在分成了一个单独的 exe 程序：改键器”，但这次拆分没有同步把主程序 manifest 降为 `asInvoker`。

### KeyChanger 独立后没有发现它自己强制 UAC

`KeyChanger/KeyChanger.csproj` 没有 `ApplicationManifest` 配置，也没有在仓库中发现 `KeyChanger` 专用 manifest。

`Pal98Timer/KeyChangerDel.cs` 通过同目录的 `KeyChanger.exe` 启动改键器：

```csharp
kcp = new Process();
kcp.StartInfo.FileName = delpath;
kcp.Start();
```

这里没有设置 `Verb = "runas"`，也没有显式要求 shell 提权。也就是说，当前源码里主计时器启动 KeyChanger 时不会主动要求 KeyChanger 管理员运行。

KeyChanger 自己的核心是：

- `KeyChanger/KeyboardLib.cs` 使用 `SetWindowsHookEx(WH_KEYBOARD_LL)` 安装低级键盘 hook。
- `KeyChanger/MainForm.cs` 使用 `keybd_event` 重放映射后的按键。

这类普通桌面键盘 hook / key event 对非提升窗口通常不需要管理员权限；但如果目标游戏或目标窗口本身是管理员权限运行，Windows UIPI 会限制低完整性进程影响高完整性进程。这种情况下需要“同完整性级别”，不一定意味着主计时器永远要管理员运行。

### 主计时器本身仍安装低级键盘 hook

需要注意：3.34.1 后并不是所有键盘逻辑都移到了 KeyChanger。主程序 `Pal98Timer/GForm.cs` 仍然：

- 创建 `KeyboardLib`；
- 调用 `_keyboardHook.InstallHook(this.OnKeyPress)`；
- 在 `OnKeyPress` 中处理 F9 暂停、F10 重置、F11 改键开关等快捷键。

所以去掉主程序 UAC 后，要回归主计时器自己的 F9/F10/F11 快捷键，不只测 `KeyChanger.exe`。

### 内存读取/写入不必然要求管理员，但有风险点

PalTimer 多个内核使用：

```csharp
Kernel32.OpenProcess(0x1F0FFF, false, PID)
ReadProcessMemory(...)
WriteProcessMemory(...)
```

对同用户、非提升的 PAL/PAL98DX9 进程，普通权限通常可以 `OpenProcess` / `ReadProcessMemory`。如果目标游戏以管理员运行，则非提升计时器可能打不开进程或读写失败。

风险点：

- 当前 `OpenProcess(0x1F0FFF)` 请求的是接近全权限，权限面偏大；即使只读场景也可能因为权限过宽而更容易失败。
- `WriteProcessMemory` 出现在部分非 PAL98DX9 路径，例如 Steam 版快速对白、仙剑二 Steam、古剑二等功能路径。`asInvoker` 对这些写内存功能的影响需要单独验证。
- PAL98DX9 默认 speedrun 验证路径主要关注读内存和节点判定，但不能因此证明其他内核都安全。

## 为什么这很可能不是 KeyChanger 必须提权导致

1. 实际失败是启动 `Pal98Timer.exe` 时直接返回 `WinError 740`，此时还没有进入主程序逻辑，也不是启动 `KeyChanger.exe` 失败。
2. 主程序 manifest 明确写了 `requireAdministrator`。
3. `KeyChanger.exe` 项目没有发现自己的 `requireAdministrator` manifest。
4. 主程序启动 KeyChanger 没有 `runas`。
5. 3.34.1 拆分 KeyChanger 后，README 记录了架构变化，但源码 manifest 仍保留早期主程序提权策略。

因此，当前更准确的说法是：

> PalTimer 主程序仍因历史 manifest 设置而强制 UAC；KeyChanger 独立后，主程序不一定还需要把管理员权限作为默认启动条件。

## 候选实现方案

### 方案 A：主程序改为 asInvoker

最小改动：

- 修改 `Pal98Timer/Properties/app.manifest`：
  - `requireAdministrator` -> `asInvoker`
- 保持 `uiAccess="false"`。
- 不改 `KeyChanger.exe`。
- 不改内存读取和节点判定代码。

优点：

- 最小 diff。
- `pal98autotest` 可以从普通用户进程启动 PalTimer。
- 普通用户启动计时器不再弹 UAC。
- KeyChanger 仍可作为独立进程按普通权限运行。

主要风险：

- 如果用户把 PAL/PAL98DX9 或其他游戏以管理员运行，非提升 PalTimer 可能无法读内存或无法影响该窗口快捷键。
- 如果计时器安装目录不可写，例如放在受保护目录，`size`、`LastCore`、配置、best 文件、背景图、音效配置等写文件路径可能失败。当前 manifest 无论 `requireAdministrator` 还是 `asInvoker`，只要指定 `requestedExecutionLevel` 都会禁用文件/注册表虚拟化；不要指望虚拟化兜底。
- 某些依赖写内存的非 PAL98DX9 功能可能需要更高权限或更精细的 `OpenProcess` 权限拆分。

### 方案 B：主程序 asInvoker，必要时提示“游戏正在管理员运行”

在方案 A 之后，如果发现 `OpenProcess` 失败，可以后续加清晰提示：

- PalTimer 当前非管理员；
- 目标游戏可能是管理员权限；
- 建议用相同权限级别启动两者，或不要管理员运行游戏。

这比默认强制 PalTimer 提权更适合自动化和普通用户启动。

### 方案 C：保留普通主程序，单独给 KeyChanger 或特定 helper 提权

如果实测发现只有改键器对某些场景需要高完整性，可以考虑：

- 主程序默认 `asInvoker`；
- KeyChanger 普通启动失败或目标窗口提升时，用户手动选择“以管理员方式启动改键器”；
- 或提供独立的 elevated helper。

这不是第一阶段建议，因为会扩大实现范围，也会影响 UAC、窗口消息和关闭生命周期。

## 建议第一阶段开发任务

给后续 PalTimer Codex Session 的建议任务：

1. 新建分支。
2. 只改 `Pal98Timer/Properties/app.manifest`，把主程序从 `requireAdministrator` 改为 `asInvoker`。
3. 使用 VS2026/MSBuild 18 构建 `Release|x64`。
4. 检查生成的 `Pal98Timer.exe` manifest 确实为 `asInvoker`。
5. 用非提升 PowerShell 或 `pal98autotest real paltimer-launch` 启动生成产物，确认不再出现 `WinError 740`。
6. 做人工实机回归，确认 PAL98DX9 常规读内存、计时节点和快捷键行为。

## 必跑回归清单

### 静态 / 构建

- `git diff --check`
- VS2026/MSBuild 18：`Pal98Timer.sln` `Release|x64`
- 检查产物 manifest：
  - `requestedExecutionLevel=asInvoker`
  - 未恢复 x86 输出
  - `PalCloudLib.dll` 仍按 x64 部署

### 非提升启动

- 从普通 PowerShell 启动 `Pal98Timer.exe`：
  - 不弹 UAC；
  - 主窗口出现；
  - `KeyChanger.exe` 按现有逻辑随主程序启动；
  - 关闭 PalTimer 时 KeyChanger 能跟随退出。

### PAL98DX9 实机

- 启动普通非提升 PAL98DX9。
- PalTimer 选择 `仙剑98DX9` 内核。
- 确认标题/版本识别正常。
- 确认 Map/坐标/物品/战斗等读内存路径没有明显失败。
- F9 暂停/恢复。
- F10 重置。
- F11 启用/禁用 KeyChanger。
- 关闭游戏后计时器不刷“窗口句柄无效”。

### KeyChanger

- 主程序启动后，确认 `KeyChanger.exe` 存在。
- F11 打开改键功能，按钮变橙。
- 打开改键设置窗口。
- 设置一个安全映射，确认普通非提升 PAL98DX9 窗口可收到映射后的输入。
- F11 再次关闭改键功能，KeyChanger 退出或禁用符合当前语义。

### 权限边界

- 如果 PAL98DX9 被管理员方式启动，记录 PalTimer 非提升时的失败表现：
  - 读内存失败？
  - 快捷键失效？
  - KeyChanger 输入失效？
- 这个场景可以作为“需要同权限级别”的已知限制，而不一定阻塞主程序默认 `asInvoker`。

### 其他内核抽样

至少抽样以下路径，避免 PAL98DX9 以外的功能被权限变化伤到：

- `PAL98` 原版内核：启动、读内存、F9/F10。
- `PAL98UNHAPPY`：启动、读内存、F9/F10。
- 有写内存功能的内核或功能，例如 Steam 快速对白路径：确认失败时有可理解表现，不要静默破坏计时。

## 对 pal98autotest 的影响

如果主程序改为 `asInvoker` 并通过验证：

- `pal98autotest real paltimer-launch` 应能从普通进程直接启动 PalTimer。
- PalTimer launch report 应从 `failed_process_start / winerror 740` 变成 `completed_left_running` 或等价的 process-start success。
- 仍然不能把“进程已启动”当作官方计时或 split 证据；它只说明计时器进程可被编排启动。

## 暂时不要做

- 不要把 KeyChanger 逻辑重新塞回主程序。
- 不要为了去 UAC 顺手改 `OpenProcess` / `ReadProcessMemory` / `WriteProcessMemory` 行为。
- 不要恢复 x86 输出。
- 不要替换或重做 `PalCloudLib.dll`。
- 不要把 `uiAccess=true` 当作普通解决方案；那会引入签名和受信任安装位置要求。
- 不要把 PALDLL_DX9、PAL98 游戏目录或 pal98autotest 的真实路径写入源码文档。

## 当前判断

建议后续先按方案 A 做一个小分支验证。若普通非提升 PAL98DX9 读内存、F9/F10/F11、KeyChanger、云验证和关闭生命周期都正常，就可以把主程序默认 UAC 提权去掉。

如果某些用户确实需要管理员权限，优先把它作为文档化的“目标游戏提升时需要同权限级别”或可选启动方式，而不是继续让所有 PalTimer 启动都强制 UAC。

## 后续实施记录

同日后续会话已按方案 A 做最小实现：

- 分支：`codex/paltimer-uac-asinvoker`
- 修改：`Pal98Timer/Properties/app.manifest` 的有效 `requestedExecutionLevel` 从 `requireAdministrator` 改为 `asInvoker`
- 未修改：KeyChanger、C# 运行时代码、工程文件、`OpenProcess` / `ReadProcessMemory` / `WriteProcessMemory`、x64 发布配置、真实部署目录

已完成验证：

- `Pal98Timer.sln` `Release|x64` 构建通过
- 使用 Windows SDK `mt.exe` 抽取 `Pal98Timer/bin/x64/Release/Pal98Timer.exe` manifest，确认有效节点为 `level="asInvoker"`
- 从普通 PowerShell 启动 `Pal98Timer/bin/x64/Release/Pal98Timer.exe` 成功，不再出现 `WinError 740`
- `git diff --check` 通过
- `.ai/banana_pause_resume_regression_check.py` 和 `.ai/cloud_save_load_pause_regression_check.py` 通过

仍需人工实机验证：

- 普通权限 PAL98DX9/PAL98/PAL98UNHAPPY 的读内存、F9/F10/F11、KeyChanger、云功能和关闭游戏生命周期
- PAL98DX9 以管理员权限启动时，非提升 PalTimer 的读内存、快捷键和 KeyChanger 失败表现

## 权限失败提示后续实施记录

根据后续确认：如果 PAL.exe 被管理员权限启动，而 PalTimer 以普通权限启动，PalTimer 很可能无法打开目标进程句柄并读取内存。已做最小提示增强：

- `Pal98Timer/Kernel32.cs`：给 `OpenProcess` 增加 `SetLastError=true`，并暴露 `ERROR_ACCESS_DENIED=5` / `GetLastWin32Error()`
- `Pal98Timer/仙剑98柔情.cs`
- `Pal98Timer/仙剑98柔情DX9.cs`
- `Pal98Timer/仙剑98柔情不欢乐模式.cs`

三套 PAL98 内核现在在找到 Pal.exe 但 `OpenProcess(0x1F0FFF, ...)` 返回 0 时：

- 不设置 `PID`，避免进入“看似已连接但句柄无效”的状态
- 通过 `cryerror` 显示一次提示，避免循环刷屏
- 当 Windows 错误码为 5 时，提示 PAL.exe 可能以管理员身份运行，并建议用普通权限重启 PAL.exe，或让 PalTimer 与 PAL.exe 使用相同权限级别

新增结构性检查：

```bash
C:\Users\other\miniconda3\envs\paltools-hermes\python.exe .ai\pal_open_process_permission_regression_check.py
```

已完成验证：

- `Pal98Timer.sln` `Release|x64` 构建通过
- `git diff --check` 通过
- `.ai/pal_open_process_permission_regression_check.py` 通过
- 既有香蕉树反作弊暂停和云/接力暂停结构性检查继续通过

仍需人工实机验证：

- 普通权限 PAL.exe + 普通权限 PalTimer：确认 PAL98/PAL98DX9/PAL98UNHAPPY 读内存、F9/F10/F11、KeyChanger 行为不变
- 管理员权限 PAL.exe + 普通权限 PalTimer：确认提示出现一次且不刷屏，记录读内存、快捷键和 KeyChanger 的失败表现
