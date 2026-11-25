using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace PoiResize.App.Infrastructure;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute()) { }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);
}

/// <summary>コンボボックス用の「表示名と値」。</summary>
public sealed record Option<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>エクスプローラーと同じ並び順（img2 &lt; img10）で文字列を比べる。</summary>
public sealed class NaturalStringComparer : IComparer<string?>
{
    public static readonly NaturalStringComparer Instance = new();

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int StrCmpLogicalW(string x, string y);

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;
        try { return StrCmpLogicalW(x, y); }
        catch { return string.Compare(x, y, StringComparison.CurrentCultureIgnoreCase); }
    }
}

public static class AppInfo
{
    /// <summary>リリース時に埋め込まれたバージョン（例：1.0.0）。</summary>
    public static string Version
    {
        get
        {
            var asm = Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly;
            var v = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                    ?? asm.GetName().Version?.ToString() ?? "unknown";
            int plus = v.IndexOf('+');
            return plus >= 0 ? v[..plus] : v;
        }
    }
}
