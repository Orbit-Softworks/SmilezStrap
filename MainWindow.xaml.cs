using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Linq;
using Microsoft.Win32;

namespace SmilezStrap
{
    public partial class MainWindow : Window
    {
        private static readonly string VERSION = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.43";
        private const string GITHUB_REPO = "Orbit-Softworks/SmilezStrap";
        private readonly HttpClient httpClient = new HttpClient();
        
        private string? appDataPath;
        private Config? config;
        
        private int currentTabIndex = 0;
        private bool isAnimating = false;
        
        private FileSystemWatcher? globalSettingsWatcher;
        private System.Timers.Timer? settingsSyncTimer;
        private bool isSyncing = false;
        private bool isUpdatingUI = false;
        
        public MainWindow()
        {
            InitializeComponent();
            VersionText.Text = $"v{VERSION}";
            
            httpClient.DefaultRequestHeaders.Add("User-Agent", "SmilezStrap");
            
            InitializeApp();
            
            this.Loaded += (s, e) =>
            {
                LoadSettings();
                StartGlobalSettingsMonitor();
            };
            
            HomeView.Visibility = Visibility.Visible;
            SettingsView.Visibility = Visibility.Collapsed;
            AboutView.Visibility = Visibility.Collapsed;
            UpdateMenuButtonState(HomeButton, SettingsButton, AboutButton);
            
            CheckForUpdatesOnStartup();
            LoadAboutContent();
        }

        private void StartGlobalSettingsMonitor()
        {
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string robloxPath = Path.Combine(localAppData, "Roblox");
                
                if (!Directory.Exists(robloxPath))
                    return;
                
                globalSettingsWatcher = new FileSystemWatcher(robloxPath);
                globalSettingsWatcher.Filter = "GlobalBasicSettings_*.xml";
                globalSettingsWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size;
                globalSettingsWatcher.Changed += GlobalSettingsFile_Changed;
                globalSettingsWatcher.EnableRaisingEvents = true;
                
                settingsSyncTimer = new System.Timers.Timer(3000);
                settingsSyncTimer.Elapsed += (sender, e) => SyncSettingsFromRoblox();
                settingsSyncTimer.AutoReset = true;
                settingsSyncTimer.Start();
            }
            catch (Exception ex)
            {
            }
        }

        private void GlobalSettingsFile_Changed(object sender, FileSystemEventArgs e)
        {
            Task.Delay(500).ContinueWith(_ => SyncSettingsFromRoblox());
        }

        private void SyncSettingsFromRoblox()
        {
            if (isSyncing || config == null) return;
            
            isSyncing = true;
            
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string robloxPath = Path.Combine(localAppData, "Roblox");
                string globalSettingsPath = Path.Combine(robloxPath, "GlobalBasicSettings_13.xml");
                
                if (!File.Exists(globalSettingsPath))
                {
                    var files = Directory.GetFiles(robloxPath, "GlobalBasicSettings_*.xml");
                    if (files.Length == 0)
                    {
                        isSyncing = false;
                        return;
                    }
                    globalSettingsPath = files[0];
                }
                
                XDocument doc = XDocument.Load(globalSettingsPath);
                var properties = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Properties");
                
                if (properties == null)
                {
                    isSyncing = false;
                    return;
                }

                bool settingsChanged = false;
                
                var fpsElement = properties.Elements().FirstOrDefault(e => 
                    e.Name.LocalName == "int" && e.Attribute("name")?.Value == "FramerateCap");
                if (fpsElement != null && int.TryParse(fpsElement.Value, out int fps))
                {
                    if (config.FpsLimit != fps)
                    {
                        config.FpsLimit = fps;
                        settingsChanged = true;
                    }
                }

                var savedLevelElement = properties.Elements().FirstOrDefault(e => 
                    e.Name.LocalName == "int" && e.Attribute("name")?.Value == "SavedQualityLevel");
                if (savedLevelElement != null && int.TryParse(savedLevelElement.Value, out int savedLevel))
                {
                    int graphicsQuality = (savedLevel * 2) + 1;
                    
                    if (config.GraphicsQuality != graphicsQuality)
                    {
                        config.GraphicsQuality = graphicsQuality;
                        settingsChanged = true;
                    }
                }
                else
                {
                    var graphicsElement = properties.Elements().FirstOrDefault(e => 
                        e.Name.LocalName == "int" && e.Attribute("name")?.Value == "GraphicsQualityLevel");
                    if (graphicsElement != null && int.TryParse(graphicsElement.Value, out int graphics))
                    {
                        if (config.GraphicsQuality != graphics)
                        {
                            config.GraphicsQuality = graphics;
                            settingsChanged = true;
                        }
                    }
                }

                var transparencyElement = properties.Elements().FirstOrDefault(e => 
                    e.Name.LocalName == "float" && e.Attribute("name")?.Value == "PreferredTransparency");
                if (transparencyElement != null && float.TryParse(transparencyElement.Value, out float transparency))
                {
                    if (Math.Abs(config.Transparency - transparency) > 0.01f)
                    {
                        config.Transparency = transparency;
                        settingsChanged = true;
                    }
                }

                var reducedMotionElement = properties.Elements().FirstOrDefault(e => 
                    e.Name.LocalName == "bool" && e.Attribute("name")?.Value == "ReducedMotion");
                if (reducedMotionElement != null && bool.TryParse(reducedMotionElement.Value, out bool reducedMotion))
                {
                    if (config.ReducedMotion != reducedMotion)
                    {
                        config.ReducedMotion = reducedMotion;
                        settingsChanged = true;
                    }
                }

                var mouseSensElement = properties.Elements().FirstOrDefault(e => 
                    e.Name.LocalName == "float" && e.Attribute("name")?.Value == "MouseSensitivity");
                if (mouseSensElement != null && float.TryParse(mouseSensElement.Value, out float mouseSens))
                {
                    if (Math.Abs(config.MouseSensitivity - mouseSens) > 0.001f)
                    {
                        config.MouseSensitivity = mouseSens;
                        settingsChanged = true;
                    }
                }

                var vrElement = properties.Elements().FirstOrDefault(e => 
                    e.Name.LocalName == "bool" && e.Attribute("name")?.Value == "VREnabled");
                if (vrElement != null && bool.TryParse(vrElement.Value, out bool vrEnabled))
                {
                    if (config.VREnabled != vrEnabled)
                    {
                        config.VREnabled = vrEnabled;
                        settingsChanged = true;
                    }
                }

                if (settingsChanged)
                {
                    SaveConfig();
                    
                    Dispatcher.Invoke(() =>
                    {
                        UpdateUIFromConfig();
                    });
                }
            }
            catch (Exception ex)
            {
            }
            finally
            {
                isSyncing = false;
            }
        }

        private void UpdateUIFromConfig()
        {
            if (config == null || isUpdatingUI) return;
            
            isUpdatingUI = true;
            
            try
            {
                FpsLimitTextBox.Text = config.FpsLimit.ToString();
                
                int savedLevel = (config.GraphicsQuality - 1) / 2;
                int sliderValue = savedLevel - 1;
                
                sliderValue = Math.Max(0, Math.Min(9, sliderValue));
                GraphicsQualitySlider.Value = sliderValue;
                
                GraphicsQualityValue.Text = $"Level {savedLevel}";
                
                TransparencySlider.Value = config.Transparency;
                TransparencyValue.Text = config.Transparency.ToString("F1");
                ReducedMotionCheckBox.IsChecked = config.ReducedMotion;
                MouseSensitivityTextBox.Text = config.MouseSensitivity.ToString("F9");
                VREnabledCheckBox.IsChecked = config.VREnabled;
                ReadOnlyCheckBox.IsChecked = config.SetAsReadOnly;
            }
            finally
            {
                isUpdatingUI = false;
            }
        }

        private void ApplyAllSettings()
        {
            if (config == null) return;
            
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string robloxPath = Path.Combine(localAppData, "Roblox");
                string globalSettingsPath = Path.Combine(robloxPath, "GlobalBasicSettings_13.xml");
                
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
                
                if (properties == null) return;

                SetOrUpdateElement(properties, "int", "FramerateCap", config.FpsLimit.ToString());
                
                int sliderValue = (int)GraphicsQualitySlider.Value;
                int savedQualityLevel = sliderValue + 1;
                
                int graphicsQualityLevel = (savedQualityLevel * 2) + 1;
                
                SetOrUpdateElement(properties, "int", "GraphicsQualityLevel", graphicsQualityLevel.ToString());
                SetOrUpdateElement(properties, "int", "SavedQualityLevel", savedQualityLevel.ToString());
                
                SetOrUpdateElement(properties, "float", "PreferredTransparency", config.Transparency.ToString("F1"));
                SetOrUpdateElement(properties, "bool", "ReducedMotion", config.ReducedMotion.ToString().ToLower());
                SetOrUpdateElement(properties, "float", "MouseSensitivity", config.MouseSensitivity.ToString("F9"));
                SetOrUpdateElement(properties, "bool", "VREnabled", config.VREnabled.ToString().ToLower());
                
                doc.Save(globalSettingsPath);
                
                if (config.SetAsReadOnly)
                {
                    File.SetAttributes(globalSettingsPath, File.GetAttributes(globalSettingsPath) | FileAttributes.ReadOnly);
                }
                
                config.GraphicsQuality = graphicsQualityLevel;
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
            <int name=""SavedQualityLevel"">{(config.GraphicsQuality - 1) / 2}</int>
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

        private void GraphicsQualitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (GraphicsQualityValue != null)
            {
                int sliderValue = (int)GraphicsQualitySlider.Value;
                int robloxLevel = sliderValue + 1;
                GraphicsQualityValue.Text = $"Level {robloxLevel}";
            }
        }

        private void TransparencySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TransparencyValue != null)
            {
                TransparencyValue.Text = TransparencySlider.Value.ToString("F1");
            }
        }

        private void SaveAllSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (config == null) return;
            
            try
            {
                if (int.TryParse(FpsLimitTextBox.Text, out int fpsLimit) && fpsLimit >= 1 && fpsLimit <= 9999)
                {
                    config.FpsLimit = fpsLimit;
                }
                else
                {
                    MessageBox.Show("Please enter a valid FPS limit between 1 and 9999.", 
                                    "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                int sliderValue = (int)GraphicsQualitySlider.Value;
                int savedQualityLevel = sliderValue + 1;
                config.GraphicsQuality = (savedQualityLevel * 2) + 1;
                
                config.Transparency = (float)TransparencySlider.Value;
                config.ReducedMotion = ReducedMotionCheckBox.IsChecked ?? false;
                
                if (float.TryParse(MouseSensitivityTextBox.Text, out float mouseSens))
                {
                    config.MouseSensitivity = mouseSens;
                }
                
                config.VREnabled = VREnabledCheckBox.IsChecked ?? true;
                config.SetAsReadOnly = ReadOnlyCheckBox.IsChecked ?? false;
                
                SaveConfig();
                
                ApplyAllSettings();
                
                MessageBox.Show(
                    $"✓ All settings saved successfully!\n\n" +
                    $"FPS Limit: {config.FpsLimit}\n" +
                    $"Graphics Quality: Level {savedQualityLevel}\n" +
                    $"Transparency: {config.Transparency:F1}\n" +
                    $"Reduced Motion: {config.ReducedMotion}\n" +
                    $"Mouse Sensitivity: {config.MouseSensitivity:F2}\n" +
                    $"VR Enabled: {config.VREnabled}\n" +
                    $"Read-Only: {config.SetAsReadOnly}\n\n" +
                    $"Settings will be applied on next Roblox launch!",
                    "Settings Saved", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving settings: {ex.Message}", 
                               "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void CheckForUpdatesOnStartup()
        {
            if (config?.AutoCheckUpdates ?? true)
            {
                await CheckForAppUpdate(false);
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            AnimateToTab(2, AboutView, AboutViewTransform, AboutButton, HomeButton, SettingsButton);
        }

        private void VisitGitHub_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://github.com/Orbit-Softworks/SmilezStrap") { UseShellExecute = true });
        }

        private void Discord_Click(object sender, RoutedEventArgs e)
        {
            DiscordPopup.Visibility = Visibility.Visible;
        }

        private void DiscordPopupYes_Click(object sender, RoutedEventArgs e)
        {
            DiscordPopup.Visibility = Visibility.Collapsed;
            Process.Start(new ProcessStartInfo("https://discord.gg/JSJcNC4Jv9") { UseShellExecute = true });
        }

        private void DiscordPopupNo_Click(object sender, RoutedEventArgs e)
        {
            DiscordPopup.Visibility = Visibility.Collapsed;
        }

        private void HomeButton_Click(object? sender, RoutedEventArgs? e)
        {
            AnimateToTab(0, HomeView, HomeViewTransform, HomeButton, SettingsButton, AboutButton);
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            AnimateToTab(1, SettingsView, SettingsViewTransform, SettingsButton, HomeButton, AboutButton);
        }

        private async void AnimateToTab(int newTabIndex, UIElement newView, TranslateTransform newTransform, Button activeButton, params Button[] inactiveButtons)
        {
            if (isAnimating || newTabIndex == currentTabIndex)
                return;
            
            isAnimating = true;
            
            UIElement currentView;
            TranslateTransform currentTransform;
            
            switch (currentTabIndex)
            {
                case 0:
                    currentView = HomeView;
                    currentTransform = HomeViewTransform;
                    break;
                case 1:
                    currentView = SettingsView;
                    currentTransform = SettingsViewTransform;
                    break;
                case 2:
                    currentView = AboutView;
                    currentTransform = AboutViewTransform;
                    break;
                default:
                    currentView = HomeView;
                    currentTransform = HomeViewTransform;
                    break;
            }
            
            int direction = newTabIndex > currentTabIndex ? 1 : -1;
            double slideDistance = 500;
            
            newView.Visibility = Visibility.Visible;
            newTransform.Y = slideDistance * direction;
            
            var currentAnimation = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0,
                To = -slideDistance * direction,
                Duration = TimeSpan.FromMilliseconds(400),
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
            };
            
            var newAnimation = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = slideDistance * direction,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(400),
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
            };
            
            currentTransform.BeginAnimation(TranslateTransform.YProperty, currentAnimation);
            newTransform.BeginAnimation(TranslateTransform.YProperty, newAnimation);
            
            await Task.Delay(400);
            
            currentView.Visibility = Visibility.Collapsed;
            currentTransform.Y = 0;
            
            currentTabIndex = newTabIndex;
            isAnimating = false;
            
            UpdateMenuButtonState(activeButton, inactiveButtons);
        }

        private void UpdateMenuButtonState(Button activeButton, params Button[] inactiveButtons)
        {
            activeButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2A2A"));
            activeButton.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF4444"));
            
            foreach (var button in inactiveButtons)
            {
                button.Background = Brushes.Transparent;
                button.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#999"));
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        private async void LaunchRoblox_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
            var progressWindow = new ProgressWindow(false, config, null);
            progressWindow.Closed += (s, args) => this.Show();
            progressWindow.Show();
        }

        private async void LaunchStudio_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
            var progressWindow = new ProgressWindow(true, config, null);
            progressWindow.Closed += (s, args) => this.Show();
            progressWindow.Show();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                this.DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            this.Close();
        }

        private void InitializeApp()
        {
            appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SmilezStrap");
            Directory.CreateDirectory(appDataPath!);
            LoadConfig();
        }

        private void LoadSettings()
        {
            if (config != null)
            {
                UpdateUIFromConfig();
            }
        }

        private void SaveSettings()
        {
            if (config != null)
            {
                SaveConfig();
            }
        }

        private void LoadConfig()
        {
            string configPath = Path.Combine(appDataPath!, "config.json");
            if (File.Exists(configPath))
            {
                try
                {
                    string json = File.ReadAllText(configPath);
                    config = JsonSerializer.Deserialize<Config>(json) ?? new Config();
                }
                catch
                {
                    config = new Config();
                }
            }
            else
            {
                config = new Config();
                SaveConfig();
            }
        }

        private void SaveConfig()
        {
            try
            {
                string configPath = Path.Combine(appDataPath!, "config.json");
                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configPath, json);
            }
            catch (Exception ex)
            {
            }
        }

        private async void LoadAboutContent()
        {
            try
            {
                string readmeUrl = $"https://raw.githubusercontent.com/{GITHUB_REPO}/main/README.md";
                string readmeContent = await httpClient.GetStringAsync(readmeUrl);
                
                readmeContent = readmeContent.Replace("# ", "")
                                           .Replace("## ", "")
                                           .Replace("### ", "")
                                           .Replace("**", "")
                                           .Replace("*", "")
                                           .Replace("`", "");
                
                readmeContent = Regex.Replace(readmeContent, @"\[([^\]]+)\]\([^\)]+\)", "$1");
                
                AboutContentText.Text = readmeContent;
            }
            catch (Exception ex)
            {
                AboutContentText.Text = $"SmilezStrap - Roblox Bootstrapper\n\n" +
                                       $"Version: {VERSION}\n\n" +
                                       $"SmilezStrap is a custom Roblox bootstrapper with enhanced settings control.\n\n" +
                                       $"Features:\n" +
                                       $"• Comprehensive settings management\n" +
                                       $"• Bidirectional settings sync\n" +
                                       $"• Automatic updates\n" +
                                       $"• Clean and modern interface\n\n" +
                                       $"Could not load README from GitHub.";
            }
        }

        private async Task<bool> CheckForAppUpdate(bool showNoUpdateMessage = true)
        {
            try
            {
                var response = await httpClient.GetStringAsync($"https://api.github.com/repos/{GITHUB_REPO}/releases/latest");
                var releaseInfo = JsonDocument.Parse(response);
                string? latestVersion = releaseInfo.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v') ?? "1.0.43";
                
                if (string.IsNullOrEmpty(latestVersion))
                {
                    if (showNoUpdateMessage)
                    {
                        MessageBox.Show("Could not check for updates. Please try again later.",
                                        "Update Check", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    return true;
                }
                
                if (Version.TryParse(VERSION, out Version? currentVersion) && 
                    Version.TryParse(latestVersion, out Version? latestVersionObj))
                {
                    if (latestVersionObj <= currentVersion)
                    {
                        if (showNoUpdateMessage)
                        {
                            MessageBox.Show($"You are using the latest version (v{VERSION}).",
                                            "Up to Date", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        return true;
                    }
                }
                else
                {
                    if (showNoUpdateMessage)
                    {
                        MessageBox.Show("Could not check for updates. Please try again later.",
                                        "Update Check", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    return true;
                }

                var result = MessageBox.Show(
                    $"SmilezStrap v{latestVersion} is available!\n\nCurrent version: v{VERSION}\n\nDownload and install automatically?",
                    "Update Available",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information
                );
                
                if (result != MessageBoxResult.Yes)
                    return true;

                string? downloadUrl = null;
                var assets = releaseInfo.RootElement.GetProperty("assets").EnumerateArray();
                foreach (var asset in assets)
                {
                    string? name = asset.GetProperty("name").GetString();
                    if (name != null && (name.EndsWith("SmilezStrap-Setup.exe", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("SmilezStrap.exe", StringComparison.OrdinalIgnoreCase)))
                    {
                        downloadUrl = asset.GetProperty("browser_download_url").GetString();
                        break;
                    }
                }

                if (string.IsNullOrEmpty(downloadUrl))
                {
                    MessageBox.Show("Update found, but no installer found in release assets. Please try again in about 60 seconds.", 
                                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return true;
                }

                var updateWindow = new UpdateProgressWindow(downloadUrl, latestVersion);
                this.Hide();
                updateWindow.ShowDialog();
                
                return false;
            }
            catch (HttpRequestException ex)
            {
                if (showNoUpdateMessage)
                {
                    MessageBox.Show($"Failed to check for updates.",
                                    "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                return true;
            }
            catch (Exception ex)
            {
                if (showNoUpdateMessage)
                {
                    MessageBox.Show($"Failed to check for updates.",
                                    "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                return true;
            }
        }

        private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
        {
            var checkButton = sender as Button;
            if (checkButton != null)
            {
                string originalText = checkButton.Content.ToString();
                checkButton.Content = "Checking...";
                checkButton.IsEnabled = false;
                
                try
                {
                    await CheckForAppUpdate(true);
                }
                finally
                {
                    checkButton.Content = originalText;
                    checkButton.IsEnabled = true;
                }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            SaveSettings();
            
            if (globalSettingsWatcher != null)
            {
                globalSettingsWatcher.EnableRaisingEvents = false;
                globalSettingsWatcher.Dispose();
            }
            
            if (settingsSyncTimer != null)
            {
                settingsSyncTimer.Stop();
                settingsSyncTimer.Dispose();
            }
            
            base.OnClosed(e);
        }
    }

    public class Config
    {
        public string RobloxVersion { get; set; } = string.Empty;
        public string StudioVersion { get; set; } = string.Empty;
        public int FpsLimit { get; set; } = 60;
        public int GraphicsQuality { get; set; } = 5;
        public float Transparency { get; set; } = 1.0f;
        public bool ReducedMotion { get; set; } = false;
        public string FontSize { get; set; } = "Default";
        public float MouseSensitivity { get; set; } = 0.360000014f;
        public bool VREnabled { get; set; } = true;
        public bool SetAsReadOnly { get; set; } = false;
        public bool AutoCheckUpdates { get; set; } = true;
    }
}
