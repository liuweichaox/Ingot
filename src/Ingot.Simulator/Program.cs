using System.Text;
using HslCommunication.Profinet.Melsec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Ingot.Simulator;

internal class Program
{
    private static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        PrintHeader();

        // 加载配置
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", true)
            .AddCommandLine(args)
            .Build();

        var options = new SimulatorOptions
        {
            Port = configuration.GetValue("Port", 502),
            Mode = configuration.GetValue("Mode", "Continuous") ?? "Continuous",
            ScenarioSpeed = configuration.GetValue("ScenarioSpeed", 1d),
            UpdateIntervalMs = configuration.GetValue("UpdateIntervalMs", 100),
            ConsoleIntervalMs = configuration.GetValue("ConsoleIntervalMs", 1_000)
        };

        // 创建日志
        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        var logger = loggerFactory.CreateLogger<Simulator>();

        // 监听 Ctrl+C 退出
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true; // 阻止进程立即退出
            cts.Cancel();    // 触发取消
            logger.LogInformation("收到退出信号，正在关闭...");
        };

        // 创建并启动模拟器
        using var simulator = new Simulator(options, logger);
        simulator.Start();
        PrintInfo(options);

        try
        {
            // 保持应用运行，实时数据输出已在 UpdateSimulatedData 中进行
            await Task.Delay(Timeout.Infinite, cts.Token);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("应用已退出");
        }
        finally
        {
            simulator.Stop();
            logger.LogInformation("模拟器已关闭");
        }
    }

    private static void PrintHeader()
    {
        Console.WriteLine("=".PadRight(60, '='));
        Console.WriteLine("Plc 模拟器 - Ingot Simulator");
        Console.WriteLine("=".PadRight(60, '='));
        Console.WriteLine();
    }

    private static void PrintInfo(SimulatorOptions options)
    {
        Console.WriteLine($"PLC 模拟器运行在 0.0.0.0:{options.Port}");
        Console.WriteLine($"运行模式: {options.Mode}");
        if (options.IsScenario)
            Console.WriteLine($"剧本: 换批次 → 换工装 → 3 个周期 → 报警并恢复（速度 {options.ScenarioSpeed:0.##}x）");
        Console.WriteLine($"提示: 使用 'telnet 127.0.0.1 {options.Port}' 测试连接");
        Console.WriteLine();
        Console.WriteLine("支持的寄存器范围: D0-D9999");
        Console.WriteLine("测试寄存器（自动更新）:");
        Console.WriteLine($"  {SimRegisters.D100Heartbeat} - 心跳寄存器");
        Console.WriteLine($"  传感器数据（批量读取起始地址 {SimRegisters.D6000Temperature}）:");
        Console.WriteLine($"    {SimRegisters.D6000Temperature} (索引0) - 温度 (200-300, 单位0.1°C)");
        Console.WriteLine($"    {SimRegisters.D6001Pressure} (索引2) - 压力 (100-200, 单位0.1MPa)");
        Console.WriteLine($"    {SimRegisters.D6002Current} (索引4) - 电流 (0-500, 单位0.1A)");
        Console.WriteLine($"    {SimRegisters.D6003Voltage} (索引6) - 电压 (3800-4200, 单位0.1V)");
        Console.WriteLine($"    {SimRegisters.D6004LightBarrier} (索引8) - 光栅位置 (0-1000, 单位mm)");
        Console.WriteLine($"    {SimRegisters.D6005ServoSpeed} (索引10) - 伺服速度 (0-3000, 单位rpm)");
        Console.WriteLine($"    {SimRegisters.D6006DeviceFlag} (索引12) - 设备的生产状态 (0=休息 1=生产中)");
        Console.WriteLine($"    {SimRegisters.D6010ProductCode} - 字符串模拟值（示例：BATCH-001，需 string 读取）");
        Console.WriteLine($"    {SimRegisters.D6200MaterialLot} - 当前物料批次");
        Console.WriteLine($"    {SimRegisters.D6300Tooling} - 当前工装");
        Console.WriteLine($"    {SimRegisters.M100SpindleAlarm} - 主轴过载报警位");
    }
}
