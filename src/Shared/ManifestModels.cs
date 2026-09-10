using Newtonsoft.Json;
using System.Collections.Generic;

namespace KitUpdater.Shared
{
    // Modelli del manifest condivisi tra Updater e ManifestEditor.
    // La stessa serializzazione JSON garantisce che i due programmi
    // leggano e scrivano esattamente lo stesso schema.

    public class RootManifest
    {
        [JsonProperty("updater_version")] public string UpdaterVersion { get; set; }
        [JsonProperty("updater_url")] public string UpdaterUrl { get; set; }
        [JsonProperty("updater_description")] public string UpdaterDescription { get; set; }
        [JsonProperty("components")] public Dictionary<string, ComponentData> Components { get; set; } = new Dictionary<string, ComponentData>();
        [JsonProperty("extras")] public Dictionary<string, ExtraData> Extras { get; set; } = new Dictionary<string, ExtraData>();
    }

    public class ComponentData
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("description")] public string Description { get; set; }
        [JsonProperty("version")] public string Version { get; set; }
        [JsonProperty("folder")] public string Folder { get; set; }
        // Percorso relativo (rispetto alla cartella del componente) di un file
        // la cui versione leggibile (FileVersionInfo) rappresenta la versione
        // installata, es. "run.exe". Facoltativo: se assente si usa il manifest locale.
        [JsonProperty("version_file")] public string VersionFile { get; set; }
        [JsonProperty("download_url")] public string DownloadUrl { get; set; }
        [JsonProperty("preserve_files")] public List<string> PreserveFiles { get; set; } = new List<string>();
    }

    public class ExtraData
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("description")] public string Description { get; set; }
        [JsonProperty("action")] public string Action { get; set; }
        [JsonProperty("download_url")] public string DownloadUrl { get; set; }
        [JsonProperty("guide_url")] public string GuideUrl { get; set; }
    }
}
