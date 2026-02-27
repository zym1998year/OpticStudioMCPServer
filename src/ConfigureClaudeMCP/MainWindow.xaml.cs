using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Newtonsoft.Json.Linq;

namespace ConfigureClaudeMCP
{
    public partial class MainWindow : Window
    {
        private const string ServerName = "zemax-mcp";
        private string _serverExePath = "";

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _serverExePath = FindServerExe();
            txtServerPath.Text = _serverExePath;
            UpdateAllStatuses();
        }

        // ── Server Detection ──────────────────────────────────────────

        private string FindServerExe()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;

            var candidates = new[]
            {
                // From bin/Debug/ -> ZemaxMCP.Server bin/Debug/
                Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", "ZemaxMCP.Server", "bin", "Debug", "ZemaxMCP.Server.exe")),
                // From bin/Release/ -> ZemaxMCP.Server bin/Release/
                Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", "ZemaxMCP.Server", "bin", "Release", "ZemaxMCP.Server.exe")),
                // From bin/Debug/net48/ -> ZemaxMCP.Server bin/Debug/net48/
                Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", "..", "ZemaxMCP.Server", "bin", "Debug", "net48", "ZemaxMCP.Server.exe")),
                // From bin/Release/net48/ -> ZemaxMCP.Server bin/Release/net48/
                Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", "..", "ZemaxMCP.Server", "bin", "Release", "net48", "ZemaxMCP.Server.exe")),
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return "";
        }

        private void BrowseServer_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select ZemaxMCP.Server.exe",
                Filter = "ZemaxMCP Server|ZemaxMCP.Server.exe|All executables|*.exe",
                FileName = "ZemaxMCP.Server.exe"
            };

            if (dialog.ShowDialog() == true)
            {
                _serverExePath = dialog.FileName;
                txtServerPath.Text = _serverExePath;
                UpdateAllStatuses();
            }
        }

        // ── Claude Desktop Configuration ──────────────────────────────

        private string GetClaudeDesktopConfigPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "Claude", "claude_desktop_config.json");
        }

        private bool IsClaudeDesktopConfigured()
        {
            try
            {
                string configPath = GetClaudeDesktopConfigPath();
                if (!File.Exists(configPath))
                    return false;

                string json = File.ReadAllText(configPath);
                var root = JObject.Parse(json);
                var servers = root["mcpServers"] as JObject;
                return servers != null && servers[ServerName] != null;
            }
            catch
            {
                return false;
            }
        }

        private void ConfigureClaudeDesktop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_serverExePath) || !File.Exists(_serverExePath))
                {
                    SetStatus("Server executable not found. Use Browse to select it.", false);
                    return;
                }

                string configPath = GetClaudeDesktopConfigPath();
                string configDir = Path.GetDirectoryName(configPath);

                if (!Directory.Exists(configDir))
                    Directory.CreateDirectory(configDir);

                JObject root;
                if (File.Exists(configPath))
                {
                    string existing = File.ReadAllText(configPath);
                    root = JObject.Parse(existing);
                }
                else
                {
                    root = new JObject();
                }

                if (root["mcpServers"] == null)
                    root["mcpServers"] = new JObject();

                var servers = (JObject)root["mcpServers"]!;
                servers![ServerName] = new JObject
                {
                    ["command"] = _serverExePath,
                    ["args"] = new JArray()
                };

                File.WriteAllText(configPath, root.ToString(Newtonsoft.Json.Formatting.Indented));

                SetStatus("Claude Desktop configured successfully.", true);
                UpdateAllStatuses();
            }
            catch (Exception ex)
            {
                SetStatus("Error configuring Claude Desktop: " + ex.Message, false);
            }
        }

        private void RemoveClaudeDesktop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string configPath = GetClaudeDesktopConfigPath();
                if (!File.Exists(configPath))
                {
                    SetStatus("Claude Desktop config file not found.", false);
                    return;
                }

                string json = File.ReadAllText(configPath);
                var root = JObject.Parse(json);
                var servers = root["mcpServers"] as JObject;

                if (servers != null && servers[ServerName] != null)
                {
                    servers.Remove(ServerName);
                    File.WriteAllText(configPath, root.ToString(Newtonsoft.Json.Formatting.Indented));
                    SetStatus("Removed zemax-mcp from Claude Desktop.", true);
                }
                else
                {
                    SetStatus("zemax-mcp was not configured in Claude Desktop.", false);
                }

                UpdateAllStatuses();
            }
            catch (Exception ex)
            {
                SetStatus("Error removing from Claude Desktop: " + ex.Message, false);
            }
        }

        // ── Claude Code Configuration ─────────────────────────────────

        private bool IsClaudeCodeConfigured()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "claude",
                    Arguments = "mcp list",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var proc = Process.Start(psi))
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(5000);
                    return output.Contains(ServerName);
                }
            }
            catch
            {
                return false;
            }
        }

        private void ConfigureClaudeCode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_serverExePath) || !File.Exists(_serverExePath))
                {
                    SetStatus("Server executable not found. Use Browse to select it.", false);
                    return;
                }

                string args = $"mcp add --transport stdio --scope user {ServerName} -- \"{_serverExePath}\"";

                var psi = new ProcessStartInfo
                {
                    FileName = "claude",
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var proc = Process.Start(psi))
                {
                    string stdout = proc.StandardOutput.ReadToEnd();
                    string stderr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit(10000);

                    if (proc.ExitCode == 0)
                    {
                        SetStatus("Claude Code configured successfully.", true);
                    }
                    else
                    {
                        string msg = string.IsNullOrEmpty(stderr) ? stdout : stderr;
                        SetStatus("Claude Code error: " + msg.Trim(), false);
                    }
                }

                UpdateAllStatuses();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // claude CLI not found in PATH
                string cmd = $"claude mcp add --transport stdio --scope user {ServerName} -- \"{_serverExePath}\"";
                Clipboard.SetText(cmd);
                SetStatus("'claude' not found in PATH. Command copied to clipboard for manual use.", false);
            }
            catch (Exception ex)
            {
                SetStatus("Error configuring Claude Code: " + ex.Message, false);
            }
        }

        private void RemoveClaudeCode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "claude",
                    Arguments = $"mcp remove --scope user {ServerName}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var proc = Process.Start(psi))
                {
                    string stdout = proc.StandardOutput.ReadToEnd();
                    string stderr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit(10000);

                    if (proc.ExitCode == 0)
                    {
                        SetStatus("Removed zemax-mcp from Claude Code.", true);
                    }
                    else
                    {
                        string msg = string.IsNullOrEmpty(stderr) ? stdout : stderr;
                        SetStatus("Claude Code error: " + msg.Trim(), false);
                    }
                }

                UpdateAllStatuses();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                SetStatus("'claude' not found in PATH.", false);
            }
            catch (Exception ex)
            {
                SetStatus("Error removing from Claude Code: " + ex.Message, false);
            }
        }

        // ── Status Updates ────────────────────────────────────────────

        private void UpdateAllStatuses()
        {
            // Server exe
            bool serverFound = !string.IsNullOrEmpty(_serverExePath) && File.Exists(_serverExePath);
            SetIndicator(txtServerStatus, "ZemaxMCP.Server.exe", serverFound);

            // Claude Desktop
            bool desktopConfigured = IsClaudeDesktopConfigured();
            SetIndicator(txtDesktopStatus, "Claude Desktop", desktopConfigured, "configured", "not configured");

            // Claude Code
            bool codeConfigured = IsClaudeCodeConfigured();
            SetIndicator(txtCodeStatus, "Claude Code", codeConfigured, "configured", "not configured");
        }

        private static void SetIndicator(System.Windows.Controls.TextBlock indicator, string label, bool ok,
            string trueText = "found", string falseText = "not found")
        {
            if (ok)
            {
                indicator.Text = "\u2714 " + label + " " + trueText;
                indicator.Foreground = new SolidColorBrush(Color.FromRgb(0, 128, 0));
            }
            else
            {
                indicator.Text = "\u2718 " + label + " " + falseText;
                indicator.Foreground = new SolidColorBrush(Color.FromRgb(192, 0, 0));
            }
        }

        private void SetStatus(string message, bool success)
        {
            txtStatus.Text = message;
            txtStatus.Foreground = success
                ? new SolidColorBrush(Color.FromRgb(0, 128, 0))
                : new SolidColorBrush(Color.FromRgb(192, 0, 0));
        }
    }
}
