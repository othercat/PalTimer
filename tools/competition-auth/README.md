> 2026-10-27 / PalTimer #39：v2 增加有界可选 speed、battle_speed（null 或严格整数 0..100）及 release_revision（null 或 r1..r999999）格式校验；完整原始字节签封，资格仍由服务器审核。test-native.ps1 必须显式传 TimerDirectory 与对应 Issue 下 OutputDirectory；仅使用测试发布者、fixture.invalid 离线验证。

> 3.37.7.13 配套更新：先冻结 EXE 和 PalTimerOnline.dll，再使用 `build.ps1 -TimerExe <exe> -OnlineDll <dll>` 构建。输出按 EXE / composition 哈希隔离，同一 EXE 可以对应后续兼容模块。Candidate.v2 签名绑定 EXE、Online、Auth 三个哈希以及 Online 完整版本/API1；使用原发布者公钥信任根，不产生新的发布者身份。新版认证逐次独立核对真实宿主和同目录 Online 文件，不接受外部指定待校验路径。
>
> 新 `PAL98.TimerOnline.v2` 只保留格式/身份/容量/请求用途限制；节点乱序、缺失、未知规则与硬核证据不足均可签封交服务端审核。v1 兼容规则保持原样。旧签封不重签，未知字段不编造。`test-native.ps1` 使用测试发布者，绝不批准生产构建。私有源码与所有密钥仍仅存在本项目。

# PalTimer competition upload authentication (private)

SDLPAL 隔世追凶配套：Online.v2 的 gameplay / initial_gameplay 增加有界可选 `walk_frame_ms`（null 或 0..1000000 的整数）。本模块仍只校验 v2 的格式、身份和用途，家族/走速/资源/配置一致性及成绩资格由 PalServer 审核；v1 exact schema 不变。使用 `test-native.ps1 -TimerRoot <PalTimer worktree> -PalSharedSourceRoot <PALDLL shared>` 可复核新客户端契约。测试发布者、测试成绩和测试DLL不可进入正式游玩包。

Private native x64 component for `PAL98.TimerUploadAuth.v2`. Do not copy this
project, its generated headers, private key files, object files or PDBs into
PalTimer, PalServer, a public repository, or a player archive.

Freeze the timer EXE first. `build.ps1 -TimerExe <frozen exe>` generates a unique
RSA-2048 release key and binds the DLL to that exact executable SHA-256. Only
The client receives `PalCompetitionAuth.dll` and `PalCompetitionRegistration.public.json`
with the frozen EXE. The latter is public signed metadata automatically sent by the
3.37.7.10 client. It does not itself activate uploads.

`publish-registration.ps1` signs exact release EXE/component hashes and verification
key using a persistent offline publisher key. This publisher private key is NEVER
embedded in the component. Administrators trust `publisher.public.json` once after
checking its fingerprint via a trusted release channel. Future builds appear as
pending candidates and still require explicit hash approval.

`registration.public.json` remains public fallback material for the old manual
administrator workflow. Do not ship the private project, keys or build directories.
Fixture builds use `-TestPublisher` with a separate publisher key and are never
release approval material.

The component derives its host path from the current process, checks the full
executable hash, and checks its local device identity. Its narrow C ABI can seal
a complete timer run or answer a challenge bound to an already sealed run; it
does not export keys or provide a general signing operation. All callers must
run it off the UI and timing threads. Failure means local-only records.

This is user-mode resistance to casual source modifications, not hardware
attestation. A determined debugger can patch code or extract a distributed
signing key. Keys are release-specific and revocable. The server must always
verify both the producing build and the currently submitting build.

The native API targets Windows 7 SP1 x64 without CLR, drivers or elevation.
`PCA_ProveLiveSession` additionally signs only a strict, short-lived
`PAL98.TimerLive.v1` server challenge for the current host and local device.
It cannot sign arbitrary telemetry or change complete-run signatures. The
server verifies approval and returns a scoped live-session token; heartbeat
updates do not re-hash the host or perform RSA work. Tokens are private,
ephemeral and never part of public registration or player packages.
Windows ARM executes the existing x64 timer/component under emulation; actual
Win7 and ARM acceptance must be performed on those systems.

The 3.37.7.2 experiment also accepts the strict `PAL98.TimerCompetition.v2`
body: timeline identity, gameplay identity, and hardcore evidence are sealed
with the entire original payload. Unknown fields, inconsistent speed/track or
timeline digest, and invalid evidence combinations are rejected. Original v1
seals stay byte-for-byte intact and receive no retrospective hardcore status.
That historical v1 proof was superseded by Online.v1 / TimerUploadAuth.v2.
Candidate.v1 is an additive registration certificate, not a replacement for
run signatures, one-time upload proofs, or the original cloud ID service.
