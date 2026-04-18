# LiveSplit 与仙剑98 DX9 知识库

## 1) LiveSplit 简介

- LiveSplit 是著名的开源速通计时器项目。  
- GitHub: https://github.com/LiveSplit/LiveSplit  
- 典型能力：分段计时（splits）、自动分段（Auto Splitter）、丰富布局组件、对外通信（如 LiveSplit Server）。

## 2) LiveSplit 与 PalTimer 对比

- **共同点**：都支持分段计时、PB/最佳段等速通核心场景。  
- **PalTimer 优势**：针对仙剑系列（尤其仙剑98 DX9）内核已做深度适配，已有检查点与内存读取逻辑。  
- **LiveSplit 优势**：生态成熟（ASL 脚本社区、组件体系、布局可定制、外部工具兼容广）。

## 3) 通过 Auto Splitter（ASL）支持仙剑98 DX9

LiveSplit 通常通过 ASL（Auto Split Language）脚本读取进程内存并驱动计时状态：

- 识别目标进程（如 `PAL98.exe`/DX9 对应进程名）
- 读取关键状态（地图、剧情标志、战斗状态、章节变量等）
- 根据状态变化触发 `start/split/reset`
- 可通过 `isLoading` 控制计时是否扣除读盘/载入时间

## 4) ASL 脚本结构示例（简化）

```asl
state("PAL98")
{
    int sceneId : 0x123456;
    int flagMainQuest : 0x234567;
}

start
{
    return old.flagMainQuest == 0 && current.flagMainQuest == 1;
}

split
{
    // 示例：场景切换到某关键节点
    return old.sceneId != current.sceneId && current.sceneId == 2101;
}

reset
{
    return current.flagMainQuest == 0 && current.sceneId == 1001;
}

isLoading
{
    // 示例：按加载状态位判断
    return false;
}
```

## 5) 仙剑98 DX9 内存地址读取思路

可参考 PalTimer 现有内核做法：

1. 先定位目标进程与模块基址  
2. 维护稳定的“检查点判定字段”（地图ID、剧情变量、道具标志等）  
3. 用“状态边沿变化”判定节点触发（避免同一状态重复触发）  
4. 把检查点与最佳时间线关联，生成当前段差与预计完赛时间  

实践中建议：

- 优先使用“语义稳定”的判定字段（剧情flag比瞬时动画状态更稳）  
- 对易抖动状态加去抖/二次确认  
- 地址版本变化时通过签名或版本分支管理地址表。

## 6) LiveSplit Server 与 WebSocket 通信

LiveSplit Server 常用于把计时状态提供给外部工具（覆盖层、OBS脚本、远端控制器等）：

- 可通过 TCP 文本命令协议交互（开始/分段/重置/读状态）  
- 在工程扩展里也可做 WebSocket 网关，将状态转发给网页或直播辅助层  
- PalTimer 若对齐该协议，可直接复用大量现有工具链。

## 7) 自定义 Layout 与组件配置

LiveSplit 的 Layout 可组合多种组件：

- Timer / Splits / Delta / Previous Segment / Sum of Best / Graph 等  
- 支持字体、颜色、间距、阴影、背景等细粒度配置  
- 通过布局配置与组件组合，可以快速适配“练习版”“比赛版”“直播版”三类界面需求。
