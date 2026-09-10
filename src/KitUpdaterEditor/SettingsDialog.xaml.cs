using KitUpdater.Shared;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace KitUpdaterEditor
{
    public partial class SettingsDialog : Window
    {
        // --- BARRA DEL TITOLO SCURA (come la finestra principale) ---
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                IntPtr handle = new WindowInteropHelper(this).Handle;
                int useImmersiveDarkMode = 1;
                int result = DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useImmersiveDarkMode, sizeof(int));
                if (result != 0)
                {
                    DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useImmersiveDarkMode, sizeof(int));
                }
            }
            catch { /* Ignora se il sistema operativo non lo supporta */ }
        }

        public EditorConfig Config { get; private set; }

        public SettingsDialog(EditorConfig config)
        {
            InitializeComponent();
            AppIcon.TryOverride(this, AppDomain.CurrentDomain.BaseDirectory);
            Config = new EditorConfig
            {
                ServerUrl = config.ServerUrl,
                Username = config.Username,
                RemotePath = config.RemotePath,
                PublicManifestUrl = config.PublicManifestUrl,
                EncryptedPassword = config.EncryptedPassword,
                Language = config.Language
            };

            TxtServerUrl.Text = Config.ServerUrl;
            TxtUsername.Text = Config.Username;
            TxtPassword.Password = Config.Password;
            TxtRemotePath.Text = Config.RemotePath;
            TxtPublicUrl.Text = Config.PublicManifestUrl;
            TxtLanguage.Text = Config.Language ?? "";
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            string server = TxtServerUrl.Text.Trim().TrimEnd('/');
            if (!string.IsNullOrEmpty(server) && !server.StartsWith("http"))
            {
                MessageBox.Show(this, Loc.T("L'URL del server deve iniziare con http:// o https://"), Loc.T("Errore"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string remotePath = TxtRemotePath.Text.Trim();
            if (!string.IsNullOrEmpty(remotePath) && !remotePath.StartsWith("/"))
            {
                MessageBox.Show(this, Loc.T("Il percorso remoto deve iniziare con '/' (es. /MioKit/manifest.json)."), Loc.T("Errore"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            Config.ServerUrl = server;
            Config.Username = TxtUsername.Text.Trim();
            Config.Password = TxtPassword.Password;
            Config.RemotePath = remotePath;
            Config.PublicManifestUrl = TxtPublicUrl.Text.Trim();
            Config.Language = TxtLanguage.Text.Trim();
            Config.Save();

            DialogResult = true;
        }
    }
}
