using BatteryTray.Core;
using BatteryTray.Ui;

namespace BatteryTray;

internal static class Program
{
    private const string MutexName = @"Local\" + AppInfo.Id + ".SingleInstance";
    private const string ShowEventName = @"Local\" + AppInfo.Id + ".ShowPanel";

    [STAThread]
    private static void Main(string[] args)
    {
        // Velopack primeiro: o instalador e o desinstalador chamam o exe com
        // argumentos proprios, e este Run trata e sai antes de abrir qualquer coisa.
        // So o main liga o autostart ao instalar; o teste abre quando voce quiser.
        Velopack.VelopackApp.Build()
            .OnAfterInstallFastCallback(_ => { if (!AppInfo.IsTest) Autostart.Set(true); })
            .OnBeforeUninstallFastCallback(_ =>
            {
                Autostart.Set(false);
                CloseOtherInstances(); // o desinstalador nao fecha o app aberto: o exe em uso ficava para tras
            })
            .Run();

        bool startedByWindows = args.Any(a => a.Equals("--startup", StringComparison.OrdinalIgnoreCase));

        using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool isFirstInstance);
        // "Reabrir agora" (troca de idioma): a instancia velha ainda esta saindo; espera ela soltar.
        if (!isFirstInstance && args.Contains("--restart"))
        {
            try { isFirstInstance = mutex.WaitOne(TimeSpan.FromSeconds(10)); }
            catch (AbandonedMutexException) { isFirstInstance = true; }
        }
        if (!isFirstInstance)
        {
            // Ja existe uma instancia: pede para ela abrir o painel e sai.
            // Aberta pelo Windows (autostart duplicado), so sai: no login nada abre.
            if (startedByWindows) return;
            try
            {
                if (EventWaitHandle.TryOpenExisting(ShowEventName, out var existing))
                {
                    using (existing) existing.Set();
                }
            }
            catch { }
            return;
        }

        AppPaths.EnsureDir();

        var app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) =>
        {
            Log.Error("DispatcherUnhandledException", e.Exception);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Log.Error("UnhandledException", ex);
        };

        var settings = Settings.Load();
        L.Set(settings.Language); // antes de qualquer texto da interface
        var cache = ReadingCache.Load();
        var registry = ProviderRegistry.CreateDefault();
        var service = new ReadingService(registry, cache, settings);

        // --demo [variante]: painel com os dados do design, sem ler hardware.
        int demoAt = Array.FindIndex(args, a => a.Equals("--demo", StringComparison.OrdinalIgnoreCase));
        string? demo = demoAt < 0 ? null : demoAt + 1 < args.Length ? args[demoAt + 1].ToLowerInvariant() : "";

        using var controller = new TrayController(settings, service, startedByWindows, demo);

        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        using var registration = new WaitHandleRegistration(showEvent, controller);

        app.Run();

        GC.KeepAlive(mutex);
    }

    private static void CloseOtherInstances()
    {
        using var self = System.Diagnostics.Process.GetCurrentProcess();
        foreach (var p in System.Diagnostics.Process.GetProcessesByName(self.ProcessName))
        {
            using (p)
            {
                if (p.Id == self.Id) continue;
                try { p.Kill(); p.WaitForExit(5000); } catch (Exception ex) { Log.Error("Desinstalar", ex); }
            }
        }
    }

    /// <summary>
    /// Escuta o pedido "abrir painel" vindo de uma segunda instancia. Isto nao
    /// e' polling de dispositivo: e' so um evento do Windows entre processos.
    /// </summary>
    private sealed class WaitHandleRegistration : IDisposable
    {
        private readonly RegisteredWaitHandle _handle;

        public WaitHandleRegistration(WaitHandle waitHandle, TrayController context)
        {
            _handle = ThreadPool.RegisterWaitForSingleObject(
                waitHandle,
                (_, _) =>
                {
                    try { context.ShowPanelFromAnyThread(); }
                    catch (Exception ex) { Log.Error("ShowPanelRequest", ex); }
                },
                state: null,
                millisecondsTimeOutInterval: -1,
                executeOnlyOnce: false);
        }

        public void Dispose() => _handle.Unregister(null);
    }
}
