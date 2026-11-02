# 原版与 SDLPal 共用计时器

Refs othercat/PALDLL_DX9#266；总览 othercat/PALDLL_DX9#253。

当前维护候选为 **Pal98Timer 3.38.6.5 / Online 1.0.2.4**，基于已统一的默认分支。
Refs othercat/PalTimer#53、othercat/sdlpal-lostedit#224、othercat/sdlpal-lostedit#226。
本次完整配置改动关联 othercat/PALDLL_DX9#275、othercat/sdlpal-lostedit#189。新组合需独立生产批准及云激活，不能继承 3.38.6.4 的审批。
原版 PALDLL_DX9 和
SDLPal-lostedit 使用同一个 `othercat/PalTimer` 默认分支 `master`，从同次构建中取得
完全相同的程序、依赖和认证组合。原版 DLL/配置工具维持 1.7.2.x，显示 1.72。

## 保留的功能边界

- 原版继续使用 PAL98DX9 自动及实际时序核心；怀旧显示和原硬核上传字段、在线挑战、
  默认联机和正式 DLL 精确识别保持，覆盖本轮完整包的 1.7.2.4 及既有版本。原版在线挑战按本地内容名称关联，不绑定 ZIP/参考资源摘要；实际资源事实和本地 PB 保留。已退役的原版魂牵剧情、梦幻专用核心与旧配置锁不恢复。
- SDL 使用 `SdlpalGeshi` 核心，在 r100 的 20 种内容上新增梦时空 3.5，提供 21 种内容识别、独立路线、
  可编辑节点、公式身份、最佳线、云档和实时内容显示；不同内容/规则的记录继续隔离。
- SDL 的“灵儿永跟随”开启时加入 `return_linger=1`，用于榜单、最佳线和接力身份；关闭时省略此事实，保留旧普通记录。
- 服务器分别声明 `original-challenge-ranking.v1` 和 `sdl-live-identity.v1`。
  客户端按所属核心发送相应事实；新版 `config.*` 完整证据始终随实时请求发送，不因能力尚未声明或 422 回退而清空配置。配套服务必须先部署 PR #62。
- 挑战桥继续透传既有协议，由游戏/配置工具解释规则。原版与 SDL 的根目录识别和配置写入各走原路径。

完整实际配置单独保存在 `config.slot.*`，原下载规则仍作为来源。原版保留原生运行核验及本地 PB；SDL 的 33 个开关、人物和优先级目前只能读取配置文件，明确标为 `configuration-file`，首次读取后按进程固定，变更提示重启且保留旧完整证据。文件读取在后台执行，不阻塞计时线程。仅封面/显示文字变化不改变配置身份。

离线检查入口：`tests/run_effective_configuration_tests.ps1`（使用配置工具 `--verify-effective-rules` 产生的 fixture）、`tests/run_original_challenge_identity_tests.ps1`、`tests/run_live_tests.ps1`。这些检查不启动或游玩真实游戏。

## 唯一构建和发包流程

从最新 `master` 构建，禁止按游戏包另编译“专用版”。使用 VS 2026、.NET Framework 4.7.2，
在实际 Issue 的 artifacts 目录保存输出：

```powershell
& .\scripts\build-unified.ps1 -OutputDirectory '<本 Issue 的全新构建目录>' -PalSharedSourceRoot '<PALDLL_DX9 的 shared 目录>'
```

入口同时生成 EXE、Online、改键器和插件接口，记录源码提交、共享依赖及文件哈希。
发布前使用干净提交构建；冻结 EXE/Online 后，通过已有正式发布者流程签发 Auth/公开登记。
所有游戏包都复制这同一套冻结文件，禁止混用旧的 TimerPluginBase.dll 或认证组件。
源码交付需包含对应 PalTimer 提交、PALDLL_DX9 共享源和其运行时契约文档；私钥不随包分发。

完整更新包中的 `Install.ps1` 用法：关闭计时器/改键器，选择含 Pal.exe 的游戏根目录，
原版传 `-Edition Original`，SDL 传 `-Edition SDL`。先核验 payload 清单，再备份原文件，
成套替换，读回核验。已有设置、最佳线和存档保留；首次安装才写入对应 LastCore。
备份在计时器目录的 `Rollback/PalTimer-时间戳`。需回退时关闭工具，按备份 `install.json`
恢复 `replaced` 的全套文件，并停用仅本次新增的 `added` 文件；首次生成的设置列在 `created_settings`。

## 云身份和运维交接

合并减少了维护和重复构建，但**本次新 EXE 仍需登记**：

- PalServer 批准同一个 EXE/Online/Auth/公开登记组合，两种游戏共用该组合。
- 忽爷云按核心名和 EXE MD5 校验。原版直接相关核心为 `PAL98DX9`、`PAL98DX9_AUTO`、
  `PAL98DX9_800`、`PAL98DX9_800_SPEED`；SDL 的 21 种内容共用 `SDLPAL_GESHI_EASY`。
  这些核心名继续保留，不能把 SDL 伪装成原版以绕过激活。
- 历史可选核心仍有 `PAL98`、`PAL98UNHAPPY`、`PAL98STM`、`PAL2STM`、`PAL3`、
  `PAL5QSTM`、`GUJIAN2`、`DREAM22`、`CUSTOM`、`S`，有使用需求时按同一新 MD5 登记。
- 后续只有 Online API 1 兼容改动时可保持 EXE 原字节，重新签发/批准组合；不必为了
  分发到另一个游戏包而重编 EXE。EXE 变更仍需要新的忽爷云激活。
- 生产部署、批准和忽爷云激活交由用户或现有运维执行。个人联机凭据不进入发行包。

## 验证与玩家验收

离线检查覆盖 SDL 内容/路线、原版怀旧及精确完整性、两个联机能力的隔离、身份和挑战、
云会话切换/取消、真实 CLR 配套加载以及两种目录安装后的字节一致性。
用户仍需分别验收原版和 SDL 的真实计时、存读档/重启、在线挑战名称和生产云档往返；
自动检查不等于游玩验收。专项 Issue 保持开放。
