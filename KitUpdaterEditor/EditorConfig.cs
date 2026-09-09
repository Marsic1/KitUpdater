using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace KitUpdaterEditor
{
    // Configurazione del ManifestEditor salvata accanto all'exe.
    // La password dell'app Nextcloud viene cifrata con DPAPI (CurrentUser):
    // non compare in chiaro su disco e resta leggibile solo dallo stesso utente.
    public class EditorConfig
    {
        [JsonProperty("server_url")] public string ServerUrl { get; set; } = "";
        [JsonProperty("username")] public string Username { get; set; } = "";
        [JsonProperty("remote_path")] public string RemotePath { get; set; } = "/manifest.json";
        [JsonProperty("public_manifest_url")] public string PublicManifestUrl { get; set; } = "";

        // Lingua dell'interfaccia ("en", "it", ...): vuota = lingua di Windows
        [JsonProperty("language")] public string Language { get; set; } = "";

        [JsonProperty("encrypted_password")]
        public string EncryptedPassword { get; set; } = "";

        private const string ConfigFileName = "manifesteditor.config.json";
        private static readonly string ConfigPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFileName);

        [JsonIgnore]
        public string Password
        {
            get
            {
                try
                {
                    if (string.IsNullOrEmpty(EncryptedPassword)) return "";
                    byte[] protectedBytes = Convert.FromBase64String(EncryptedPassword);
                    byte[] plain = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(plain);
                }
                catch
                {
                    return "";
                }
            }
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    EncryptedPassword = "";
                }
                else
                {
                    byte[] plain = Encoding.UTF8.GetBytes(value);
                    byte[] protectedBytes = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
                    EncryptedPassword = Convert.ToBase64String(protectedBytes);
                }
            }
        }

        public static EditorConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    return JsonConvert.DeserializeObject<EditorConfig>(File.ReadAllText(ConfigPath)) ?? new EditorConfig();
                }
            }
            catch { /* configurazione non leggibile: usa i default */ }
            return new EditorConfig();
        }

        /// <summary>
        /// Legge solo il campo "language" dal file di configurazione, senza
        /// costruire l'oggetto completo: serve in App.OnStartup prima delle
        /// finestre. Restituisce null se assente o non leggibile.
        /// </summary>
        public static string ReadLanguage(string baseDir)
        {
            try
            {
                string path = Path.Combine(baseDir, ConfigFileName);
                if (File.Exists(path))
                {
                    var parsed = JObject.Parse(File.ReadAllText(path));
                    string lang = (string)parsed["language"];
                    return string.IsNullOrWhiteSpace(lang) ? null : lang.Trim();
                }
            }
            catch { /* config illeggibile: lingua automatica */ }
            return null;
        }

        public void Save()
        {
            File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(this, Formatting.Indented));
        }
    }
}
