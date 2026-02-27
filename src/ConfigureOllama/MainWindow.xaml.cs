using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using Newtonsoft.Json.Linq;

namespace ConfigureOllama
{
    public partial class MainWindow : Window
    {
        private string _serverExePath = "";
        private string _bridgeExePath = "";
        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _serverExePath = FindExe("ZemaxMCP.Server.exe");
            txtServerPath.Text = _serverExePath;

            _bridgeExePath = FindExe("ZemaxMCP.OllamaBridge.exe");
            txtBridgePath.Text = _bridgeExePath;

            UpdateAllStatuses();
        }

        // ── Executable Detection ────────────────────────────────────────

        private string FindExe(string fileName)
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;

            var candidates = new[]
            {
                Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", fileName.Replace(".exe", ""), "bin", "Debug", fileName)),
                Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", fileName.Replace(".exe", ""), "bin", "Release", fileName)),
                Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", fileName.Replace(".exe", ""), "bin", "Debug", "net48", fileName)),
                Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", fileName.Replace(".exe", ""), "bin", "Release", "net48", fileName)),
                Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", "..", fileName.Replace(".exe", ""), "bin", "Debug", "net48", fileName)),
                Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", "..", fileName.Replace(".exe", ""), "bin", "Release", "net48", fileName)),
                Path.GetFullPath(Path.Combine(exeDir, fileName)),
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
            var path = BrowseForExe("Select ZemaxMCP.Server.exe", "ZemaxMCP.Server.exe");
            if (path != null)
            {
                _serverExePath = path;
                txtServerPath.Text = path;
                UpdateAllStatuses();
            }
        }

        private void BrowseBridge_Click(object sender, RoutedEventArgs e)
        {
            var path = BrowseForExe("Select ZemaxMCP.OllamaBridge.exe", "ZemaxMCP.OllamaBridge.exe");
            if (path != null)
            {
                _bridgeExePath = path;
                txtBridgePath.Text = path;
                UpdateAllStatuses();
            }
        }

        private string BrowseForExe(string title, string defaultName)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = title,
                Filter = $"{defaultName}|{defaultName}|All executables|*.exe",
                FileName = defaultName
            };

            if (dialog.ShowDialog() == true)
                return dialog.FileName;

            return null;
        }

        // ── Ollama Detection ────────────────────────────────────────────

        private async void RefreshOllama_Click(object sender, RoutedEventArgs e)
        {
            await RefreshOllamaAsync();
        }

        private async System.Threading.Tasks.Task RefreshOllamaAsync()
        {
            string baseUrl = txtOllamaUrl.Text.TrimEnd('/');

            try
            {
                var response = await _http.GetAsync($"{baseUrl}/api/tags");
                var body = await response.Content.ReadAsStringAsync();
                var obj = JObject.Parse(body);
                var modelsArray = obj["models"] as JArray;

                var models = new List<string>();
                if (modelsArray != null)
                {
                    foreach (var m in modelsArray)
                    {
                        var name = m["name"]?.ToString();
                        if (name != null)
                            models.Add(name);
                    }
                }

                cmbModels.Items.Clear();
                foreach (var model in models)
                    cmbModels.Items.Add(model);

                if (models.Count > 0)
                    cmbModels.SelectedIndex = 0;

                SetIndicator(txtOllamaStatus, "Ollama", true, $"running ({models.Count} models)", "");
            }
            catch (Exception)
            {
                SetIndicator(txtOllamaStatus, "Ollama", false, "", "not reachable - is it running?");
                cmbModels.Items.Clear();
            }
        }

        // ── Pull Model ──────────────────────────────────────────────────

        private void PullModel_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new InputDialog("Pull Ollama Model", "Enter model name (e.g. llama3.1, mistral):", "llama3.1");
            if (dlg.ShowDialog() != true)
                return;

            var modelName = dlg.ResponseText;
            if (string.IsNullOrWhiteSpace(modelName))
                return;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c ollama pull {modelName} & pause",
                    UseShellExecute = true,
                    CreateNoWindow = false
                };

                Process.Start(psi);
                SetStatus($"Pulling {modelName} in a new window. Refresh when done.", true);
            }
            catch (Exception ex)
            {
                SetStatus("Error pulling model: " + ex.Message, false);
            }
        }

        // ── Actions ─────────────────────────────────────────────────────

        private void LaunchBridge_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidatePaths()) return;

            var model = GetSelectedModel();
            if (model == null) return;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/k \"\"{_bridgeExePath}\" \"{_serverExePath}\"\"",
                    UseShellExecute = true,
                    CreateNoWindow = false
                };
                psi.EnvironmentVariables["OLLAMA_MODEL"] = model;
                psi.EnvironmentVariables["OLLAMA_URL"] = txtOllamaUrl.Text.TrimEnd('/');

                Process.Start(psi);
                SetStatus("Bridge launched in a new window.", true);
            }
            catch (Exception ex)
            {
                SetStatus("Error launching bridge: " + ex.Message, false);
            }
        }

        private void CreateShortcut_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidatePaths()) return;

            var model = GetSelectedModel();
            if (model == null) return;

            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string shortcutPath = Path.Combine(desktop, "Zemax Ollama Bridge.lnk");

                // Create a batch file that the shortcut will call
                string batchDir = Path.GetDirectoryName(_bridgeExePath);
                string batchPath = Path.Combine(batchDir, "launch_ollama_bridge.bat");

                string batchContent =
                    $"@echo off\r\n" +
                    $"set OLLAMA_MODEL={model}\r\n" +
                    $"set OLLAMA_URL={txtOllamaUrl.Text.TrimEnd('/')}\r\n" +
                    $"\"{_bridgeExePath}\" \"{_serverExePath}\"\r\n" +
                    $"pause\r\n";

                File.WriteAllText(batchPath, batchContent);

                // Create .lnk shortcut using WScript.Shell COM via reflection
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                var shell = Activator.CreateInstance(shellType);
                var shortcut = shellType.InvokeMember("CreateShortcut",
                    System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                var scType = shortcut.GetType();
                scType.InvokeMember("TargetPath",
                    System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { batchPath });
                scType.InvokeMember("WorkingDirectory",
                    System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { batchDir });
                scType.InvokeMember("Description",
                    System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { "Launch Zemax OpticStudio Ollama Bridge" });
                scType.InvokeMember("Save",
                    System.Reflection.BindingFlags.InvokeMethod, null, shortcut, null);

                SetStatus($"Desktop shortcut created: {shortcutPath}", true);
            }
            catch (Exception ex)
            {
                SetStatus("Error creating shortcut: " + ex.Message, false);
            }
        }

        private void CreateBatchFile_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidatePaths()) return;

            var model = GetSelectedModel();
            if (model == null) return;

            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Save Batch File",
                    Filter = "Batch files|*.bat|All files|*.*",
                    FileName = "launch_ollama_bridge.bat",
                    InitialDirectory = Path.GetDirectoryName(_bridgeExePath)
                };

                if (dialog.ShowDialog() != true) return;

                string batchContent =
                    $"@echo off\r\n" +
                    $"echo === Zemax OpticStudio + Ollama Bridge ===\r\n" +
                    $"echo.\r\n" +
                    $"set OLLAMA_MODEL={model}\r\n" +
                    $"set OLLAMA_URL={txtOllamaUrl.Text.TrimEnd('/')}\r\n" +
                    $"\"{_bridgeExePath}\" \"{_serverExePath}\"\r\n" +
                    $"pause\r\n";

                File.WriteAllText(dialog.FileName, batchContent);
                SetStatus($"Batch file saved: {dialog.FileName}", true);
            }
            catch (Exception ex)
            {
                SetStatus("Error creating batch file: " + ex.Message, false);
            }
        }

        // ── Validation ──────────────────────────────────────────────────

        private bool ValidatePaths()
        {
            if (string.IsNullOrEmpty(_serverExePath) || !File.Exists(_serverExePath))
            {
                SetStatus("MCP Server executable not found. Use Browse to select it.", false);
                return false;
            }
            if (string.IsNullOrEmpty(_bridgeExePath) || !File.Exists(_bridgeExePath))
            {
                SetStatus("Ollama Bridge executable not found. Use Browse to select it.", false);
                return false;
            }
            return true;
        }

        private string GetSelectedModel()
        {
            var model = cmbModels.Text;
            if (string.IsNullOrWhiteSpace(model))
            {
                SetStatus("Please select or enter a model name.", false);
                return null;
            }
            return model;
        }

        // ── Status Updates ──────────────────────────────────────────────

        private async void UpdateAllStatuses()
        {
            bool serverFound = !string.IsNullOrEmpty(_serverExePath) && File.Exists(_serverExePath);
            SetIndicator(txtServerStatus, "ZemaxMCP.Server.exe", serverFound);

            bool bridgeFound = !string.IsNullOrEmpty(_bridgeExePath) && File.Exists(_bridgeExePath);
            SetIndicator(txtBridgeStatus, "ZemaxMCP.OllamaBridge.exe", bridgeFound);

            await RefreshOllamaAsync();
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
