using System;
using System.Threading;
using System.Windows;

namespace ZemaxMCP.Launcher
{
    public partial class App : System.Windows.Application
    {
        private Mutex? _instance;
        protected override void OnStartup(StartupEventArgs e)
        {
            _instance = new Mutex(true, "ZemaxMCP.Launcher.SingleInstance", out var created);
            if (!created)
            {
                System.Windows.MessageBox.Show("Zemax MCP is already running.", "Zemax MCP", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }
            base.OnStartup(e);
        }
        protected override void OnExit(ExitEventArgs e) { _instance?.Dispose(); base.OnExit(e); }
    }
}
