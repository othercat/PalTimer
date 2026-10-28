# 隔世 r50 后续维护：怪物规则身份

关联本仓 #35、othercat/sdlpal-lostedit#115、#117、#118。版本 3.38.3.3 / Online 1.0.0.8。

引擎快照新增 `respawnRule=pal98-current-scene-v1`、`monsterRush`、`invisibleMonsters`。旧引擎缺字段时保留旧身份；新版记录、最佳线与接力按规则和模式区分。嵌套 `identity` 与顶层字段须一致，不接受文本冒充布尔值或未知规则。当前标题增加“突脸”“隐形”提示。Online 的配置规则校验同步接受这三项；显示和节点配置不进入玩法规则摘要。

计时起止、F8/F9 暂停、反作弊、读档、接力恢复、自动节点及 OBS 语义保持。PAL98、PAL98DX9、PAL98UNHAPPY 的固定内存、进程匹配及读写入口未修改；这三个传统核心不需要同步怪物规则。

VS2026 Release x64 构建通过。隔离行为套件 704 项通过，覆盖实际原生快照、旧身份兼容、新模式/接力隔离、字段一致性、读档暂停、重连、节点及真实控件。证据位于本机 `PALWork/artifacts/PalTimer-issue-35/validation-20261026-r2`。脚本 `tests/run_sdlpal_tests.ps1` 现要求显式 `-OutputDirectory`，支持已构建的 `-SkipBuild -RuntimeDirectory -PluginResource`；测试与中间产物留在 Issue 目录。

已通过既有正式发布者流程为精确 EXE/Online 签发匹配 Auth 与公开登记，并以真实加载器通过有效组合、缺模块、EXE/Online/Auth 改动、签名损坏和错误 API 共七类检查。证据 `PALWork/artifacts/PalTimer-issue-35/online-composition-20261026-r1`。仅使用 `-CompositionOnly`，未把与本隔世构建无关的 PALDLL 签名锁夹具记作通过。

新组合尚未提交服务器批准；本地签发/加载通过不能继承 r50 的批准状态。完整路线、实际云存档往返、玩家设备、真实 OBS 输出和正式联网成绩仍分别验收。配套包生成前保持冻结 EXE/Online 字节；若再次改变任一二进制，须重新正常签发、加载验证并记录新身份。
