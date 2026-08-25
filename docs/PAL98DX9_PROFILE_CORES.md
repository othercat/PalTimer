# PAL98DX9 改版计时核心边界

本文冻结 PalTimer 3.38.0 的玩家可见名称、内部身份、profile 读取合同和发布门。它只描述公开工具可用的最小兼容信息，不包含 PALDLL_DX9、配置工具或私有资源包源码。

## 三条独立时间线

| 玩家可见核心 | 内部 `CoreName` | 游戏内容 | 最佳成绩文件 | 接力存档长度 |
|---|---|---|---|---:|
| 仙剑98柔情DX9 | `PAL98DX9` | 仙剑98柔情 DX9 原版内容 | `bestPAL98DX9.txt` | 176528 |
| 仙剑98柔情DX9魂牵 | `PAL98DX9HUNQIAN` | 魂牵 1.67 简单、困难、非人基础包 | `bestPAL98DX9HUNQIAN.txt` | 184688 |
| 仙剑98柔情DX9梦幻22显血 | `DREAM220VISIBLE` | 梦幻2.2显血版 / Dream2.20 内容 | `bestDREAM220VISIBLE.txt` | 185872 |

三者都以 `仙剑98柔情DX9` 的 PAL.exe 读取实现为基类，但路线、节点、最佳时间与接力存档长度相互独立。旧 `梦幻22 / DREAM22` 仍是连接 `sdlpal` 的历史内核，不与新梦幻显血核心合并。

## 公开 profile 合同

两个改版核心只读取以下公开数据：

- `palmod/Profiles/current.json`，schema 为 `PAL98.EffectiveGameProfilePointer.v1`；
- 指针指向的 `manifest/game-profile.json`，schema 为 `PAL98.GameProfile.v1`；
- 固定的 profile ID、版本、descriptor SHA-256、`save_namespace` 和 `virtual_party_max`；
- 计时器不读取私有 Hook 符号、固定地址清单或 PALDLL 内部配置实现。

支持身份如下：

| 内容 | profile |
|---|---|
| 魂牵简单 | `pal98.hunqian167.easy@1.0.2` |
| 魂牵困难 | `pal98.hunqian167.hard@1.0.2` |
| 魂牵非人 | `pal98.hunqian167.nightmare@1.0.3` |
| 梦幻2.2显血版 | `pal98.dream220.compat@1.0.18`；本次冻结叠加包 `pal98.dream220.compat.drawcard.16e143813df5@1.0.18` |

profile 缺失、版本不符、descriptor 哈希不符、目录越界或 reparse point 均拒绝连接。魂牵抽卡派生包不属于三个基础版本，也不会被基础核心静默接受。

## 内存与运行时所有权

固定 PAL.exe 读取仍集中在 `仙剑98柔情DX9.GameObject`：基址 `0x00428000`，位置/场景/朝向/战斗槽/物品槽沿用已有偏移。新核心只消费其内存快照，不增加 `ReadProcessMemory`、`WriteProcessMemory`、进程枚举、线程、计时器或网络服务。主路径继续由现有 70ms Windows Forms tick 驱动。

接力存档只有用户触发接力/云存档时才读取。基础类默认保留 176528 字节；魂牵和梦幻核心分别覆盖事件对象段长度并强制校验最终字节数，不改变每 tick 行为。

## 证据与发布门

- 已自动验证：核心显示名与内部身份、profile 失败关闭、descriptor 哈希、目录边界、路线纯谓词、三个存档长度、无新增内存写路径、Release x64 构建。
- 资源对齐但仍待人工：魂牵与梦幻的自然剧情触发、真实战斗结算、结局分支、接力存读档和一条完整跑线。
- PalTimer 的仓库 Owner/当前维护者为 `othercat`，项目按 `GPL-2.0-only` 发布；二进制分发必须随附许可证与完整对应源代码。

梦幻2.2显血版署名顺序固定为：`主播粉丝，孙小柔，othercat`。魂牵计时支持署名为：`女尸，孙小柔，othercat`。
