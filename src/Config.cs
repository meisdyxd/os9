using System.Globalization;

namespace MeiOS;

public readonly record struct ParameterRange(int Min, int Max, int Step)
{
    public void Validate()
    {
        if (Min is < 0 or > 65535 || Max is < 0 or > 65535 || Step is < 0 or > 65535)
            throw new ArgumentException($"Неверный диапазон {Min} {Max} {Step}");
    }
    public int Sample(Random random)
    {
        var r = Normalize();
        return r.Min + random.Next((r.Max - r.Min) / r.Step + 1) * r.Step;
    }
    public ParameterRange Normalize(int lower = 0, int upper = 65535)
    {
        Validate();
        return new(Math.Clamp(Math.Min(Min, Max), lower, upper),
            Math.Clamp(Math.Max(Min, Max), lower, upper), Math.Max(1, Step));
    }
}
public sealed class ModelConfig
{
    public ParameterRange Memory = new(1536, 2560, 256);
    public ParameterRange TaskSize = new(96, 384, 16);
    public ParameterRange Commands = new(12, 24, 1);
    public ParameterRange IoPercent = new(25, 25, 1);
    public int Slots = 8, Quantum = 4, AgingTicks = 12, SizeBand = 128;
    public int LoadTicks = 1, DispatchTicks = 1, IoInitTicks = 1, IoInterruptTicks = 1, FinishTicks = 1, IoDuration = 4;
    public double Speed = 2;
    public readonly Dictionary<string, ParameterRange> SystemRanges = new();
    public readonly List<string> Notices = [];

    public void SampleSystemParameters(Random random)
    {
        foreach (var key in SystemRanges.Keys.Order(StringComparer.Ordinal))
            AssignSystemParameter(key, SystemRanges[key].Sample(random));
    }

    private void AssignSystemParameter(string key, int value)
    {
        switch (key)
        {
            case "slots": Slots = value; break;
            case "quantum": Quantum = value; break;
            case "aging": AgingTicks = value; break;
            case "band": SizeBand = value; break;
            case "load": LoadTicks = value; break;
            case "dispatch": DispatchTicks = value; break;
            case "io_init": IoInitTicks = value; break;
            case "io_interrupt": IoInterruptTicks = value; break;
            case "finish": FinishTicks = value; break;
            case "io_duration": IoDuration = value; break;
            default: throw new FormatException($"Неизвестный ключ {key}");
        }
    }

    public void Validate()
    {
        Memory.Validate(); TaskSize.Validate(); Commands.Validate(); IoPercent.Validate();
        if (Memory.Min < 1 || TaskSize.Min < 1 || Commands.Min < 1 || IoPercent.Max > 100 || Slots is < 1 or > 16 || Quantum < 1 || AgingTicks < 1 || SizeBand < 1 || IoDuration < 1)
            throw new ArgumentException("Недопустимые параметры модели");
        if (new[] { LoadTicks, DispatchTicks, IoInitTicks, IoInterruptTicks, FinishTicks }.Any(x => x < 0))
            throw new ArgumentException("Системные затраты не могут быть отрицательными");
        if (!double.IsFinite(Speed) || Speed is < 0.1 or > 1000) throw new ArgumentException("Скорость: 0.1..1000");
    }
    public static ModelConfig ReadFile(string path)
    {
        var config = new ModelConfig();
        var seen = new HashSet<string>();
        int number = 0;
        foreach (var line in File.ReadLines(path))
        {
            number++;
            var content = line.Split('#', 2)[0].Trim();
            if (content.Length == 0) continue;
            var parts = content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4) throw new FormatException($"Строка {number}: key min max step");
            if (!seen.Add(parts[0])) throw new FormatException($"Строка {number}: повтор ключа");
            var key = parts[0];
            if (!new[] { "memory", "size", "commands", "io", "slots", "quantum", "aging", "band", "load", "dispatch", "io_init", "io_interrupt", "finish", "io_duration" }.Contains(key))
                throw new FormatException($"Строка {number}: неизвестный ключ {key}");
            if (!int.TryParse(parts[1], CultureInfo.InvariantCulture, out int min)
                || !int.TryParse(parts[2], CultureInfo.InvariantCulture, out int max)
                || !int.TryParse(parts[3], CultureInfo.InvariantCulture, out int step))
                throw new FormatException($"Строка {number}: min, max и step должны быть целыми числами word");
            var raw = new ParameterRange(min, max, step);
            ParameterRange range;
            try
            {
                int lower = key is "memory" or "size" or "commands" or "slots" or "quantum" or "aging" or "band" or "io_duration" ? 1 : 0;
                range = raw.Normalize(lower, key == "io" ? 100 : key == "slots" ? 16 : 65535);
            }
            catch (ArgumentException error) { throw new FormatException($"Строка {number}: {error.Message}", error); }
            if (range != raw) config.Notices.Add($"Строка {number}, {key}: диапазон приведён к {range.Min} {range.Max} {range.Step}");
            switch (key)
            {
                case "memory": config.Memory = range; break;
                case "size": config.TaskSize = range; break;
                case "commands": config.Commands = range; break;
                case "io": config.IoPercent = range; break;
                default:
                    config.SystemRanges.Add(key, range);
                    config.AssignSystemParameter(key, range.Min);
                    break;
            }
        }
        config.Validate();
        return config;
    }
}
