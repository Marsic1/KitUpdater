using Marsic1UtilityUpdater.Shared;
using System;
using System.IO;
using System.Windows;

namespace Marsic1ManifestEditor
{
    /// <summary>
    /// Logica di interazione per App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // La localizzazione va inizializzata prima che la finestra principale
            // carichi lo XAML: le estensioni {l:Loc ...} valutano la lingua lì.
            // L'oggetto EditorConfig ha il suo Load/Init, ma la lingua serve
            // prima della finestra: si legge solo quel campo dal file.
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string language = EditorConfig.ReadLanguage(baseDir);
            L10n.Init(baseDir, language);

            base.OnStartup(e);
        }
    }
}
