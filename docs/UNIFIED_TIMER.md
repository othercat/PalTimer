# 原版与 SDLPal 共用计时器

Refs othercat/PALDLL_DX9#266；总览 othercat/PALDLL_DX9#253。

统一版本为 **Pal98Timer 3.38.6.1 / Online 1.0.2.0**。原版 PALDLL_DX9 和
SDLPal-lostedit 使用同一个 `othercat/PalTimer` 默认分支 `master`，从同次构建中取得
完全相同的程序、依赖和认证组合。原版 DLL/配置工具维持 1.7.2.x，显示 1.72。

## 保留的功能边界

- 原版继续使用 PAL98DX9 自动及实际时序核心；怀旧显示和原硬核上传字段、在线挑战、
  默认联机和正式 DLL 精确识别保持，覆盖本轮完整包的 1.7.2.4 及既有版本。原版在线挑战按本地内容名称关联，不绑定 ZIP/参考资源摘要；实际资源事实和本地 PB 保留。已退役的原版魂牵剧情、梦幻专用核心与旧配置锁不恢复。
- SDL 使用 `SdlpalGeshi` 核心，吸收 r100 随包源码中的 20 种内容识别、独立路线、
  可编辑节点、公式身份、最佳线、云档和实时内容显示；不同内容/规则的记录继续隔离。
- 服务器分别声明 `original-challenge-ranking.v1` 和 `sdl-live-identity.v1`。
  客户端按所属核心发送相应事实；一种能力不能替代另一种。
- 挑战桥继续透传既有协议，由游戏/配置工具解释规则。原版与 SDL 的根目录识别和配置写入各走原路径。

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
  `PAL98DX9_800`、`PAL98DX9_800_SPEED`；SDL 的 20 种内容共用 `SDLPAL_GESHI_EASY`。
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
