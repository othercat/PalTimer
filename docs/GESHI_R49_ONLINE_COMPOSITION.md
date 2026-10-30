# 隔世 r49 联机组件修复

Refs #34；Refs othercat/sdlpal-lostedit#1；Refs othercat/sdlpal-lostedit#66。

r37–r47 更新为计时器 3.38.3.1，r48 更新为 3.38.3.2，但仍携带绑定 r30 / 3.38.3.0 的认证及公开登记。实际加载器因此报 `Host identity failed`，尚未发起服务器连接。服务器地址和启用开关没有丢失。

r49 保留 3.38.3.2 的计时器 EXE（提交 `2638365f0d35f94654d77329430fd2c13e55e36e`），从相同源码重新构建 Online 1.0.0.7，使 Online 使用当前普通/吴强桥接及规则校验；再为冻结的 EXE/Online 正常签发匹配 Auth 和 Candidate.v2。现有发布者身份、签名检查、组件哈希检查保持。

发布必须一起核对 `Pal98Timer.exe`、`PalTimerOnline.dll`、`PalCompetitionAuth.dll` 和 `PalCompetitionRegistration.public.json`。实际加载器通过后才允许打包；缺组件、修改 EXE/Online/Auth、损坏签名和错误 API 必须仍被拒绝。`run_online_module_tests.ps1 -OutputDirectory` 可把证据放入实际 Issue 产物目录。

正常签发及本地加载通过不代表服务器批准。候选登记、管理员批准和真实连接分别记录在游戏仓 Issue #1 与本题；连接验证不上传合成通关成绩。玩家设备、完整路线、云存档及吴强正式成绩归属仍按各自验收记录处理。

未修改计时、暂停、反作弊、存读档、接力、路线节点、内存地址或传统 PAL98/PAL98DX9/PAL98UNHAPPY 内核。回退时恢复整套已匹配组件，保留设备凭据、设置、最佳线和待传记录。

## 2026-10-24 交付记录

- Online 源码提交 `ed343cac10c0cc48fafc18ad070e0c224fe7c41c`；EXE 沿用上述已冻结的 `2638365`，未为认证修复改变 EXE 字节。
- 正式服务器已批准 key `74c885174dd74e8e9daef9d115c3c245` 对应精确组合；状态接口返回 approved。使用实际 EXE 和 native Auth 取得普通实时连接 580、吴强实时连接 581；两种标题均含 r49，吴强含“（吴强）”。未上传通关成绩。
- 停止测试前，679 项隔离 SDL 行为检查和 7 项真实加载器组件组合检查通过。后者保留模块重启桥接、文件读锁和本地秒表检查。
- 初次完整通用加载器套件在传统 PALDLL 签名锁夹具处失败：冻结的隔世 EXE 未嵌入该夹具所需的 trusted tournament integrity key。失败证据保留；随后显式使用 `-CompositionOnly` 跳过这个无关夹具，不把该项算作通过。生产签名/哈希验证未放宽。
- 用户随后明确要求由自己测试。停止 Agent 功能与实机测试，测试进程已结束；后续玩家设备、完整路线、云存档和吴强正式排名由用户验收。Issue 保持开放。

证据：`PALWork/artifacts/so-lostedit-issue-1/r49-online-repair-20261024/`，包括 `real-loader-tests/`（原失败记录）、`real-loader-composition/`、`timer-tests-r49/`、`server-verification.json` 和 `live-connection-verification.json`。

源码分支 `codex/fix/sdlpal-34-online-release`，后续合入目标为 `feature`；本次只本地提交，未推送、未合并，无合并 SHA。
