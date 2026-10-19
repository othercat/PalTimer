# 隔世 r23：重连、实时状态与 F8

历史记录：r26 已进一步取消普通同进程读档生成的手动暂停，详见 [r26 续计修复](GESHI_R26_LOAD_RESUME.md)。下面的“同一进程读档仍暂停”只描述 r23 行为。

对应 #31/#32。新游戏进程的 PID/Created 变化会保留一次重连待进入状态，跨越“标题页→读档”两个 epoch；进入后自动续计，不清除此前手动或接力暂停。之后同一进程读档仍暂停。既有用时、节点及成绩审核证据不被重置。

隔世核心的 F8 在释放时调用和 F9 相同的 `GForm.UIPause()`，暂停次数、按钮和实际时钟行为一致；其他核心不变。保留 F9 和 F11。

内部 observation 新增可空 `LiveTimingValid`，通过 Online API 1 的扩展字段传递，旧核心继续原逻辑。实时运行/暂停不再由读档、中途连接等成绩资格标记直接判无效；快照不可用、诊断、过期时钟仍无效。最终成绩的 `ValidationError`、`BeganHere` 与审核标记原样保留。

验证：

- `run_sdlpal_tests.ps1 -NoGui` 365项，含真实Release EXE/Online映射、12种重连组合和F8/F9交替切换；不安装系统钩子。
- `run_live_tests.ps1` 124项；`run_competition_tests.ps1` 266项。后者一次旧兼容fixture的异步 pending 读取遇空，未改实现重跑通过，保留两次日志。
- SDL证据 `artifacts/sdlpal-tests-20261018-204947-812`；live `artifacts/lt-4ef429de`；competition `artifacts/ct-20261018-204106-642`。

显示版本按原要求保持3.38.3。新的EXE/Online必须成套重新签发Auth和公开候选；冻结字节后做真实loader检查，不复用r14已批准的身份。本轮不改服务器、不上传模拟通关、不提交或推送。
