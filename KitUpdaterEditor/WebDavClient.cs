using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace KitUpdaterEditor
{
    // Client WebDAV minimale per Nextcloud: upload (PUT) e download (GET)
    // di un file sul percorso remoto.php/dav/files/<utente>/<percorso>.
    public class WebDavClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _username;

        public WebDavClient(string serverUrl, string username, string password)
        {
            string baseUrl = (serverUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
            {
                throw new InvalidOperationException("URL del server non configurato.");
            }

            _username = username ?? "";
            var handler = new HttpClientHandler
            {
                Credentials = new NetworkCredential(_username, password),
                PreAuthenticate = true
            };
            _httpClient = new HttpClient(handler) { BaseAddress = new Uri(baseUrl) };
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        // L'endpoint DAV di Nextcloud è files/<utente>/<percorso>: il segmento
        // utente è obbligatorio — senza di lui il server risponde 404.
        // Utente e singoli segmenti del percorso vengono escapati (spazi, ecc.).
        private string DavFilePath(string remotePath)
        {
            string path = (remotePath ?? "/").Trim();
            if (!path.StartsWith("/")) path = "/" + path;
            string escaped = string.Join("/",
                path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(Uri.EscapeDataString));
            return $"/remote.php/dav/files/{Uri.EscapeDataString(_username)}/{escaped}";
        }

        public async Task UploadFileAsync(string remotePath, string content)
        {
            string davPath = DavFilePath(remotePath);
            var request = new HttpRequestMessage(HttpMethod.Put, davPath)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            };
            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException(
                    $"Upload WebDAV fallito: {DescribeUploadError((int)response.StatusCode, davPath, body)}");
            }
        }

        private static string DescribeUploadError(int status, string davPath, string body)
        {
            switch (status)
            {
                case 423: // Locked: file aperto in un editor web (text/Collabora/OnlyOffice)
                    return "il file manifest.json è bloccato perché risulta aperto in modifica " +
                           "sull'interfaccia web di Nextcloud (editor di testo, Collabora o OnlyOffice). " +
                           "Chiudi l'editor sul web e riprova l'upload.";
                case 401:
                    return "nome utente o password dell'app errati (401). Ricrea la password dell'app " +
                           "in Nextcloud → Impostazioni → Sicurezza e reinseriscila.";
                case 403:
                    return "l'utente non ha il permesso di scrivere su questo percorso (403).";
                case 404:
                    return $"percorso non trovato (404) su {davPath}: verifica che il percorso remoto " +
                           "corrisponda al nome reale della cartella nei File di Nextcloud.";
                case 409:
                    return $"la cartella di destinazione non esiste (409) su {davPath}.";
                default:
                    return $"errore {status} su {davPath}: {TrimBody(body)}";
            }
        }

        public async Task<string> DownloadFileAsync(string remotePath)
        {
            string davPath = DavFilePath(remotePath);
            var response = await _httpClient.GetAsync(davPath);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Download WebDAV fallito ({(int)response.StatusCode}) su {davPath}. " +
                    "Verifica che il percorso remoto corrisponda al nome reale della cartella nei File di Nextcloud.");
            }
            return await response.Content.ReadAsStringAsync();
        }

        private static string TrimBody(string body)
        {
            if (string.IsNullOrEmpty(body)) return "nessuna risposta dal server";
            return body.Length > 200 ? body.Substring(0, 200) + "..." : body;
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
