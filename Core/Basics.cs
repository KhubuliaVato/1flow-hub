using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;

namespace OneFlowHub.Core;

public abstract class Bindable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class RelayCommand : ICommand
{
    private readonly Func<object?, Task>? _async;
    private readonly Action<object?>? _sync;
    private readonly Func<object?, bool>? _can;

    public RelayCommand(Action<object?> run, Func<object?, bool>? can = null) { _sync = run; _can = can; }
    public RelayCommand(Func<object?, Task> run, Func<object?, bool>? can = null) { _async = run; _can = can; }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _can?.Invoke(parameter) ?? true;

    public async void Execute(object? parameter)
    {
        try
        {
            if (_async != null) await _async(parameter);
            else _sync?.Invoke(parameter);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
    }
}

public static class Paths
{
    public static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "1Flow");
    public static string Apps => Path.Combine(Root, "Apps");
    public static string Cache => Path.Combine(Root, "cache");
    public static string Temp => Path.Combine(Path.GetTempPath(), "1FlowHub");
    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string LogFile => Path.Combine(Root, "hub.log");

    public static void EnsureAll()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Apps);
        Directory.CreateDirectory(Cache);
    }
}

public static class Log
{
    private static readonly object Gate = new();

    public static void Error(Exception ex) => Write("ERROR " + ex);
    public static void Info(string msg) => Write("INFO  " + msg);

    private static void Write(string line)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Paths.Root);
                var f = new FileInfo(Paths.LogFile);
                if (f.Exists && f.Length > 512 * 1024) f.Delete();
                File.AppendAllText(Paths.LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
            }
        }
        catch { /* logging must never crash the hub */ }
    }
}

public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static T? TryReadFile<T>(string path)
    {
        try
        {
            if (!File.Exists(path)) return default;
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
        }
        catch { return default; }
    }

    public static void TryWriteFile<T>(string path, T value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(value, Options));
        }
        catch (Exception ex) { Log.Error(ex); }
    }
}

public static class VersionUtil
{
    public static string Clean(string? tag) => (tag ?? "").Trim().TrimStart('v', 'V');

    private static int[] Parts(string v)
    {
        var core = Clean(v).Split('-', '+')[0];
        return core.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
    }

    /// <summary>true when <paramref name="candidate"/> is strictly newer than <paramref name="current"/>.</summary>
    public static bool IsNewer(string? candidate, string? current)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return false;
        if (string.IsNullOrWhiteSpace(current)) return true;
        var a = Parts(candidate); var b = Parts(current);
        for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            int x = i < a.Length ? a[i] : 0, y = i < b.Length ? b[i] : 0;
            if (x != y) return x > y;
        }
        return false;
    }
}

public static class Ka
{
    private static readonly string[] Months =
        { "იან", "თებ", "მარ", "აპრ", "მაი", "ივნ", "ივლ", "აგვ", "სექ", "ოქტ", "ნოე", "დეკ" };

    public static string Date(DateTimeOffset d)
    {
        var l = d.ToLocalTime();
        return $"{l.Day} {Months[l.Month - 1]} {l.Year}";
    }

    public static string Ago(DateTimeOffset d)
    {
        int days = (int)Math.Floor((DateTimeOffset.Now - d).TotalDays);
        if (days < 1) return "დღეს";
        if (days == 1) return "გუშინ";
        if (days < 7) return $"{days} დღის წინ";
        if (days < 30) return $"{days / 7} კვირის წინ";
        if (days < 365) return $"{days / 30} თვის წინ";
        return $"{days / 365} წლის წინ";
    }

    public static string Size(long bytes) =>
        bytes >= 1L << 20 ? $"{bytes / 1048576.0:0.#} MB" : $"{Math.Max(1, bytes / 1024)} KB";
}
