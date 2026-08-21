using System;
using System.Collections.Generic;
using System.Linq;

namespace PromptHelper.Services;

public enum StartupDiagnosticSeverity
{
    Information,
    Warning,
    Error
}

public sealed record StartupDiagnostic(
    string Code,
    StartupDiagnosticSeverity Severity,
    string Message);

public sealed class StartupDiagnosticCollector
{
    private readonly List<StartupDiagnostic> _items = [];

    public IReadOnlyList<StartupDiagnostic> Items => _items;

    public void Add(string code, StartupDiagnosticSeverity severity, string message)
    {
        _items.Add(new StartupDiagnostic(code, severity, message));
    }

    public void Warning(string code, string message)
    {
        Add(code, StartupDiagnosticSeverity.Warning, message);
    }

    public void Information(string code, string message)
    {
        Add(code, StartupDiagnosticSeverity.Information, message);
    }

    public string? BuildAggregatedWarning()
    {
        var warnings = _items
            .Where(x => x.Severity == StartupDiagnosticSeverity.Warning)
            .ToList();

        if (warnings.Count == 0)
        {
            return null;
        }

        return string.Join("\r\n\r\n", warnings.Select(w => $"[{w.Code}] {w.Message}"));
    }
}
