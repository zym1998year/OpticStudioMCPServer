# 长优化任务 — 异步 API 与心跳

ZemaxMCP 的优化类工具有 5 个长任务族（runtime 可达数分钟到几十分钟），
每个都同时提供同步入口和异步入口：

| 同步入口 | 异步三件套（Pattern B） |
|---|---|
| `zemax_optimize` | `zemax_optimize_async` / `zemax_optimize_status` / `zemax_optimize_stop` |
| `zemax_global_search` | `zemax_global_search_async` / `zemax_global_search_status` / `zemax_global_search_stop` |
| `zemax_hammer` | `zemax_hammer_async` / `zemax_hammer_status` / `zemax_hammer_stop` |
| `zemax_constrained_optimize` | `zemax_constrained_optimize_async` / `zemax_constrained_optimize_status` / `zemax_constrained_optimize_stop` |
| `zemax_multistart_optimize`（已是异步） | `zemax_multistart_status` / `zemax_multistart_stop` |

## Pattern A — 同步入口的进度心跳

如果 MCP 客户端在请求中传 `_meta.progressToken`，server 在执行期间每 5 秒推一条
`notifications/progress`。包括：当前 merit、improvements、运行时长。客户端不传
则不发，对老调用方零影响。

同步入口同时支持 `CancellationToken`（C# MCP SDK 自动绑定）— 客户端断开 / 显式取消时，
正在跑的 Zemax 优化器会立即收到 `Cancel()` + `WaitForCompletion()`。

## Pattern B — 异步生命周期

适用于预期 >5 分钟的任务：

1. **Submit** — `zemax_<name>_async(参数)` 立即返回 `{success:true, message:"..."}`，
   后台 Task 运行 Zemax 优化器。
2. **Poll** — `zemax_<name>_status()` 任意频率轮询，返回 `{isRunning, currentMerit,
   bestMerit, runtimeSeconds, terminationReason, ...}`。
3. **Cancel** — `zemax_<name>_stop()` 触发取消。状态会**短暂仍读为 running**
   （runner 收尾期），客户端需要再次 poll status 看终态。
4. **同 family 不能并发** — 第二次 submit 直接报 `Already running`。要求先
   `_stop` 或等完成。

## 已知限制
- Server 进程崩溃后任务状态丢失（同 multistart 现状）
- Cancel 不能立即停止 Zemax 内部正在跑的迭代；要等当前迭代完成
- 同一类型 async 任务进程内只能跑 1 个（per-tool State 是 Singleton）

## 设计文档
`docs/superpowers/specs/2026-04-29-zemax-mcp-long-task-async-design.md`
