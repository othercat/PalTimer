# Windows Defender 拦截 / 误报部署知识

更新时间：2026-05-18

状态：部署知识沉淀。用于后续 PalTimer 发布、交接和问题排查；不表示当前文件一定安全，也不建议无条件关闭安全软件。

## 1. 触发背景

在将 `Pal98Timer.exe` 3.36.5 x64 build 单独发给他人覆盖旧版本后，对方 Windows 安全中心提示：

```text
Behavior:Win32/DefenseEvasion.A!ml
严重
```

本机事件日志中此前还确认过另一个独立问题：如果部署 `Any CPU` 输出，启动时可能因 32/64 位不匹配触发：

```text
System.BadImageFormatException
at Pal98Timer.GForm.InitCloud()
```

该启动崩溃与 Defender 拦截是两个问题：

- `BadImageFormatException`：通常是误用了 `Any CPU` 输出，导致 32 位进程加载 64 位 `PalCloudLib.dll`。
- `Behavior:Win32/DefenseEvasion.A!ml`：是 Defender 对未签名程序行为的机器学习拦截，可能发生在正确 x64 build 上。

## 2. 已知触发因素

PalTimer 的功能天然包含若干安全软件敏感行为：

- 程序启动时安装低级键盘 hook：`SetWindowsHookEx(WH_KEYBOARD_LL)`。
- 自动计时需要打开游戏进程：`OpenProcess(...)`。
- 自动计时和部分功能需要读取游戏进程内存：`ReadProcessMemory(...)`。
- 云读档、辅助或旧功能路径中存在写进程内存能力：`WriteProcessMemory(...)`。
- 发行文件未代码签名。
- 单独发送 `.exe` 覆盖旧版本时，没有签名信誉、下载信誉和完整包上下文。

这些行为组合容易被 Defender / SmartScreen / 第三方杀软判为可疑工具、进程操控或规避类行为。自动计时器领域很容易踩这类误报。

## 3. 排查顺序

遇到“打开闪退”或“安全中心拦截”时，不要立即加白或修改代码。先分清是哪一类：

1. 查看 Windows 安全中心“保护历史记录”，确认“受影响的项目”是否是 `Pal98Timer.exe`。
2. 查看事件查看器 Application / `.NET Runtime`：
   - 如果是 `System.BadImageFormatException`，优先检查是否误用了 `Any CPU` 输出。
   - 如果是 `Behavior:Win32/DefenseEvasion.A!ml`，优先按安全软件误报/信誉问题处理。
3. 对比文件 hash，确认对方运行的是本次构建产物。
4. 确认部署输出来自：

```text
Pal98Timer\bin\x64\Release
```

5. 不要把旧版 `KeyChanger1.exe`、旧 `ModuleAddrX86Delegate.exe` 或未知来源文件混入新的测试包。

## 4. 短期测试建议

短期发给熟悉来源的测试者时，建议：

1. 优先发送完整 x64 发布目录压缩包，而不是裸发单个 exe。
2. 明确说明该程序会读取游戏进程内存并注册全局快捷键，因此可能触发杀软行为拦截。
3. 让测试者只在确认文件 hash 和来源可信时，临时允许该文件。
4. 不建议指导普通用户关闭 Defender 或全盘加白。

当前 x64 build 的已知本机 hash 示例：

```text
Pal98Timer.exe
SHA256: 3C637B91319E2CF1C1A8BCDB24C2E81531962E246CC0F3E19FD68FB0DC243B27
```

hash 只用于本次诊断记录；后续重新编译后会变化。

## 5. 长期改进方向

后续如要面向更多用户发布，应单独开任务评估：

- 代码签名：最有效，能积累 SmartScreen / Defender 信誉。
- 发布包完整性：提供 zip / installer、hash、版本说明和来源说明。
- 降低可疑行为面：
  - 不在启动第一时间安装低级键盘 hook，改为用户启用快捷键或进入主界面后再安装。
  - 减少 `OpenProcess(0x1F0FFF)` 这类全权限请求，按读取/写入能力拆分权限。
  - 将 `WriteProcessMemory` 能力隔离到明确功能路径，并在文档中说明用途。
  - 对改键器、模块地址辅助程序和主程序分别做 x64 / x86 发行边界说明。

这些改动可能影响快捷键、改键、内存读取、云读档或比赛规则，不应作为热修随手改。

## 6. 当前结论

- PalTimer 3.36.5 版本号保持不变。
- 编译/部署应使用 `Release|x64` 输出，不使用 `Any CPU` 输出部署。
- Defender `Behavior:Win32/DefenseEvasion.A!ml` 需要按“敏感行为 + 未签名 + 信誉不足”的方向处理。
- 对外发布前，代码签名和发布包信誉是比临时加白更稳妥的方向。
