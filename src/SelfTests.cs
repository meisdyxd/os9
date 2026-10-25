using System.Text.Json;

namespace MeiOS;

public static class SelfTests
{
    private static readonly List<object> Results = [];
    private static void Check(string name, bool condition, string actual)
    {
        Results.Add(new { name, passed=condition, actual });
        if (!condition) throw new InvalidOperationException("Тест не пройден: " + name + " / " + actual);
    }
    public static SimulationModel Empty(int quantum=4, int duration=3)
    {
        var config=new ModelConfig { Slots=8, Memory=new(512,512,1), TaskSize=new(64,64,1), Quantum=quantum, AgingTicks=3, LoadTicks=0, DispatchTicks=0, IoInitTicks=0, IoInterruptTicks=0, FinishTicks=0, IoDuration=duration };
        var m=new SimulationModel(config);
        m.Initialize();
        m.AutoLoad=false;
        Array.Clear(m.Table);
        foreach(var q in m.ReadyQueues) q.Clear();
        m.Cpu.Active=null; m.SystemJobs.Clear(); m.Io.Clear();
        return m;
    }
    public static Psw Insert(SimulationModel m, int size, Command[]? commands=null)
    {
        m.SetPendingTask(size,commands);
        if(!m.LoadNewTask()) throw new InvalidOperationException("Ошибка тестовой загрузки");
        return m.Residents.Last();
    }
    public static void Run()
    {
        // Интерфейс: узкие окна, все экраны, длинные строки и границы прокрутки.
        var ui = Empty();
        Insert(ui, 64); Insert(ui, 200);
        ui.Log(new string('Ж', 500));
        foreach (var process in ui.Residents) { process.Id = 12345678 + process.Slot; process.Pc = int.MaxValue; }
        ui.Cpu.Counter = int.MaxValue;
        bool layoutOk = true;
        foreach (int width in new[] { 1, 40, 79, 119, 179 })
        foreach (int height in new[] { 5, 10, 24, 40 })
        for (int view = 0; view < 3; view++)
        {
            int actualScroll = -1;
            var frame = Dashboard.Build(ui, "task " + new string('9', 100), width, height, view, int.MaxValue, true, value => actualScroll = value);
            layoutOk &= frame.Count == height && frame.All(line => line.Length <= width) && actualScroll >= 0 && actualScroll < int.MaxValue;
            if (width >= 5) layoutOk &= frame[^1].EndsWith('9');
            frame = Dashboard.Build(ui, "", width, height, view, -100);
            layoutOk &= frame.Count == height && frame.All(line => line.Length <= width);
        }
        Check("dashboard_layout", layoutOk, "all views, 20 window sizes, long IDs/log/input, scroll limits");
        var fullScreen = Empty();
        for (int i = 0; i < 100; i++) fullScreen.ExecuteOneTick();
        for (int i = 0; i < 8; i++) Insert(fullScreen, 64);
        fullScreen.Cpu.UsefulTicks = 40; fullScreen.Cpu.SystemTicks = 20; fullScreen.Cpu.IdleTicks = 40;
        fullScreen.Completed.Add(new Psw
        {
            Id = 99, Size = 128, Program = [new(Op.Compute), new(Op.Exit)], IoCount = 0,
            State = ProcessState.Completed, Arrival = 0, End = 2, ActiveTicks = 2
        });
        var completeFrame = Dashboard.Build(fullScreen, width: 180, height: 48);
        Check("dashboard_metrics_full_screen", !completeFrame.Any(line => line.StartsWith("Строки "))
            && completeFrame.Any(line => line.Contains("Пропускная способность"))
            && completeFrame.Any(line => line.Contains("полезная загрузка"))
            && completeFrame.Any(line => line.Contains("Средние входные"))
            && completeFrame.Any(line => line.Contains("Средние выходные")),
            $"180x48, relative={fullScreen.RelativePerformance:F2}%, utilization={fullScreen.UsefulUtilization:F1}%");
        var m=Empty();
        Insert(m,200); Insert(m,300);
        m.SetPendingTask(20);
        Check("memory_reject",!m.LoadNewTask() && m.FreeMemory==12,$"free={m.FreeMemory}");
        m=Empty();
        for(int i=0;i<8;i++) Insert(m,1);
        m.SetPendingTask(1);
        Check("psw_capacity",!m.LoadNewTask() && m.FreeMemory==504,$"slots={m.Residents.Count()}");
        m=Empty(); var p=Insert(m,64);
        m.Dispatch(); m.ExecuteOneTick();
        Check("hardware_pc",m.Cpu.Counter==1 && p.Pc==1,$"pc={p.Pc}");
        m.ExecuteDirective("speed 1000"); m.ChangeSpeed(1.1);
        Check("speed_max",m.Speed==1000,$"speed={m.Speed}");
        m.ExecuteDirective("speed 0.1"); m.ChangeSpeed(0.9);
        Check("speed_min",m.Speed==0.1,$"speed={m.Speed}");
        m.ExecuteDirective("speed 100"); m.ExecuteDirective("+");
        Check("speed_plus",Math.Abs(m.Speed-110)<1e-9,$"speed={m.Speed}");
        m.ExecuteDirective("speed 100"); m.ExecuteDirective("-");
        Check("speed_minus",Math.Abs(m.Speed-90)<1e-9,$"speed={m.Speed}");
        var a=new SimulationModel(); var b=new SimulationModel(); a.Initialize(); b.Initialize();
        Check("seed_repeat",string.Join('|',a.Residents)==string.Join('|',b.Residents),string.Join('|',a.Residents));
        var computeOnly=Enumerable.Repeat(new Command(Op.Compute),30).Append(new Command(Op.Exit)).ToArray();
        m=Empty(2); var first=Insert(m,64,computeOnly); var second=Insert(m,64,computeOnly); m.Dispatch();
        m.ExecuteOneTick(); m.ExecuteOneTick();
        Check("rr_equal_priority",m.Cpu.Active==second,$"active={m.Cpu.Active?.Id}");
        m=Empty(20); first=Insert(m,64,computeOnly); m.Dispatch(); second=Insert(m,300,computeOnly);
        m.ExecuteOneTick();
        Check("relative_no_preemption",m.Cpu.Active==first,$"active={m.Cpu.Active?.Id}, new_priority={second.Priority}");
        m=Empty(); first=Insert(m,64); second=Insert(m,300);
        Check("large_jobs_priority",m.GetNextProcess()==second,$"base small={first.BasePriority}, large={second.BasePriority}");
        for(int i=0;i<6;i++) m.AgeReadyProcesses();
        Check("aging",first.Priority==2,$"priority={first.Priority}");
        m=Empty(100); p=Insert(m,64,[new(Op.Compute),new(Op.Exit)]);
        m.ExecuteOneTick(); m.ExecuteOneTick();
        Check("compute_exit_memory",p.Memory[2]==5 && m.Completed.Count==1 && m.UsedMemory==0,$"result={p.Memory[2]}, completed={m.Completed.Count}, used={m.UsedMemory}");
        m=Empty(100); p=Insert(m,64,[new(Op.Compute,99),new(Op.Exit)]);
        m.ExecuteOneTick();
        Check("memory_protection",p.State==ProcessState.Killed && m.UsedMemory==0,$"state={p.State}");
        m=Empty(100,3); p=Insert(m,64,[new(Op.Io),new(Op.Exit)]);
        m.ExecuteOneTick();
        Check("io_block",p.State==ProcessState.BlockedIo && m.Io.Single().Left==3,$"state={p.State}, IO_left={m.Io.Single().Left}");
        for(int i=0;i<4;i++) m.ExecuteOneTick();
        Check("io_interrupt_completion",p.State==ProcessState.Completed && p.End==5,$"state={p.State}, end={p.End}");
        var screen = Dashboard.Build(m, width: 79, height: 24);
        Check("stable_dashboard",screen.Count == 24 && screen.All(line => line.Length <= 79) && screen[^1].Contains("Q выход"), $"lines={screen.Count}, controls fixed");
        var noIoModel=Empty(100); var noIoProcess=Insert(noIoModel,64,[new(Op.Compute),new(Op.Exit)]);
        noIoModel.ExecuteOneTick(); noIoModel.ExecuteOneTick();
        Check("mono_multi_no_io",noIoModel.Completed.Count == 1 && noIoModel.MonoTime(noIoProcess) == 2 && noIoModel.MultiTime(noIoProcess) == 2,
            $"commands=2, IO=0, expected=2/2, actual={noIoModel.MonoTime(noIoProcess)}/{noIoModel.MultiTime(noIoProcess)}");
        Check("mono_multi_one_io",m.MonoTime(p)==5 && m.MultiTime(p)==5,$"mono={m.MonoTime(p)}, multi={m.MultiTime(p)}");
        Check("residence_decomposition",p.ReadyTicks+p.ActiveTicks+p.IoTicks+p.SystemTicks+p.SuspendedTicks==m.MultiTime(p),$"R/A/IO/S={p.ReadyTicks}/{p.ActiveTicks}/{p.IoTicks}/{p.SystemTicks}");
        m=Empty(1); first=Insert(m,64,[new(Op.Compute),new(Op.Exit)]); second=Insert(m,64,[new(Op.Compute),new(Op.Exit)]);
        for(int i=0;i<4;i++) m.ExecuteOneTick();
        Check("multi_two_jobs",m.Completed.Count==2 && m.MultiTime(first)==3 && m.MultiTime(second)==4 && m.RelativePerformance==100,$"multi={m.MultiTime(first)}/{m.MultiTime(second)}, P={m.RelativePerformance}");
        m=Empty(100,3); p=Insert(m,64,[new(Op.Io),new(Op.Io),new(Op.Exit)]);
        for(int i=0;i<9;i++) m.ExecuteOneTick();
        Check("two_io",m.MonoTime(p)==9 && m.MultiTime(p)==9,$"mono={m.MonoTime(p)}, multi={m.MultiTime(p)}");
        m=Empty(100); m.Config.LoadTicks=1; m.Config.DispatchTicks=1; m.Config.FinishTicks=1;
        p=Insert(m,64,[new(Op.Compute),new(Op.Exit)]);
        for(int i=0;i<5;i++) m.ExecuteOneTick();
        Check("system_costs",m.MonoTime(p)==5 && m.MultiTime(p)==5 && m.Cpu.SystemTicks==3,$"mono={m.MonoTime(p)}, multi={m.MultiTime(p)}, sys={m.Cpu.SystemTicks}");
        RunConfigurationTests();
        RunOperatorTests();
        Console.WriteLine(JsonSerializer.Serialize(Results,new JsonSerializerOptions{WriteIndented=true}));
    }

    private static void RunConfigurationTests()
    {
        var rng = new Random(14);
        var range = new ParameterRange(100, 510, 50);
        var values = Enumerable.Range(0, 1000).Select(_ => range.Sample(rng)).ToArray();
        Check("range_grid", values.All(v => v >= 100 && v <= 500 && (v - 100) % 50 == 0), "сетка 100..500, шаг 50");
        bool allWord = true;
        foreach (int min in new[] { 0, 1, 65535 })
        foreach (int max in new[] { 0, 1, 65535 })
        foreach (int step in new[] { 0, 1, 65535 })
        {
            var normalized = new ParameterRange(min, max, step).Normalize();
            int value = new ParameterRange(min, max, step).Sample(rng);
            allWord &= value >= normalized.Min && value <= normalized.Max && (value - normalized.Min) % normalized.Step == 0;
        }
        Check("word_combinations", allWord, "27 комбинаций крайних word, обратные границы и нулевой шаг");
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "# диапазоны\nmemory 512 512 1 память\nsize 64 128 16 размер\ncommands 2 6 1\nio 0 25 5\nquantum 1 7 2\nslots 2 4 1\nload 0 2 1\ndispatch 0 2 1\n");
            var config = ModelConfig.ReadFile(path);
            var first = new SimulationModel(config, 14); first.Initialize();
            string before = Snapshot(first);
            File.WriteAllText(path, "unknown 1 1 1");
            first.Initialize();
            Check("read_once_reset", Snapshot(first) == before, "reset использует сохранённую конфигурацию");
            Check("system_ranges", first.Config.Quantum is 1 or 3 or 5 or 7 && first.Table.Length == first.Config.Slots && first.Config.Slots is >= 2 and <= 4 && first.Config.LoadTicks is >= 0 and <= 2,
                $"quantum={first.Config.Quantum}, slots={first.Config.Slots}, load={first.Config.LoadTicks}");
            var second = new SimulationModel(config, 14); second.Initialize();
            for (int i = 0; i < 200; i++) { first.ExecuteOneTick(); second.ExecuteOneTick(); }
            Check("config_trace_repeat", Snapshot(first) == Snapshot(second), "200 тактов, одинаковые программы, состояния и метрики");
            int different = 0;
            for (int seed = 1; seed < 20; seed++) { var model = new SimulationModel(config, seed); model.Initialize(); if (model.Config.Quantum != 1) different++; }
            Check("system_random_values", different > 0, $"получены значения диапазона выше минимума: {different}");
            bool rejects = true;
            foreach (string bad in new[] { "unknown 1 1 1", "size 1 2 1\nsize 2 3 1", "size 1 x 1", "size 1 65536 1", "size 1 2", "size -1 2 1" })
            {
                File.WriteAllText(path, bad);
                try { ModelConfig.ReadFile(path); rejects = false; }
                catch (Exception e) when (e is FormatException or ArgumentException) { }
            }
            Check("config_invalid", rejects, "неизвестный/повторный ключ, нечисло, выход за word и неполная строка отвергнуты");
            File.WriteAllText(path, "size 65535 0 0\nio 65535 0 0\nslots 0 0 0\nquantum 0 0 0");
            var edge = ModelConfig.ReadFile(path);
            Check("config_word_normalization", edge.TaskSize == new ParameterRange(1, 65535, 1) && edge.IoPercent == new ParameterRange(0, 100, 1) && edge.Slots == 1 && edge.Quantum == 1 && edge.Notices.Count == 4,
                "крайние word приведены к допустимым диапазонам с диагностикой");
        }
        finally { File.Delete(path); }
    }

    private static void RunOperatorTests()
    {
        static bool Rejected(SimulationModel model, string text)
        {
            try { model.ExecuteDirective(text); return false; }
            catch (Exception e) when (e is ArgumentException or FormatException or InvalidOperationException or OverflowException) { return true; }
        }
        var m = Empty(2);
        var p = Insert(m, 64, [new(Op.Compute), new(Op.Compute), new(Op.Compute), new(Op.Compute), new(Op.Exit)]);
        m.Dispatch(); m.ExecuteOneTick();
        m.ExecuteDirective($"suspend {p.Id}");
        int pc = p.Pc;
        m.ExecuteOneTick();
        Check("suspend_preserves_pc", p.State == ProcessState.Suspended && p.Pc == pc && m.Cpu.Active is null && p.SuspendedTicks == 1, "PC сохранён, CPU освобождён, такт паузы учтён");
        m.ExecuteDirective($"resume {p.Id}"); m.CheckSystemState();
        Check("resume_dispatch", m.Cpu.Active == p && m.Cpu.Counter == pc && p.Quantum == 2, "продолжение с сохранённого PC и полным квантом");
        m.ExecuteDirective("task 80 3 100");
        Check("edit_pending_only", m.NewTask!.Size == 80 && m.NewTask.Program.Length == 4 && m.NewTask.IoCount == 3 && p.Size == 64,
            "изменено только новое задание; 3 IO и завершающий Exit");
        bool argsRejected = new[] { "task", "task 1 2", "task 1 2 101", "task 65536 2 0", "speed", "auto maybe", "load extra", "kill", "memory", "suspend 999999" }.All(text => Rejected(m, text));
        Check("directive_validation", argsRejected && p.State == ProcessState.Active && m.MemorySize == 512, "ошибки команд не изменяют резидентов и память");
        Check("memory_busy_reject", Rejected(m, "memory 32") && m.MemorySize == 512, "нельзя уменьшить память ниже 64 занятых единиц");
        m.ExecuteDirective("memory 1024");
        Check("memory_resize", m.MemorySize == 1024 && m.UsedMemory == 64 && m.FreeMemory == 960, "занятая память сохранена");
        m.ExecuteDirective("auto off"); m.ExecuteDirective("load"); m.CheckSystemState();
        Check("load_deferred", m.ManualLoadRequested && m.Residents.Count() == 1 && m.Cpu.Active == p, "ручная загрузка ожидает освобождения CPU");
        m.ExecuteOneTick(); m.ExecuteOneTick();
        Check("manual_load_boundary", !m.ManualLoadRequested && m.Residents.Count() == 2 && m.Residents.Any(x => x.Size == 80), "одна ручная загрузка на границе кванта");
        m = Empty(); m.SetPendingTask(64, [new(Op.Compute), new(Op.Exit)]);
        m.ExecuteDirective("auto off"); m.CheckSystemState();
        bool stopped = !m.Residents.Any();
        m.ExecuteDirective("auto on"); m.CheckSystemState();
        Check("auto_off_on", stopped && m.Residents.Any(), "автозагрузка остановлена и возобновлена");
        m = Empty(100); p = Insert(m, 64, [new(Op.Io), new(Op.Exit)]);
        m.ExecuteOneTick(); m.ExecuteDirective($"kill {p.Id}");
        Check("kill_io_cleanup", !m.Residents.Any() && m.Io.Count == 0 && m.SystemJobs.Count == 0 && m.UsedMemory == 0 && m.Completed.Count == 0,
            "IO и память освобождены, завершение не засчитано");
        m = Empty(); m.Config.LoadTicks = 1; p = Insert(m, 64, [new(Op.Exit)]);
        m.ExecuteDirective($"kill {p.Id}");
        Check("kill_system_cleanup", m.SystemJobs.Count == 0 && m.UsedMemory == 0, "незавершённая системная операция отменена");
        m = Empty(); p = Insert(m, 64, [new(Op.Exit)]); m.ExecuteDirective($"suspend {p.Id}");
        Check("drain_suspended_reject", Rejected(m, "drain") && !m.StopWhenEmpty, "приостановленный процесс нужно возобновить или удалить");
        m.ExecuteDirective($"resume {p.Id}"); m.ExecuteDirective("load"); m.ExecuteDirective("drain");
        bool protectedDrain = !m.ManualLoadRequested && Rejected(m, "load") && Rejected(m, "auto on") && Rejected(m, $"suspend {p.Id}");
        for (int i = 0; i < 20 && m.Running; i++) m.ExecuteOneTick();
        Check("drain_stop", protectedDrain && !m.Running && !m.Residents.Any() && m.Completed.Count == 1, "новые поступления запрещены, модель остановлена после завершения резидентов");
        m = Empty(); m.ExecuteDirective("drain"); m.ExecuteOneTick();
        Check("drain_empty_no_tick", !m.Running && m.Tick == 0, "пустая модель останавливается без лишнего такта");
        m = Empty(); m.ExecuteDirective("q");
        Check("operator_quit", !m.Running, "немедленная остановка");
    }

    private static string Snapshot(SimulationModel m) => string.Join("|", m.Residents.Concat(m.Completed).Select(p =>
        $"{p.Id}:{p.Size}:{p.State}:{p.Pc}:{p.ReadyTicks}:{p.ActiveTicks}:{p.IoTicks}:{p.SystemTicks}:" + string.Join(",", p.Program.Select(c => c.Code))))
        + $";{m.Tick};{m.UsedMemory};{m.Cpu.UsefulTicks};{m.Cpu.SystemTicks};{m.Cpu.IdleTicks}";
}
