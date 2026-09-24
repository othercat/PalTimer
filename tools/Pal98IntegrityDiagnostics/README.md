# PAL98 文件与运行核验诊断

用于 v1.68 r10 配套 PalTimer 3.37.5 出现“测试版”（旧构建显示“文件不匹配”）等提示时收集证据。它使用**当前实际运行的计时器 EXE 中内嵌的清单**，不使用可替换的外部清单。它不修改游戏文件、配置、进程内存或成绩，也不会开始或停止计时、联系云服务、退出游戏或启动第二个游戏。

1. 在发生问题的 Windows 中解压工具到任意可写目录。不要在 ZIP 内直接运行。
2. 启动发生问题的游戏和计时器，只保留各一个实例；停在标题画面即可。
3. 双击 `Run-Diagnostic.cmd`，保持游戏运行，最多等四分钟。
4. 把生成的 `Reports/日期编号` 文件夹内的报告发回。报告含 Windows 架构、程序路径、文件身份、失败项目和运行代码核验结果；不收集云 ID、账号密码、存档或原始配置内容。

请在有问题的 Parallels Windows 11 ARM 虚拟机内运行，不能在 macOS 终端运行。此工具构建为 x64，沿用计时器的 .NET Framework 4.7.2 运行要求，通过系统模拟执行；ARM 实机执行结果仍待本次取证，不能提前宣称已通过。

`diagnostic.json` 中的 `final.file_verifier_state` 和 `final.file_verifier_detail` 是文件检查本身的结果；`final.code` 和 `final.detail` 包含进程代码及访问情况。`file_checks` 为固定核心、当前内容和两个图形候选组合分别列出实际及预期哈希。**未选中的图形候选不匹配属于正常情况**，以 `graphics_chain` 和最终结果为准。`Incomplete` 是没有完成核验，不等同于篡改。

同一游戏进程已经确认过的异常仍会保留；本工具启动的是独立只读检查，不能清除计时器已经保存的异常，也不作为比赛认证。

2026-09-25 起，新计时器的测试版标签只依据 PAL.dll 白名单（最新 1.68、指定 1.14、指定 1.02）。新版报告同时输出 PalDll、PalDllVersion、PalDllMismatchSeen；其它文件仍按当前完整清单记录，旧版完整包的文件清单 Mismatch 不等同于 DLL 为测试版。“运行诊断不可用”只保留为诊断状态，不显示在标题。

开发用参数：`Start-Diagnostic.ps1 -GameRoot <目录> -TimerExe <实际计时器EXE> -ReportDirectory <输出目录> -NoPause`。源码 `Pal98IntegrityProbe.cs` 基于 `tests/v169_readonly_probe.cs`，按仓库 GPL-2.0-only 许可提供。只需使用 VS 2026 Roslyn 编译该文件为 x64 控制台程序，不需要重新编译、覆盖或激活正式计时器。
