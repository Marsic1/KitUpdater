using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media.Imaging;

namespace Marsic1UtilityUpdater.Shared
{
    // Localizzazione runtime condivisa da Updater e ManifestEditor.
    //
    // L'italiano è la lingua incorporata: le stringhe nel codice e nello XAML
    // sono in italiano e funzionano senza alcun file. Ogni lingua aggiuntiva
    // è un file Languages\<lingua>.json (es. en.json) che mappa la stringa
    // italiana esatta -> traduzione; le stringhe assenti restano in italiano.
    //
    // Lingua scelta all'avvio: campo "language" del file di configurazione
    // dell'app, altrimenti la lingua di Windows, altrimenti italiano.
    // Nuovi file di lingua possono essere aggiunti accanto all'exe senza
    // ricompilare nulla.
    public static class L10n
    {
        private static Dictionary<string, string> _map;

        /// <summary>Lingua attiva ("it" quando nessun pacchetto è caricato).</summary>
        public static string Language { get; private set; } = "it";

        public static void Init(string baseDir, string preferredLanguage = null)
        {
            try
            {
                string lang = PickLanguage(baseDir, preferredLanguage);
                Language = lang;

                string file = Path.Combine(baseDir, "Languages", lang + ".json");
                if (lang != "it" && File.Exists(file))
                {
                    var parsed = JObject.Parse(File.ReadAllText(file));
                    var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var prop in parsed.Properties())
                    {
                        string value = (string)prop.Value;
                        if (!string.IsNullOrEmpty(value))
                            dict[prop.Name] = value;
                    }
                    _map = dict;
                }
                else
                {
                    _map = null;
                }
            }
            catch
            {
                _map = null; // pacchetto illeggibile: resta l'italiano incorporato
            }
        }

        private static string PickLanguage(string baseDir, string preferred)
        {
            if (!string.IsNullOrWhiteSpace(preferred))
            {
                string p = preferred.Trim();
                if (p == "it") return "it"; // forzato esplicitamente
                if (File.Exists(Path.Combine(baseDir, "Languages", p + ".json"))) return p;
            }
            string os = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            if (File.Exists(Path.Combine(baseDir, "Languages", os + ".json"))) return os;
            return "it";
        }

        /// <summary>Traduce una stringa italiana; senza pacchetto la restituisce invariata.</summary>
        public static string T(string text)
        {
            if (_map != null && text != null && _map.TryGetValue(text, out string translated))
                return translated;
            return text;
        }

        /// <summary>String.Format sulla versione tradotta del pattern (segnaposto {0}, {1}...).</summary>
        public static string F(string pattern, params object[] args)
        {
            return string.Format(T(pattern), args);
        }
    }

    /// <summary>Scorciatoia per Localization.T/F usata dal codice: Loc.T("..."), Loc.F("...{0}", x).</summary>
    public static class Loc
    {
        public static string T(string text) => L10n.T(text);
        public static string F(string pattern, params object[] args) => L10n.F(pattern, args);
    }

    /// <summary>
    /// Markup extension XAML: {l:Loc 'testo italiano'} restituisce la stringa
    /// tradotta per la lingua attiva (valutata al caricamento della finestra,
    /// quindi Localization.Init deve già essere stato chiamato in App.OnStartup).
    /// </summary>
    public class LocExtension : MarkupExtension
    {
        public string Key { get; set; }

        public LocExtension()
        {
        }

        public LocExtension(string key)
        {
            Key = key;
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return L10n.T(Key ?? "");
        }
    }

    /// <summary>
    /// Override runtime dell'icona: se esiste un file "app.ico" accanto all'exe
    /// viene usato come icona delle finestre (finestra + taskbar) al posto di
    /// quella incorporata nell'exe. Per cambiare anche l'icona del file exe
    /// serve ricompilare (sostituire icon.ico nel progetto).
    /// </summary>
    public static class AppIcon
    {
        public static void TryOverride(Window window, string baseDir)
        {
            try
            {
                string path = Path.Combine(baseDir, "app.ico");
                if (File.Exists(path))
                {
                    window.Icon = new BitmapImage(new Uri(path, UriKind.Absolute));
                }
            }
            catch { /* file non valido o in uso: resta l'icona incorporata */ }
        }
    }
}
