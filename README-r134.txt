Pal98Timer 3.38.6.17 / Online 1.0.2.13

Refs othercat/PalTimer#68; Refs othercat/sdlpal-lostedit#115.

修复时间线保存时 JSON 字符串没有转义的问题。RunStatisticsState 本身包含 JSON，旧写法会产生不合法的外层 JSON；随包序列化库在报告此格式错误时又会因缺少 AtlasWeb.resources 显示资源异常。单独替换运行库仍会遇到格式错误。

HObj 的键、字符串和字符由原有 JavaScriptSerializer 完成转义。保存格式仍以字符串存放统计状态；读取接受 HObj 自动转换的嵌套对象及旧对象形式。没有改变计时、内存地址、节点顺序、规则合同、统计语义或上传协议。

更新：先退出计时器，备份 Pal98Timer.exe、PalTimerOnline.dll、PalCompetitionAuth.dll、PalCompetitionRegistration.public.json，再一并替换这四个匹配文件。使用原有 .NET Framework 和随包 System.Web.Script.Serialization.dll，无需额外安装 runtime。保留自己的 PB、时间线、配置、存档和 UserData。需回退时一并恢复这四个备份文件。

tests/run_timeline_json_tests.ps1 针对实际 EXE 测试字符串转义、保存读回、64位统计、旧格式兼容和失败恢复。tests/run_sdlpal_tests.ps1 -BestConfigurationOnly 验证不同配置的 PB 保存和保护；该测试的 Dream 合成快照补齐了 r132 必需的 storyFacts。

2026-11-17：本组合已提交正式服务器，并由用户在线批准。服务端 approved=true，上传授权挑战预检 HTTP 200；旧云 MD5 激活独立，尚未执行。r134 随包 SOURCE-STATE.json 保留打包时的源码身份，后续提交与合并记录见 Issue #68。实机保存与读回验收由用户完成，自动测试未操作游玩。
