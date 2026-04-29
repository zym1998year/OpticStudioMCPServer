# ZemaxMCP 长任务异步改造 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 给 ZemaxMCP 4 个 sync 长优化工具加 Pattern A（progress 心跳 + CancellationToken）+ 各自独立的 async 工具三件套（Pattern B），让它们能扛住 MCP 客户端 30s/600s watchdog。

**Architecture:** 复刻已有的 `MultistartState` per-tool 富状态模式。每个长工具一个 `<Name>State` 单例 + 一对 `<Name>AsyncTool` / `<Name>StatusTool` / `<Name>StopTool` 工具。Sync 入口加 `IProgress<ProgressNotificationValue>` + `CancellationToken` 由 C# MCP SDK 自动绑定。Multistart 不动。

**Tech Stack:** .NET Framework 4.8、`ModelContextProtocol` C# SDK、`Microsoft.Extensions.DependencyInjection` (Singleton)、ZOSAPI (Zemax COM interop)。

**Spec:** `docs/superpowers/specs/2026-04-29-zemax-mcp-long-task-async-design.md`

**Working tree:**
- 仓库：`E:\ZemaxProject\ZemaxMCP_源码_本地开发版` (fork：zym1998year/OpticStudioMCPServer)
- 分支：`feature/long-task-async`（已创建）
- Spec 已 commit `464e165`

**Build / deploy paths:**
- 构建：`dotnet build src/ZemaxMCP.Server/ZemaxMCP.Server.csproj -c Release`
- 产物：`src/ZemaxMCP.Server/bin/Release/net48/`
- 部署：`C:\Users\xufen\Documents\Zemax\ZemaxMCP\` （含整个 net48 目录的所有 .exe + .dll）

**所有路径若以 `src/` / `docs/` 开头 = 仓库相对路径。**

---

## Phase 0 — 环境与 baseline

### Task 0.1: 确认 Bash + dotnet + 仓库状态

**Files:** 无文件改动。

- [ ] **Step 1: 验证 dotnet 可用**

```bash
source ~/.bash_env 2>/dev/null
dotnet --version
```
Expected：8.x 或 9.x（任何 .NET SDK 都能 build net48 项目）。如果未安装：`winget install Microsoft.DotNet.SDK.8` 或从 https://dotnet.microsoft.com/download 装。

- [ ] **Step 2: 确认在正确分支 + clean working tree**

```bash
cd /e/ZemaxProject/ZemaxMCP_源码_本地开发版
git branch --show-current
git status --short
```
Expected：`feature/long-task-async`，working tree clean（spec 已 commit）。

### Task 0.2: Baseline build

- [ ] **Step 1: 还原依赖**

```bash
cd /e/ZemaxProject/ZemaxMCP_源码_本地开发版
dotnet restore OpticStudioMCPServer.sln
```
Expected：success，无 error。

- [ ] **Step 2: Build 全 solution**

```bash
dotnet build OpticStudioMCPServer.sln -c Release
```
Expected：`Build succeeded.` 0 error；warnings 视项目现有基线决定，但**不增加新 warning**。

- [ ] **Step 3: 列产物**

```bash
ls src/ZemaxMCP.Server/bin/Release/net48/ZemaxMCP.Server.exe
```
Expected：文件存在。

---

## Phase 1 — 4 个 State 类 + DI 注册（Commit 1）

模仿 `MultistartState` 模式，给 4 个长工具各加一个 State 类。**纯新增 + DI 注册一行**，不动业务逻辑。

### Task 1.1: 新建 HammerState

**Files:**
- Create: `src/ZemaxMCP.Core/Services/ConstrainedOptimization/HammerState.cs`

- [ ] **Step 1: 写 HammerState.cs**

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
    public bool HasState => !_isRunning && !string.IsNullOrEmpty(_terminationReason);

    public void Reset()
    {
        _isRunning = false;
        _algorithm = null;
        _improvements = 0;
        _terminationReason = null;
        _errorMessage = null;
        lock (_lock)
        {
            _initialMerit = 0;
            _currentMerit = 0;
            _bestMerit = 0;
            _runtimeSeconds = 0;
        }
    }

    public void SetRunning(string algorithm, double initialMerit)
    {
        _isRunning = true;
        _algorithm = algorithm;
        _terminationReason = null;
        _errorMessage = null;
        _improvements = 0;
        lock (_lock)
        {
            _initialMerit = initialMerit;
            _currentMerit = initialMerit;
            _bestMerit = initialMerit;
            _runtimeSeconds = 0;
        }
    }

    public void UpdateMerit(double currentMerit, double bestMerit, double runtimeSeconds, int improvements)
    {
        _improvements = improvements;
        lock (_lock)
        {
            _currentMerit = currentMerit;
            _bestMerit = bestMerit;
            _runtimeSeconds = runtimeSeconds;
        }
    }

    public void SetCompleted(string terminationReason, string? error = null)
    {
        _isRunning = false;
        _terminationReason = terminationReason;
        _errorMessage = error;
    }

    /// <summary>
    /// Create a fresh CancellationTokenSource for a new run. Disposes any
    /// previous one. Returns the token to be passed into the background task.
    /// </summary>
    public CancellationToken CreateCancellationToken()
    {
        lock (_lock)
        {
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            return _cts.Token;
        }
    }

    /// <summary>
    /// Request cancellation of the running Hammer optimization. The runner's
    /// loop will observe the cancellation in its next polling tick and call
    /// hammer.Cancel() before returning.
    /// </summary>
    public void RequestCancellation()
    {
        lock (_lock) { _cts?.Cancel(); }
    }

    public void SetBackgroundTask(Task task) { _backgroundTask = task; }
    public Task? BackgroundTask => _backgroundTask;
}
```

- [ ] **Step 2: 编译**

```bash
dotnet build src/ZemaxMCP.Core/ZemaxMCP.Core.csproj -c Release
```
Expected：success，无 error。

### Task 1.2: 新建 GlobalSearchState

**Files:**
- Create: `src/ZemaxMCP.Core/Services/ConstrainedOptimization/GlobalSearchState.cs`

- [ ] **Step 1: 写 GlobalSearchState.cs**

```csharp
namespace ZemaxMCP.Core.Services.ConstrainedOptimization;

/// <summary>
/// Persists Global Search optimization state. Mirrors MultistartState design.
/// </summary>
public class GlobalSearchState
{
    private volatile bool _isRunning;
    private volatile string? _algorithm;
    private volatile int _solutionsValid;
    private volatile string? _terminationReason;
    private volatile string? _errorMessage;

    private double _initialMerit;
    private double _bestMerit;
    private double _runtimeSeconds;
    private CancellationTokenSource? _cts;
    private Task? _backgroundTask;
    private readonly object _lock = new();

    public bool IsRunning => _isRunning;
    public string? Algorithm => _algorithm;
    public int SolutionsValid => _solutionsValid;
    public string? TerminationReason => _terminationReason;
    public string? ErrorMessage => _errorMessage;
    public double InitialMerit { get { lock (_lock) return _initialMerit; } }
    public double BestMerit { get { lock (_lock) return _bestMerit; } }
    public double RuntimeSeconds { get { lock (_lock) return _runtimeSeconds; } }
    public bool HasState => !_isRunning && !string.IsNullOrEmpty(_terminationReason);

    public void Reset()
    {
        _isRunning = false;
        _algorithm = null;
        _solutionsValid = 0;
        _terminationReason = null;
        _errorMessage = null;
        lock (_lock)
        {
            _initialMerit = 0;
            _bestMerit = 0;
            _runtimeSeconds = 0;
        }
    }

    public void SetRunning(string algorithm, double initialMerit)
    {
        _isRunning = true;
        _algorithm = algorithm;
        _terminationReason = null;
        _errorMessage = null;
        _solutionsValid = 0;
        lock (_lock)
        {
            _initialMerit = initialMerit;
            _bestMerit = initialMerit;
            _runtimeSeconds = 0;
        }
    }

    public void UpdateProgress(double bestMerit, double runtimeSeconds, int solutionsValid)
    {
        _solutionsValid = solutionsValid;
        lock (_lock)
        {
            _bestMerit = bestMerit;
            _runtimeSeconds = runtimeSeconds;
        }
    }

    public void SetCompleted(string terminationReason, string? error = null)
    {
        _isRunning = false;
        _terminationReason = terminationReason;
        _errorMessage = error;
    }

    public CancellationToken CreateCancellationToken()
    {
        lock (_lock)
        {
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            return _cts.Token;
        }
    }

    public void RequestCancellation()
    {
        lock (_lock) { _cts?.Cancel(); }
    }

    public void SetBackgroundTask(Task task) { _backgroundTask = task; }
    public Task? BackgroundTask => _backgroundTask;
}
```

- [ ] **Step 2: 编译**

```bash
dotnet build src/ZemaxMCP.Core/ZemaxMCP.Core.csproj -c Release
```
Expected：success。

### Task 1.3: 新建 OptimizeState

**Files:**
- Create: `src/ZemaxMCP.Core/Services/ConstrainedOptimization/OptimizeState.cs`

- [ ] **Step 1: 写 OptimizeState.cs**

```csharp
namespace ZemaxMCP.Core.Services.ConstrainedOptimization;

/// <summary>
/// Persists Local Optimization (zemax_optimize) state. Mirrors MultistartState design.
/// </summary>
public class OptimizeState
{
    private volatile bool _isRunning;
    private volatile string? _algorithm;
    private volatile int _cyclesCompleted;
    private volatile int _cyclesRequested;
    private volatile string? _terminationReason;
    private volatile string? _errorMessage;

    private double _initialMerit;
    private double _currentMerit;
    private double _runtimeSeconds;
    private CancellationTokenSource? _cts;
    private Task? _backgroundTask;
    private readonly object _lock = new();

    public bool IsRunning => _isRunning;
    public string? Algorithm => _algorithm;
    public int CyclesCompleted => _cyclesCompleted;
    public int CyclesRequested => _cyclesRequested;
    public string? TerminationReason => _terminationReason;
    public string? ErrorMessage => _errorMessage;
    public double InitialMerit { get { lock (_lock) return _initialMerit; } }
    public double CurrentMerit { get { lock (_lock) return _currentMerit; } }
    public double RuntimeSeconds { get { lock (_lock) return _runtimeSeconds; } }
    public bool HasState => !_isRunning && !string.IsNullOrEmpty(_terminationReason);

    public void Reset()
    {
        _isRunning = false;
        _algorithm = null;
        _cyclesCompleted = 0;
        _cyclesRequested = 0;
        _terminationReason = null;
        _errorMessage = null;
        lock (_lock)
        {
            _initialMerit = 0;
            _currentMerit = 0;
            _runtimeSeconds = 0;
        }
    }

    public void SetRunning(string algorithm, int cyclesRequested, double initialMerit)
    {
        _isRunning = true;
        _algorithm = algorithm;
        _cyclesRequested = cyclesRequested;
        _cyclesCompleted = 0;
        _terminationReason = null;
        _errorMessage = null;
        lock (_lock)
        {
            _initialMerit = initialMerit;
            _currentMerit = initialMerit;
            _runtimeSeconds = 0;
        }
    }

    public void UpdateProgress(int cyclesCompleted, double currentMerit, double runtimeSeconds)
    {
        _cyclesCompleted = cyclesCompleted;
        lock (_lock)
        {
            _currentMerit = currentMerit;
            _runtimeSeconds = runtimeSeconds;
        }
    }

    public void SetCompleted(string terminationReason, string? error = null)
    {
        _isRunning = false;
        _terminationReason = terminationReason;
        _errorMessage = error;
    }

    public CancellationToken CreateCancellationToken()
    {
        lock (_lock)
        {
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            return _cts.Token;
        }
    }

    public void RequestCancellation()
    {
        lock (_lock) { _cts?.Cancel(); }
    }

    public void SetBackgroundTask(Task task) { _backgroundTask = task; }
    public Task? BackgroundTask => _backgroundTask;
}
```

- [ ] **Step 2: 编译**

```bash
dotnet build src/ZemaxMCP.Core/ZemaxMCP.Core.csproj -c Release
```
Expected：success。

### Task 1.4: 新建 ConstrainedOptimizeState

**Files:**
- Create: `src/ZemaxMCP.Core/Services/ConstrainedOptimization/ConstrainedOptimizeState.cs`

- [ ] **Step 1: 写 ConstrainedOptimizeState.cs**

```csharp
namespace ZemaxMCP.Core.Services.ConstrainedOptimization;

/// <summary>
/// Persists Constrained Optimization (zemax_constrained_optimize) state.
/// LM-specific fields: iteration / mu / restartsUsed.
/// </summary>
public class ConstrainedOptimizeState
{
    private volatile bool _isRunning;
    private volatile int _iteration;
    private volatile int _maxIterations;
    private volatile int _restartsUsed;
    private volatile string? _terminationReason;
    private volatile string? _errorMessage;

    private double _initialMerit;
    private double _currentMerit;
    private double _mu;
    private double _runtimeSeconds;
    private CancellationTokenSource? _cts;
    private Task? _backgroundTask;
    private readonly object _lock = new();

    public bool IsRunning => _isRunning;
    public int Iteration => _iteration;
    public int MaxIterations => _maxIterations;
    public int RestartsUsed => _restartsUsed;
    public string? TerminationReason => _terminationReason;
    public string? ErrorMessage => _errorMessage;
    public double InitialMerit { get { lock (_lock) return _initialMerit; } }
    public double CurrentMerit { get { lock (_lock) return _currentMerit; } }
    public double Mu { get { lock (_lock) return _mu; } }
    public double RuntimeSeconds { get { lock (_lock) return _runtimeSeconds; } }
    public bool HasState => !_isRunning && !string.IsNullOrEmpty(_terminationReason);

    public void Reset()
    {
        _isRunning = false;
        _iteration = 0;
        _maxIterations = 0;
        _restartsUsed = 0;
        _terminationReason = null;
        _errorMessage = null;
        lock (_lock)
        {
            _initialMerit = 0;
            _currentMerit = 0;
            _mu = 0;
            _runtimeSeconds = 0;
        }
    }

    public void SetRunning(int maxIterations, double initialMu, double initialMerit)
    {
        _isRunning = true;
        _maxIterations = maxIterations;
        _iteration = 0;
        _restartsUsed = 0;
        _terminationReason = null;
        _errorMessage = null;
        lock (_lock)
        {
            _initialMerit = initialMerit;
            _currentMerit = initialMerit;
            _mu = initialMu;
            _runtimeSeconds = 0;
        }
    }

    public void UpdateProgress(int iteration, double currentMerit, double mu, double runtimeSeconds, int restartsUsed)
    {
        _iteration = iteration;
        _restartsUsed = restartsUsed;
        lock (_lock)
        {
            _currentMerit = currentMerit;
            _mu = mu;
            _runtimeSeconds = runtimeSeconds;
        }
    }

    public void SetCompleted(string terminationReason, string? error = null)
    {
        _isRunning = false;
        _terminationReason = terminationReason;
        _errorMessage = error;
    }

    public CancellationToken CreateCancellationToken()
    {
        lock (_lock)
        {
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            return _cts.Token;
        }
    }

    public void RequestCancellation()
    {
        lock (_lock) { _cts?.Cancel(); }
    }

    public void SetBackgroundTask(Task task) { _backgroundTask = task; }
    public Task? BackgroundTask => _backgroundTask;
}
```

- [ ] **Step 2: 编译**

```bash
dotnet build src/ZemaxMCP.Core/ZemaxMCP.Core.csproj -c Release
```
Expected：success。

### Task 1.5: 注册 4 个 State Singleton + commit

**Files:**
- Modify: `src/ZemaxMCP.Server/Program.cs` (插入 4 行 DI 注册)

- [ ] **Step 1: 修改 Program.cs**

找到 `builder.Services.AddSingleton<MultistartState>();`（约第 55 行），紧接其后插入：

```csharp
    builder.Services.AddSingleton<HammerState>();
    builder.Services.AddSingleton<GlobalSearchState>();
    builder.Services.AddSingleton<OptimizeState>();
    builder.Services.AddSingleton<ConstrainedOptimizeState>();
```

完整修改后该段代码看起来像：

```csharp
    builder.Services.AddSingleton<ConstraintStore>();
    builder.Services.AddSingleton<MultistartState>();
    builder.Services.AddSingleton<HammerState>();
    builder.Services.AddSingleton<GlobalSearchState>();
    builder.Services.AddSingleton<OptimizeState>();
    builder.Services.AddSingleton<ConstrainedOptimizeState>();
```

- [ ] **Step 2: 编译全 solution**

```bash
dotnet build OpticStudioMCPServer.sln -c Release
```
Expected：success，无 error 无新 warning。

- [ ] **Step 3: Commit**

```bash
git add src/ZemaxMCP.Core/Services/ConstrainedOptimization/HammerState.cs \
        src/ZemaxMCP.Core/Services/ConstrainedOptimization/GlobalSearchState.cs \
        src/ZemaxMCP.Core/Services/ConstrainedOptimization/OptimizeState.cs \
        src/ZemaxMCP.Core/Services/ConstrainedOptimization/ConstrainedOptimizeState.cs \
        src/ZemaxMCP.Server/Program.cs

git commit -m "feat(state): per-tool optimization state classes (Hammer/GlobalSearch/Optimize/ConstrainedOptimize)

Adds 4 new State classes mirroring MultistartState design:
- HammerState: algorithm + improvements + terminationReason + merit fields
- GlobalSearchState: algorithm + solutionsValid + merit fields
- OptimizeState: algorithm + cyclesCompleted/cyclesRequested + merit fields
- ConstrainedOptimizeState: iteration/maxIterations + mu + restartsUsed + merit fields

All registered as Singleton in Program.cs DI container. Pure additive — no
existing code paths affected.

These states will be consumed by Pattern A heartbeat (next commit) and the
async tool families (subsequent commits)."
```

---

## Phase 2 — 4 个 sync 长工具加 Pattern A 心跳 + CancellationToken（Commit 2）

每个 sync 工具的 `ExecuteAsync` 加两个参数（`IProgress<ProgressNotificationValue>?` + `CancellationToken`），由 C# MCP SDK 自动绑定。每 5s 推 progress + ct cancel 触发优化中断。

### Task 2.1: HammerOptimizationTool 加心跳 + ct

**Files:**
- Modify: `src/ZemaxMCP.Server/Tools/Optimization/HammerOptimizationTool.cs`

- [ ] **Step 1: 在 using 区加 import**

文件顶部在 `using ZemaxMCP.Core.Session;` 之后添加：

```csharp
using ModelContextProtocol.Protocol;
```

- [ ] **Step 2: 修改 ExecuteAsync 方法签名**

旧：
```csharp
    public async Task<HammerResult> ExecuteAsync(
        [Description("Optimization algorithm: DLS or Orthogonal")] string algorithm = "DLS",
        [Description("Number of CPU cores to use (0 for all available)")] int cores = 0,
        [Description("Target runtime in minutes (for automatic mode)")] double targetRuntimeMinutes = 5.0,
        [Description("Maximum runtime in seconds (timeout)")] double timeoutSeconds = 120,
        [Description("Use automatic optimization mode (true) or fixed cycles (false)")] bool automatic = true)
    {
```

新（追加 2 个参数 + ct 转发）：
```csharp
    public async Task<HammerResult> ExecuteAsync(
        [Description("Optimization algorithm: DLS or Orthogonal")] string algorithm = "DLS",
        [Description("Number of CPU cores to use (0 for all available)")] int cores = 0,
        [Description("Target runtime in minutes (for automatic mode)")] double targetRuntimeMinutes = 5.0,
        [Description("Maximum runtime in seconds (timeout)")] double timeoutSeconds = 120,
        [Description("Use automatic optimization mode (true) or fixed cycles (false)")] bool automatic = true,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
```

- [ ] **Step 3: 把 ct 转发到 _session.ExecuteAsync**

旧（约第 49 行）：
```csharp
            var result = await _session.ExecuteAsync("Hammer", parameters, system =>
```

新：
```csharp
            var result = await _session.ExecuteAsync("Hammer", parameters, system =>
```
（注意：这一行不变；改的是末尾 closing — 把 lambda 关闭后增加 `, cancellationToken`）。具体查找：

旧的方法体末尾（lambda 闭合处）：
```csharp
                finally
                {
                    hammer.Close();
                }
            });

            return result;
```

改为：
```csharp
                finally
                {
                    hammer.Close();
                }
            }, cancellationToken);

            return result;
```

- [ ] **Step 4: 在 polling loop 中插入 progress + ct 检查**

找到现有的 polling loop（约第 105-142 行）：
```csharp
                    while (true)
                    {
                        Thread.Sleep(1000);

                        try
                        {
                            double currentMerit = hammer.CurrentMeritFunction;
                            long now = stopwatch.ElapsedMilliseconds;

                            if (currentMerit < bestMerit)
                            {
                                if (bestMerit < double.MaxValue)
                                    improvements++;
                                bestMerit = currentMerit;
                                lastImprovedMs = now;
                            }

                            long idleMs = now - lastImprovedMs;

                            // Stop if stagnated...
                            if (idleMs >= timeoutMs)
                            {
                                terminationReason = improvements > 0 ? "Stagnation" : "NoImprovement";
                                break;
                            }

                            if (now >= totalRuntimeMs)
                            {
                                terminationReason = "MaxRuntime";
                                break;
                            }
                        }
                        catch
                        {
                            // Hammer may throw while running; ignore and keep polling
                        }
                    }
```

插入两个检查（progress emit 每 5s 一拍 + ct 检查），改为：

```csharp
                    long lastProgressMs = 0;
                    const long progressIntervalMs = 5000;

                    while (true)
                    {
                        Thread.Sleep(1000);

                        try
                        {
                            double currentMerit = hammer.CurrentMeritFunction;
                            long now = stopwatch.ElapsedMilliseconds;

                            if (currentMerit < bestMerit)
                            {
                                if (bestMerit < double.MaxValue)
                                    improvements++;
                                bestMerit = currentMerit;
                                lastImprovedMs = now;
                            }

                            // Emit progress every 5s; SDK is a no-op when client did
                            // not provide a progressToken.
                            if (now - lastProgressMs >= progressIntervalMs)
                            {
                                progress?.Report(new ProgressNotificationValue
                                {
                                    Progress = (float)(stopwatch.Elapsed.TotalSeconds),
                                    Total = (float)(targetRuntimeMinutes * 60),
                                    Message = $"hammer running for {(int)stopwatch.Elapsed.TotalSeconds}s, " +
                                              $"best merit: {bestMerit:F6}, improvements: {improvements}"
                                });
                                lastProgressMs = now;
                            }

                            // Honor client-side cancellation.
                            if (cancellationToken.IsCancellationRequested)
                            {
                                terminationReason = "Cancelled";
                                break;
                            }

                            long idleMs = now - lastImprovedMs;

                            if (idleMs >= timeoutMs)
                            {
                                terminationReason = improvements > 0 ? "Stagnation" : "NoImprovement";
                                break;
                            }

                            if (now >= totalRuntimeMs)
                            {
                                terminationReason = "MaxRuntime";
                                break;
                            }
                        }
                        catch
                        {
                            // Hammer may throw while running; ignore and keep polling
                        }
                    }
```

- [ ] **Step 5: 编译**

```bash
dotnet build src/ZemaxMCP.Server/ZemaxMCP.Server.csproj -c Release
```
Expected：success。

> 如果 `ProgressNotificationValue` 类型未找到，看 `using` 是否漏了 `ModelContextProtocol.Protocol`。如果 SDK 版本不同（旧版可能叫 `ProgressNotificationParams`），用 `find ~/.nuget/packages/modelcontextprotocol -name "ProgressNotification*.cs"` 查实际类型名，调整 import。

### Task 2.2: GlobalSearchTool 加心跳 + ct

**Files:**
- Modify: `src/ZemaxMCP.Server/Tools/Optimization/GlobalSearchTool.cs`

- [ ] **Step 1: 加 import**

```csharp
using ModelContextProtocol.Protocol;
```

- [ ] **Step 2: 方法签名加 2 个参数**

找到 `public async Task<GlobalSearchResult> ExecuteAsync(`，在所有现有参数末尾追加：

```csharp
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
```

- [ ] **Step 3: ct 转发到 _session.ExecuteAsync**

把 lambda 闭合处 `});` 改为 `}, cancellationToken);`。

- [ ] **Step 4: 由于 GlobalSearch 用 `RunAndWaitWithTimeout(timeoutSeconds)` 单调用，不像 Hammer 有 polling loop，progress + ct 用 Task 包装并发**

旧（约第 100-114 行）：
```csharp
                    string terminationReason;
                    double actualRuntime = timeoutSeconds;

                    if (timeoutSeconds > 0)
                    {
                        var runStatus = globalOpt.RunAndWaitWithTimeout(timeoutSeconds);
                        terminationReason = runStatus.ToString();
                    }
                    else
                    {
                        // Run without timeout (automatic termination)
                        globalOpt.RunAndWaitForCompletion();
                        terminationReason = "Completed";
                    }
```

改为带 polling 的版本，把 `RunAndWaitWithTimeout` 异步起跑后自轮询：

```csharp
                    string terminationReason;
                    double actualRuntime;
                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                    // Start GlobalSearch non-blocking via Run() (Run returns immediately,
                    // optimization runs in Zemax's background thread). We poll for
                    // completion or external cancellation.
                    globalOpt.Run();

                    long lastProgressMs = 0;
                    const long progressIntervalMs = 5000;
                    long timeoutMs = (long)(timeoutSeconds * 1000);
                    bool completed = false;

                    while (!completed)
                    {
                        Thread.Sleep(1000);

                        long now = stopwatch.ElapsedMilliseconds;

                        // Emit progress every 5s.
                        if (now - lastProgressMs >= progressIntervalMs)
                        {
                            double currentBest = 0;
                            try { currentBest = globalOpt.CurrentMeritFunction(1); } catch { }
                            progress?.Report(new ProgressNotificationValue
                            {
                                Progress = (float)(stopwatch.Elapsed.TotalSeconds),
                                Total = (float)(timeoutSeconds > 0 ? timeoutSeconds : 0),
                                Message = $"global search running for {(int)stopwatch.Elapsed.TotalSeconds}s, " +
                                          $"best merit so far: {currentBest:F6}"
                            });
                            lastProgressMs = now;
                        }

                        // Honor client-side cancellation.
                        if (cancellationToken.IsCancellationRequested)
                        {
                            terminationReason = "Cancelled";
                            globalOpt.Cancel();
                            globalOpt.WaitForCompletion();
                            completed = true;
                            break;
                        }

                        // Timeout check (mimic RunAndWaitWithTimeout behavior).
                        if (timeoutSeconds > 0 && now >= timeoutMs)
                        {
                            terminationReason = "Timeout";
                            globalOpt.Cancel();
                            globalOpt.WaitForCompletion();
                            completed = true;
                            break;
                        }

                        // Check natural completion.
                        try
                        {
                            if (!globalOpt.IsRunning)
                            {
                                terminationReason = "Completed";
                                completed = true;
                                break;
                            }
                        }
                        catch { /* keep polling */ }
                    }

                    actualRuntime = stopwatch.Elapsed.TotalSeconds;
                    stopwatch.Stop();
```

> **Note**：上面的 `globalOpt.Run()` 与原 `RunAndWaitWithTimeout()` 行为差异 — 原版会原地阻塞，新版用 `Run()` 立即返回再自轮询。Zemax ZOSAPI 的 `IGlobalOptimization.Run()` 是非阻塞的（与 Hammer 一致），polling `IsRunning` 检测自然结束。如果 `IsRunning` 属性不存在，改用 try/catch 包 `CurrentMeritFunction(1)`：异常时认为还在跑。

- [ ] **Step 5: 编译**

```bash
dotnet build src/ZemaxMCP.Server/ZemaxMCP.Server.csproj -c Release
```
Expected：success。如果 `IsRunning` 属性报错（`'IGlobalOptimization' does not contain a definition for 'IsRunning'`），用现有 `RunAndWaitForCompletion` + 后台 Task 包裹的方式重做这一段；上述实现是基于 ZOSAPI 标准 API 的合理推测。

### Task 2.3: OptimizeTool 加心跳 + ct

**Files:**
- Modify: `src/ZemaxMCP.Server/Tools/Optimization/OptimizeTool.cs`

OptimizeTool 用 `RunAndWaitForNumberOfCycles(cycles)` 或 `RunAndWaitWithTimeout(...)`。处理与 GlobalSearch 类似 — 改为 `Run()` + 自轮询。

- [ ] **Step 1: 加 import**

```csharp
using ModelContextProtocol.Protocol;
```

- [ ] **Step 2: 方法签名加 2 个参数**

```csharp
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
```

- [ ] **Step 3: ct 转发到 _session.ExecuteAsync**

`});` → `}, cancellationToken);`

- [ ] **Step 4: 把 RunAndWait* 调用改为 Run() + polling**

读现有文件找到 `optimizer.RunAndWaitForNumberOfCycles(...)` 或 `RunAndWaitWithTimeout(...)` 的调用，替换为：

```csharp
                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    optimizer.Run();

                    string terminationReason;
                    long lastProgressMs = 0;
                    const long progressIntervalMs = 5000;
                    bool completed = false;

                    while (!completed)
                    {
                        Thread.Sleep(1000);

                        long now = stopwatch.ElapsedMilliseconds;

                        if (now - lastProgressMs >= progressIntervalMs)
                        {
                            double currentMerit = 0;
                            try { currentMerit = optimizer.CurrentMeritFunction; } catch { }
                            progress?.Report(new ProgressNotificationValue
                            {
                                Progress = (float)(stopwatch.Elapsed.TotalSeconds),
                                Total = 0,  // total runtime unknown for optimize
                                Message = $"optimize running for {(int)stopwatch.Elapsed.TotalSeconds}s, " +
                                          $"current merit: {currentMerit:F6}"
                            });
                            lastProgressMs = now;
                        }

                        if (cancellationToken.IsCancellationRequested)
                        {
                            terminationReason = "Cancelled";
                            optimizer.Cancel();
                            optimizer.WaitForCompletion();
                            completed = true;
                            break;
                        }

                        try
                        {
                            // Check natural completion. ZOSAPI typically exposes IsRunning
                            // or the loop ends when CurrentNumberOfCycles >= request.
                            if (!optimizer.IsRunning)
                            {
                                terminationReason = "Completed";
                                completed = true;
                                break;
                            }
                        }
                        catch { /* keep polling */ }
                    }
                    stopwatch.Stop();
```

> **Note**：实际 OptimizeTool 现有循环结构可能与上面不完全一致（用户该读现有源码再调整）。关键点是：(1) Run() 替代 RunAndWait*；(2) 加 progress + ct 检查；(3) `IsRunning` 不存在时改用 `CurrentNumberOfCycles >= cycles` 判定（cycles==0 时基于 `globalOpt.IsRunning` 也可）。

- [ ] **Step 5: 编译**

```bash
dotnet build src/ZemaxMCP.Server/ZemaxMCP.Server.csproj -c Release
```
Expected：success。

### Task 2.4: ConstrainedOptimizeTool 加心跳 + ct

ConstrainedOptimize 是项目自实现的 LM 优化器（在 `LMOptimizer.cs`），不调 ZOSAPI 优化器。Loop 在 LMOptimizer 内部。

**Files:**
- Modify: `src/ZemaxMCP.Server/Tools/Optimization/ConstrainedOptimizeTool.cs`
- Modify: `src/ZemaxMCP.Core/Services/ConstrainedOptimization/LMOptimizer.cs`（如有 LM 主循环则在循环内加 progress 回调）

- [ ] **Step 1: ConstrainedOptimizeTool 加 import**

```csharp
using ModelContextProtocol.Protocol;
```

- [ ] **Step 2: 方法签名加 2 个参数**

```csharp
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
```

- [ ] **Step 3: ct 转发到 _session.ExecuteAsync**

`});` → `}, cancellationToken);`

- [ ] **Step 4: 把 progress + ct 传入 LMOptimizer**

读现有 `LMOptimizer.cs`，找到主迭代循环（应当是 `for (int iter = 0; iter < maxIterations; iter++) { ... }`）。当前调用形如：
```csharp
var lmResult = lmOptimizer.Optimize(system, ..., maxIterations, ...);
```

改 `LMOptimizer.Optimize` 签名追加：
```csharp
public LMResult Optimize(
    ...,
    Action<int, double, double, double>? onProgress = null,  // (iter, currentMerit, mu, runtimeSeconds)
    CancellationToken cancellationToken = default)
```

在内部主循环每次迭代结束加：
```csharp
    var elapsed = stopwatch.Elapsed.TotalSeconds;
    onProgress?.Invoke(iter, currentMerit, mu, elapsed);
    cancellationToken.ThrowIfCancellationRequested();
```

ConstrainedOptimizeTool 调用 LMOptimizer 时传：
```csharp
long lastProgressMs = 0;
const long progressIntervalMs = 5000;
var swProgress = System.Diagnostics.Stopwatch.StartNew();

Action<int, double, double, double> onProgress = (iter, merit, mu, runtimeSec) =>
{
    long now = swProgress.ElapsedMilliseconds;
    if (now - lastProgressMs >= progressIntervalMs)
    {
        progress?.Report(new ProgressNotificationValue
        {
            Progress = (float)iter,
            Total = (float)maxIterations,
            Message = $"constrained_optimize iter {iter}/{maxIterations}, " +
                      $"merit: {merit:F6}, mu: {mu:F3}, runtime: {(int)runtimeSec}s"
        });
        lastProgressMs = now;
    }
};

var lmResult = lmOptimizer.Optimize(system, ..., maxIterations, ..., onProgress, cancellationToken);
```

> **Note**：`LMOptimizer` 真实 API 形状要从 `LMOptimizer.cs` 现有源码读出。上面的 `(int iter, double merit, double mu, double runtimeSec)` 是合理建议；如果现有 LMOptimizer 不接 progress 回调或不接 ct，要么 wrap、要么进 LMOptimizer 加这两个参数。最少侵入：仅加 ct（用 `ThrowIfCancellationRequested`）+ 在 ConstrainedOptimizeTool 层用单独的 timer task 推 progress（读 ConstrainedOptimizeState 字段）。

- [ ] **Step 5: 编译**

```bash
dotnet build OpticStudioMCPServer.sln -c Release
```
Expected：success。

### Task 2.5: Phase 2 commit

- [ ] **Step 1: 加 stage**

```bash
git add src/ZemaxMCP.Server/Tools/Optimization/HammerOptimizationTool.cs \
        src/ZemaxMCP.Server/Tools/Optimization/GlobalSearchTool.cs \
        src/ZemaxMCP.Server/Tools/Optimization/OptimizeTool.cs \
        src/ZemaxMCP.Server/Tools/Optimization/ConstrainedOptimizeTool.cs \
        src/ZemaxMCP.Core/Services/ConstrainedOptimization/LMOptimizer.cs
```

- [ ] **Step 2: Commit**

```bash
git commit -m "feat(tools): Pattern A heartbeat on 4 sync long optimization tools

Adds IProgress<ProgressNotificationValue> and CancellationToken parameters
to ExecuteAsync of:
- HammerOptimizationTool
- GlobalSearchTool (with Run()+poll instead of RunAndWaitWithTimeout)
- OptimizeTool (with Run()+poll instead of RunAndWait*)
- ConstrainedOptimizeTool (forwards through LMOptimizer)

Each long-running loop emits a progress notification every 5s and honors
client-side cancellation. SDK auto-binds these parameters from the request
when the client supplies _meta.progressToken; clients that don't are
unaffected.

LMOptimizer.Optimize signature extended with onProgress callback +
CancellationToken (additive, defaults preserve old behavior)."
```

---

## Phase 3 — Hammer async 三件套（Commit 3）

### Task 3.1: HammerAsyncTool

**Files:**
- Create: `src/ZemaxMCP.Server/Tools/Optimization/HammerAsyncTool.cs`

- [ ] **Step 1: 写 HammerAsyncTool.cs**

```csharp
using System.ComponentModel;
using System.Diagnostics;
using ModelContextProtocol.Server;
using ZOSAPI.Tools.Optimization;
using ZemaxMCP.Core.Services.ConstrainedOptimization;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.Optimization;

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
Same algorithm as zemax_hammer but the call returns within ~100ms regardless
of optimization length.")]
    public HammerAsyncResult Execute(
        [Description("Optimization algorithm: DLS or Orthogonal")] string algorithm = "DLS",
        [Description("Number of CPU cores to use (0 for all available)")] int cores = 0,
        [Description("Target runtime in minutes")] double targetRuntimeMinutes = 5.0,
        [Description("Stagnation timeout in seconds (no improvement)")] double timeoutSeconds = 120,
        [Description("Use automatic optimization mode")] bool automatic = true)
    {
        if (_state.IsRunning)
        {
            return new HammerAsyncResult(false, "Already running",
                "Hammer optimization is already running. " +
                "Use zemax_hammer_status to check progress or zemax_hammer_stop to cancel.");
        }

        _state.Reset();
        var ct = _state.CreateCancellationToken();

        var task = Task.Run(async () =>
        {
            try
            {
                await _session.ExecuteAsync("HammerAsync",
                    new Dictionary<string, object?>
                    {
                        ["algorithm"] = algorithm,
                        ["cores"] = cores,
                        ["targetRuntimeMinutes"] = targetRuntimeMinutes,
                        ["timeoutSeconds"] = timeoutSeconds,
                        ["automatic"] = automatic
                    },
                    system =>
                    {
                        if (system == null)
                            throw new InvalidOperationException("Optical system is not available");

                        var mfe = system.MFE
                            ?? throw new InvalidOperationException("Merit Function Editor is not available");

                        var initialMerit = mfe.CalculateMeritFunction();
                        _state.SetRunning(algorithm, initialMerit);

                        var hammer = system.Tools?.OpenHammerOptimization()
                            ?? throw new InvalidOperationException("Failed to open Hammer Optimization tool");

                        try
                        {
                            hammer.Algorithm = algorithm.ToUpper() switch
                            {
                                "DLS" => OptimizationAlgorithm.DampedLeastSquares,
                                "ORTHOGONAL" => OptimizationAlgorithm.OrthogonalDescent,
                                _ => OptimizationAlgorithm.DampedLeastSquares
                            };

                            hammer.NumberOfCores = cores > 0
                                ? Math.Min(cores, hammer.MaxCores)
                                : hammer.MaxCores;

                            string terminationReason = "Unknown";
                            var stopwatch = Stopwatch.StartNew();
                            hammer.Run();

                            double bestMerit = double.MaxValue;
                            long lastImprovedMs = 0;
                            long timeoutMs = (long)(timeoutSeconds * 1000);
                            long totalRuntimeMs = (long)(targetRuntimeMinutes * 60 * 1000);
                            int improvements = 0;

                            while (true)
                            {
                                Thread.Sleep(1000);

                                try
                                {
                                    double currentMerit = hammer.CurrentMeritFunction;
                                    long now = stopwatch.ElapsedMilliseconds;

                                    if (currentMerit < bestMerit)
                                    {
                                        if (bestMerit < double.MaxValue)
                                            improvements++;
                                        bestMerit = currentMerit;
                                        lastImprovedMs = now;
                                    }

                                    _state.UpdateMerit(currentMerit, bestMerit,
                                                       stopwatch.Elapsed.TotalSeconds, improvements);

                                    if (ct.IsCancellationRequested)
                                    {
                                        terminationReason = "Cancelled";
                                        break;
                                    }

                                    long idleMs = now - lastImprovedMs;
                                    if (idleMs >= timeoutMs)
                                    {
                                        terminationReason = improvements > 0 ? "Stagnation" : "NoImprovement";
                                        break;
                                    }

                                    if (now >= totalRuntimeMs)
                                    {
                                        terminationReason = "MaxRuntime";
                                        break;
                                    }
                                }
                                catch
                                {
                                    // Hammer may throw while running; ignore and keep polling
                                }
                            }

                            hammer.Cancel();
                            hammer.WaitForCompletion();
                            stopwatch.Stop();

                            // Final merit snapshot
                            try
                            {
                                _state.UpdateMerit(hammer.CurrentMeritFunction, bestMerit,
                                                   stopwatch.Elapsed.TotalSeconds, improvements);
                            }
                            catch { }

                            _state.SetCompleted(terminationReason);
                        }
                        finally
                        {
                            hammer.Close();
                        }
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
            $"Hammer optimization started (algorithm={algorithm}, " +
            $"targetRuntimeMinutes={targetRuntimeMinutes}). " +
            "Use zemax_hammer_status to check progress, zemax_hammer_stop to cancel.");
    }
}
```

- [ ] **Step 2: 编译**

```bash
dotnet build src/ZemaxMCP.Server/ZemaxMCP.Server.csproj -c Release
```

### Task 3.2: HammerStatusTool

**Files:**
- Create: `src/ZemaxMCP.Server/Tools/Optimization/HammerStatusTool.cs`

- [ ] **Step 1: 写 HammerStatusTool.cs**

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Services.ConstrainedOptimization;

namespace ZemaxMCP.Server.Tools.Optimization;

[McpServerToolType]
public class HammerStatusTool
{
    private readonly HammerState _state;
    public HammerStatusTool(HammerState state) => _state = state;

    public record HammerStatusResult(
        bool IsRunning,
        string? Algorithm,
        double InitialMerit,
        double CurrentMerit,
        double BestMerit,
        int Improvements,
        double RuntimeSeconds,
        string? TerminationReason,
        string? ErrorMessage,
        string Summary);

    [McpServerTool(Name = "zemax_hammer_status")]
    [Description("Check the progress of a running zemax_hammer_async optimization. Non-blocking; safe to call any time.")]
    public HammerStatusResult Execute()
    {
        string summary;
        if (_state.IsRunning)
        {
            summary = $"Hammer running ({_state.Algorithm}): {_state.RuntimeSeconds:F0}s elapsed, " +
                      $"best merit {_state.BestMerit:F6}, improvements: {_state.Improvements}";
        }
        else if (_state.HasState)
        {
            var errPart = _state.ErrorMessage != null ? $" ({_state.ErrorMessage})" : "";
            summary = $"Hammer {_state.TerminationReason}{errPart}: " +
                      $"final merit {_state.BestMerit:F6}, improvements: {_state.Improvements}, " +
                      $"runtime: {_state.RuntimeSeconds:F1}s";
        }
        else
        {
            summary = "No Hammer optimization has been run yet.";
        }

        return new HammerStatusResult(
            IsRunning: _state.IsRunning,
            Algorithm: _state.Algorithm,
            InitialMerit: _state.InitialMerit,
            CurrentMerit: _state.CurrentMerit,
            BestMerit: _state.BestMerit,
            Improvements: _state.Improvements,
            RuntimeSeconds: _state.RuntimeSeconds,
            TerminationReason: _state.TerminationReason,
            ErrorMessage: _state.ErrorMessage,
            Summary: summary);
    }
}
```

- [ ] **Step 2: 编译**

```bash
dotnet build src/ZemaxMCP.Server/ZemaxMCP.Server.csproj -c Release
```

### Task 3.3: HammerStopTool

**Files:**
- Create: `src/ZemaxMCP.Server/Tools/Optimization/HammerStopTool.cs`

- [ ] **Step 1: 写 HammerStopTool.cs**

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Services.ConstrainedOptimization;

namespace ZemaxMCP.Server.Tools.Optimization;

[McpServerToolType]
public class HammerStopTool
{
    private readonly HammerState _state;
    public HammerStopTool(HammerState state) => _state = state;

    public record HammerStopResult(bool Success, string Message);

    [McpServerTool(Name = "zemax_hammer_stop")]
    [Description(@"Cancel the running zemax_hammer_async optimization. The Hammer.Cancel()
will be issued and the runner finalises shortly after. Status often reads
""running"" briefly post-cancel — re-poll zemax_hammer_status to observe the
final ""Cancelled"" state.")]
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

- [ ] **Step 2: 编译**

```bash
dotnet build src/ZemaxMCP.Server/ZemaxMCP.Server.csproj -c Release
```

### Task 3.4: 注册 Hammer 三件套到 Program.cs

**Files:**
- Modify: `src/ZemaxMCP.Server/Program.cs`

- [ ] **Step 1: 在 Optimization Tools 段末尾插入 3 行**

找到现有 Optimization Tools 段（约第 91-110 行），在最后一个 `.WithTools<...>()` 前或后追加：

```csharp
    .WithTools<ZemaxMCP.Server.Tools.Optimization.HammerAsyncTool>()
    .WithTools<ZemaxMCP.Server.Tools.Optimization.HammerStatusTool>()
    .WithTools<ZemaxMCP.Server.Tools.Optimization.HammerStopTool>()
```

> 推荐位置：放在 `MultistartStopTool` 注册之后，与 multistart 三件套相邻。

- [ ] **Step 2: 编译全 solution**

```bash
dotnet build OpticStudioMCPServer.sln -c Release
```
Expected：success，无 error 无新 warning。

### Task 3.5: Phase 3 commit

- [ ] **Step 1: Commit**

```bash
git add src/ZemaxMCP.Server/Tools/Optimization/HammerAsyncTool.cs \
        src/ZemaxMCP.Server/Tools/Optimization/HammerStatusTool.cs \
        src/ZemaxMCP.Server/Tools/Optimization/HammerStopTool.cs \
        src/ZemaxMCP.Server/Program.cs

git commit -m "feat(async/hammer): zemax_hammer_async + status + stop

Adds the async tool triplet for Hammer optimization, mirroring the
existing multistart pattern:
- zemax_hammer_async (submit, returns <100ms with success/already-running)
- zemax_hammer_status (snapshot of HammerState fields, non-blocking)
- zemax_hammer_stop (issues HammerState.RequestCancellation())

Reuses HammerState (added Phase 1) for thread-safe state sharing across
the submit task and status/stop polling. Backround Task.Run wraps the
existing Hammer execution body, writing progress to _state instead of
IProgress (which is the sync tool's path)."
```

---

## Phase 4 — GlobalSearch async 三件套（Commit 4）

### Task 4.1: GlobalSearchAsyncTool

**Files:**
- Create: `src/ZemaxMCP.Server/Tools/Optimization/GlobalSearchAsyncTool.cs`

- [ ] **Step 1: 写 GlobalSearchAsyncTool.cs**

```csharp
using System.ComponentModel;
using System.Diagnostics;
using ModelContextProtocol.Server;
using ZOSAPI.Tools.Optimization;
using ZemaxMCP.Core.Services.ConstrainedOptimization;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.Optimization;

[McpServerToolType]
public class GlobalSearchAsyncTool
{
    private readonly IZemaxSession _session;
    private readonly GlobalSearchState _state;

    public GlobalSearchAsyncTool(IZemaxSession session, GlobalSearchState state)
    {
        _session = session;
        _state = state;
    }

    public record GlobalSearchAsyncResult(bool Success, string? Error, string Message);

    [McpServerTool(Name = "zemax_global_search_async")]
    [Description(@"Start a non-blocking Global Search optimization. Returns immediately —
use zemax_global_search_status to poll progress and zemax_global_search_stop to cancel.
Same algorithm as zemax_global_search but the call returns within ~100ms regardless
of optimization length.")]
    public GlobalSearchAsyncResult Execute(
        [Description("Optimization algorithm: DLS or Orthogonal")] string algorithm = "DLS",
        [Description("Number of CPU cores to use (0 for all available)")] int cores = 0,
        [Description("Number of solutions to save: 10, 20, 50, or 100")] int solutionsToSave = 10,
        [Description("Maximum runtime in seconds (0 for no limit)")] double timeoutSeconds = 60)
    {
        if (_state.IsRunning)
        {
            return new GlobalSearchAsyncResult(false, "Already running",
                "Global Search is already running. Use zemax_global_search_status / zemax_global_search_stop.");
        }

        _state.Reset();
        var ct = _state.CreateCancellationToken();

        var task = Task.Run(async () =>
        {
            try
            {
                await _session.ExecuteAsync("GlobalSearchAsync",
                    new Dictionary<string, object?>
                    {
                        ["algorithm"] = algorithm,
                        ["cores"] = cores,
                        ["solutionsToSave"] = solutionsToSave,
                        ["timeoutSeconds"] = timeoutSeconds
                    },
                    system =>
                    {
                        if (system == null)
                            throw new InvalidOperationException("Optical system is not available");

                        var mfe = system.MFE
                            ?? throw new InvalidOperationException("Merit Function Editor is not available");

                        var initialMerit = mfe.CalculateMeritFunction();
                        _state.SetRunning(algorithm, initialMerit);

                        var globalOpt = system.Tools?.OpenGlobalOptimization()
                            ?? throw new InvalidOperationException("Failed to open Global Optimization tool");

                        try
                        {
                            globalOpt.Algorithm = algorithm.ToUpper() switch
                            {
                                "DLS" => OptimizationAlgorithm.DampedLeastSquares,
                                "ORTHOGONAL" => OptimizationAlgorithm.OrthogonalDescent,
                                _ => OptimizationAlgorithm.DampedLeastSquares
                            };

                            globalOpt.NumberOfCores = cores > 0
                                ? Math.Min(cores, globalOpt.MaxCores)
                                : globalOpt.MaxCores;

                            globalOpt.NumberToSave = solutionsToSave switch
                            {
                                <= 10 => OptimizationSaveCount.Save_10,
                                <= 20 => OptimizationSaveCount.Save_20,
                                <= 50 => OptimizationSaveCount.Save_50,
                                _ => OptimizationSaveCount.Save_100
                            };

                            string terminationReason = "Unknown";
                            var stopwatch = Stopwatch.StartNew();
                            globalOpt.Run();

                            long timeoutMs = (long)(timeoutSeconds * 1000);
                            bool completed = false;

                            while (!completed)
                            {
                                Thread.Sleep(1000);
                                long now = stopwatch.ElapsedMilliseconds;

                                try
                                {
                                    double currentBest = globalOpt.CurrentMeritFunction(1);
                                    int validCount = 0;
                                    for (int i = 1; i <= solutionsToSave; i++)
                                    {
                                        var m = globalOpt.CurrentMeritFunction(i);
                                        if (m > 0 && m < double.MaxValue) validCount++;
                                        else break;
                                    }
                                    _state.UpdateProgress(currentBest, stopwatch.Elapsed.TotalSeconds, validCount);
                                }
                                catch { }

                                if (ct.IsCancellationRequested)
                                {
                                    terminationReason = "Cancelled";
                                    break;
                                }

                                if (timeoutSeconds > 0 && now >= timeoutMs)
                                {
                                    terminationReason = "Timeout";
                                    break;
                                }

                                try
                                {
                                    if (!globalOpt.IsRunning)
                                    {
                                        terminationReason = "Completed";
                                        completed = true;
                                    }
                                }
                                catch { }
                            }

                            globalOpt.Cancel();
                            globalOpt.WaitForCompletion();
                            stopwatch.Stop();

                            try
                            {
                                int finalValid = 0;
                                for (int i = 1; i <= solutionsToSave; i++)
                                {
                                    var m = globalOpt.CurrentMeritFunction(i);
                                    if (m > 0 && m < double.MaxValue) finalValid++;
                                    else break;
                                }
                                _state.UpdateProgress(globalOpt.CurrentMeritFunction(1),
                                                      stopwatch.Elapsed.TotalSeconds, finalValid);
                            }
                            catch { }

                            _state.SetCompleted(terminationReason);
                        }
                        finally
                        {
                            globalOpt.Close();
                        }
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

        return new GlobalSearchAsyncResult(true, null,
            $"Global Search started (algorithm={algorithm}, solutionsToSave={solutionsToSave}). " +
            "Use zemax_global_search_status / zemax_global_search_stop.");
    }
}
```

- [ ] **Step 2: 编译**

```bash
dotnet build src/ZemaxMCP.Server/ZemaxMCP.Server.csproj -c Release
```

### Task 4.2: GlobalSearchStatusTool

**Files:**
- Create: `src/ZemaxMCP.Server/Tools/Optimization/GlobalSearchStatusTool.cs`

- [ ] **Step 1: 写 GlobalSearchStatusTool.cs**

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Services.ConstrainedOptimization;

namespace ZemaxMCP.Server.Tools.Optimization;

[McpServerToolType]
public class GlobalSearchStatusTool
{
    private readonly GlobalSearchState _state;
    public GlobalSearchStatusTool(GlobalSearchState state) => _state = state;

    public record GlobalSearchStatusResult(
        bool IsRunning,
        string? Algorithm,
        double InitialMerit,
        double BestMerit,
        int SolutionsValid,
        double RuntimeSeconds,
        string? TerminationReason,
        string? ErrorMessage,
        string Summary);

    [McpServerTool(Name = "zemax_global_search_status")]
    [Description("Check the progress of a running zemax_global_search_async optimization. Non-blocking.")]
    public GlobalSearchStatusResult Execute()
    {
        string summary;
        if (_state.IsRunning)
        {
            summary = $"Global Search running ({_state.Algorithm}): {_state.RuntimeSeconds:F0}s elapsed, " +
                      $"best merit {_state.BestMerit:F6}, valid solutions: {_state.SolutionsValid}";
        }
        else if (_state.HasState)
        {
            var errPart = _state.ErrorMessage != null ? $" ({_state.ErrorMessage})" : "";
            summary = $"Global Search {_state.TerminationReason}{errPart}: " +
                      $"final best {_state.BestMerit:F6}, " +
                      $"solutions: {_state.SolutionsValid}, runtime: {_state.RuntimeSeconds:F1}s";
        }
        else
        {
            summary = "No Global Search has been run yet.";
        }

        return new GlobalSearchStatusResult(
            IsRunning: _state.IsRunning,
            Algorithm: _state.Algorithm,
            InitialMerit: _state.InitialMerit,
            BestMerit: _state.BestMerit,
            SolutionsValid: _state.SolutionsValid,
            RuntimeSeconds: _state.RuntimeSeconds,
            TerminationReason: _state.TerminationReason,
            ErrorMessage: _state.ErrorMessage,
            Summary: summary);
    }
}
```

- [ ] **Step 2: 编译**

```bash
dotnet build src/ZemaxMCP.Server/ZemaxMCP.Server.csproj -c Release
```

### Task 4.3: GlobalSearchStopTool

**Files:**
- Create: `src/ZemaxMCP.Server/Tools/Optimization/GlobalSearchStopTool.cs`

- [ ] **Step 1: 写 GlobalSearchStopTool.cs**

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Services.ConstrainedOptimization;

namespace ZemaxMCP.Server.Tools.Optimization;

[McpServerToolType]
public class GlobalSearchStopTool
{
    private readonly GlobalSearchState _state;
    public GlobalSearchStopTool(GlobalSearchState state) => _state = state;

    public record GlobalSearchStopResult(bool Success, string Message);

    [McpServerTool(Name = "zemax_global_search_stop")]
    [Description(@"Cancel the running zemax_global_search_async optimization. The
GlobalOptimization.Cancel() will be issued. Re-poll zemax_global_search_status
to observe the final ""Cancelled"" state.")]
    public GlobalSearchStopResult Execute()
    {
        if (!_state.IsRunning)
        {
            return new GlobalSearchStopResult(false, "No running Global Search to stop.");
        }
        _state.RequestCancellation();
        return new GlobalSearchStopResult(true,
            "Cancellation requested. Re-poll zemax_global_search_status for final state.");
    }
}
```

### Task 4.4: 注册 GlobalSearch 三件套 + commit

**Files:**
- Modify: `src/ZemaxMCP.Server/Program.cs`

- [ ] **Step 1: 在 Hammer 三件套注册之后追加 3 行**

```csharp
    .WithTools<ZemaxMCP.Server.Tools.Optimization.GlobalSearchAsyncTool>()
    .WithTools<ZemaxMCP.Server.Tools.Optimization.GlobalSearchStatusTool>()
    .WithTools<ZemaxMCP.Server.Tools.Optimization.GlobalSearchStopTool>()
```

- [ ] **Step 2: 编译 + commit**

```bash
dotnet build OpticStudioMCPServer.sln -c Release

git add src/ZemaxMCP.Server/Tools/Optimization/GlobalSearchAsyncTool.cs \
        src/ZemaxMCP.Server/Tools/Optimization/GlobalSearchStatusTool.cs \
        src/ZemaxMCP.Server/Tools/Optimization/GlobalSearchStopTool.cs \
        src/ZemaxMCP.Server/Program.cs

git commit -m "feat(async/global_search): zemax_global_search_async + status + stop

Mirrors Hammer triplet pattern. Uses GlobalSearchState (Phase 1) for
thread-safe progress sharing. Background task replaces RunAndWaitWithTimeout
with Run() + 1s polling loop, updating _state every tick and honoring
cancellation."
```

---

## Phase 5 — Optimize async 三件套（Commit 5）

### Task 5.1: OptimizeAsyncTool

**Files:**
- Create: `src/ZemaxMCP.Server/Tools/Optimization/OptimizeAsyncTool.cs`

- [ ] **Step 1: 写 OptimizeAsyncTool.cs**

```csharp
using System.ComponentModel;
using System.Diagnostics;
using ModelContextProtocol.Server;
using ZOSAPI.Tools.Optimization;
using ZemaxMCP.Core.Services.ConstrainedOptimization;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.Optimization;

[McpServerToolType]
public class OptimizeAsyncTool
{
    private readonly IZemaxSession _session;
    private readonly OptimizeState _state;

    public OptimizeAsyncTool(IZemaxSession session, OptimizeState state)
    {
        _session = session;
        _state = state;
    }

    public record OptimizeAsyncResult(bool Success, string? Error, string Message);

    [McpServerTool(Name = "zemax_optimize_async")]
    [Description(@"Start a non-blocking local optimization. Returns immediately —
use zemax_optimize_status to poll progress and zemax_optimize_stop to cancel.")]
    public OptimizeAsyncResult Execute(
        [Description("Optimization algorithm: DLS or Orthogonal")] string algorithm = "DLS",
        [Description("Number of cycles (0 for automatic)")] int cycles = 0)
    {
        if (_state.IsRunning)
        {
            return new OptimizeAsyncResult(false, "Already running",
                "Optimize is already running. Use zemax_optimize_status / zemax_optimize_stop.");
        }

        _state.Reset();
        var ct = _state.CreateCancellationToken();

        var task = Task.Run(async () =>
        {
            try
            {
                await _session.ExecuteAsync("OptimizeAsync",
                    new Dictionary<string, object?>
                    {
                        ["algorithm"] = algorithm,
                        ["cycles"] = cycles
                    },
                    system =>
                    {
                        if (system == null)
                            throw new InvalidOperationException("Optical system is not available");

                        var mfe = system.MFE
                            ?? throw new InvalidOperationException("Merit Function Editor is not available");

                        var initialMerit = mfe.CalculateMeritFunction();
                        _state.SetRunning(algorithm, cycles, initialMerit);

                        var optimizer = system.Tools?.OpenLocalOptimization()
                            ?? throw new InvalidOperationException("Failed to open Local Optimization tool");

                        try
                        {
                            optimizer.Algorithm = algorithm.ToUpper() switch
                            {
                                "DLS" => OptimizationAlgorithm.DampedLeastSquares,
                                "ORTHOGONAL" => OptimizationAlgorithm.OrthogonalDescent,
                                _ => OptimizationAlgorithm.DampedLeastSquares
                            };

                            string terminationReason = "Unknown";
                            var stopwatch = Stopwatch.StartNew();
                            optimizer.Run();

                            bool completed = false;
                            while (!completed)
                            {
                                Thread.Sleep(1000);

                                try
                                {
                                    double currentMerit = optimizer.CurrentMeritFunction;
                                    int currentCycles = optimizer.CurrentNumberOfCycles;
                                    _state.UpdateProgress(currentCycles, currentMerit,
                                                          stopwatch.Elapsed.TotalSeconds);
                                }
                                catch { }

                                if (ct.IsCancellationRequested)
                                {
                                    terminationReason = "Cancelled";
                                    break;
                                }

                                try
                                {
                                    if (!optimizer.IsRunning)
                                    {
                                        terminationReason = "Completed";
                                        completed = true;
                                    }
                                }
                                catch { }
                            }

                            optimizer.Cancel();
                            optimizer.WaitForCompletion();
                            stopwatch.Stop();

                            try
                            {
                                _state.UpdateProgress(optimizer.CurrentNumberOfCycles,
                                                      optimizer.CurrentMeritFunction,
                                                      stopwatch.Elapsed.TotalSeconds);
                            }
                            catch { }

                            _state.SetCompleted(terminationReason);
                        }
                        finally
                        {
                            optimizer.Close();
                        }
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

        return new OptimizeAsyncResult(true, null,
            $"Optimize started (algorithm={algorithm}, cycles={cycles}). " +
            "Use zemax_optimize_status / zemax_optimize_stop.");
    }
}
```

- [ ] **Step 2: 编译**

```bash
dotnet build src/ZemaxMCP.Server/ZemaxMCP.Server.csproj -c Release
```

### Task 5.2: OptimizeStatusTool

**Files:**
- Create: `src/ZemaxMCP.Server/Tools/Optimization/OptimizeStatusTool.cs`

- [ ] **Step 1: 写 OptimizeStatusTool.cs**

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Services.ConstrainedOptimization;

namespace ZemaxMCP.Server.Tools.Optimization;

[McpServerToolType]
public class OptimizeStatusTool
{
    private readonly OptimizeState _state;
    public OptimizeStatusTool(OptimizeState state) => _state = state;

    public record OptimizeStatusResult(
        bool IsRunning,
        string? Algorithm,
        double InitialMerit,
        double CurrentMerit,
        int CyclesCompleted,
        int CyclesRequested,
        double RuntimeSeconds,
        string? TerminationReason,
        string? ErrorMessage,
        string Summary);

    [McpServerTool(Name = "zemax_optimize_status")]
    [Description("Check the progress of a running zemax_optimize_async optimization. Non-blocking.")]
    public OptimizeStatusResult Execute()
    {
        string summary;
        if (_state.IsRunning)
        {
            summary = $"Optimize running ({_state.Algorithm}): cycle {_state.CyclesCompleted}" +
                      (_state.CyclesRequested > 0 ? $"/{_state.CyclesRequested}" : "/auto") +
                      $", merit {_state.CurrentMerit:F6}, runtime {_state.RuntimeSeconds:F0}s";
        }
        else if (_state.HasState)
        {
            var errPart = _state.ErrorMessage != null ? $" ({_state.ErrorMessage})" : "";
            summary = $"Optimize {_state.TerminationReason}{errPart}: " +
                      $"final merit {_state.CurrentMerit:F6}, cycles {_state.CyclesCompleted}, " +
                      $"runtime {_state.RuntimeSeconds:F1}s";
        }
        else
        {
            summary = "No Optimize has been run yet.";
        }

        return new OptimizeStatusResult(
            IsRunning: _state.IsRunning,
            Algorithm: _state.Algorithm,
            InitialMerit: _state.InitialMerit,
            CurrentMerit: _state.CurrentMerit,
            CyclesCompleted: _state.CyclesCompleted,
            CyclesRequested: _state.CyclesRequested,
            RuntimeSeconds: _state.RuntimeSeconds,
            TerminationReason: _state.TerminationReason,
            ErrorMessage: _state.ErrorMessage,
            Summary: summary);
    }
}
```

### Task 5.3: OptimizeStopTool

**Files:**
- Create: `src/ZemaxMCP.Server/Tools/Optimization/OptimizeStopTool.cs`

- [ ] **Step 1: 写 OptimizeStopTool.cs**

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Services.ConstrainedOptimization;

namespace ZemaxMCP.Server.Tools.Optimization;

[McpServerToolType]
public class OptimizeStopTool
{
    private readonly OptimizeState _state;
    public OptimizeStopTool(OptimizeState state) => _state = state;

    public record OptimizeStopResult(bool Success, string Message);

    [McpServerTool(Name = "zemax_optimize_stop")]
    [Description("Cancel the running zemax_optimize_async optimization. Re-poll zemax_optimize_status for final state.")]
    public OptimizeStopResult Execute()
    {
        if (!_state.IsRunning)
        {
            return new OptimizeStopResult(false, "No running Optimize to stop.");
        }
        _state.RequestCancellation();
        return new OptimizeStopResult(true,
            "Cancellation requested. Re-poll zemax_optimize_status for final state.");
    }
}
```

### Task 5.4: 注册 Optimize 三件套 + commit

**Files:**
- Modify: `src/ZemaxMCP.Server/Program.cs`

- [ ] **Step 1: 追加 3 行注册**

```csharp
    .WithTools<ZemaxMCP.Server.Tools.Optimization.OptimizeAsyncTool>()
    .WithTools<ZemaxMCP.Server.Tools.Optimization.OptimizeStatusTool>()
    .WithTools<ZemaxMCP.Server.Tools.Optimization.OptimizeStopTool>()
```

- [ ] **Step 2: 编译 + commit**

```bash
dotnet build OpticStudioMCPServer.sln -c Release

git add src/ZemaxMCP.Server/Tools/Optimization/OptimizeAsyncTool.cs \
        src/ZemaxMCP.Server/Tools/Optimization/OptimizeStatusTool.cs \
        src/ZemaxMCP.Server/Tools/Optimization/OptimizeStopTool.cs \
        src/ZemaxMCP.Server/Program.cs

git commit -m "feat(async/optimize): zemax_optimize_async + status + stop

Mirrors Hammer/GlobalSearch triplet. Uses OptimizeState (Phase 1).
Background runner replaces RunAndWait* with Run() + polling, tracking
CurrentNumberOfCycles in OptimizeState."
```

---

## Phase 6 — ConstrainedOptimize async 三件套（Commit 6）

### Task 6.1: ConstrainedOptimizeAsyncTool

**Files:**
- Create: `src/ZemaxMCP.Server/Tools/Optimization/ConstrainedOptimizeAsyncTool.cs`

- [ ] **Step 1: 写 ConstrainedOptimizeAsyncTool.cs**

```csharp
using System.ComponentModel;
using System.Diagnostics;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Services.ConstrainedOptimization;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.Optimization;

[McpServerToolType]
public class ConstrainedOptimizeAsyncTool
{
    private readonly IZemaxSession _session;
    private readonly ConstraintStore _constraintStore;
    private readonly ConstrainedOptimizeState _state;

    public ConstrainedOptimizeAsyncTool(
        IZemaxSession session,
        ConstraintStore constraintStore,
        ConstrainedOptimizeState state)
    {
        _session = session;
        _constraintStore = constraintStore;
        _state = state;
    }

    public record ConstrainedOptimizeAsyncResult(bool Success, string? Error, string Message);

    [McpServerTool(Name = "zemax_constrained_optimize_async")]
    [Description(@"Start a non-blocking constrained LM optimization. Returns immediately —
use zemax_constrained_optimize_status to poll progress and
zemax_constrained_optimize_stop to cancel.")]
    public ConstrainedOptimizeAsyncResult Execute(
        [Description("Maximum iterations (default 200)")] int maxIterations = 200,
        [Description("Initial damping parameter mu (default 1e-3)")] double initialMu = 1e-3,
        [Description("Finite difference step size delta (default 1e-7)")] double delta = 1e-7,
        [Description("Use Broyden rank-1 Jacobian updates (default true)")] bool useBroydenUpdate = true,
        [Description("Maximum auto-restarts (default 2)")] int maxRestarts = 2)
    {
        if (_state.IsRunning)
        {
            return new ConstrainedOptimizeAsyncResult(false, "Already running",
                "Constrained Optimize is already running. " +
                "Use zemax_constrained_optimize_status / zemax_constrained_optimize_stop.");
        }

        _state.Reset();
        var ct = _state.CreateCancellationToken();

        var task = Task.Run(async () =>
        {
            try
            {
                await _session.ExecuteAsync("ConstrainedOptimizeAsync",
                    new Dictionary<string, object?>
                    {
                        ["maxIterations"] = maxIterations,
                        ["initialMu"] = initialMu,
                        ["delta"] = delta,
                        ["useBroydenUpdate"] = useBroydenUpdate,
                        ["maxRestarts"] = maxRestarts
                    },
                    system =>
                    {
                        if (system == null)
                            throw new InvalidOperationException("Optical system is not available");

                        var scanner = new VariableScanner();
                        var meritReader = new MeritFunctionReader();
                        var lmOptimizer = new LMOptimizer(meritReader);

                        var variables = scanner.ScanVariables(system);
                        _constraintStore.ApplyConstraints(variables);

                        var initialMerit = meritReader.Read(system);
                        _state.SetRunning(maxIterations, initialMu, initialMerit);

                        var stopwatch = Stopwatch.StartNew();

                        Action<int, double, double, double> onProgress = (iter, merit, mu, runtimeSec) =>
                        {
                            // restartsUsed not exposed by LMOptimizer's onProgress in the
                            // current signature; pass 0 for now and let SetCompleted set
                            // the final value if available.
                            _state.UpdateProgress(iter, merit, mu, runtimeSec, 0);
                        };

                        var lmResult = lmOptimizer.Optimize(
                            system, variables,
                            maxIterations, initialMu, delta,
                            useBroydenUpdate, maxRestarts,
                            onProgress: onProgress,
                            cancellationToken: ct);

                        stopwatch.Stop();

                        _state.UpdateProgress(lmResult.Iterations, lmResult.FinalMerit,
                                              0 /* final mu */, stopwatch.Elapsed.TotalSeconds,
                                              lmResult.Restarts);
                        _state.SetCompleted(ct.IsCancellationRequested ? "Cancelled" : "Completed");
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

        return new ConstrainedOptimizeAsyncResult(true, null,
            $"Constrained Optimize started (maxIterations={maxIterations}). " +
            "Use zemax_constrained_optimize_status / zemax_constrained_optimize_stop.");
    }
}
```

> **Note**：`LMOptimizer.Optimize` 的精确签名需从源码确认（Phase 2 Task 2.4 会改 signature 增加 onProgress + ct）。如果 Phase 2 没改完整，这里的调用要回过头改。

- [ ] **Step 2: 编译**

```bash
dotnet build src/ZemaxMCP.Server/ZemaxMCP.Server.csproj -c Release
```

### Task 6.2: ConstrainedOptimizeStatusTool

**Files:**
- Create: `src/ZemaxMCP.Server/Tools/Optimization/ConstrainedOptimizeStatusTool.cs`

- [ ] **Step 1: 写**

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Services.ConstrainedOptimization;

namespace ZemaxMCP.Server.Tools.Optimization;

[McpServerToolType]
public class ConstrainedOptimizeStatusTool
{
    private readonly ConstrainedOptimizeState _state;
    public ConstrainedOptimizeStatusTool(ConstrainedOptimizeState state) => _state = state;

    public record ConstrainedOptimizeStatusResult(
        bool IsRunning,
        int Iteration,
        int MaxIterations,
        double InitialMerit,
        double CurrentMerit,
        double Mu,
        int RestartsUsed,
        double RuntimeSeconds,
        string? TerminationReason,
        string? ErrorMessage,
        string Summary);

    [McpServerTool(Name = "zemax_constrained_optimize_status")]
    [Description("Check progress of a running zemax_constrained_optimize_async. Non-blocking.")]
    public ConstrainedOptimizeStatusResult Execute()
    {
        string summary;
        if (_state.IsRunning)
        {
            summary = $"Constrained Optimize running: iter {_state.Iteration}/{_state.MaxIterations}, " +
                      $"merit {_state.CurrentMerit:F6}, mu {_state.Mu:F4}, " +
                      $"restarts {_state.RestartsUsed}, runtime {_state.RuntimeSeconds:F0}s";
        }
        else if (_state.HasState)
        {
            var errPart = _state.ErrorMessage != null ? $" ({_state.ErrorMessage})" : "";
            summary = $"Constrained Optimize {_state.TerminationReason}{errPart}: " +
                      $"final merit {_state.CurrentMerit:F6}, iter {_state.Iteration}, " +
                      $"runtime {_state.RuntimeSeconds:F1}s";
        }
        else
        {
            summary = "No Constrained Optimize has been run yet.";
        }

        return new ConstrainedOptimizeStatusResult(
            IsRunning: _state.IsRunning,
            Iteration: _state.Iteration,
            MaxIterations: _state.MaxIterations,
            InitialMerit: _state.InitialMerit,
            CurrentMerit: _state.CurrentMerit,
            Mu: _state.Mu,
            RestartsUsed: _state.RestartsUsed,
            RuntimeSeconds: _state.RuntimeSeconds,
            TerminationReason: _state.TerminationReason,
            ErrorMessage: _state.ErrorMessage,
            Summary: summary);
    }
}
```

### Task 6.3: ConstrainedOptimizeStopTool

**Files:**
- Create: `src/ZemaxMCP.Server/Tools/Optimization/ConstrainedOptimizeStopTool.cs`

- [ ] **Step 1: 写**

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Services.ConstrainedOptimization;

namespace ZemaxMCP.Server.Tools.Optimization;

[McpServerToolType]
public class ConstrainedOptimizeStopTool
{
    private readonly ConstrainedOptimizeState _state;
    public ConstrainedOptimizeStopTool(ConstrainedOptimizeState state) => _state = state;

    public record ConstrainedOptimizeStopResult(bool Success, string Message);

    [McpServerTool(Name = "zemax_constrained_optimize_stop")]
    [Description("Cancel the running zemax_constrained_optimize_async. Re-poll status for final state.")]
    public ConstrainedOptimizeStopResult Execute()
    {
        if (!_state.IsRunning)
        {
            return new ConstrainedOptimizeStopResult(false, "No running Constrained Optimize to stop.");
        }
        _state.RequestCancellation();
        return new ConstrainedOptimizeStopResult(true,
            "Cancellation requested. Re-poll zemax_constrained_optimize_status for final state.");
    }
}
```

### Task 6.4: 注册 ConstrainedOptimize 三件套 + commit

**Files:**
- Modify: `src/ZemaxMCP.Server/Program.cs`

- [ ] **Step 1: 追加 3 行**

```csharp
    .WithTools<ZemaxMCP.Server.Tools.Optimization.ConstrainedOptimizeAsyncTool>()
    .WithTools<ZemaxMCP.Server.Tools.Optimization.ConstrainedOptimizeStatusTool>()
    .WithTools<ZemaxMCP.Server.Tools.Optimization.ConstrainedOptimizeStopTool>()
```

- [ ] **Step 2: 编译 + commit**

```bash
dotnet build OpticStudioMCPServer.sln -c Release

git add src/ZemaxMCP.Server/Tools/Optimization/ConstrainedOptimizeAsyncTool.cs \
        src/ZemaxMCP.Server/Tools/Optimization/ConstrainedOptimizeStatusTool.cs \
        src/ZemaxMCP.Server/Tools/Optimization/ConstrainedOptimizeStopTool.cs \
        src/ZemaxMCP.Server/Program.cs

git commit -m "feat(async/constrained_optimize): zemax_constrained_optimize_async + status + stop

Completes the async tool family for all 4 sync long optimization tools.
Uses ConstrainedOptimizeState (Phase 1) and LMOptimizer's onProgress
callback (Phase 2) to track LM iteration / mu / restarts."
```

---

## Phase 7 — 文档（Commit 7）

### Task 7.1: 写 docs/async-optimization.md

**Files:**
- Create: `docs/async-optimization.md`

- [ ] **Step 1: 写文档**

```markdown
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
```

### Task 7.2: 更新 README.md

**Files:**
- Modify: `README.md`

- [ ] **Step 1: 找到工具列表段**

```bash
grep -n 'zemax_hammer\|zemax_optimize\|zemax_global_search' README.md | head
```

- [ ] **Step 2: 在 Hammer / GlobalSearch / Optimize / ConstrainedOptimize 工具描述附近追加 async 变体说明**

形式（具体行号取决于现有 README 结构，原则是放在每个对应同步工具的下方）：

```markdown
- `zemax_hammer_async`, `zemax_hammer_status`, `zemax_hammer_stop` — non-blocking variant of `zemax_hammer`. See [docs/async-optimization.md](docs/async-optimization.md).
- `zemax_global_search_async`, `zemax_global_search_status`, `zemax_global_search_stop` — non-blocking variant of `zemax_global_search`.
- `zemax_optimize_async`, `zemax_optimize_status`, `zemax_optimize_stop` — non-blocking variant of `zemax_optimize`.
- `zemax_constrained_optimize_async`, `zemax_constrained_optimize_status`, `zemax_constrained_optimize_stop` — non-blocking variant of `zemax_constrained_optimize`.
```

如果 README 用表格描述工具，按表格列结构填即可。

### Task 7.3: Phase 7 commit

- [ ] **Step 1: Commit**

```bash
git add docs/async-optimization.md README.md
git commit -m "docs: async optimization guide + tool list update

Adds docs/async-optimization.md describing Pattern A (sync heartbeat) and
Pattern B (per-tool async triplets) for the 4 long optimization tools.
Updates README with the 12 new tool names cross-referencing the guide."
```

---

## Phase 8 — 部署与冒烟验收

### Task 8.1: 部署

**Files:**
- Replace: `C:\Users\xufen\Documents\Zemax\ZemaxMCP\` (整个 net48 目录)

- [ ] **Step 1: 杀现役 server 释放 .exe 锁**

```bash
taskkill //F //IM ZemaxMCP.Server.exe 2>/dev/null || true
```

- [ ] **Step 2: 备份**

```bash
DEPLOY="C:/Users/xufen/Documents/Zemax/ZemaxMCP"
cp -r "$DEPLOY" "${DEPLOY}.bak.$(date +%Y%m%d-%H%M%S)"
```

- [ ] **Step 3: 部署新 binary**

```bash
SRC="/e/ZemaxProject/ZemaxMCP_源码_本地开发版/src/ZemaxMCP.Server/bin/Release/net48"
cp -r "$SRC/." "$DEPLOY/"
```

- [ ] **Step 4: 让用户重启 Claude Code**

提示用户：关闭当前 Claude Code，重开新窗口。新 session 拉起的 zemax-mcp 会用新 binary。

### Task 8.2: 冒烟测试矩阵

**前置**：用户 Claude Code 重启完成 + 已有 .zos 模型加载在 Zemax 中（或 standalone 模式启动 Zemax）。

- [ ] **Smoke 1（同步 baseline 不退化）**：
  ```
  mcp__zemax-mcp__zemax_hammer(targetRuntimeMinutes=0.5, timeoutSeconds=15)
  ```
  Expected：约 30-45s 返回，结果含 `Success=true` + `FinalMerit`。

- [ ] **Smoke 2（同步 + Pattern A 心跳）**：
  ```
  mcp__zemax-mcp__zemax_hammer(targetRuntimeMinutes=2, timeoutSeconds=60)
  ```
  Expected：约 1-2 min 返回；客户端日志（如可见）有 ≥10 条 progress notifications。

- [ ] **Smoke 3（async lifecycle）**：
  ```
  out = mcp__zemax-mcp__zemax_hammer_async(targetRuntimeMinutes=1, timeoutSeconds=15)
  # 立即返回 success
  
  mcp__zemax-mcp__zemax_hammer_status()
  # IsRunning=true, RuntimeSeconds 自增
  
  # 等 ~70s 后再调
  mcp__zemax-mcp__zemax_hammer_status()
  # IsRunning=false, TerminationReason in ["MaxRuntime","Stagnation","NoImprovement"]
  ```

- [ ] **Smoke 4（async cancel）**：
  ```
  out = mcp__zemax-mcp__zemax_hammer_async(targetRuntimeMinutes=5)
  # 立即返回
  
  # 5s 后
  mcp__zemax-mcp__zemax_hammer_stop()
  # success=true, message: "Cancellation requested..."
  
  # 1-3s 再调
  mcp__zemax-mcp__zemax_hammer_status()
  # IsRunning=false, TerminationReason="Cancelled"
  ```

- [ ] **Smoke 5（global_search async）**：
  ```
  mcp__zemax-mcp__zemax_global_search_async(timeoutSeconds=30)
  mcp__zemax-mcp__zemax_global_search_status()
  # 30s 后再 poll，状态翻 Completed
  ```

- [ ] **Smoke 6（optimize async）**：
  ```
  mcp__zemax-mcp__zemax_optimize_async(cycles=5)
  # poll status 直到完成
  ```

- [ ] **Smoke 7（constrained_optimize async）**：
  ```
  mcp__zemax-mcp__zemax_constrained_optimize_async(maxIterations=20)
  # poll status 直到完成
  ```

- [ ] **Smoke 8（multistart 不回归）**：
  ```
  mcp__zemax-mcp__zemax_multistart_optimize(maxTrials=5, lmIterationsPerTrial=10)
  mcp__zemax-mcp__zemax_multistart_status()
  ```
  Expected：行为与本次改造前完全一致。

任一项失败 → Task 8.3 回滚。

### Task 8.3: 回滚

- [ ] **如失败：恢复**

```bash
DEPLOY="C:/Users/xufen/Documents/Zemax/ZemaxMCP"
LATEST_BAK=$(ls -dt ${DEPLOY}.bak.* 2>/dev/null | head -1)
if [ -n "$LATEST_BAK" ]; then
    taskkill //F //IM ZemaxMCP.Server.exe 2>/dev/null || true
    rm -rf "$DEPLOY"
    cp -r "$LATEST_BAK" "$DEPLOY"
fi
```
让用户重启 Claude Code 即可还原。

改动留在 fork 的 `feature/long-task-async` 分支不丢。

---

## Appendix A — 常见 gotcha

1. **`ProgressNotificationValue` 类型在新版 ModelContextProtocol SDK 里**：旧版 SDK 可能叫 `ProgressNotificationParams`。如果 `using ModelContextProtocol.Protocol;` 后类型仍找不到，去 `~/.nuget/packages/modelcontextprotocol/<ver>/lib/...` 翻 SDK 源码确认实际类型。

2. **`IGlobalOptimization.IsRunning` 不存在**：ZOSAPI 不同版本 API 略有不同。如果该属性不存在，改用 try/catch 包 `CurrentMeritFunction(1)` — 异常 = 还在跑、无异常 = 跑完了；或者用 Task 包装 `RunAndWaitForCompletion()` 配合 `Task.WhenAny(... , Task.Delay(timeoutMs))` 实现可取消等待。

3. **`LMOptimizer.Optimize` 现有签名不接 onProgress / ct**：Phase 2 Task 2.4 步骤明确要求扩展该方法签名。如果改起来侵入面太大，最小路径是用单独的"心跳 Timer Task"读 `_state` 字段然后调 `progress?.Report(...)`，不动 LMOptimizer 内部。

4. **`Task.Run(async () => await _session.ExecuteAsync(...))` 嵌套 async**：ExecuteAsync 已是 async，无需再 `Task.Run`。但用 `Task.Run` 把它推到 worker thread 是为了"submit 立即返回"。这跟 multistart 现有写法一致，保留。

5. **DI 注入新参数**：C# MCP SDK 通过反射 + DI 注入 tool 类的构造器参数。确保 Program.cs 注册了所有 State Singleton (`AddSingleton<HammerState>()` 等) — 否则 SDK 起 server 时会以 `InvalidOperationException: Unable to resolve service` fail。

6. **Build 报警告 net48 下某 API 不可用**：例如 `nullable reference types`。看 `Directory.Build.props` 是否启用 `<Nullable>enable</Nullable>`。如启用，所有 nullable annotation (`string?`) 必须保留；否则去掉。

---

## Appendix B — 风格约定

- 命名空间：`ZemaxMCP.Server.Tools.Optimization` / `ZemaxMCP.Core.Services.ConstrainedOptimization`
- 文件编码：UTF-8（与现有保持一致）
- 缩进：4 空格（C# 默认）
- public 类型：均带 XML doc comment（参考 MultistartState）
- nullable：尊重项目现有设置
- record 类型：result/status DTO 全部用 `record` + 命名参数（与 multistart 一致）
- attribute 顺序：`[McpServerToolType]` 在类上、`[McpServerTool(Name=...)]` + `[Description(...)]` 在方法上
