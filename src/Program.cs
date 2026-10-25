using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using MeiOS;

Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
if (args.Any(x => x is "/?" or "--help"))
{
    Console.WriteLine("ЛР 9. " + SimulationModel.HelpText());
    Console.WriteLine("dotnet run --project src -- [--ticks N] [--seed N] [--speed V] [--width N] [--height N] [--benchmark seconds] [--self-test] [--json path]");
    Console.WriteLine("[--config path] / params.txt читается один раз до инициализации");
    Console.WriteLine("[--script path] строки файла: номер_такта|директива");
    return;
}
string? Option(string key)
{
    int index = Array.IndexOf(args, key);
    if (index < 0) return null;
    if (index + 1 == args.Length) throw new ArgumentException($"Нет значения {key}");
    return args[index+1];
}

try
{
    if (args.Contains("--self-test")) { SelfTests.Run(); return; }
    ModelConfig config = new();
    config = ModelConfig.ReadFile(Option("--config") ?? Path.Combine(AppContext.BaseDirectory, "params.txt"));
    var model = new SimulationModel(config, int.Parse(Option("--seed") ?? "14"));
    model.Initialize();
    foreach (var notice in config.Notices) Console.Error.WriteLine(notice);
    int screenWidth = int.Parse(Option("--width") ?? "100");
    int screenHeight = int.Parse(Option("--height") ?? "32");
    if (screenWidth is < 20 or > 240 || screenHeight is < 10 or > 80)
        throw new ArgumentOutOfRangeException("Размер снимка экрана: ширина 20..240, высота 10..80");
    if (Option("--speed") is { } speed) model.ExecuteDirective("speed " + speed);
    var script = new Dictionary<long, List<string>>();
    if (Option("--script") is { } path)
    {
        if (Option("--ticks") is null) throw new ArgumentException("--script используется вместе с --ticks N");
        int lineNumber = 0;
        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;
            var content = line.Split('#', 2)[0].Trim();
            if (content.Length == 0) continue;
            var parts = content.Split('|', 2);
            if (parts.Length != 2 || !long.TryParse(parts[0], out long tick) || tick < 0 || string.IsNullOrWhiteSpace(parts[1]))
                throw new FormatException($"Строка сценария {lineNumber}: ожидается неотрицательный_такт|директива");
            if (!script.ContainsKey(tick)) script[tick] = [];
            script[tick].Add(parts[1].Trim());
        }
    }
    double elapsed = 0;
    if (Option("--ticks") is { } count)
    {
        long ticks = long.Parse(count);
        if (ticks < 0) throw new ArgumentException("Число тактов неотрицательное");
        var watch = Stopwatch.StartNew();
        for (long i = 0; i < ticks && model.Running; i++)
        {
            if (script.TryGetValue(model.Tick, out var directives))
                foreach (var directive in directives)
                {
                    model.ExecuteDirective(directive);
                    if (!model.Running) break;
                }
            model.ExecuteOneTick();
        }
        elapsed = watch.Elapsed.TotalSeconds;
        Console.WriteLine(string.Join(Environment.NewLine, Dashboard.Build(model, width: screenWidth, height: screenHeight)));
    }
    else
    {
        double? seconds = Option("--benchmark") is { } duration ? double.Parse(duration) : null;
        if (seconds is <= 0) throw new ArgumentException("Длительность замера положительная");
        if (seconds is null && Console.IsInputRedirected) throw new InvalidOperationException("Нужен интерактивный терминал; для пакетного запуска --ticks N");
        elapsed = RealtimeLoop.Run(model, seconds);
    }
    var result = new
    {
        Lab = 9, model.Seed, model.Tick, model.Speed, ElapsedSeconds = elapsed,
        ActualTicksPerSecond = elapsed > 0 ? model.Tick/elapsed : 0,
        model.UsedMemory, model.FreeMemory, model.MemorySize,
        Useful = model.Cpu.UsefulTicks, System = model.Cpu.SystemTicks, Idle = model.Cpu.IdleTicks,
        Completed = model.Completed.Count,
        model.AutoLoad, model.ManualLoadRequested, model.StopWhenEmpty, model.Running,
        Residents = model.Residents.Select(p => new { p.Id, p.Size, p.Pc, State=p.State.ToString(), p.BasePriority, p.Priority, p.ReadyTicks, p.IoTicks }).ToArray(),
        model.Throughput, model.RelativePerformance, model.UsefulUtilization,
        CompletedProcesses = model.Completed.Select(p => new { p.Id, Mono=model.MonoTime(p), Multi=model.MultiTime(p), p.ReadyTicks, p.ActiveTicks, p.IoTicks, p.SystemTicks, p.SuspendedTicks }).ToArray(),
        Lines = Dashboard.Build(model, width: screenWidth, height: screenHeight), Events = model.Events.ToArray()
    };
    if (Option("--json") is { } json) File.WriteAllText(json, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented=true }));
    Console.WriteLine($"Итог: тактов={model.Tick}, секунд={elapsed:F3}, фактическая скорость={(elapsed>0?model.Tick/elapsed:0):F3} такт/с");
}
catch (Exception error)
{
    Console.Error.WriteLine($"Ошибка: {error.Message}");
    Environment.ExitCode = 1;
}
