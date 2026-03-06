using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;
using System.Xml.Linq;
using IOPath = System.IO.Path;

namespace SmilezStrap
{
    public partial class ProgressWindow : Window
    {
        private readonly HttpClient httpClient = new HttpClient();
        private CancellationTokenSource cancellationTokenSource = null!;
        private bool isCompleted = false;
        private bool isStudio = false;
        private bool isFFlag = false;
        private Config? config;
        private string? protocolUrl = null;
        private Process? robloxProcess = null;
        private System.Timers.Timer? processMonitorTimer;
        
        private const string ROBLOX_DOWNLOAD_URL = "https://www.roblox.com/download/client?os=win";
        private const string STUDIO_DOWNLOAD_URL = "https://setup.rbxcdn.com/RobloxStudioInstaller.exe";

        public ProgressWindow(bool launchStudio = false, Config? appConfig = null, string? gameUrl = null, bool launchFFlag = false)
        {
            InitializeComponent();
            
            // Apply open animation
            var storyboard = (Storyboard)FindResource("WindowOpenAnimation");
            storyboard.Begin(this);
            
            isStudio = launchStudio;
            isFFlag = launchFFlag;
            config = appConfig;
            protocolUrl = gameUrl;
            cancellationTokenSource = new CancellationTokenSource();
            
            httpClient.DefaultRequestHeaders.Add("User-Agent", "SmilezStrap");
            
            if (isFFlag)
            {
                TitleText.Text = "FFlag Injector";
                TitleIcon.Text = "🚩";
            }
            else if (isStudio)
            {
                TitleText.Text = "Launching Studio";
                TitleIcon.Text = "🛠️";
            }
            else
            {
                TitleText.Text = "Launching Roblox";
                TitleIcon.Text = "🎮";
            }
            
            Loaded += async (s, e) => await StartLaunchProcess();
            Closed += (s, e) => processMonitorTimer?.Stop();
        }

        // Public methods for FFlag Injector to control progress
        public void SetDownloadInfo(string status, string detail)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = status;
                DetailText.Text = detail;
                TitleText.Text = "FFlag Injector";
                TitleIcon.Text = "🚩";
            });
        }

        public void SetProgress(int percent)
        {
            Dispatcher.Invoke(() =>
            {
                PercentText.Text = $"{percent}%";
                var targetWidth = 390.0 * (percent / 100.0);
                var animation = new DoubleAnimation
                {
                    To = targetWidth,
                    Duration = TimeSpan.FromMilliseconds(200),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                ProgressBarFill.BeginAnimation(WidthProperty, animation);
                
                if (percent < 100 && percent > 0)
                {
                    int secondsRemaining = (int)((100 - percent) * 0.5);
                    TimeEstimateText.Text = $"~{secondsRemaining}s remaining";
                }
                else
                {
                    TimeEstimateText.Text = "";
                }
            });
        }

        private void UpdateStatus(string status, string detail = "", string stage = "")
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = status;
                DetailText.Text = detail;
                if (!string.IsNullOrEmpty(stage))
                    StageText.Text = stage;
            });
        }

        private void ShowCompletion(bool success, string message = "")
        {
            Dispatcher.Invoke(() =>
            {
                isCompleted = true;
                CancelButton.Visibility = Visibility.Collapsed;
                CloseButton.Visibility = Visibility.Visible;
                GlowBorder.BeginAnimation(OpacityProperty, null);
                
                if (success)
                {
                    if (isFFlag)
                    {
                        StatusText.Text = "FFlag Injector launched successfully!";
                        TitleIcon.Text = "✅";
                    }
                    else if (isStudio)
                    {
                        StatusText.Text = "Studio launched successfully!";
                        TitleIcon.Text = "✅";
                    }
                    else
                    {
                        StatusText.Text = "Roblox launched successfully!";
                        TitleIcon.Text = "✅";
                    }
                    SetProgress(100, "Complete");
                    DetailText.Text = message;
                }
                else
                {
                    StatusText.Text = "Error occurred";
                    TitleIcon.Text = "❌";
                    DetailText.Text = message;
                }
            });
        }

        private void SetProgress(int percent, string stage = "")
        {
            Dispatcher.Invoke(() =>
            {
                PercentText.Text = $"{percent}%";
                if (!string.IsNullOrEmpty(stage))
                    StageText.Text = stage;
                
                var targetWidth = 390.0 * (percent / 100.0);
                
                var animation = new DoubleAnimation
                {
                    To = targetWidth,
                    Duration = TimeSpan.FromMilliseconds(200),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                ProgressBarFill.BeginAnimation(WidthProperty, animation);
                
                if (percent < 100 && percent > 0)
                {
                    int secondsRemaining = (int)((100 - percent) * 0.5);
                    TimeEstimateText.Text = $"~{secondsRemaining}s remaining";
                }
                else
                {
                    TimeEstimateText.Text = "";
                }
            });
        }

        private async Task StartLaunchProcess()
        {
            try
            {
                if (isFFlag)
                {
                    // For FFlag, we don't actually launch anything here
                    // The MainWindow handles the download and launch
                    // Just keep the window open until MainWindow closes it
                    await Task.Delay(-1, cancellationTokenSource.Token);
                }
                else if (isStudio)
                    await LaunchStudio();
                else
                    await LaunchRoblox();
            }
            catch (OperationCanceledException)
            {
                Dispatcher.Invoke(() =>
                {
                    UpdateStatus("Cancelled", "Process was cancelled by user");
                    CancelButton.Visibility = Visibility.Collapsed;
                    CloseButton.Visibility = Visibility.Visible;
                    TitleIcon.Text = "⏹️";
                });
            }
            catch (Exception ex)
            {
                ShowCompletion(false, ex.Message);
            }
        }

        private async Task LaunchRoblox()
        {
            var token = cancellationTokenSource.Token;
            
            UpdateStatus("Checking Roblox status...", "Verifying installation", "Checking");
            SetProgress(5);
            await Task.Delay(500, token);
            token.ThrowIfCancellationRequested();

            // Roblox Player will launch regardless of existing instances

            UpdateStatus("Checking for updates...", "Getting latest version", "Version check");
            SetProgress(10);
            token.ThrowIfCancellationRequested();

            string? installedVersion = GetInstalledRobloxVersion();
            string latestVersion = await GetLatestRobloxVersion();
            bool needsUpdate = installedVersion == null || installedVersion != latestVersion;

            if (needsUpdate)
            {
                UpdateStatus("Update available", $"Downloading Roblox {latestVersion}", "Downloading");
                SetProgress(15, "Downloading");
                
                string tempPath = IOPath.Combine(IOPath.GetTempPath(), "SmilezStrap", "RobloxPlayerInstaller.exe");
                Directory.CreateDirectory(IOPath.GetDirectoryName(tempPath)!);
                
                var downloadProgress = new Progress<int>(p =>
                {
                    int totalProgress = 15 + (p * 35 / 100);
                    UpdateStatus($"Downloading Roblox... {p}%", $"Version {latestVersion}", "Downloading");
                    SetProgress(totalProgress);
                });
                
                await DownloadFile(ROBLOX_DOWNLOAD_URL, tempPath, downloadProgress, token);
                token.ThrowIfCancellationRequested();

                UpdateStatus("Installing Roblox...", "Please wait while Roblox is being installed", "Installing");
                SetProgress(55);
                
                var installTask = RunInstallerSilently(tempPath);
                
                int installProgress = 55;
                while (!installTask.IsCompleted && installProgress < 75)
                {
                    token.ThrowIfCancellationRequested();
                    await Task.Delay(500, token);
                    installProgress += 2;
                    SetProgress(installProgress, "Installing");
                }
                
                await installTask;
                token.ThrowIfCancellationRequested();
               
                SetProgress(78, "Finalizing");
                await Task.Delay(1000, token);
                
                try { File.Delete(tempPath); } catch { }
                
                installedVersion = GetInstalledRobloxVersion();
                if (installedVersion == null)
                    throw new Exception("Installation failed to register.");
            }
            else
            {
                UpdateStatus("Roblox is up to date", $"Version {installedVersion}", "Ready");
                SetProgress(40);
                await Task.Delay(500, token);
            }
            
            token.ThrowIfCancellationRequested();
           
            UpdateStatus("Applying settings...", "Configuring preferences", "Configuring");
            SetProgress(needsUpdate ? 80 : 55);
            await ApplyAllSettings();
            await Task.Delay(500, token);
           
            UpdateStatus("Launching Roblox...", "Starting game client", "Launching");
            SetProgress(needsUpdate ? 90 : 70);
            await Task.Delay(400, token);
           
            if (needsUpdate)
            {
                await Task.Delay(500, token);
                RemoveDesktopShortcuts();
            }
           
            string exePath = IOPath.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Roblox", "Versions", installedVersion!, "RobloxPlayerBeta.exe");
                
            if (!File.Exists(exePath))
                throw new Exception("Roblox executable not found.");
                
            ProcessStartInfo startInfo;
            if (!string.IsNullOrEmpty(protocolUrl))
            {
                startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = $"\"{protocolUrl}\"",
                    UseShellExecute = false
                };
            }
            else
            {
                startInfo = new ProcessStartInfo(exePath) { UseShellExecute = true };
            }
            
            robloxProcess = Process.Start(startInfo);
            
            SetProgress(95, "Running");
            UpdateStatus("Roblox is starting...", "Waiting for game window", "Running");
            
            await MonitorRobloxProcess(robloxProcess);
           
            SetProgress(100, "Complete");
            await Task.Delay(800, token);
            RemoveDesktopShortcuts();
            ShowCompletion(true, "Roblox is now running");
            await Task.Delay(1500);
            this.Close();
        }

        private async Task LaunchStudio()
        {
            var token = cancellationTokenSource.Token;
            
            UpdateStatus("Checking Studio status...", "Verifying installation", "Checking");
            SetProgress(5);
            await Task.Delay(500, token);
            token.ThrowIfCancellationRequested();

            // Studio still checks for existing processes
            var existingProcesses = Process.GetProcessesByName("RobloxStudioBeta");
            if (existingProcesses.Length > 0)
            {
                UpdateStatus("Studio is already running...", "Activating existing window", "Running");
                SetProgress(30);
                await Task.Delay(300, token);
                
                foreach (var proc in existingProcesses)
                {
                    if (proc.MainWindowHandle != IntPtr.Zero)
                    {
                        SetForegroundWindow(proc.MainWindowHandle);
                        break;
                    }
                }
                
                SetProgress(100, "Already Running");
                await Task.Delay(800);
                ShowCompletion(true, "Studio was already running");
                await Task.Delay(1500);
                this.Close();
                return;
            }

            UpdateStatus("Checking for updates...", "Getting latest version", "Version check");
            SetProgress(10);
            token.ThrowIfCancellationRequested();

            string? installedVersion = GetInstalledStudioVersion();
            string latestVersion = await GetLatestStudioVersion();
            bool needsUpdate = installedVersion == null || installedVersion != latestVersion;

            if (needsUpdate)
            {
                UpdateStatus("Update available", $"Downloading Studio {latestVersion}", "Downloading");
                SetProgress(15, "Downloading");
                
                string tempPath = IOPath.Combine(IOPath.GetTempPath(), "SmilezStrap", "RobloxStudioInstaller.exe");
                Directory.CreateDirectory(IOPath.GetDirectoryName(tempPath)!);
                
                var downloadProgress = new Progress<int>(p =>
                {
                    int totalProgress = 15 + (p * 45 / 100);
                    UpdateStatus($"Downloading Studio... {p}%", $"Version {latestVersion}", "Downloading");
                    SetProgress(totalProgress);
                });
                
                await DownloadFile(STUDIO_DOWNLOAD_URL, tempPath, downloadProgress, token);
                token.ThrowIfCancellationRequested();

                UpdateStatus("Installing Studio...", "Please wait while Studio is being installed", "Installing");
                SetProgress(65);
                
                var installTask = RunInstallerSilently(tempPath);
                
                int installProgress = 65;
                while (!installTask.IsCompleted && installProgress < 85)
                {
                    token.ThrowIfCancellationRequested();
                    await Task.Delay(500, token);
                    installProgress += 2;
                    SetProgress(installProgress, "Installing");
                }
                
                await installTask;
                token.ThrowIfCancellationRequested();
               
                SetProgress(88, "Finalizing");
                await Task.Delay(1000, token);
                
                try { File.Delete(tempPath); } catch { }
                
                for (int i = 0; i < 5; i++)
                {
                    token.ThrowIfCancellationRequested();
                    installedVersion = GetInstalledStudioVersion();
                    if (installedVersion != null) break;
                    await Task.Delay(1000, token);
                }
                
                if (installedVersion == null)
                    throw new Exception("Studio installation completed but version not detected.");
            }
            else
            {
                UpdateStatus("Studio is up to date", $"Version {installedVersion}", "Ready");
                SetProgress(50);
                await Task.Delay(500, token);
            }
            
            token.ThrowIfCancellationRequested();

            UpdateStatus("Launching Studio...", "Starting application", "Launching");
            SetProgress(needsUpdate ? 92 : 70);
            await Task.Delay(500, token);
            
            if (needsUpdate)
            {
                await Task.Delay(500, token);
                RemoveDesktopShortcuts();
            }
            
            string exePath = IOPath.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Roblox", "Versions", installedVersion!, "RobloxStudioBeta.exe");
                
            if (!File.Exists(exePath))
                throw new Exception("Studio executable not found.");
                
            if (!string.IsNullOrEmpty(protocolUrl))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = $"\"{protocolUrl}\"",
                    UseShellExecute = false
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
            }
           
            SetProgress(100, "Complete");
            await Task.Delay(800, token);
            RemoveDesktopShortcuts();
            ShowCompletion(true, "Studio is now running");
            await Task.Delay(1500);
            this.Close();
        }

        private async Task MonitorRobloxProcess(Process? process)
        {
            if (process == null) return;
            
            var tcs = new TaskCompletionSource<bool>();
            
            processMonitorTimer = new System.Timers.Timer(1000);
            processMonitorTimer.Elapsed += (sender, e) =>
            {
                try
                {
                    if (process.HasExited)
                    {
                        processMonitorTimer.Stop();
                        Dispatcher.Invoke(() =>
                        {
                            UpdateStatus("Roblox closed", "Game window was closed", "Exited");
                        });
                    }
                    else if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        processMonitorTimer.Stop();
                        tcs.TrySetResult(true);
                    }
                }
                catch { }
            };
            
            processMonitorTimer.Start();
            
            await tcs.Task;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private async Task ApplyAllSettings()
        {
            if (config == null) return;
           
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string robloxPath = IOPath.Combine(localAppData, "Roblox");
               
                if (!Directory.Exists(robloxPath))
                    Directory.CreateDirectory(robloxPath);
               
                string globalSettingsPath = IOPath.Combine(robloxPath, "GlobalBasicSettings_13.xml");
               
                if (!File.Exists(globalSettingsPath))
                {
                    var files = Directory.GetFiles(robloxPath, "GlobalBasicSettings_*.xml");
                    if (files.Length > 0)
                    {
                        globalSettingsPath = files[0];
                    }
                    else
                    {
                        CreateDefaultGlobalSettings(globalSettingsPath);
                    }
                }
               
                FileAttributes attributes = File.GetAttributes(globalSettingsPath);
                bool wasReadOnly = (attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly;
               
                if (wasReadOnly)
                {
                    File.SetAttributes(globalSettingsPath, attributes & ~FileAttributes.ReadOnly);
                }
               
                XDocument doc = XDocument.Load(globalSettingsPath);
                var properties = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Properties");
               
                if (properties != null)
                {
                    SetOrUpdateElement(properties, "int", "FramerateCap", config.FpsLimit.ToString());
                    SetOrUpdateElement(properties, "int", "GraphicsQualityLevel", config.GraphicsQuality.ToString());
                    SetOrUpdateElement(properties, "float", "PreferredTransparency", config.Transparency.ToString("F1"));
                    SetOrUpdateElement(properties, "bool", "ReducedMotion", config.ReducedMotion.ToString().ToLower());
                    SetOrUpdateElement(properties, "float", "MouseSensitivity", config.MouseSensitivity.ToString("F9"));
                    SetOrUpdateElement(properties, "bool", "VREnabled", config.VREnabled.ToString().ToLower());
                   
                    doc.Save(globalSettingsPath);
                   
                    if (config.SetAsReadOnly)
                    {
                        File.SetAttributes(globalSettingsPath, File.GetAttributes(globalSettingsPath) | FileAttributes.ReadOnly);
                    }
                }
            }
            catch
            {
                // Silently fail if settings can't be applied
            }
        }

        private void SetOrUpdateElement(XElement properties, string elementType, string attributeName, string value)
        {
            var element = properties.Elements().FirstOrDefault(e =>
                e.Name.LocalName == elementType && e.Attribute("name")?.Value == attributeName);
           
            if (element != null)
            {
                element.Value = value;
            }
            else
            {
                XElement newElement = new XElement(elementType);
                newElement.SetAttributeValue("name", attributeName);
                newElement.Value = value;
                properties.Add(newElement);
            }
        }

        private void CreateDefaultGlobalSettings(string filePath)
        {
            if (config == null) return;
            string defaultXml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<roblox xmlns:xmime=""http://www.w3.org/2005/05/xmlmime"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xsi:noNamespaceSchemaLocation=""http://www.roblox.com/roblox.xsd"" version=""4"">
<External>null</External>
<External>nil</External>
<Item class=""UserGameSettings"" referent=""RBXA7687B4A7ACD49728F232E4944DE926E"">
<Properties>
<int name=""FramerateCap"">{config.FpsLimit}</int>
<int name=""GraphicsQualityLevel"">{config.GraphicsQuality}</int>
<float name=""PreferredTransparency"">{config.Transparency:F1}</float>
<bool name=""ReducedMotion"">{config.ReducedMotion.ToString().ToLower()}</bool>
<float name=""MouseSensitivity"">{config.MouseSensitivity:F9}</float>
<bool name=""VREnabled"">{config.VREnabled.ToString().ToLower()}</bool>
<bool name=""AllTutorialsDisabled"">false</bool>
<string name=""DefaultCameraID"">{{DefaultDeviceGuid}}</string>
<BinaryString name=""AttributesSerialize""></BinaryString>
<string name=""Name"">GameSettings</string>
</Properties>
</Item>
</roblox>";
           
            File.WriteAllText(filePath, defaultXml);
        }

        private void RemoveDesktopShortcuts()
        {
            try
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
               
                string[] shortcuts = new string[]
                {
                    "Roblox Player.lnk",
                    "Roblox Studio.lnk"
                };
                foreach (var shortcut in shortcuts)
                {
                    string shortcutPath = IOPath.Combine(desktopPath, shortcut);
                    if (File.Exists(shortcutPath))
                    {
                        File.Delete(shortcutPath);
                    }
                }
            }
            catch
            {
                // Silently fail if shortcuts can't be removed
            }
        }

        private async Task<string> GetLatestRobloxVersion()
        {
            var response = await httpClient.GetStringAsync("https://clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer");
            var json = JsonDocument.Parse(response);
            return json.RootElement.GetProperty("clientVersionUpload").GetString()!;
        }

        private async Task<string> GetLatestStudioVersion()
        {
            var response = await httpClient.GetStringAsync("https://clientsettingscdn.roblox.com/v2/client-version/WindowsStudio64");
            var json = JsonDocument.Parse(response);
            return json.RootElement.GetProperty("clientVersionUpload").GetString()!;
        }

        private string? GetInstalledRobloxVersion()
        {
            try
            {
                string versionsPath = IOPath.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Roblox", "Versions");
                if (!Directory.Exists(versionsPath)) return null;
                var versionDirs = Directory.GetDirectories(versionsPath)
                    .Where(d => File.Exists(IOPath.Combine(d, "RobloxPlayerBeta.exe")))
                    .OrderByDescending(d => Directory.GetCreationTime(d))
                    .ToList();
                return versionDirs.Any() ? IOPath.GetFileName(versionDirs.First()) : null;
            }
            catch
            {
                return null;
            }
        }

        private string? GetInstalledStudioVersion()
        {
            try
            {
                string versionsPath = IOPath.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Roblox", "Versions");
                if (!Directory.Exists(versionsPath)) return null;
                var studioDirs = Directory.GetDirectories(versionsPath)
                    .Where(d => File.Exists(IOPath.Combine(d, "RobloxStudioBeta.exe")))
                    .OrderByDescending(d => Directory.GetLastWriteTime(d))
                    .ToList();
                return studioDirs.Any() ? IOPath.GetFileName(studioDirs.First()) : null;
            }
            catch
            {
                return null;
            }
        }

        private async Task DownloadFile(string url, string destination, IProgress<int> progress, CancellationToken token)
        {
            using (var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token))
            {
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                using (var contentStream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                {
                    var buffer = new byte[8192];
                    long totalRead = 0L;
                    int bytesRead;
                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, token)) != 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead, token);
                        totalRead += bytesRead;
                        if (totalBytes > 0)
                        {
                            var percent = (int)((totalRead * 100L) / totalBytes);
                            progress?.Report(percent);
                        }
                    }
                }
            }
        }

        private async Task<bool> RunInstallerSilently(string installerPath)
        {
            try
            {
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = installerPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (var process = Process.Start(processStartInfo))
                {
                    if (process != null)
                    {
                        await Task.Run(() => process.WaitForExit());
                        return process.ExitCode == 0 || process.ExitCode == 1;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (!isCompleted)
            {
                cancellationTokenSource?.Cancel();
                UpdateStatus("Cancelling...", "Please wait...");
                CancelButton.IsEnabled = false;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
