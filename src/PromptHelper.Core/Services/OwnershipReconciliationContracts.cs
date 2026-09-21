namespace PromptHelper.Services;

internal enum ReconciliationSeverity
{
    Notice,
    Warning,
    Fatal
}

internal sealed record ReconciliationOutcome(
    ReconciliationSeverity Severity,
    string Code,
    string Path,
    string Message);
