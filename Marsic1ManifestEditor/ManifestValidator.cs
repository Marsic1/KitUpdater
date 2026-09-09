using Marsic1UtilityUpdater.Shared;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Marsic1ManifestEditor
{
    // Validazione del manifest prima del salvataggio o dell'upload:
    // restituisce l'elenco degli errori da mostrare all'utente.
    public static class ManifestValidator
    {
        public static List<string> Validate(RootManifest manifest)
        {
            var errors = new List<string>();

            // --- Sezione Updater ---
            if (!string.IsNullOrEmpty(manifest.UpdaterVersion) && !IsValidVersion(manifest.UpdaterVersion))
            {
                errors.Add(Loc.F("updater_version non valida: '{0}' (usa es. \"1.2.0\").", manifest.UpdaterVersion));
            }
            if (!string.IsNullOrEmpty(manifest.UpdaterUrl) && !IsValidUrl(manifest.UpdaterUrl))
            {
                errors.Add(Loc.T("updater_url non è un URL valido."));
            }

            // --- Componenti ---
            if (manifest.Components != null)
            {
                foreach (var pair in manifest.Components)
                {
                    string where = $"componente '{pair.Key}'";
                    var comp = pair.Value;
                    if (comp == null)
                    {
                        errors.Add(Loc.F("{0}: dati mancanti.", where));
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(pair.Key)) errors.Add(Loc.T("Esiste un componente con chiave vuota."));
                    if (string.IsNullOrWhiteSpace(comp.Name)) errors.Add(Loc.F("{0}: 'name' mancante.", where));
                    if (string.IsNullOrWhiteSpace(comp.Version)) errors.Add(Loc.F("{0}: 'version' mancante.", where));
                    else if (!IsValidVersion(comp.Version)) errors.Add(Loc.F("{0}: version '{1}' non valida.", where, comp.Version));
                    if (string.IsNullOrWhiteSpace(comp.DownloadUrl)) errors.Add(Loc.F("{0}: 'download_url' mancante.", where));
                    else if (!IsValidUrl(comp.DownloadUrl)) errors.Add(Loc.F("{0}: 'download_url' non è un URL valido.", where));
                    if (!string.IsNullOrWhiteSpace(comp.VersionFile) && !IsValidRelativePath(comp.VersionFile))
                    {
                        errors.Add(Loc.F("{0}: version_file '{1}' non è un percorso relativo valido.", where, comp.VersionFile));
                    }

                    string folderError = ValidateFolder(comp.Folder);
                    if (folderError != null) errors.Add($"{where}: {folderError}");
                }
            }

            // --- Extra ---
            if (manifest.Extras != null)
            {
                foreach (var pair in manifest.Extras)
                {
                    string where = $"extra '{pair.Key}'";
                    var extra = pair.Value;
                    if (extra == null)
                    {
                        errors.Add(Loc.F("{0}: dati mancanti.", where));
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(pair.Key)) errors.Add(Loc.T("Esiste un extra con chiave vuota."));
                    if (string.IsNullOrWhiteSpace(extra.Name)) errors.Add(Loc.F("{0}: 'name' mancante.", where));

                    // Azione: "save_as" / "run_installer" scaricano un file;
                    // "none" o assente = extra con solo pulsante guida.
                    string action = (extra.Action ?? "").Trim();
                    if (action.Length > 0 && action != "save_as" && action != "run_installer" && action != "none")
                    {
                        errors.Add(Loc.F("{0}: action '{1}' non valida (usa \"save_as\", \"run_installer\" o \"none\").", where, extra.Action));
                    }

                    bool needsDownload = action == "save_as" || action == "run_installer";
                    if (needsDownload && string.IsNullOrWhiteSpace(extra.DownloadUrl))
                    {
                        errors.Add(Loc.F("{0}: 'download_url' mancante (obbligatoria per action '{1}').", where, action));
                    }
                    else if (!string.IsNullOrWhiteSpace(extra.DownloadUrl) && !IsValidUrl(extra.DownloadUrl))
                    {
                        errors.Add(Loc.F("{0}: 'download_url' non è un URL valido.", where));
                    }
                    if (!string.IsNullOrEmpty(extra.GuideUrl) && !IsValidUrl(extra.GuideUrl))
                    {
                        errors.Add($"{where}: guide_url non è un URL valido.");
                    }
                }
            }

            return errors;
        }

        private static string ValidateFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || folder.Trim() == ".") return null;

            string normalized = folder.Replace("/", "\\").Trim();
            if (PathIsRooted(normalized)) return Loc.F("folder '{0}' non può essere un percorso assoluto.", folder);
            if (normalized.Contains("..")) return Loc.F("folder '{0}' non può contenere '..'.", folder);
            return null;
        }

        private static bool PathIsRooted(string path)
        {
            return path.Length >= 2 && path[1] == ':' || path.StartsWith("\\\\") || path.StartsWith("/");
        }

        private static bool IsValidUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out Uri uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        private static bool IsValidRelativePath(string path)
        {
            return !string.IsNullOrWhiteSpace(path)
                && !PathIsRooted(path.Replace("/", "\\"))
                && !path.Contains("..");
        }

        private static bool IsValidVersion(string version)
        {
            string numeric = version.Trim().Split('-', '+')[0];
            string[] parts = numeric.Split('.');
            if (parts.Length == 0 || parts.Length > 4) return false;
            foreach (string p in parts)
            {
                if (!int.TryParse(p, out int n) || n < 0) return false;
            }
            return true;
        }
    }
}
