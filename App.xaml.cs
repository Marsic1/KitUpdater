using Marsic1UtilityUpdater.Shared;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Windows;

namespace Marsic1UtilityUpdater
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
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string language = null;
            try
            {
                string configPath = Path.Combine(baseDir, "updater.config.json");
                if (File.Exists(configPath))
                {
                    language = (string)JObject.Parse(File.ReadAllText(configPath))["language"];
                }
            }
            catch { /* config illeggibile: lingua automatica */ }
            L10n.Init(baseDir, language);

            base.OnStartup(e);
        }
    }
}
