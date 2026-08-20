using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace PromptHelper.Tests;

internal static class WpfTestHost
{
    private static readonly object Sync = new();
    private static readonly ManualResetEventSlim Ready = new(false);

    private static Thread? _thread;
    private static Dispatcher? _dispatcher;
    private static Exception? _startupException;

    public static void Start()
    {
        lock (Sync)
        {
            if (_thread != null)
            {
                return;
            }

            _thread = new Thread(ThreadMain)
            {
                IsBackground = true,
                Name = "PromptHelper.Tests.WPF"
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        Ready.Wait();

        if (_startupException != null)
        {
            throw new InvalidOperationException(
                "Failed to initialize WPF test host.",
                _startupException);
        }
    }

    private static void ThreadMain()
    {
        try
        {
            var app = new Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };

            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    "pack://application:,,,/PromptHelper;component/Styles/Theme.xaml",
                    UriKind.Absolute)
            });

            _dispatcher = Dispatcher.CurrentDispatcher;
            Ready.Set();
            Dispatcher.Run();
        }
        catch (Exception ex)
        {
            _startupException = ex;
            Ready.Set();
        }
    }

    public static void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Start();
        _dispatcher!.Invoke(action);
    }

    public static T Invoke<T>(Func<T> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        Start();
        return _dispatcher!.Invoke(func);
    }

    public static void Stop()
    {
        Dispatcher? dispatcher;
        Thread? thread;

        lock (Sync)
        {
            dispatcher = _dispatcher;
            thread = _thread;
        }

        if (dispatcher != null && !dispatcher.HasShutdownStarted)
        {
            dispatcher.InvokeShutdown();
        }

        thread?.Join(TimeSpan.FromSeconds(10));
    }
}
