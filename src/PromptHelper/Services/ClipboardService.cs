using System.Runtime.InteropServices;
using System.Windows;

namespace PromptHelper.Services;

public sealed class ClipboardService
{
    public void CopyText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        ExternalException? lastError = null;

        for (int attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                Clipboard.SetText(text, TextDataFormat.UnicodeText);
                return;
            }
            catch (ExternalException ex)
            {
                lastError = ex;

                if (attempt == 5)
                {
                    break;
                }

                Thread.Sleep(25);
            }
        }

        throw new InvalidOperationException(
            "Windows clipboard is currently unavailable.",
            lastError);
    }
}