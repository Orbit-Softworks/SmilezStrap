using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;

namespace SmilezStrap
{
    public partial class App : Application
    {
        private bool isProtocolLaunch = false;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            RegisterProtocolHandlers();

            if (e.Args.Length > 0)
            {
                foreach (string arg in e.Args)
                {
                    if (arg.Contains("roblox://", StringComparison.OrdinalIgnoreCase) ||
                        arg.Contains("roblox-player://", StringComparison.OrdinalIgnoreCase) ||
                        arg.Contains("placeId", StringComparison.OrdinalIgnoreCase) ||
                        arg.Contains("gameId", StringComparison.OrdinalIgnoreCase))
                    {
                        isProtocolLaunch = true;
                        HandleProtocolLaunch(arg, false);
                        return;
                    }
                    else if (arg.Contains("roblox-studio://", StringComparison.OrdinalIgnoreCase) ||
                             arg.Contains("studio", StringComparison.OrdinalIgnoreCase))
                    {
                        isProtocolLaunch = true;
                        HandleProtocolLaunch(arg, true);
                        return;
                    }
                }
            }

            if (!isProtocolLaunch)
            {
                try
                {
                    var mainWindow = new MainWindow();
                    mainWindow.Show();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Failed to open main window:\n\n{ex.Message}\n\nStack trace:\n{ex.StackTrace}",
                        "SmilezStrap - Startup Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    Current.Shutdown();
                }
            }
        }

        private void RegisterProtocolHandlers()
        {
            try
            {
                string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (string.IsNullOrEmpty(exePath)) return;

                string robloxPlayerPath = "";
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\roblox-player\shell\open\command"))
                    {
                        if (key != null)
                        {
                            string originalCommand = key.GetValue("")?.ToString() ?? "";
                            if (!originalCommand.Contains("SmilezStrap"))
                            {
                                using (var backupKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\roblox-player-original\shell\open\command"))
                                {
                                    backupKey.SetValue("", originalCommand);
                                }
                            }
                        }
                    }
                }
                catch { }

                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\roblox-player"))
                {
                    key.SetValue("", "URL:Roblox Protocol");
                    key.SetValue("URL Protocol", "");
                    key.SetValue("EditFlags", 2, RegistryValueKind.DWord);

                    using (var iconKey = key.CreateSubKey("DefaultIcon"))
                    {
                        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                        string versionsPath = Path.Combine(localAppData, "Roblox", "Versions");
                        if (Directory.Exists(versionsPath))
                        {
                            var versionDirs = Directory.GetDirectories(versionsPath)
                                .Where(d => File.Exists(Path.Combine(d, "RobloxPlayerBeta.exe")))
                                .OrderByDescending(d => Directory.GetCreationTime(d))
                                .ToList();
                            if (versionDirs.Any())
                            {
                                string robloxExe = Path.Combine(versionDirs.First(), "RobloxPlayerBeta.exe");
                                iconKey.SetValue("", $"\"{robloxExe}\",0");
                            }
                        }
                    }

                    using (var commandKey = key.CreateSubKey(@"shell\open\command"))
                    {
                        commandKey.SetValue("", $"\"{exePath}\" \"%1\"");
                    }
                }

                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\roblox"))
                {
                    key.SetValue("", "URL:Roblox Protocol");
                    key.SetValue("URL Protocol", "");
                    key.SetValue("EditFlags", 2, RegistryValueKind.DWord);

                    using (var iconKey = key.CreateSubKey("DefaultIcon"))
                    {
                        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                        string versionsPath = Path.Combine(localAppData, "Roblox", "Versions");
                        if (Directory.Exists(versionsPath))
                        {
                            var versionDirs = Directory.GetDirectories(versionsPath)
                                .Where(d => File.Exists(Path.Combine(d, "RobloxPlayerBeta.exe")))
                                .OrderByDescending(d => Directory.GetCreationTime(d))
                                .ToList();
                            if (versionDirs.Any())
                            {
                                string robloxExe = Path.Combine(versionDirs.First(), "RobloxPlayerBeta.exe");
                                iconKey.SetValue("", $"\"{robloxExe}\",0");
                            }
                        }
                    }

                    using (var commandKey = key.CreateSubKey(@"shell\open\command"))
                    {
                        commandKey.SetValue("", $"\"{exePath}\" \"%1\"");
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        private void HandleProtocolLaunch(string protocolUrl, bool isStudio)
        {
            try
            {
                string appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SmilezStrap");

                if (!Directory.Exists(appDataPath))
                    Directory.CreateDirectory(appDataPath);

                string configPath = Path.Combine(appDataPath, "config.json");

                Config? config = null;
                if (File.Exists(configPath))
                {
                    try
                    {
                        string json = File.ReadAllText(configPath);
                        config = JsonSerializer.Deserialize<Config>(json);
                    }
                    catch
                    {
                    }
                }

                if (config == null)
                {
                    config = new Config();
                }

                var progressWindow = new ProgressWindow(isStudio, config, protocolUrl);
                progressWindow.Closed += (s, args) =>
                {
                    this.Shutdown();
                };
                progressWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error launching Roblox: {ex.Message}", "SmilezStrap Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                this.Shutdown();
            }
        }
    }
}
