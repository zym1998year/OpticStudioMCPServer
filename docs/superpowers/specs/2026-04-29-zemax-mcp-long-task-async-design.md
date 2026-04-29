# ZemaxMCP 长任务异步改造设计文档

| 字段 | 值 |
|---|---|
| 创建日期 | 2026-04-29 |
| 工作分支 | `feature/long-task-async` |
| 仓库 | `E:\ZemaxProject\ZemaxMCP_源码_本地开发版`（fork: zym1998year/OpticStudioMCPServer） |
| 目标框架 | .NET Framework 4.8 |
| 关联设计 | matlab-mcp 长任务异步改造（同问题家族，不同实现风格） |

---

## 1. 动机

### 1.1 现象

ZemaxMCP 的 4 个长运行优化工具（`zemax_optimize` / `zemax_global_search` / `zemax_hammer` / `zemax_constrained_optimize`）是同步阻塞调用，handler 在 `await _session.ExecuteAsync(...)` 内一直跑到优化结束才返回。期间：
- 不发 `notifications/progress`，MCP 客户端心跳监控认为连接死了
- tool 方法签名不接 `CancellationToken`，客户端断开后 Zemax 优化继续跑
- Claude Desktop 30s 心跳、Claude Code agent 600s 流式 watchdog 都会在这些工具执行中触发误杀

### 1.2 现状盘点

```
长工具                       默认上限         async 化情况
zemax_multistart_optimize    立即返回         ✅ 已是 Pattern B
zemax_optimize               cycles 决定      ❌ sync block
zemax_global_search          timeoutSeconds   ❌ sync block (default 60s)
zemax_hammer                 5 min            ❌ sync block (Thread.Sleep 自轮询)
zemax_constrained_optimize   maxIterations    ❌ sync block (LM 迭代)
```

`zemax_multistart_optimize` 已经实现完整 Pattern B：
- `MultistartOptimize` (`zemax_multistart_optimize`) 立即返回
- `MultistartStatusTool` (`zemax_multistart_status`) 轮询查状态
- `MultistartStopTool` (`zemax_multistart_stop`) 取消
- `MultistartState` 类持有 `_cts`、`_backgroundTask`、富业务字段（`CurrentTrial`、`InitialLmIteration`、`BestMerit` ...）

它是这次改造直接复用的模板。

### 1.3 用户当前的缓解措施

`~/.claude/CLAUDE.md` § 5 显式约束：

> **优化超时**：每次执行优化类工具前，确保系统处于干净状态。不要在一次连接中连续执行多个不同类型的优化工具，否则可能导致系统状态异常或 IPC 崩溃。
> **优化超时**：调用 `global_search`、`hammer` 时务必设置 `timeoutSeconds > 0`，避免无限等待。
> **资源释放**：使用完 Zemax 工具后，调用 `disconnect`。

约束有效但脆弱：依赖调用者每次记得设 timeout、知道工具间不能混用。Pattern A + B 改造让这些防御性约束变成可选优化项。

---

## 2. 目标与非目标

### 2.1 目标

1. **Pattern A — 同步路径心跳**：4 个 sync 长工具（Hammer/GlobalSearch/Optimize/ConstrainedOptimize）在执行期间向客户端推送 `notifications/progress`，每 5s 一拍。客户端传 `_meta.progressToken` 即生效，不传则 noop。
2. **Pattern B — async 工具族**：每个 sync 长工具新增 3 个工具配套
   - `<name>_async`（提交，立即返回）
   - `<name>_status`（轮询查状态）
   - `<name>_stop`（取消）
   
   命名跟随 multistart 但加 `_async` 后缀（区分同名 sync 版）。共 12 个新工具。
3. **CancellationToken 接通**：4 个 sync 长工具方法签名加 `CancellationToken cancellationToken = default`，由 C# MCP SDK 自动绑定到客户端 cancel 信号。
4. **Per-tool 富状态**：复刻 `MultistartState` 模式，每个工具有自己的 `<Name>State` 类持有业务字段、cts、backgroundTask。Singleton DI 注入。
5. **保持 sync 工具向后兼容**：现有 sync 调用不变，只是增加 progress 心跳能力。

### 2.2 非目标

| 项 | 原因 |
|---|---|
| Server 进程崩溃后 job 存活 | 与 multistart 现状一致；State 单例进程内，崩了即丢 |
| Cancel 时硬中断当前 Zemax eval | Zemax COM API `Cancel()` 行为依赖具体工具，已尽力调用 |
| 多 Job 并发同一类型 | per-tool State 单例，已 running 时第二次 submit 直接 reject（与 multistart 一致） |
| 重构 multistart 进入新 JobManager | 已验证模式按"per-tool 富状态"风格走，没有 JobManager 抽象 |
| 短工具加 progress | mtf / pop / cardinal_points 等通常 < 30s，本期不动 |
| 持久化 / 跨 server 实例 | 同 matlab-mcp 决策 |

### 2.3 不动的源码

- `MultistartOptimizeTool.cs` / `MultistartStatusTool.cs` / `MultistartStopTool.cs`
- `MultistartState.cs`
- `IZemaxSession.ExecuteAsync(...)` 接口（已支持 ct）
- `Program.cs` 的现有 7 个 section（只在末尾追加 12 个新工具注册）
- 短工具（Analysis / LensData / Configuration 等）

---

## 3. 架构概览

### 3.1 文件结构

```
src/ZemaxMCP.Core/Services/ConstrainedOptimization/    (与 MultistartState 同目录)
  HammerState.cs                  (新)
  GlobalSearchState.cs            (新)
  OptimizeState.cs                (新)
  ConstrainedOptimizeState.cs     (新)

src/ZemaxMCP.Server/Tools/Optimization/                (与现有工具同目录)
  HammerOptimizationTool.cs       (改 +heartbeat +ct)
  GlobalSearchTool.cs             (改 +heartbeat +ct)
  OptimizeTool.cs                 (改 +heartbeat +ct)
  ConstrainedOptimizeTool.cs      (改 +heartbeat +ct)
  HammerAsyncTool.cs              (新) — zemax_hammer_async
  HammerStatusTool.cs             (新) — zemax_hammer_status
  HammerStopTool.cs               (新) — zemax_hammer_stop
  GlobalSearchAsyncTool.cs        (新)
  GlobalSearchStatusTool.cs       (新)
  GlobalSearchStopTool.cs         (新)
  OptimizeAsyncTool.cs            (新)
  OptimizeStatusTool.cs           (新)
  OptimizeStopTool.cs             (新)
  ConstrainedOptimizeAsyncTool.cs (新)
  ConstrainedOptimizeStatusTool.cs (新)
  ConstrainedOptimizeStopTool.cs  (新)

src/ZemaxMCP.Server/Program.cs    (改 +DI 4 单例 +注册 12 工具)
```

总规模：4 改 + 4 State 新 + 12 工具新 = 20 文件，~1500-2000 LOC。

### 3.2 State 类模板（以 HammerState 为例）

```csharp
namespace ZemaxMCP.Core.Services.ConstrainedOptimization;

/// <summary>
/// Persists Hammer optimization state between calls to support progress
/// reporting and cancellation. Mirrors MultistartState design.
/// </summary>
public class HammerState
{
    private volatile bool _isRunning;
    private volatile string? _algorithm;
    private volatile int _improvements;
    private volatile string? _terminationReason;
    private volatile string? _errorMessage;
    
    private double _initialMerit;
    private double _currentMerit;
    private double _bestMerit;
    private double _runtimeSeconds;
    private CancellationTokenSource? _cts;
    private Task? _backgroundTask;
    private readonly object _lock = new();

    public bool IsRunning => _isRunning;
    public string? Algorithm => _algorithm;
    public int Improvements => _improvements;
    public string? TerminationReason => _terminationReason;
    public string? ErrorMessage => _errorMessage;
    public double InitialMerit { get { lock (_lock) return _initialMerit; } }
    public double CurrentMerit { get { lock (_lock) return _currentMerit; } }
    public double BestMerit { get { lock (_lock) return _bestMerit; } }
    public double RuntimeSeconds { get { lock (_lock) return _runtimeSeconds; } }

    public void Reset() { /* ... */ }
    public void SetRunning(string algorithm) { /* ... */ }
    public void UpdateMerit(double current, double best, double runtimeSeconds, int improvements) { /* ... */ }
    public void SetCompleted(string terminationReason, string? error = null) { /* ... */ }
    public CancellationToken CreateCancellationToken() { /* same as MultistartState */ }
    public void RequestCancellation() { /* same */ }
    public void SetBackgroundTask(Task task) { _backgroundTask = task; }
}
```

每个 State 类的字段集略有不同（GlobalSearchState 有 `SolutionsValid`、OptimizeState 有 `Cycles` 等），但生命周期方法（Reset / SetRunning / SetCompleted / CreateCancellationToken / RequestCancellation / SetBackgroundTask）一致。

### 3.3 Sync 工具改造（Pattern A）

C# MCP SDK 在 tool 方法参数里支持 `IProgress<ProgressNotificationValue>` 和 `CancellationToken` — SDK 自动绑定。

以 HammerOptimizationTool 为例，改造点（其他 3 个 sync 工具同结构）：

```csharp
public async Task<HammerResult> ExecuteAsync(
    [Description("...")] string algorithm = "DLS",
    [Description("...")] int cores = 0,
    [Description("...")] double targetRuntimeMinutes = 5.0,
    [Description("...")] double timeoutSeconds = 120,
    [Description("...")] bool automatic = true,
    IProgress<ProgressNotificationValue>? progress = null,        // ← 新增
    CancellationToken cancellationToken = default)                // ← 新增
{
    var result = await _session.ExecuteAsync("Hammer", parameters, system =>
    {
        // ... 现有 setup ...
        var hammer = system.Tools?.OpenHammerOptimization();
        try
        {
            hammer.Run();
            
            long lastProgressMs = 0;
            while (true)
            {
                Thread.Sleep(1000);
                
                // 现有 stagnation/timeout 检查 ...
                
                // ← 新增：每 5s 一拍 progress
                long now = stopwatch.ElapsedMilliseconds;
                if (now - lastProgressMs >= 5000)
                {
                    progress?.Report(new ProgressNotificationValue
                    {
                        Progress = stopwatch.Elapsed.TotalSeconds,
                        Total = targetRuntimeMinutes * 60,
                        Message = $"hammer running for {(int)stopwatch.Elapsed.TotalSeconds}s, " +
                                  $"current merit: {bestMerit:F6}, improvements: {improvements}"
                    });
                    lastProgressMs = now;
                }
                
                // ← 新增：响应客户端取消
                if (cancellationToken.IsCancellationRequested)
                {
                    terminationReason = "Cancelled";
                    break;
                }
            }
            
            hammer.Cancel();
            hammer.WaitForCompletion();
            // ...
        }
        finally { hammer.Close(); }
    }, cancellationToken);                                        // ← 新增：转发 ct
}
```

### 3.4 Async 工具三件套（以 zemax_hammer_async 为例）

#### Submit (`HammerAsyncTool.cs`)

```csharp
[McpServerToolType]
public class HammerAsyncTool
{
    private readonly IZemaxSession _session;
    private readonly HammerState _state;

    public HammerAsyncTool(IZemaxSession session, HammerState state)
    {
        _session = session;
        _state = state;
    }

    public record HammerAsyncResult(bool Success, string? Error, string Message);

    [McpServerTool(Name = "zemax_hammer_async")]
    [Description(@"Start a non-blocking Hammer optimization. Returns immediately —
use zemax_hammer_status to poll progress and zemax_hammer_stop to cancel.
Same algorithm as zemax_hammer but the call doesn't block.")]
    public HammerAsyncResult Execute(
        string algorithm = "DLS",
        int cores = 0,
        double targetRuntimeMinutes = 5.0,
        double timeoutSeconds = 120,
        bool automatic = true)
    {
        if (_state.IsRunning)
        {
            return new HammerAsyncResult(false, "Already running",
                $"Hammer optimization is already running. Use zemax_hammer_status or zemax_hammer_stop.");
        }

        _state.Reset();
        _state.SetRunning(algorithm);
        var ct = _state.CreateCancellationToken();

        var task = Task.Run(async () =>
        {
            try
            {
                await _session.ExecuteAsync("HammerAsync", null, system =>
                {
                    // 同步 Hammer 执行体的拷贝，但 progress 写到 _state 而不是 IProgress
                    // (status 工具会读 _state)
                    var stopwatch = Stopwatch.StartNew();
                    var mfe = system.MFE;
                    var initialMerit = mfe.CalculateMeritFunction();
                    var hammer = system.Tools?.OpenHammerOptimization();
                    
                    try
                    {
                        // ... 同 sync 设置 ...
                        hammer.Run();
                        double bestMerit = double.MaxValue;
                        int improvements = 0;
                        
                        while (true)
                        {
                            Thread.Sleep(1000);
                            try
                            {
                                double currentMerit = hammer.CurrentMeritFunction;
                                if (currentMerit < bestMerit) {
                                    if (bestMerit < double.MaxValue) improvements++;
                                    bestMerit = currentMerit;
                                }
                                _state.UpdateMerit(currentMerit, bestMerit,
                                                   stopwatch.Elapsed.TotalSeconds, improvements);
                            } catch { }
                            
                            if (ct.IsCancellationRequested) break;
                            // ... stagnation / timeout 同 sync ...
                        }
                        
                        hammer.Cancel();
                        hammer.WaitForCompletion();
                        _state.SetCompleted(ct.IsCancellationRequested ? "Cancelled" : "Completed");
                    }
                    finally { hammer.Close(); }
                }, ct);
            }
            catch (OperationCanceledException)
            {
                _state.SetCompleted("Cancelled", "Cancelled by user");
            }
            catch (Exception ex)
            {
                _state.SetCompleted("Error", ex.Message);
            }
        });
        _state.SetBackgroundTask(task);

        return new HammerAsyncResult(true, null,
            $"Hammer optimization started (algorithm={algorithm}). " +
            "Use zemax_hammer_status to check progress, zemax_hammer_stop to cancel.");
    }
}
```

#### Status (`HammerStatusTool.cs`)

```csharp
[McpServerToolType]
public class HammerStatusTool
{
    private readonly HammerState _state;
    public HammerStatusTool(HammerState state) => _state = state;

    public record HammerStatus(
        bool IsRunning,
        string? Algorithm,
        double InitialMerit,
        double CurrentMerit,
        double BestMerit,
        int Improvements,
        double RuntimeSeconds,
        string? TerminationReason,
        string? ErrorMessage);

    [McpServerTool(Name = "zemax_hammer_status")]
    [Description("Get the current status of the running zemax_hammer_async optimization.")]
    public HammerStatus Execute()
    {
        return new HammerStatus(
            _state.IsRunning,
            _state.Algorithm,
            _state.InitialMerit,
            _state.CurrentMerit,
            _state.BestMerit,
            _state.Improvements,
            _state.RuntimeSeconds,
            _state.TerminationReason,
            _state.ErrorMessage);
    }
}
```

#### Stop (`HammerStopTool.cs`)

```csharp
[McpServerToolType]
public class HammerStopTool
{
    private readonly HammerState _state;
    public HammerStopTool(HammerState state) => _state = state;

    public record HammerStopResult(bool Success, string Message);

    [McpServerTool(Name = "zemax_hammer_stop")]
    [Description(@"Cancel the running zemax_hammer_async optimization.
The Hammer Cancel() will be issued; the runner finalises shortly after.
Status often reads ""running"" briefly post-cancel — re-poll zemax_hammer_status.")]
    public HammerStopResult Execute()
    {
        if (!_state.IsRunning)
        {
            return new HammerStopResult(false, "No running Hammer optimization to stop.");
        }
        _state.RequestCancellation();
        return new HammerStopResult(true,
            "Cancellation requested. Re-poll zemax_hammer_status for final state.");
    }
}
```

GlobalSearch / Optimize / ConstrainedOptimize 三套 async 工具与 Hammer 完全同结构，仅业务字段（cycles / solutionsToSave / maxIterations / mu / delta 等）和 State 类不同。

### 3.5 Program.cs DI 注册

```csharp
// 在 services 注册段加：
services.AddSingleton<HammerState>();
services.AddSingleton<GlobalSearchState>();
services.AddSingleton<OptimizeState>();
services.AddSingleton<ConstrainedOptimizeState>();

// 在 .WithTools<>() 链路上追加 12 个工具：
.WithTools<ZemaxMCP.Server.Tools.Optimization.HammerAsyncTool>()
.WithTools<ZemaxMCP.Server.Tools.Optimization.HammerStatusTool>()
.WithTools<ZemaxMCP.Server.Tools.Optimization.HammerStopTool>()
.WithTools<ZemaxMCP.Server.Tools.Optimization.GlobalSearchAsyncTool>()
.WithTools<ZemaxMCP.Server.Tools.Optimization.GlobalSearchStatusTool>()
.WithTools<ZemaxMCP.Server.Tools.Optimization.GlobalSearchStopTool>()
.WithTools<ZemaxMCP.Server.Tools.Optimization.OptimizeAsyncTool>()
.WithTools<ZemaxMCP.Server.Tools.Optimization.OptimizeStatusTool>()
.WithTools<ZemaxMCP.Server.Tools.Optimization.OptimizeStopTool>()
.WithTools<ZemaxMCP.Server.Tools.Optimization.ConstrainedOptimizeAsyncTool>()
.WithTools<ZemaxMCP.Server.Tools.Optimization.ConstrainedOptimizeStatusTool>()
.WithTools<ZemaxMCP.Server.Tools.Optimization.ConstrainedOptimizeStopTool>()
```

---

## 4. 数据流（4 路径）

### 路径 A — 同步 Hammer + 心跳

```
Client → tools/call zemax_hammer (+ _meta.progressToken)
  → SDK 自动绑定 IProgress<ProgressNotificationValue> + CancellationToken 入参
  → handler 进入 polling loop
  ← progress.Report(...) every 5s（SDK 翻译为 notifications/progress）
  → 检测 cancellationToken.IsCancellationRequested → break + Cancel()
  ← tools/call result（含 finalMerit）
```

### 路径 B — Async submit

```
Client → tools/call zemax_hammer_async(...)
  → 检查 _state.IsRunning，已跑 → reject
  → _state.Reset(); _state.SetRunning(algorithm)
  → ct = _state.CreateCancellationToken()
  → Task.Run(async () => _session.ExecuteAsync(..., ct, callback))
  → _state.SetBackgroundTask(task)
  ← {success: true, message: "Started, use zemax_hammer_status to poll"}
  
后台 task 中：每秒更新 _state.UpdateMerit(...)，结束调 _state.SetCompleted(...)
```

### 路径 C — Status 轮询

```
Client → tools/call zemax_hammer_status()
  → 直接读 _state 各字段（volatile 读 + 必要 lock）
  ← {isRunning, currentMerit, bestMerit, runtimeSeconds, ...}
```

无阻塞，立即返回。客户端可任意频率调用（建议 5-10s/次）。

### 路径 D — Stop

```
Client → tools/call zemax_hammer_stop()
  → 检查 _state.IsRunning，未跑 → no-op + warn
  → _state.RequestCancellation() — _cts.Cancel()
  ← {success: true, message: "Cancellation requested..."}

后台 task 内 ct.IsCancellationRequested 检测命中 → 退出 polling → hammer.Cancel() → _state.SetCompleted("Cancelled")
```

注意：
- Status 在 stop 后**短暂仍读为 "running"**，因为 runner goroutine 还在 finalising
- 这跟 multistart 现状一致；description 已写明，要求客户端再 poll status

---

## 5. 错误处理与边界

| 场景 | 行为 |
|---|---|
| Submit 时 IsRunning=true | reject，返回 `{success:false, error:"Already running"}` |
| Zemax COM 抛 InvalidOperationException | runner catch → `_state.SetCompleted("Error", ex.Message)` |
| 客户端断开 sync 调用 | sync handler 的 ct cancel → `Hammer.Cancel()` → tool 返回 `Cancelled` |
| async stop 后 status 立即查 | status="running"（runner 还在 finalising），再 poll 转 cancelled |
| Status 在 IsRunning=false 时调 | 返回最后一次 SetCompleted 的字段（terminationReason / error 都有） |
| Stop 在 IsRunning=false 时调 | warn，不报错；幂等 |
| 同时两次 submit | 第二次直接 reject（per-tool State 是单例） |
| Server 崩溃 | State 丢失，与 multistart 现状一致；不持久化 |

### 5.1 Pattern A 心跳的 emit 失败容错

`progress?.Report(...)` 在客户端断开时由 SDK 内部抛 `ObjectDisposedException`。runner 内不主动 catch（让 ct.IsCancellationRequested 在下一拍命中即可），让 SDK 自然管理 transport 状态。

### 5.2 Cancel 与 Zemax 内部 timeout 的优先级

Hammer 自身的 stagnation timeout（`timeoutSeconds`）与 client cancel（`cancellationToken`）独立。优先级：
1. ct.IsCancellationRequested 命中 → break loop → terminationReason = "Cancelled"
2. stagnation 命中 → break loop → terminationReason = "Stagnation"
3. maxRuntime 命中 → break loop → terminationReason = "MaxRuntime"

第一个满足的条件决定 termination。

---

## 6. 测试策略

### 6.1 测试基础设施
ZemaxMCP 现有测试：
```bash
ls TestCase/
ls src/ZemaxMCP.Core/  # 没有 Tests 子项目
```

需要确认 ZemaxMCP 有没有正式单测项目。如果没有：
- 不为 State 类强制建测试项目（最小侵入）
- 优先靠端到端 smoke（用真 Zemax / mock COM）覆盖关键路径
- 如果有：State 类与 sync 工具的 progress 路径加测试

### 6.2 端到端冒烟（每 commit 后跑）

| Smoke | 内容 | 通过标准 |
|---|---|---|
| 1 | `zemax_hammer(targetRuntimeMinutes=0.5, timeoutSeconds=15)` 正常返回 | <40s 拿到 result |
| 2 | `zemax_hammer(targetRuntimeMinutes=2)` + 客户端传 progressToken | 期间收到 ≥3 progress 通知 |
| 3 | `zemax_hammer_async(...)` → status 显示 IsRunning=true → 等待完成 → status 显示 terminationReason | 全流程畅通 |
| 4 | `zemax_hammer_async(...)` → 30s 后 stop → status 转 Cancelled | cancel 路径 OK |
| 5 | 4 个 async 工具各跑一次（hammer/global_search/optimize/constrained_optimize） | 无回归 |

### 6.3 编译 + 静态分析

每 commit：
```bash
dotnet build src/ZemaxMCP.Server/ZemaxMCP.Server.csproj
dotnet build src/ZemaxMCP.Core/ZemaxMCP.Core.csproj
```

无 warning（视项目现有 warning 基线决定 -warnaserror 是否开）。

---

## 7. 提交切片（7 个 commit）

| # | 内容 | 部署后冒烟 |
|---|---|---|
| 1 | 4 个 State 类 + DI singleton 注册 | 编译通过即可（不引入功能变化） |
| 2 | 4 个 sync 工具加 Pattern A 心跳 + CancellationToken | Smoke 1+2（hammer 例） |
| 3 | Hammer async 三件套（Submit/Status/Stop） + Program.cs 注册 | Smoke 3+4（hammer） |
| 4 | GlobalSearch async 三件套 | Smoke 3+4（global_search） |
| 5 | Optimize async 三件套 | Smoke 3+4（optimize） |
| 6 | ConstrainedOptimize async 三件套 | Smoke 3+4（constrained_optimize） |
| 7 | docs/async-optimization.md + README 工具列表更新 | N/A |

每 commit 项目独立编译 + 通过冒烟。

---

## 8. 部署与回滚

### 8.1 构建
```bash
cd /e/ZemaxProject/ZemaxMCP_源码_本地开发版
dotnet build src/ZemaxMCP.Server/ZemaxMCP.Server.csproj -c Release
```
产物：`src/ZemaxMCP.Server/bin/Release/net48/ZemaxMCP.Server.exe`

### 8.2 部署
当前 Claude Code 中 `zemax-mcp` 入口指向 `C:\Users\xufen\Documents\Zemax\ZemaxMCP\ZemaxMCP.Server.exe`。.NET 4.8 项目部署需要把 build output 整个 `bin/Release/net48/` 目录拷过去（含 `.exe` + 依赖 `.dll`），不能只换 exe：

```bash
DEPLOY_DIR="/c/Users/xufen/Documents/Zemax/ZemaxMCP"
BUILD_DIR="/e/ZemaxProject/ZemaxMCP_源码_本地开发版/src/ZemaxMCP.Server/bin/Release/net48"

# 部署前先杀进程释放 .exe 文件锁
taskkill //F //IM ZemaxMCP.Server.exe 2>/dev/null
# 备份
cp -r "$DEPLOY_DIR" "${DEPLOY_DIR}.bak"
# 替换
cp -r "$BUILD_DIR/." "$DEPLOY_DIR/"
```

部署后让用户重启 Claude Code，下一次 zemax 工具调用会拉起新 binary。

### 8.3 回滚
原 binary 备份到 `.bak`，失败时直接还原。Git 分支保留所有改动。

---

## 9. 验收标准

| 场景 | 期望 |
|---|---|
| `zemax_hammer(targetRuntimeMinutes=5)` 同步调用 | 5 min 内不被 30s/600s watchdog 杀 |
| `zemax_hammer_async` 提交 30 min 任务 | submit <100ms 返回 jobId-like 应答 |
| `zemax_*_async` 期间 stop | status 后续 poll 显示 cancelled |
| 客户端断开 sync 长任务 | Zemax `Hammer.Cancel()` 立即触发，工具不留挂起 |
| MultistartOptimize 不回归 | 现有 multistart 行为完全不变 |
| 4 个 sync 长工具调用方式不变 | 不传 progressToken 时行为与改造前一致 |

---

## 10. 已知限制（写入 PR description）

1. Server 进程崩溃后 async job 不存活（与 multistart 现状一致）
2. Cancel 后 status 短暂读 "running"（runner 收尾期）
3. 同一类型 async 工具同时只能跑 1 个（per-tool State 是单例）
4. multistart 仍走原始 per-tool State 模式，不重构进新统一抽象
5. C# Tests 项目暂未存在 → 单元测试覆盖仅在新 State 行为可验证时加，否则靠 smoke

---

## 11. 参考

- 关联设计：`E:\202603CWFSWork\WFS_Mephisto_yellowChannel\docs\superpowers\specs\2026-04-28-matlab-mcp-long-task-async-design.md`（matlab-mcp 同问题家族）
- multistart 模板：
  - `src/ZemaxMCP.Core/Services/ConstrainedOptimization/MultistartState.cs`
  - `src/ZemaxMCP.Server/Tools/Optimization/MultistartOptimizeTool.cs`
  - `src/ZemaxMCP.Server/Tools/Optimization/MultistartStatusTool.cs`
  - `src/ZemaxMCP.Server/Tools/Optimization/MultistartStopTool.cs`
- C# MCP SDK：`ModelContextProtocol`（NuGet 包，Server attribute-based 模式 + IProgress<ProgressNotificationValue> 自动绑定）
- 用户运行约束：`~/.claude/CLAUDE.md` § 5 Zemax OpticStudio MCP 规则
