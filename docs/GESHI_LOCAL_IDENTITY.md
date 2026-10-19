# 隔世本地显示身份（sdlpal-lostedit #54）

Online 1.0.0.6 在已验证隔世目标的 EXE 根目录发布 palmod/player-identity.v1.json，
只包含 source/server_origin/player_id/display_name/bound/updated_at_utc/revision 和 schema=1。
游戏与配置工具离线读取，身份文件不是计时、权限、设备凭据或成绩证据。

用户最终确认的时机：每次检测新游戏进程，等最新有效时钟进入 running 后，强制一次新注册刷新。
菜单注册及旧缓存不发布身份。成功写入后，本进程不再随暂停／继续、重试、排名或心跳反复覆盖；
下次重开游戏开始计时重新同步。失败保留文件，待同步状态按现有连接流程重试；
有效解绑／空名清空旧值。单纯切服务器不提前清空本地名字。

发布只来自待同步期间的新设备响应、校验后的排名或附带有效 standings 的实时 ACK。
字段缺失／类型错误不当作解绑。来源、请求代次、目标进程创建时间和 EXE 路径在写前复核；
请求旧目标的响应不会转投新目录。原子写入仅在联机后台循环执行，不持有计时 publication 锁进行磁盘操作。
PrepareLocalIdentitySync 使用现有实时状态投影，不改变核心的开始、暂停、计时有效性或成绩签封。
传统三个核心没有可写身份目标，保持原行为。

LocalPlayerIdentity.cs 与配置工具 Geshi/LocalPlayerIdentity.cs 使用同一显示合同；
修改时同步两处及跨语言序列化样例。只支持本地普通目录，限长、严格 UTF-8/JSON、目录专属写锁和原子替换。
真实身份、备份、凭据均不能进入发布包；维护者可分发不含真实身份的规则示例。

2026-10-19：联机行为 316 项、Live 136 项、真实 Online/Auth 加载 7 场景通过。
证据：artifacts/ct-20261019-110923-187、artifacts/lt-d91c8bf6、
artifacts/online-module-20261019-111010-946。
测试使用合成设备／玩家和 Fake transport，没有连接生产服务器。
EXE SHA-256 保持 9857d05e3612b875e86c427236c41d2e85a7667eee38a9e28aa58b27391c2949；
新 Online/Auth/公开登记已配套生成，服务器批准与真实帐号验收尚未进行。
不能将 r26 的批准用于新组件组合；回退应恢复完整旧组合。
# 后台身份行为协议补充（PalServer #27）

同步时机继续遵循本文原规则：每个新游戏进入游玩并开始计时后只成功同步一次。
服务端的 identity_behaviors 为 PAL98.IdentityBehaviors.v1，按稳定玩家 ID 选择事件脚本。
新增 IdentityBehaviorSnapshot.cs 严格校验事件、操作、类型、长度、坐标和资源引用；
本次只接受 geshi.death-taunt 的 portrait / text 显示操作，任一步无效即拒绝整份同步。

player-behavior.v1.json 与 player-identity.v1.json 在同一游戏根目录发布，行为快照引用本次身份 revision。
先原子替换行为，再替换身份，游戏匹配来源、ID 与 revision 后执行。中途失败最多造成回退，不能串玩家。
声明能力却缺少脚本视为失败；旧服务器、解绑和禁用绑定写空覆盖。普通排名和心跳不会在成功后持续覆盖。
不改变联机成绩、计时／暂停／云存档或三种传统 PAL98 核心，不增加内存地址或读取权限。

2026-10-19 本地：联机 337、实时心跳 136、实际组件加载 7 项通过。
服务端实际注册响应的隔离 fixture 经真实同步方法生成 data/native-behavior-fixture，
用于游戏 189 项像素／状态回归。Online 为未发布的 1.0.0.6；主 EXE 字节保持 3.38.3.0，
新 Auth 组合证据在 artifacts/behavior-auth-output-path.txt 与 artifacts/online-module-20261019-114519-077，仍需服务器审批。
