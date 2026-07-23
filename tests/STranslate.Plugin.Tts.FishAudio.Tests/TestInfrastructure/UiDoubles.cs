using STranslate.Plugin;
using STranslate.Plugin.Tts.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.Model;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

public sealed class TestSnackbar : ISnackbar
{
    public string? LastError { get; private set; }
    public string? LastWarning { get; private set; }

    public void Show(string message, Severity severity, int duration, string? actionLabel, Action? action)
    {
    }

    public void ShowSuccess(string message, int duration)
    {
    }

    public void ShowError(string message, int duration)
    {
        LastError = message;
    }

    public void ShowWarning(string message, int duration)
    {
        LastWarning = message;
    }

    public void ShowInfo(string message, int duration)
    {
    }

    public void Clear()
    {
        LastError = null;
        LastWarning = null;
    }
}

public sealed class TestLogger : ILogger
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        _entries.Add((logLevel, formatter(state, exception)));
    }

    public bool Contains(LogLevel level, string messagePart) =>
        _entries.Any(e => e.Level == level && e.Message.Contains(messagePart, StringComparison.OrdinalIgnoreCase));

    public bool Contains(string messagePart) =>
        _entries.Any(e => e.Message.Contains(messagePart, StringComparison.OrdinalIgnoreCase));

    public int Count(LogLevel level) => _entries.Count(e => e.Level == level);

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose()
        {
        }
    }
}
