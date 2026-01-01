using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace SmilezStrap
{
    public partial class UpdateProgressWindow : Window
    {
        private readonly string downloadUrl;
        private readonly string newVersion;
        
        public UpdateProgressWindow(string downloadUrl, string newVersion)
        {
            InitializeComponent();
            this.downloadUrl = downloadUrl;
            this.newVersion = newVersion;
            
            Loaded += UpdateProgressWindow_Loaded;
        }

        private async void UpdateProgressWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await PerformUpdate();
            }
            catch (Exception)
            {
                MessageBox.Show($"Update failed.\n\nPlease download manually from GitHub.",
                    "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(1);
            }
        }

        private async Task PerformUpdate()
        {
            UpdateStatus("Downloading update...", 0);
            DetailsText.Text = $"Downloading SmilezStrap v{newVersion} installer";
            
            string tempFolder = Path.Combine(Path.GetTempPath(), "SmilezStrap_Update");
            Directory.CreateDirectory(tempFolder);
            
            string installerPath = Path.Combine(tempFolder, "SmilezStrap_Installer.exe");
            
            using (var httpClient = new HttpClient())
            {
                httpClient.DefaultRequestHeaders.Add("User-Agent", "SmilezStrap");
                httpClient.Timeout = TimeSpan.FromMinutes(10);
                
                using (var response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    
                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                    var canReportProgress = totalBytes != -1;
                    
                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        var buffer = new byte[8192];
                        long totalRead = 0L;
                        int bytesRead;
                        
                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalRead += bytesRead;
                            
                            if (canReportProgress && totalBytes > 0)
                            {
                                var progressPercentage = (int)((totalRead * 70) / totalBytes);
                                UpdateStatus("Downloading installer...", progressPercentage);
                                double mbRead = totalRead / 1024.0 / 1024.0;
                                double mbTotal = totalBytes / 1024.0 / 1024.0;
                                DetailsText.Text = $"Downloaded {mbRead:F1} MB of {mbTotal:F1} MB";
                            }
                        }
                    }
                }
            }
            
            UpdateStatus("Download complete!", 70);
            DetailsText.Text = $"Installer downloaded ({new FileInfo(installerPath).Length / 1024 / 1024:F1} MB)";
            await Task.Delay(800);
            
            UpdateStatus("Creating installer launcher...", 75);
            DetailsText.Text = "Preparing installation script...";
            
            int currentPID = Process.GetCurrentProcess().Id;
            string? currentExePath = Process.GetCurrentProcess().MainModule?.FileName ??
                                   Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SmilezStrap.exe");
            string? installDir = currentExePath != null ? Path.GetDirectoryName(currentExePath) : AppDomain.CurrentDomain.BaseDirectory;
            
            string batchPath = Path.Combine(tempFolder, "run_installer.bat");
            
            string vbsPath = Path.Combine(tempFolder, "create_shortcut.vbs");
            string vbsContent = $@"Set WshShell = CreateObject(""WScript.Shell"")
DesktopPath = WshShell.SpecialFolders(""Desktop"")
Set oShellLink = WshShell.CreateShortcut(DesktopPath & ""\SmilezStrap.lnk"")
oShellLink.TargetPath = ""{currentExePath}""
oShellLink.WorkingDirectory = ""{installDir}""
oShellLink.Description = ""SmilezStrap - Roblox Bootstrapper""
oShellLink.Save";
            
            File.WriteAllText(vbsPath, vbsContent);
            
            string batchContent = $@"@echo off
title SmilezStrap Update Installer v{newVersion}
color 0A
echo.
echo =====================================================
echo    SmilezStrap Update Installer v{newVersion}
echo =====================================================
echo.

echo [1/6] Waiting for SmilezStrap to close...
timeout /t 2 /nobreak >nul

:waitloop
tasklist /FI ""PID eq {currentPID}"" 2>NUL | find ""{currentPID}"" >NUL
if NOT ERRORLEVEL 1 (
    timeout /t 1 /nobreak >nul
    goto waitloop
)

echo     SmilezStrap closed!
echo.

echo [2/6] Waiting for file locks to release...
timeout /t 2 /nobreak >nul
echo     Ready to proceed.
echo.

echo [3/6] Looking for previous installation...
set ""UNINSTALLER={installDir}\unins000.exe""

if exist ""%UNINSTALLER%"" (
    echo     Uninstalling previous version...
    start /wait """" ""%UNINSTALLER%"" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
    echo     Uninstall complete!
    timeout /t 1 /nobreak >nul
) else (
    echo     No previous installation found.
)
echo.

echo [4/6] Installing SmilezStrap v{newVersion}...
start /wait """" ""{installerPath}"" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS

echo     Installation complete!
echo.

echo [5/6] Creating desktop shortcut...
cscript //nologo ""{vbsPath}""
echo     Desktop shortcut created!
echo.

echo [6/6] Starting SmilezStrap v{newVersion}...
start """" ""{currentExePath}""
echo     SmilezStrap launched!
echo.

echo Cleaning up...
timeout /t 1 /nobreak >nul

del /f /q ""{installerPath}"" 2>nul
del /f /q ""{vbsPath}"" 2>nul
del /f /q ""{batchPath}"" 2>nul
rd /s /q ""{tempFolder}"" 2>nul

echo.
echo =====================================================
echo    Update completed successfully!
echo =====================================================
echo.
echo This window will close in 3 seconds...
timeout /t 3 /nobreak >nul
exit
";

            File.WriteAllText(batchPath, batchContent);
            
            UpdateStatus("Launcher created", 85);
            DetailsText.Text = "Installation script ready";
            await Task.Delay(600);
            
            for (int i = 3; i > 0; i--)
            {
                int progress = 85 + ((3 - i) * 4);
                UpdateStatus($"Installing in {i}...", progress);
                DetailsText.Text = $"SmilezStrap will close and update in {i}...";
                await Task.Delay(1000);
            }
            
            UpdateStatus("Starting installer...", 98);
            DetailsText.Text = "Launching installation process...";
            await Task.Delay(500);
            
            var processInfo = new ProcessStartInfo
            {
                FileName = batchPath,
                UseShellExecute = true,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Normal
            };
            
            Process.Start(processInfo);
            
            UpdateStatus("SmilezStrap closing...", 100);
            DetailsText.Text = "Update in progress...";
            await Task.Delay(300);
            
            Process.GetCurrentProcess().Kill();
        }

        private void UpdateStatus(string message, int progress)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = message;
                ProgressText.Text = $"{progress}%";
                
                var parentBorder = ProgressBarFill.Parent as Border;
                if (parentBorder != null)
                {
                    double maxWidth = parentBorder.ActualWidth > 0 ? parentBorder.ActualWidth : 440;
                    ProgressBarFill.Width = (maxWidth * progress) / 100;
                }
            });
        }
    }
}
