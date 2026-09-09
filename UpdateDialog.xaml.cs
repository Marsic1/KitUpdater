using Marsic1UtilityUpdater.Shared;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Marsic1UtilityUpdater
{
    public partial class UpdateDialog : Window
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        public bool Result { get; private set; } = false;

        // Costruttore aggiornato per ricevere il changelog
        public UpdateDialog(string message, string changelog)
        {
            InitializeComponent();
            AppIcon.TryOverride(this, AppDomain.CurrentDomain.BaseDirectory);
            TxtMessage.Text = message;

            // Se nel JSON c'è una descrizione, mostrala. Altrimenti avvisa che non ci sono note.
            if (string.IsNullOrWhiteSpace(changelog))
            {
                TxtChangelog.Text = Loc.T("Nessuna nota di rilascio fornita per questa versione.");
            }
            else
            {
                TxtChangelog.Text = changelog;
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                IntPtr handle = new WindowInteropHelper(this).Handle;
                int useImmersiveDarkMode = 1;
                int result = DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useImmersiveDarkMode, sizeof(int));
                if (result != 0) DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useImmersiveDarkMode, sizeof(int));
            }
            catch { }
        }

        private void BtnYes_Click(object sender, RoutedEventArgs e)
        {
            Result = true;
            this.Close();
        }

        private void BtnNo_Click(object sender, RoutedEventArgs e)
        {
            Result = false;
            this.Close();
        }
    }
}