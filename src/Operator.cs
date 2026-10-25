using System.Globalization;

namespace MeiOS;

public sealed partial class SimulationModel
{
    public void ExecuteDirective(string text)
    {
        var words = text.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return;
        string command = words[0].ToLowerInvariant();
        int expected = command switch
        {
            "q" or "quit" or "+" or "-" or "reset" or "help" or "load" or "drain" => 1,
            "speed" or "auto" or "suspend" or "resume" or "kill" or "memory" => 2,
            "task" => 4,
            _ => throw new ArgumentException("Неизвестная директива. help")
        };
        if (words.Length != expected) throw new ArgumentException($"{command}: ожидается аргументов {expected - 1}. help");
        switch (command)
        {
            case "q": case "quit": Running = false; break;
            case "+": ChangeSpeed(1.1); break;
            case "-": ChangeSpeed(0.9); break;
            case "speed":
                double speed = double.Parse(words[1], CultureInfo.InvariantCulture);
                if (!double.IsFinite(speed) || speed is < 0.1 or > 1000) throw new ArgumentException("Скорость: 0.1..1000");
                Speed = speed; break;
            case "reset": Initialize(); break;
            case "help": Log(HelpText()); break;
            case "task":
                int size = int.Parse(words[1]);
                int count = int.Parse(words[2]);
                int io = int.Parse(words[3]);
                if (size is < 1 or > 65535 || count is < 1 or > 65535 || io is < 0 or > 100) throw new ArgumentException("task: размер и число команд 1..65535, IO 0..100");
                SetPendingTask(size, GenerateCommands(count, io)); break;
            case "auto":
                if (words[1] is not ("on" or "off")) throw new ArgumentException("auto on|off");
                if (StopWhenEmpty && words[1] == "on") throw new InvalidOperationException("Выполняется drain: новые задания запрещены до reset");
                AutoLoad = words[1] == "on"; break;
            case "load":
                if (StopWhenEmpty) throw new InvalidOperationException("Выполняется drain: новые задания запрещены до reset");
                ManualLoadRequested = true;
                Log("Ручная загрузка запрошена при освобождении CPU");
                break;
            case "suspend": Suspend(int.Parse(words[1])); break;
            case "resume": Resume(int.Parse(words[1])); break;
            case "kill": Terminate(FindProcess(int.Parse(words[1])), false); break;
            case "drain":
                if (Residents.Any(p => p.State == ProcessState.Suspended)) throw new InvalidOperationException("Возобновите или удалите приостановленные процессы перед drain");
                AutoLoad = false; ManualLoadRequested = false; StopWhenEmpty = true; break;
            case "memory":
                int memory = int.Parse(words[1]);
                if (memory is < 1 or > 65535 || memory < UsedMemory) throw new ArgumentException("memory: 1..65535, не меньше занятого объёма");
                MemorySize = memory; break;
            default: throw new ArgumentException("Неизвестная директива. help");
        }
        ValidateInvariants();
    }

    public static string HelpText() => "q | + | - | speed 0.1..1000 | reset | help"
        + " | task size commands io% | auto on/off | load | suspend id | resume id | kill id | drain | memory size"
        ;

    public Psw FindProcess(int id) => Residents.FirstOrDefault(p => p.Id == id)
        ?? throw new ArgumentException("Процесс не найден");

    public void Suspend(int id)
    {
        if (StopWhenEmpty) throw new InvalidOperationException("Во время drain приостановка запрещена");
        var p = FindProcess(id);
        if (p.State is not (ProcessState.Ready or ProcessState.Active)) throw new InvalidOperationException("Приостановка разрешена для Ready/Active");
        Process(p, ProcessState.Suspended, "оператор");
    }

    public void Resume(int id)
    {
        var p = FindProcess(id);
        if (p.State != ProcessState.Suspended) throw new InvalidOperationException("Процесс не приостановлен");
        Process(p, ProcessState.Ready, "оператор");
    }
}
