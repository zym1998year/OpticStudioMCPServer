using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace ZemaxMCP.Installer;

public partial class MainWindow : Window
{
    public MainWindow() { InitializeComponent(); Status.Text = "Destination: " + InstallDirectory; }
    private static string InstallDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZemaxMCP");

    private void Install_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var source = AppDomain.CurrentDomain.BaseDirectory;
            var target = InstallDirectory;
            Directory.CreateDirectory(target);
            var alreadyInstalled = string.Equals(
                Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
            if (!alreadyInstalled)
            {
                Status.Text = Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any()
                    ? "Updating the existing installation. Stopping the old launcher…"
                    : "Installing Zemax MCP…";
                StopExistingProcesses();
                foreach (var file in Directory.GetFiles(source))
                {
                    var name = Path.GetFileName(file);
                    if (!name.StartsWith("Install", StringComparison.OrdinalIgnoreCase)) File.Copy(file, Path.Combine(target, name), true);
                }
                foreach (var directory in Directory.GetDirectories(source)) CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
            }
            var launcher = Path.Combine(target, "Start-Zemax-MCP.exe");
            if (!File.Exists(launcher)) throw new FileNotFoundException("The release package is missing Start-Zemax-MCP.exe.");
            CreateDesktopShortcut(launcher);
            Status.Text = "Installed successfully. A desktop shortcut was created. Starting Zemax MCP…";
            Process.Start(launcher);
            Close();
        }
        catch (Exception ex) { Status.Text = "Installation failed: " + ex.Message; }
    }
    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
        foreach (var folder in Directory.GetDirectories(source)) CopyDirectory(folder, Path.Combine(target, Path.GetFileName(folder)));
    }
    private static void StopExistingProcesses()
    {
        foreach (var name in new[] { "Start-Zemax-MCP", "ZemaxMCP.HttpBridge" })
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                try
                {
                    string executable;
                    try { executable = process.MainModule?.FileName ?? string.Empty; }
                    catch { continue; }
                    if (!executable.StartsWith(InstallDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                    process.Kill();
                    if (!process.WaitForExit(5000)) throw new InvalidOperationException($"Could not stop {name}. Please click Exit from its tray icon, then retry.");
                }
                finally { process.Dispose(); }
            }
        }
    }
    private static void CreateDesktopShortcut(string target)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        dynamic shortcut = shell.CreateShortcut(Path.Combine(desktop, "Start Zemax MCP.lnk"));
        shortcut.TargetPath = target;
        shortcut.WorkingDirectory = Path.GetDirectoryName(target);
        shortcut.Description = "Start Zemax MCP HTTP bridge";
        shortcut.Save();
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
