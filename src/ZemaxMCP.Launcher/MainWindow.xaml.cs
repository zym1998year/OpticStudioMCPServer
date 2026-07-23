using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using Newtonsoft.Json.Linq;

namespace ZemaxMCP.Launcher;

public partial class MainWindow : Window
{
    private Process? _bridge;
    public MainWindow() => InitializeComponent();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var installs = ZemaxInstallation.FindAll();
        ZemaxVersions.ItemsSource = installs;
        ZemaxVersions.SelectedIndex = installs.Count > 0 ? 0 : -1;
        Report(installs.Count == 0 ? "No OpticStudio installation detected. Install OpticStudio or select a supported installation before starting." : "Starting local MCP endpoint automatically…");
        RefreshEndpoint();
        if (installs.Count > 0) StartBridge();
    }
    private void ZemaxVersions_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => RefreshEndpoint();
    private string HostName => ShareOnLan.IsChecked == true ? "0.0.0.0" : "127.0.0.1";
    private void RefreshEndpoint() => Endpoint.Text = "MCP endpoint: " + Url;
    private ZemaxInstallation? Installation => ZemaxVersions.SelectedItem as ZemaxInstallation;
    private string Url => "http://" + (ShareOnLan.IsChecked == true ? GetLanAddress() : "127.0.0.1") + ":" + Port.Text + "/mcp";
    private string McpUrl => Uri.TryCreate(RemoteEndpoint.Text, UriKind.Absolute, out var remote) &&
        (remote.Scheme == Uri.UriSchemeHttp || remote.Scheme == Uri.UriSchemeHttps) ? remote.ToString().TrimEnd('/') : Url;

    private void ShareOnLan_Changed(object sender, RoutedEventArgs e) { RefreshEndpoint(); StopBridge(); if (Installation != null) StartBridge(); }

    private void Start_Click(object sender, RoutedEventArgs e) => StartBridge();
    private void StartBridge()
    {
        if (Installation == null) { Report("Choose a detected OpticStudio installation first."); return; }
        if (!int.TryParse(Port.Text, out var port) || port < 1 || port > 65535)
        {
            Report("Port must be a number from 1 to 65535.");
            return;
        }
        StopBridge();
        var bridge = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ZemaxMCP.HttpBridge.exe");
        var server = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ZemaxMCP.Server.exe");
        if (!File.Exists(bridge) || !File.Exists(server)) { Report("Release package is incomplete: ZemaxMCP.HttpBridge.exe and ZemaxMCP.Server.exe must be beside this launcher."); return; }
        var firewallReady = ShareOnLan.IsChecked != true || FirewallRule.TryEnsure(port);
        _bridge = Process.Start(new ProcessStartInfo(bridge, $"--server \"{server}\" --zemax-root \"{Installation.Root}\" --host {HostName} --port {port}") { UseShellExecute = false, CreateNoWindow = true });
        Report("HTTP MCP started: " + Url + Environment.NewLine + "Logs: " + Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs") +
            (firewallReady ? "" : Environment.NewLine + "Firewall permission was not granted; another PC may not reach this endpoint."));
    }
    private void Stop_Click(object sender, RoutedEventArgs e) { StopBridge(); Report("HTTP MCP stopped."); }
    private void StopBridge() { try { if (_bridge != null && !_bridge.HasExited) _bridge.Kill(); } catch { } _bridge = null; }
    private void CopyEndpoint_Click(object sender, RoutedEventArgs e) { Clipboard.SetText(McpUrl); Report("MCP address copied: " + McpUrl); }
    private void ConfigureDetected_Click(object sender, RoutedEventArgs e)
    {
        var configured = new List<string>();
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"))) { Configurator.ConfigureCodex(McpUrl); configured.Add("Codex"); }
        if (Directory.Exists(Path.Combine(appData, "Claude"))) { Configurator.ConfigureJson(Path.Combine(appData, "Claude", "claude_desktop_config.json"), "mcpServers", McpUrl); configured.Add("Claude Desktop"); }
        if (Directory.Exists(Path.Combine(appData, "Cursor"))) { Configurator.ConfigureJson(Path.Combine(appData, "Cursor", "User", "mcp.json"), "mcpServers", McpUrl); configured.Add("Cursor"); }
        Report(configured.Count == 0 ? "No supported AI client was detected. Use the individual configuration buttons after installing one." : "Configured: " + string.Join(", ", configured) + ". Restart the client to connect.");
    }
    private void Codex_Click(object sender, RoutedEventArgs e) { Configurator.ConfigureCodex(McpUrl); Report("Codex configured for " + McpUrl); }
    private void Claude_Click(object sender, RoutedEventArgs e) { Configurator.ConfigureJson(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", "claude_desktop_config.json"), "mcpServers", McpUrl); Report("Claude Desktop configured for " + McpUrl); }
    private void Cursor_Click(object sender, RoutedEventArgs e) { Configurator.ConfigureJson(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cursor", "User", "mcp.json"), "mcpServers", McpUrl); Report("Cursor configured for " + McpUrl); }
    private void Update_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using (var client = new WebClient())
            {
                client.Headers.Add("User-Agent", "ZemaxMCP-Launcher");
                var release = JObject.Parse(client.DownloadString("https://api.github.com/repos/joeyijun/OpticStudioMCPServer/releases/latest"));
                var asset = release["assets"]?.FirstOrDefault(x => x["name"]?.ToString().Equals("ZemaxMCP-win-x64.zip", StringComparison.OrdinalIgnoreCase) == true);
                if (asset == null) { Report("Latest release " + release["tag_name"] + " has no Windows package yet."); return; }
                var staging = Path.Combine(Path.GetTempPath(), "ZemaxMCP-update-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(staging);
                var zip = Path.Combine(staging, "release.zip");
                Report("Downloading " + release["tag_name"] + "…");
                client.DownloadFile(asset["browser_download_url"]!.ToString(), zip);
                ZipFile.ExtractToDirectory(zip, staging);
                var install = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
                var script = Path.Combine(staging, "apply-update.cmd");
                File.WriteAllText(script, "@echo off\r\nping 127.0.0.1 -n 4 > nul\r\nrobocopy \"" + staging + "\" \"" + install + "\" /E /MOV /XF release.zip apply-update.cmd > nul\r\nstart \"\" \"" + Path.Combine(install, "Start-Zemax-MCP.exe") + "\"\r\n");
                Process.Start(new ProcessStartInfo("cmd.exe", "/c \"" + script + "\"") { CreateNoWindow = true, UseShellExecute = false });
                Report("Update downloaded. Restarting with " + release["tag_name"] + "…");
                Application.Current.Shutdown();
            }
        }
        catch (Exception ex) { Report("Could not check GitHub releases: " + ex.Message); }
    }
    private void Report(string text) => Status.Text = DateTime.Now.ToString("HH:mm:ss") + "  " + text;
    private static string GetLanAddress() => Dns.GetHostEntry(Dns.GetHostName()).AddressList.FirstOrDefault(x => x.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(x))?.ToString() ?? "127.0.0.1";
    protected override void OnClosed(EventArgs e) { StopBridge(); base.OnClosed(e); }
}

internal static class FirewallRule
{
    public static bool TryEnsure(int port)
    {
        try
        {
            var rule = "Zemax MCP HTTP " + port;
            var user = Environment.UserDomainName + "\\" + Environment.UserName;
            var firewall = "netsh advfirewall firewall add rule name=\"" + rule + "\" dir=in action=allow protocol=TCP localport=" + port + " profile=private";
            var urlAcl = "netsh http add urlacl url=http://+:" + port + "/mcp/ user=\"" + user + "\"";
            // cmd.exe lets one UAC confirmation configure both HTTP.SYS and the
            // private-network firewall rule. Existing URL ACLs are harmless.
            var arguments = "/c \"" + firewall + " & " + urlAcl + " & exit /b 0\"";
            using (var process = Process.Start(new ProcessStartInfo("cmd.exe", arguments) { Verb = "runas", UseShellExecute = true }))
            {
                process.WaitForExit();
                return process.ExitCode == 0;
            }
        }
        catch { return false; }
    }
}

public sealed class ZemaxInstallation
{
    public string Root { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public static List<ZemaxInstallation> FindAll()
    {
        var roots = new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) }
            .Where(Directory.Exists).SelectMany(p => Directory.GetDirectories(p, "*Zemax OpticStudio*"));
        return roots.Where(p => File.Exists(Path.Combine(p, "ZOSAPI.dll")) && File.Exists(Path.Combine(p, "ZOSAPI_NetHelper.dll")))
            .Select(p => new ZemaxInstallation { Root = p, DisplayName = Path.GetFileName(p) + " — " + p }).OrderByDescending(x => x.DisplayName).ToList();
    }
}

internal static class Configurator
{
    public static void ConfigureJson(string path, string property, string url)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var root = File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : new JObject();
        var servers = root[property] as JObject ?? new JObject(); root[property] = servers;
        servers["zemax-mcp"] = new JObject { ["type"] = "http", ["url"] = url };
        File.WriteAllText(path, root.ToString());
    }
    public static void ConfigureCodex(string url)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "config.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var content = File.Exists(path) ? File.ReadAllText(path) : "";
        var block = "[mcp_servers.zemax]\r\nurl = \"" + url + "\"\r\n";
        content = Regex.Replace(content, @"(?ms)^\[mcp_servers\.zemax\].*?(?=^\[|\z)", block);
        if (!content.Contains("[mcp_servers.zemax]")) content += (content.EndsWith("\n") || content.Length == 0 ? "" : "\r\n") + block;
        File.WriteAllText(path, content);
    }
}
