# r106 位置状态模式（Refs #58、othercat/sdlpal-lostedit#247）

只修改SDLPAL桥接：JSON schema1保留旧行为；schema2必须携带规范布尔prebattleSlotResidue=true，
且root/identity一致。MMF header仍v1。大小写别名、重复键、错误类型及降级为schema1的启用模式拒绝。

有效开启才增加prebattle_slot_residue="1"及运行身份标签；关闭时旧榜单摘要/PB序列不变。
真实石长老队伍有两个阿奴位置，新模式允许重复party角色，装备统计仍按唯一角色处理。
计时、暂停、云存读档、OBS和进程地址接口不改。PAL98/PAL98DX9/PAL98UNHAPPY三内核无需同步。
简单版三节点调整独立记录于#55，其他20内容节点保持原样。

开发/离线诊断不能代替玩家实机验收。新Timer/Online必须与同批Auth构建一起冻结，未批准生产上传。
