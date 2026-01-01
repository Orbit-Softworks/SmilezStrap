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
        private Config? config;
        private string? protocolUrl = null;
        private const string ROBLOX_DOWNLOAD_URL = "https://www.roblox.com/download/client?os=win";
        private const string STUDIO_DOWNLOAD_URL = "https://setup.rbxcdn.com/RobloxStudioInstaller.exe";

        public ProgressWindow(bool launchStudio = false, Config? appConfig = null, string? gameUrl = null)
        {
            InitializeComponent();
            isStudio = launchStudio;
            config = appConfig;
            protocolUrl = gameUrl;
            cancellationTokenSource = new CancellationTokenSource();
           
            Loaded += async (s, e) => await StartLaunchProcess();
        }

        private void UpdateStatus(string status, string detail = "")
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = status;
                DetailText.Text = detail;
            });
        }

        private void SetProgress(int percent)
        {
            Dispatcher.Invoke(() =>
            {
                PercentText.Text = $"{percent}%";
               
                var targetWidth = 400.0 * (percent / 100.0);
                
                if (percent == 100)
                {
                    targetWidth = 400.0;
                }
               
                var animation = new DoubleAnimation
                {
                    To = targetWidth,
                    Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                ProgressBarFill.BeginAnimation(WidthProperty, animation);
            });
        }

        private void ShowCompletion(bool success, string message = "")
        {
            Dispatcher.Invoke(() =>
            {
                isCompleted = true;
                CancelButton.Visibility = Visibility.Collapsed;
                CloseButton.Visibility = Visibility.Visible;
                if (success)
                {
                    StatusText.Text = isStudio ? "Studio launched successfully!" : "Roblox launched successfully!";
                    SetProgress(100);
                }
                else
                {
                    StatusText.Text = "Error occurred";
                    DetailText.Text = message;
                }
            });
        }

        private async Task StartLaunchProcess()
        {
            try
            {
                if (isStudio)
                    await LaunchStudio();
                else
                    await LaunchRoblox();
            }
            catch (OperationCanceledException)
            {
                Dispatcher.Invoke(() =>
                {
                    UpdateStatus("Cancelled", "Launch process was cancelled by user");
                    CancelButton.Visibility = Visibility.Collapsed;
                    CloseButton.Visibility = Visibility.Visible;
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
            UpdateStatus("Connecting To Roblox...");
            SetProgress(5);
            await Task.Delay(500, token);
            token.ThrowIfCancellationRequested();

            UpdateStatus("Checking version...");
            SetProgress(10);
            token.ThrowIfCancellationRequested();

            string? installedVersion = GetInstalledRobloxVersion();
            string latestVersion = await GetLatestRobloxVersion();
            bool needsUpdate = installedVersion == null || installedVersion != latestVersion;

            if (needsUpdate)
            {
                UpdateStatus("Downloading Roblox...", "This may take a few minutes");
                string tempPath = IOPath.Combine(IOPath.GetTempPath(), "SmilezStrap", "RobloxPlayerInstaller.exe");
                Directory.CreateDirectory(IOPath.GetDirectoryName(tempPath)!);
                var downloadProgress = new Progress<int>(p =>
                {
                    UpdateStatus($"Downloading Roblox... {p}%");
                    SetProgress(10 + (p * 35 / 100));
                });
                await DownloadFile(ROBLOX_DOWNLOAD_URL, tempPath, downloadProgress, token);
                token.ThrowIfCancellationRequested();

                UpdateStatus("Installing Roblox...", "Please wait while Roblox is being installed");
                SetProgress(50);
                var installTask = RunInstallerSilently(tempPath);
                int simProgress = 50;
                while (!installTask.IsCompleted && simProgress < 75)
                {
                    token.ThrowIfCancellationRequested();
                    await Task.Delay(500, token);
                    simProgress += 2;
                    SetProgress(simProgress);
                }
                await installTask;
                token.ThrowIfCancellationRequested();
               
                SetProgress(75);
                await Task.Delay(1000, token);
                try { File.Delete(tempPath); } catch { }
                installedVersion = GetInstalledRobloxVersion();
                if (installedVersion == null)
                    throw new Exception("Installation failed to register.");
            }
            else
            {
                SetProgress(40);
                await Task.Delay(300, token);
            }
            token.ThrowIfCancellationRequested();
           
            UpdateStatus("Applying Modifications...");
            SetProgress(needsUpdate ? 78 : 50);
            await ApplyAllSettings();
            await Task.Delay(800, token);
           
            UpdateStatus("Launching Roblox...");
            SetProgress(needsUpdate ? 90 : 80);
            await Task.Delay(600, token);
            if (needsUpdate)
            {
                await Task.Delay(1500, token);
                RemoveDesktopShortcuts();
                await Task.Delay(1000, token);
                RemoveDesktopShortcuts();
            }
           
            await Task.Delay(400, token);
           
            string exePath = IOPath.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Roblox", "Versions", installedVersion!, "RobloxPlayerBeta.exe");
            if (!File.Exists(exePath))
                throw new Exception("Roblox executable not found.");
                
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
           
            SetProgress(100);
            await Task.Delay(800, token);
            RemoveDesktopShortcuts();
            ShowCompletion(true);
            await Task.Delay(1500);
            this.Close();
        }

        private async Task LaunchStudio()
        {
            var token = cancellationTokenSource.Token;
            UpdateStatus("Connecting To Roblox Studio...");
            SetProgress(5);
            await Task.Delay(500, token);
            token.ThrowIfCancellationRequested();

            UpdateStatus("Checking version...");
            SetProgress(10);
            token.ThrowIfCancellationRequested();

            string? installedVersion = GetInstalledStudioVersion();
            string latestVersion = await GetLatestStudioVersion();
            bool needsUpdate = installedVersion == null || installedVersion != latestVersion;

            if (needsUpdate)
            {
                UpdateStatus("Downloading Roblox Studio...", "This may take a few minutes");
                string tempPath = IOPath.Combine(IOPath.GetTempPath(), "SmilezStrap", "RobloxStudioInstaller.exe");
                Directory.CreateDirectory(IOPath.GetDirectoryName(tempPath)!);
                var downloadProgress = new Progress<int>(p =>
                {
                    UpdateStatus($"Downloading Studio... {p}%");
                    SetProgress(10 + (p * 40 / 100));
                });
                await DownloadFile(STUDIO_DOWNLOAD_URL, tempPath, downloadProgress, token);
                token.ThrowIfCancellationRequested();

                UpdateStatus("Installing Roblox Studio...", "Please wait while Studio is being installed");
                SetProgress(55);
                var installTask = RunInstallerSilently(tempPath);
                int simProgress = 55;
                while (!installTask.IsCompleted && simProgress < 85)
                {
                    token.ThrowIfCancellationRequested();
                    await Task.Delay(500, token);
                    simProgress += 2;
                    SetProgress(simProgress);
                }
                await installTask;
                token.ThrowIfCancellationRequested();
               
                SetProgress(85);
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
                SetProgress(50);
                await Task.Delay(300, token);
            }
            token.ThrowIfCancellationRequested();

            UpdateStatus("Launching Roblox Studio...");
            SetProgress(needsUpdate ? 88 : 75);
            await Task.Delay(800, token);
            if (needsUpdate)
            {
                await Task.Delay(1500, token);
                RemoveDesktopShortcuts();
                await Task.Delay(1000, token);
                RemoveDesktopShortcuts();
            }
            await Task.Delay(400, token);
           
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
           
            SetProgress(100);
            await Task.Delay(800, token);
            RemoveDesktopShortcuts();
            ShowCompletion(true);
            await Task.Delay(1500);
            this.Close();
        }

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
            catch (Exception ex)
            {
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
            catch (Exception ex)
            {
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
