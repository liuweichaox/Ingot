using HslCommunication.Profinet.Melsec;
using Microsoft.Extensions.Logging;

namespace Ingot.Simulator;

/// <summary>
/// 模拟器数据快照
/// </summary>
public class SimulatorData
{
    public ushort Heartbeat { get; set; }
    public ushort Temperature { get; set; }
    public ushort Pressure { get; set; }
    public ushort Current { get; set; }
    public ushort Voltage { get; set; }
    public ushort LightBarrierPos { get; set; }
    public ushort ServoSpeed { get; set; }
    public ushort DeviceFlag { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string RecipeId { get; set; } = string.Empty;
    public ushort GoodCount { get; set; }
    public string MaterialLot { get; set; } = string.Empty;
    public string Tooling { get; set; } = string.Empty;
    public bool SpindleAlarm { get; set; }
    public string Phase { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

/// <summary>
///     Mitsubishi A1E 模拟器，直接使用 HslCommunication 服务器
/// </summary>
public class Simulator : IDisposable
{
    private readonly Timer? _dataUpdateTimer;
    private readonly ILogger<Simulator>? _logger;
    private readonly SimulatorOptions _options;
    private readonly MelsecA1EServer _server;
    private readonly DateTimeOffset _simulatorStartTime = DateTimeOffset.UtcNow;
    private bool _isRunning;
    private bool _isProducing;
    private DateTimeOffset _lastConsoleOutput = DateTimeOffset.MinValue;
    private string? _lastScenarioPhase;
    private int _nextBatchNumber = 1;
    private int _currentBatchNumber;

    public Simulator(int port, ILogger<Simulator>? logger = null)
        : this(new SimulatorOptions { Port = port }, logger)
    {
    }

    public Simulator(SimulatorOptions options, ILogger<Simulator>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.UpdateIntervalMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.UpdateIntervalMs));
        if (options.ConsoleIntervalMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.ConsoleIntervalMs));
        if (options.IsScenario && options.ScenarioSpeed <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.ScenarioSpeed));

        _options = options;
        _logger = logger;
        _server = new MelsecA1EServer
        {
            Port = options.Port
        };

        // 初始化一些默认寄存器值（可选，但有助于测试）
        _logger?.LogDebug("初始化 MelsecA1EServer 模拟器，端口: {Port}", options.Port);

        // 定期更新模拟数据（在启动后才会真正更新）
        _dataUpdateTimer = new Timer(
            UpdateSimulatedData,
            null,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(options.UpdateIntervalMs));
    }

    public void Dispose()
    {
        Stop();
        _dataUpdateTimer?.Dispose();
        _server?.Dispose();
    }

    /// <summary>
    ///     启动模拟器
    /// </summary>
    public void Start()
    {
        try
        {
            _logger?.LogInformation("正在启动 PLC 模拟器，端口: {Port}...", _server.Port);

            // 确保服务器处于停止状态
            if (_server.IsStarted)
            {
                _logger?.LogWarning("服务器已在运行，先停止...");
                _server.ServerClose();
                Thread.Sleep(100);
            }

            // 启动服务器（ServerStart 可能返回 void，需要检查 IsStarted 属性）
            _server.ServerStart(_server.Port);

            // 等待服务器启动并绑定端口
            var maxWaitTime = 3000; // 最多等待 3 秒
            var waited = 0;
            while (!_server.IsStarted && waited < maxWaitTime)
            {
                Thread.Sleep(100);
                waited += 100;
            }

            if (!_server.IsStarted) throw new InvalidOperationException($"服务器启动失败：在 {maxWaitTime}ms 内未能成功启动");

            // 初始化心跳寄存器
            var writeResult = _server.Write(SimRegisters.D100Heartbeat, (ushort)0);
            if (!writeResult.IsSuccess) _logger?.LogWarning("初始化心跳寄存器失败: {Message}", writeResult.Message);
            _server.Write(SimRegisters.D6006DeviceFlag, (ushort)0);
            _server.Write(SimRegisters.D6100RecipeId, string.Empty);
            _server.Write(SimRegisters.D6110GoodCount, (ushort)0);
            _server.Write(SimRegisters.D6200MaterialLot, string.Empty);
            _server.Write(SimRegisters.D6300Tooling, string.Empty);
            _server.Write(SimRegisters.M100SpindleAlarm, false);

            _isRunning = true;
            _logger?.LogInformation("✓ PLC 模拟器已成功启动，监听端口: {Port}，服务器状态: {IsStarted}",
                _server.Port, _server.IsStarted);
        }
        catch (Exception ex)
        {
            _isRunning = false;
            _logger?.LogError(ex, "✗ 启动 PLC 模拟器失败");
            throw;
        }
    }

    /// <summary>
    ///     停止模拟器
    /// </summary>
    public void Stop()
    {
        if (!_isRunning)
            return;

        _isRunning = false;
        _server.ServerClose();
        _logger?.LogInformation("PLC 模拟器已停止");
    }

    /// <summary>
    ///     更新模拟数据（定期执行）
    /// </summary>
    private void UpdateSimulatedData(object? state)
    {
        if (!_isRunning) return;

        try
        {
            var now = DateTimeOffset.UtcNow;

            var timeBase = now.Second + now.Millisecond * 0.001;

            // 心跳寄存器, 默认为0，等数据采集写入
            var heartbeatCounter = 0;
            _server.Write(SimRegisters.D100Heartbeat, (ushort)heartbeatCounter);

            // 批量数据起始地址：D6000
            // 索引0: 温度 (200-300, 单位0.1°C，实际20-30°C)
            var temperature = (short)(2500 + Math.Sin(timeBase * 0.1) * 500);
            _server.Write(SimRegisters.D6000Temperature, (ushort)temperature);

            // 索引2: 压力 (100-200, 单位0.1MPa，实际10-20MPa)
            var pressure = (short)(1500 + Math.Cos(timeBase * 0.15) * 500);
            _server.Write(SimRegisters.D6001Pressure, (ushort)pressure);

            // 索引4: 电流 (0-500, 单位0.1A，实际0-50A)
            var current = (short)(250 + Math.Sin(timeBase * 0.2) * 250);
            _server.Write(SimRegisters.D6002Current, (ushort)current);

            // 索引6: 电压 (3800-4200, 单位0.1V，实际380-420V)
            var voltage = (short)(4000 + Math.Cos(timeBase * 0.12) * 200);
            _server.Write(SimRegisters.D6003Voltage, (ushort)voltage);

            // 索引8: 光栅位置 (0-1000, 单位mm)
            var lightBarrierPos = (short)(500 + Math.Sin(timeBase * 0.08) * 500);
            _server.Write(SimRegisters.D6004LightBarrier, (ushort)lightBarrierPos);

            // 索引10: 伺服速度 (0-3000, 单位rpm)
            var servoSpeed = (short)(1500 + Math.Cos(timeBase * 0.18) * 1500);
            _server.Write(SimRegisters.D6005ServoSpeed, (ushort)servoSpeed);

            // 索引12: 设备的生产状态，这个状态为0表示设备在休息，为1表示设备再生产中
            // 逻辑：每个设备休息5秒，持续生产10秒，然后休息5秒，再生产
            // 模式：0、0、0、0、0，1、1、1、1、1、1、1、1、1、1, 0、0、0、0、0,1......
            var elapsed = now - _simulatorStartTime;
            var scenario = _options.IsScenario
                ? ProductionScenario.GetSnapshot(elapsed, _options.ScenarioSpeed)
                : null;
            var deviceFlag = scenario is not null
                ? scenario.IsProducing ? 1 : 0
                : (int)elapsed.TotalSeconds % 15 < 5 ? 0 : 1;
            var wasProducing = _isProducing;
            _isProducing = deviceFlag == 1;
            if (_isProducing && !wasProducing)
                _currentBatchNumber = _nextBatchNumber++;

            var materialLot = scenario?.MaterialLot
                              ?? (_currentBatchNumber == 0 ? string.Empty : $"LOT-{_currentBatchNumber:000}");
            var tooling = scenario?.Tooling ?? "TOOL-A";
            var alarmActive = scenario?.AlarmActive ?? false;
            var productCode = scenario is not null
                ? scenario.CycleNumber == 0 ? materialLot : $"{materialLot}-C{scenario.CycleNumber:000}"
                : _isProducing ? $"BATCH-{_currentBatchNumber:000}" : string.Empty;
            var recipeId = scenario is not null ? "R-POLISH-V3" : "R-DEFAULT";
            var goodCount = scenario is not null && scenario.CompletedCycleNumber > 0
                ? (ushort)(scenario.CompletedCycleNumber * 24)
                : (ushort)0;

            _server.Write(SimRegisters.D6006DeviceFlag, (ushort)deviceFlag);
            _server.Write(SimRegisters.D6010ProductCode, productCode);
            _server.Write(SimRegisters.D6100RecipeId, recipeId);
            _server.Write(SimRegisters.D6110GoodCount, goodCount);
            _server.Write(SimRegisters.D6200MaterialLot, materialLot);
            _server.Write(SimRegisters.D6300Tooling, tooling);
            _server.Write(SimRegisters.M100SpindleAlarm, alarmActive);

            // 保存数据快照并输出
            var lastData = new SimulatorData
            {
                Heartbeat = (ushort)heartbeatCounter,
                Temperature = (ushort)temperature,
                Pressure = (ushort)pressure,
                Current = (ushort)current,
                Voltage = (ushort)voltage,
                LightBarrierPos = (ushort)lightBarrierPos,
                ServoSpeed = (ushort)servoSpeed,
                DeviceFlag = (ushort)deviceFlag,
                ProductCode = productCode,
                RecipeId = recipeId,
                GoodCount = goodCount,
                MaterialLot = materialLot,
                Tooling = tooling,
                SpindleAlarm = alarmActive,
                Phase = scenario?.Phase ?? (_isProducing ? "连续生产" : "等待生产"),
                Timestamp = now.LocalDateTime
            };

            if (scenario is not null && !string.Equals(_lastScenarioPhase, scenario.Phase, StringComparison.Ordinal))
            {
                _lastScenarioPhase = scenario.Phase;
                _logger?.LogInformation(
                    "剧本 {Iteration}: {Phase}，批次={MaterialLot}，工装={Tooling}，周期={CycleNumber}，报警={Alarm}",
                    scenario.Iteration,
                    scenario.Phase,
                    materialLot,
                    tooling,
                    scenario.CycleNumber,
                    alarmActive);
            }

            if (now - _lastConsoleOutput >= TimeSpan.FromMilliseconds(_options.ConsoleIntervalMs))
            {
                _lastConsoleOutput = now;
                Console.WriteLine(
                    $"[{now:HH:mm:ss}] {lastData.Phase} | 温度={lastData.Temperature,4} | 压力={lastData.Pressure,4} | 生产={lastData.DeviceFlag} | 批次={lastData.MaterialLot,-7} | 工装={lastData.Tooling,-6} | 报警={(lastData.SpindleAlarm ? 1 : 0)}");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "更新模拟数据失败");
        }
    }
}
