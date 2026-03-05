using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace SmilezStrap
{
    public partial class SplashScreen : Window
    {
        public SplashScreen(string version)
        {
            InitializeComponent();
            VersionText.Text = $"v{version}";
        }

        public void BeginFadeOut()
        {
            var storyboard = (Storyboard)FindResource("FadeOutAnimation");
            storyboard.Completed += (s, e) => this.Close();
            storyboard.Begin(this);
        }
    }
}
