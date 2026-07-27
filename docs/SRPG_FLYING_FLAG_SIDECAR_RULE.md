# SRPG 飞行旗 sidecar 完整快照规则

状态：2026-07-27 用户确认，PalTimer 客户端实现。

## 适用范围

- 仅适用于 `PAL98DX9` 与 `PAL98UNHAPPY` 内核创建的新 SRPG。
- 原始 RPG 存档、计时器时间线和 `PalDrawCard.FlyingFlagAll.v1.bin` 仍由各自既有组件解释。
- PalTimer 只把 sidecar 当作不透明字节载荷传输，不修改 PALDLL_DX9 的原生 sidecar schema。
- 云服务器继续原样上传/下载 SRPG `.bin`，不解析新字段，因此不需要服务器代码或接口变更。

## 新 SRPG 的完整快照语义

1. 创建 SRPG 时必须记录已经执行过 sidecar 捕获。
2. 若游戏目录存在 `PalDrawCard.FlyingFlagAll.v1.bin`：
   - 携带文件的原始字节；
   - 携带这些字节的 SHA-256；
   - 导入时必须先验证长度和 SHA-256。
3. 若源游戏目录不存在 sidecar：
   - 明确记录“已捕获但不存在”；
   - 不允许把它误判为旧 SRPG。
4. 若 SRPG 声明 sidecar 存在但载荷、长度或哈希无效，整个导入必须报错，不得继续把 RPG 排队写入游戏目录。

## 旧 SRPG 兼容

- 旧 SRPG 没有 `FlyingFlagSidecarCaptured` 标记。
- 导入旧 SRPG 时，不读取、覆盖、移动或删除目标游戏目录中的 sidecar。
- 新字段使用 .NET `OptionalField`，确保新 PalTimer 可以反序列化旧 SRPG。

## 导入和备份

1. 带完整快照的新 SRPG 只能在 PAL.exe 已运行、PalTimer 能确定游戏目录时导入。
2. 目标 sidecar 已存在时，先移动或替换为带毫秒时间戳的：
   `PalDrawCard.FlyingFlagAll.v1.bin.paltimer-backup-YYYYMMDD-HHmmssfff`
3. 源快照存在 sidecar 时，使用同目录临时文件和原子替换写入目标路径。
4. 源快照明确不存在 sidecar 时，把目标 sidecar 移到时间戳备份；目标本来不存在则不写文件。
5. 不删除历史 sidecar 备份。

## 为什么必须重启 PAL.exe

PALDLL_DX9 在飞行旗运行时初始化时把 sidecar 载入进程内存。PalTimer 本次不增加热重载协议，只改变磁盘快照。因此带 sidecar 的 SRPG 导入后：

1. 保持 PalTimer 开启；
2. 不要继续使用飞行旗；
3. 只关闭并重新启动 PAL.exe；
4. 重启后再读取“进度一”。

旧 SRPG 没有 sidecar 操作，不增加重启要求。

## 回滚

- RPG 仍沿用既有 `1.RPG.bak` 回滚路径。
- sidecar 使用上述时间戳备份，关闭 PAL.exe 后可人工把目标文件移走，再把所需备份改回 `PalDrawCard.FlyingFlagAll.v1.bin`。
- 回滚 PalTimer 源码不会自动删除或恢复任何游戏目录文件。
