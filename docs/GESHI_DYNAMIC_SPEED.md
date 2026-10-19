# 隔世追凶正常加减速

仅 `SdlpalGeshi` 允许运行中改变 `speed`、`battleSpeed`、`walkFrameMs`、`fadeCycleMs`，不再因此产生“计时期间资源、引擎或速度配置变化”。资源 profile、四项资源哈希、引擎 EXE 哈希和 build 仍参与完整性检查，观察到变化后不会因恢复原值而自动清除异常。

现行服务端 `timer_online_review.py` 比较开局和结束的完整 `gameplay`、`ranking.configuration_id` 及 `ranking.rules`，不同即记录 `changed_gameplay` / `changed_configuration`。为兼容既有 12 键契约，本轮榜单事实和本地最佳线使用首次开始计时时的四个速度值；资源和引擎仍取当前观察。重置后重新绑定起始速度。暂停、重连或调速不重新绑定分类，不清除已记录的读档、事件缺口及人工辅助标记。

这是起始配置分类，不表示全程速度未变。实际即时速度始终保留在游戏快照、`CurrentSnapshot` 及导出的 `SdlpalProfileData`；快照过期、格式错误、诊断模式及缺少有效时序仍会判无效。游戏自己按即时速度计算的配置尾号可能和本轮计时器分类不同，不能将其解释为服务器确认的分类或回执。

PB 文件命名及旧文件不变，本轮加减速不会换掉已加载的参考值和昵称，物品检测也保留已观察到的净变化。重置时起始速度不同仍选另一分类，不自动迁移或覆盖旧最佳线。时间线和 v1 接力包保留原身份字段格式，允许四个合法速度字段不同；仍核对资源、引擎、路线、存档摘要、进程创建时间和 epoch。接力成功后仍暂停且属于辅助跑次。

不修改 PAL98、PAL98DX9、PAL98UNHAPPY 的速度检查，也不改共享 CompetitionClient 的变更锁存或服务端审核。没有更改进程匹配、内存地址、读写进程内存、认证、版本号或冻结发布件。

2026-10-18 定向验证（共享源码参数均为 `-PalSharedSourceRoot D:\Workspace\KnowledgeRoots\PAL\othercat\PALDLL_DX9\shared`）：

- `tests/run_sdlpal_tests.ps1 -NoGui`：VS2026 Release|x64 构建成功，443 项通过。覆盖四个速度字段连续增减、开局/结束配置完全一致、PB 与物品变化保留、重置重新分类、不同速度时间线及接力、资源/引擎/路线拒绝，以及先前的重连和 F8/F9 修复。证据 `artifacts/sdlpal-tests-20261018-214137-420`。
- `tests/run_live_tests.ps1`：124 项通过。证据 `artifacts/lt-76829d81`。
- `tests/run_competition_tests.ps1`：266 项通过，包括传统核心的瞬时速度/规则变化仍被锁存。证据 `artifacts/ct-20261018-214246-219`。首次运行在已有 `CompatibilityCases` 的异步 pending 读取处遇空（`artifacts/ct-20261018-214212-550`）；未改实现，单独重跑通过。
- `git diff --check` 通过。SDL 首轮新增 fixture 错把运行中的 `PTimer.SetTS` 当作仍保持走表，改为测试侧先 Stop→SetTS→Tick 后通过；未改公共时钟，失败日志保留在 `artifacts/sdlpal-tests-20261018-213941-384`。

测试使用隔离的本地映射和离线传输替身，没有启动用户游戏/计时器 GUI 或向生产服务上传模拟成绩。发布后仍需实际键盘连续加减速、跨速度接力/云档往返及真实服务回执验证。版本保持 PalTimer 3.38.3.0 / Online 1.0.0.5；发布需冻结新的 EXE/Online DLL、重新签发匹配的 Auth 与公开登记，并按新字节申请批准。旧云登记与服务端构建批准仍分别处理。
