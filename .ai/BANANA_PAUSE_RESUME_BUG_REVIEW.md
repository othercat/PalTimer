# Banana Tree Anti-Cheat Pause/Resume Bug Review

**Task:** task-013
**Version:** v3.36.4
**Date:** 2026-05-15
**Reviewer:** Claude Code (read-only analysis)

---

## 1. 结论

**LIKELY_BUG**

反作弊自动暂停和用户手动暂停确实存在共用 `IsPause` 状态导致无法恢复的问题。当玩家在反作弊窗口期内手动暂停后再拿香蕉，计时器将永久停止，`IsInUnCheat` 标志无法被清除。

---

## 2. 影响范围

| 内核 | 是否一致 |
|------|---------|
| PAL98 (仙剑98柔情) | 是 |
| PAL98DX9 (仙剑98柔情DX9) | 是 |
| PAL98UNHAPPY (仙剑98柔情不欢乐模式) | 是 |

三个内核的 `OnTick()`、`JudgePause()`、`HasStartGame()`、`CheckCheatBegin()`、`CheckCheatEnd()` 逻辑完全相同，Bug 影响一致。

---

## 3. 控制流摘要

### 3.1 OnTick 主循环（三个内核完全一致）

```
OnTick()
├── GetPalHandle()          // 获取游戏进程
├── JudgePause()            // 设置 IsPause
├── FlushGameObject()       // 刷新游戏内存数据
├── 战斗逻辑（BattleBegin/Battling/BattleEnd）
├── HasStartGame()          // ← 关键：IsPause==true 时返回 false
│   ├── [true] → ST.Stop(); 反作弊检查; MT.Start/Stop; Checking()
│   └── [false] → MT.Stop() (仅此一步，不做任何反作弊检查)
└── PreData()
```

### 3.2 反作弊检查（嵌套在 HasStartGame() 内部）

```
if (HasStartGame())           // ← 如果 IsPause，整个块被跳过
{
    if (!HasUnCheated)
    {
        if (!IsInUnCheat)
        {
            CheckCheatBegin(); // 设 IsInUnCheat=true (站到香蕉树)
            CheckCheatEnd();  // 设 IsInUnCheat=false (拿到香蕉)
        }
        else
        {
            CheckCheatEnd();  // 设 IsInUnCheat=false (拿到香蕉)
        }
    }

    if (IsInUnCheat)
        MT.Stop();            // 反作弊暂停：停计时器
    else
        MT.Start();           // 恢复计时器 ← 永远走不到这里
}
```

### 3.3 JudgePause 逻辑

```
JudgePause()
├── IsUIPause==true  → IsPause = true
└── IsUIPause==false → IsPause = (游戏窗口是否前台)
```

### 3.4 HasStartGame 逻辑

```
HasStartGame()
├── _HasGameStart==false 且 Area==0 → false
├── _HasGameStart==false 且 Area!=0 → _HasGameStart=true; return !IsPause
└── _HasGameStart==true → return !IsPause
```

---

## 4. 关键代码位置

| 文件 | 方法 | 行号 | 作用 |
|------|------|------|------|
| 仙剑98柔情.cs | `OnTick()` | 677-769 | 主循环，反作弊检查在 729-749 |
| 仙剑98柔情.cs | `HasStartGame()` | 1179-1205 | IsPause 导致返回 false |
| 仙剑98柔情.cs | `JudgePause()` | 993-1012 | IsUIPause 强制 IsPause=true |
| 仙剑98柔情.cs | `CheckCheatBegin()` | 1232-1238 | 站到香蕉树→IsInUnCheat=true |
| 仙剑98柔情.cs | `CheckCheatEnd()` | 1239-1246 | 拿到香蕉→IsInUnCheat=false |
| GForm.cs | `UIPause()` | 295-313 | 手动暂停→SetUIPause(!core.IsUIPause) |
| GForm.cs | `SetUIPause()` | 314-328 | core.IsUIPause = isp |
| TimerCore.cs | `IsUIPause` | 680 | public bool，基类字段 |

DX9 和 UNHAPPY 的对应方法位置与 PAL98 基本一致（方法签名和逻辑完全相同）。

---

## 5. 用户反馈场景下的状态流

### 正常流程（无 bug）

| 步骤 | 事件 | HasUnCheated | IsInUnCheat | IsUIPause | IsPause | MT |
|------|------|-------------|-------------|-----------|---------|-----|
| 1 | 游戏运行中 | false | false | false | false | Running |
| 2 | 站到香蕉树附近 | false | **true** | false | false | **Stopped** |
| 3 | 拿到香蕉 | false→**true** | **false** | false | false | **Running** |

### Bug 场景

| 步骤 | 事件 | HasUnCheated | IsInUnCheat | IsUIPause | IsPause | MT |
|------|------|-------------|-------------|-----------|---------|-----|
| 1 | 游戏运行中 | false | false | false | false | Running |
| 2 | 站到香蕉树附近 | false | **true** | false | false | **Stopped** |
| 3 | 用户手动暂停(F9) | false | true | **true** | **true** | Stopped |
| 4 | **此时拿到香蕉** | false | **true (未清除!)** | true | true | Stopped |
| 5 | 用户手动恢复(F9) | false | **true** | false | false | **Stopped (永久!)** |

**根因：** 步骤 4 中，`CheckCheatEnd()` 本应将 `IsInUnCheat` 设为 `false`，但因为 `HasStartGame()` 在 `IsPause==true` 时返回 `false`，导致整个反作弊检查块被跳过。`IsInUnCheat` 永远停留在 `true`，步骤 5 恢复后 `MT.Start()` 永远不会被调用。

---

## 6. 是否存在"反作弊自动暂停"和"用户手动暂停"共用状态导致无法恢复的问题

**是的，存在此问题。**

核心矛盾：

1. **反作弊暂停**通过 `IsInUnCheat` 控制，在 `HasStartGame()` 返回 `true` 时执行检查
2. **用户手动暂停**通过 `IsUIPause` → `JudgePause()` → `IsPause` 控制，当 `IsPause==true` 时 `HasStartGame()` 返回 `false`
3. `HasStartGame()` 返回 `false` 时，**所有**反作弊检查被跳过，包括 `CheckCheatEnd()`
4. 恢复后，`IsInUnCheat` 仍然为 `true`，`MT.Start()` 不会被调用

两个暂停机制独立运作但互相阻塞：手动暂停阻止了反作弊结束检查的执行，而反作弊状态又阻止了计时器恢复。

---

## 7. 最小手工复现步骤

### 前提条件
- 使用 PAL98 / PAL98DX9 / PAL98UNHAPPY 内核
- 游戏进度已到可以到圣姑家门口的阶段

### 步骤

1. 启动计时器，启动游戏
2. 控制角色走到圣姑家门口香蕉树附近（Area=177，坐标约 1088,608 或 1120,608 或 1120,592）
3. 确认计时器主时间出现 `*` 号（反作弊已触发，计时器自动停止）
4. **立即按 F9 手动暂停计时器**（此时不要移动角色）
5. 在游戏中拾取香蕉（物品 ID 0x123）
6. **按 F9 恢复计时器**

### 预期行为
- 步骤 6 后计时器应恢复计时，`*` 号消失

### 实际 Bug 行为
- 步骤 6 后计时器**仍然停止**，`*` 号仍然显示
- 计时器永久停止，无法恢复

### 观察要点
- 暂停按钮上的暂停次数是否增加（`HandPauseCount`）
- `*` 号是否持续显示（`IsInUnCheat` 是否为 true）
- 恢复后主时间是否继续走动

---

## 8. 修复候选（仅描述，不实施）

### 方案 A：让 CheckCheatEnd() 在 HasStartGame() 之外执行

将 `CheckCheatEnd()` 从 `HasStartGame()` 的 if 块中移出，确保无论暂停状态如何，拿到香蕉时都能清除 `IsInUnCheat`。

优点：简单直接
风险：需要确保 `FlushGameObject()` 仍在 `HasStartGame()` 之前执行（当前已是如此），否则物品数据可能不准确

### 方案 B：在 HasStartGame() 中区分"反作弊暂停"和"手动暂停"

在 `HasStartGame()` 返回 false 时，仍然执行 `CheckCheatEnd()` 检查。例如：

```csharp
if (HasStartGame()) {
    // 原有逻辑
} else {
    // 仅执行反作弊结束检查
    if (!HasUnCheated && IsInUnCheat) {
        CheckCheatEnd();
        if (!IsInUnCheat) {
            MT.Start(); // 反作弊结束，恢复计时
        }
    }
}
```

优点：保持 `HasStartGame()` 的语义不变
风险：`MT.Start()` 在 `IsPause==true` 时被调用，但 `JudgePause()` 在下一次 tick 时会重新设置 `IsPause`，所以实际影响有限

### 方案 C：引入独立的反作弊暂停状态

用单独的 `IsAntiCheatPause` 变量代替 `IsInUnCheat` 对 MT 的直接控制，让反作弊暂停和手动暂停有独立的状态机。

优点：架构更清晰
风险：改动较大，需要修改三个内核

### 推荐

**方案 A** 最为简洁，改动最小。将 `CheckCheatEnd()` 调用移到 `HasStartGame()` 块之前（但仍在 `GetPalHandle()` 和 `FlushGameObject()` 之后），确保无论暂停状态如何都能检测到香蕉获取。

---

## 9. 是否需要 Codex 继续开修复任务

**是的，建议开修复任务。**

- Bug 确认存在（LIKELY_BUG）
- 影响三个内核，修改需同步
- 推荐方案 A，改动小且安全
- 修复后需要实机测试验证复现和恢复

建议创建独立的修复 task context，指定 Codex 实施方案 A。

---

## 10. 附录：变量状态表

| 变量 | 类型 | 初始值 | 触发条件 | 重置条件 |
|------|------|--------|---------|---------|
| `HasUnCheated` | bool | false | `CheckCheatEnd()` 中 `GetItemCount(0x123)>0` 时设为 true | `Reset()` |
| `IsInUnCheat` | bool | false | `CheckCheatBegin()` 中站到香蕉树时设为 true；`CheckCheatEnd()` 中拿到香蕉时设为 false | `Reset()` |
| `IsUIPause` | bool | false (TimerCore) | `SetUIPause(true)` | `SetUIPause(false)` |
| `IsPause` | bool | false | `JudgePause()` 中 `IsUIPause==true` 或游戏窗口非前台时设为 true | `JudgePause()` 中条件不满足时 |
