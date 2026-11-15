# SDLPAL 完整配置在线兼容

Refs #60、othercat/sdlpal-lostedit#250、othercat/PALDLL_DX9#279。

r107 的 EXE 3.38.6.9 已包含原版 #279 修复。问题来自 SDL 新增运行事实直接扩充旧顶层字段集合：
`wuqiang_plus`、`prebattle_slot_residue`、`challenge_config_sha256`。现行服务完成握手后会拒绝这种心跳。
速通v6强制启用状态残留，开启甄吴强后同时命中前两项；这与用户报告一致。

Online 1.0.2.9 继续使用现行服务的 `PAL.ChallengeEffective.v1` 可扩展配置合同：

| 原运行事实 | 新在线字段 | 值 |
|---|---|---|
| `wuqiang_plus` | `config.slot.RuntimeWuQiangPlus` | 原值 `1` |
| `prebattle_slot_residue` | `config.slot.RuntimePrebattleSlotResidue` | 原值 `1` |
| `challenge_config_sha256` | `config.slot.RuntimeChallengeDigest` | `sha256-` 加原32字节摘要的小写无填充base32，共59字符 |

摘要编码保留256位，可按标准base32还原为完全相同的原SHA256。已有41规则、全部优先级、Hero、在线来源修订及规则摘要逐项保留。
新增字段仍参与配置ID，避免不同玩法混榜。只在原始配置、原配置ID和时间线身份均有效、完整文件证据已取得时转换；字段碰撞或缺项不会被补成有效配置。

转换发生在 API1 当前快照进入 Online 客户端时，先于本轮租约、心跳、比较和新成绩签封。
线上 ranking ID、gameplay rules hash 和 timeline ID 一起重算；开局与最终事实使用同一合同。
EXE原字节、游戏MMF/内存、本地最佳线/接力身份和历史已签封 outbox 不修改。旧记录不重签，也不删除玩法事实或退回空配置连接。

本轮独立更新 Online/Auth/公开登记三件套；EXE和忽爷云激活MD5不变。新的组件组合仍需服务器管理员批准，旧组合批准不覆盖新的DLL。
服务端代码、规则目录和生产部署均不修改。构建与离线检查不代表实际持续连接，必须另行核实对应正式组合的批准及实际心跳ACK。

定向验证入口：

- `tests/run_sdlpal_online_contract_tests.ps1`：冻结EXE→实际API1→构建Online，21内容×实际默认/8组合，共189例；参数显式指定Issue证据目录。
- `tests/sdlpal_online_contract_http.py`：上述真实输出对照未修改的服务源码，通过临时本机TCP端口和隔离SQLite；旧合同复现422，新合同确认完整事实与比较身份。仅本机测试密钥，无生产请求/成绩。
- `tests/run_competition_tests.ps1 -RuntimeDirectory ... -SdlpalContractCases ...`：实际产品桥接后的普通完成/签封路径，保留开局与最终全部字段。
- `tests/run_live_tests.ps1` 和 `tests/run_online_module_tests.ps1 -CompositionOnly`：原版免审、审核/私密/无ACK边界及正式组件加载拒绝检查。

游戏操作仅限打开程序、加载禁用自动剧情/触发的静止观察档。自然流程及真实比赛仍由玩家验收。
