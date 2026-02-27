using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace FixBinaries
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ScanInstallations();
        }

        private void ScanInstallations()
        {
            lstInstallations.Items.Clear();

            var searchPatterns = new[]
            {
                @"C:\Program Files\Ansys Zemax OpticStudio*",
                @"C:\Program Files\Zemax OpticStudio*",
                @"C:\Program Files\OpticStudio*"
            };

            foreach (var pattern in searchPatterns)
            {
                string parentDir = Path.GetDirectoryName(pattern);
                string searchPattern = Path.GetFileName(pattern);

                if (!Directory.Exists(parentDir))
                    continue;

                var dirs = Directory.GetDirectories(parentDir, searchPattern)
                    .OrderByDescending(d => d);

                foreach (var dir in dirs)
                {
                    if (File.Exists(Path.Combine(dir, "ZOSAPI.dll")))
                    {
                        lstInstallations.Items.Add(dir);
                    }
                }
            }

            // Auto-select first if any found
            if (lstInstallations.Items.Count > 0)
            {
                lstInstallations.SelectedIndex = 0;
            }

            UpdateDllIndicators();
            UpdateStatus();
        }

        private void Installations_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (lstInstallations.SelectedItem is string path)
            {
                txtOpticStudioPath.Text = path;
                UpdateDllIndicators();
                UpdateStatus();
            }
        }

        private void BrowseOpticStudio_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select OpticStudio installation folder",
                ShowNewFolderButton = false
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                txtOpticStudioPath.Text = dialog.SelectedPath;
                UpdateDllIndicators();
                UpdateStatus();
            }
        }

        private void UpdateDllIndicators()
        {
            string path = txtOpticStudioPath.Text;

            SetIndicator(txtZosapi, "ZOSAPI.dll",
                !string.IsNullOrEmpty(path) && File.Exists(Path.Combine(path, "ZOSAPI.dll")));

            SetIndicator(txtZosapiInterfaces, "ZOSAPI_Interfaces.dll",
                !string.IsNullOrEmpty(path) && File.Exists(Path.Combine(path, "ZOSAPI_Interfaces.dll")));

            SetIndicator(txtNetHelper, "ZOSAPI_NetHelper.dll",
                !string.IsNullOrEmpty(path) && File.Exists(Path.Combine(path, "ZOSAPI_NetHelper.dll")));

            btnGenerate.IsEnabled = AllDllsFound();
        }

        private static void SetIndicator(System.Windows.Controls.TextBlock indicator, string dllName, bool found)
        {
            if (found)
            {
                indicator.Text = "\u2714 " + dllName + " found";
                indicator.Foreground = new SolidColorBrush(Color.FromRgb(0, 128, 0));
            }
            else
            {
                indicator.Text = "\u2718 " + dllName + " not found";
                indicator.Foreground = new SolidColorBrush(Color.FromRgb(192, 0, 0));
            }
        }

        private bool AllDllsFound()
        {
            string path = txtOpticStudioPath.Text;

            if (string.IsNullOrEmpty(path))
                return false;

            return File.Exists(Path.Combine(path, "ZOSAPI.dll"))
                && File.Exists(Path.Combine(path, "ZOSAPI_Interfaces.dll"))
                && File.Exists(Path.Combine(path, "ZOSAPI_NetHelper.dll"));
        }

        private void UpdateStatus()
        {
            if (AllDllsFound())
            {
                txtStatus.Text = "Ready to generate ZemaxPaths.props";
                txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0, 128, 0));
            }
            else
            {
                txtStatus.Text = "Select a folder containing all three ZOS-API DLLs";
                txtStatus.Foreground = new SolidColorBrush(Colors.Gray);
            }
        }

        private void Generate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string zemaxRoot = txtOpticStudioPath.Text;

                // Find the repository root by looking for Directory.Build.props.
                // ZemaxPaths.props is placed at the repo root so that
                // Directory.Build.props can import it for all projects.
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                string? outputPath = null;

                var candidatePaths = new[]
                {
                    // From exe in src/FixBinaries/bin/Debug/ -> repo root
                    Path.Combine(exeDir, "..", "..", "..", "..", "ZemaxPaths.props"),
                    // From exe in src/FixBinaries/bin/Release/ -> repo root
                    Path.Combine(exeDir, "..", "..", "..", "..", "ZemaxPaths.props"),
                    // From repo root directly
                    Path.Combine(exeDir, "ZemaxPaths.props"),
                    // From current working directory traversal
                    Path.Combine("..", "..", "..", "..", "ZemaxPaths.props"),
                    Path.Combine("..", "..", "ZemaxPaths.props"),
                    "ZemaxPaths.props"
                };

                foreach (var candidate in candidatePaths)
                {
                    string fullCandidate = Path.GetFullPath(candidate);
                    string candidateDir = Path.GetDirectoryName(fullCandidate);

                    // Verify this is the repo root by checking for Directory.Build.props
                    if (Directory.Exists(candidateDir) &&
                        File.Exists(Path.Combine(candidateDir, "Directory.Build.props")))
                    {
                        outputPath = fullCandidate;
                        break;
                    }
                }

                if (outputPath == null)
                {
                    // Fallback: write next to the executable
                    outputPath = Path.Combine(exeDir, "ZemaxPaths.props");
                }

                // Ensure trailing backslash
                if (!zemaxRoot.EndsWith("\\"))
                    zemaxRoot += "\\";

                string propsContent = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<!-- Auto-generated by FixBinaries. Do not edit manually. -->
<Project xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <PropertyGroup>
    <ZEMAX_ROOT>{zemaxRoot}</ZEMAX_ROOT>
  </PropertyGroup>
</Project>
";

                File.WriteAllText(outputPath, propsContent);
                string fullOutputPath = Path.GetFullPath(outputPath);

                txtStatus.Text = "Generated successfully: " + fullOutputPath;
                txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0, 128, 0));
            }
            catch (Exception ex)
            {
                txtStatus.Text = "Error: " + ex.Message;
                txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(192, 0, 0));
            }
        }
    }
}
