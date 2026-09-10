using KitUpdater.Shared;
using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace KitUpdaterEditor
{
    public partial class MainWindow : Window
    {
        // --- CHIAMATE DI SISTEMA PER LA BARRA DEL TITOLO SCURA ---
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        // ---------------------------------------------------------

        private RootManifest _manifest = new RootManifest();
        private string _loadedFrom = null;
        private EditorConfig _config;
        private readonly HttpClient _httpClient = new HttpClient();

        // riga attualmente mostrata nel pannello di dettaglio: le modifiche
        // vengono applicate al modello quando si cambia selezione, così si
        // possono modificare più voci di fila e salvare/caricare alla fine
        private ComponentRow _currentComponent;
        private ExtraRow _currentExtra;

        // true mentre il pannello viene riempito via codice: gli handler
        // dell'aggiornamento in tempo reale devono stare fermi, altrimenti
        // copiano i valori della riga precedente dentro quella nuova
        private bool _loadingDetail;

        public ObservableCollection<ComponentRow> ComponentRows { get; set; } = new ObservableCollection<ComponentRow>();
        public ObservableCollection<ExtraRow> ExtraRows { get; set; } = new ObservableCollection<ExtraRow>();

        public MainWindow()
        {
            InitializeComponent();
            AppIcon.TryOverride(this, AppDomain.CurrentDomain.BaseDirectory);

            // Nome del brand: "KitUpdater" di default, personalizzabile in
            // fase di build con la proprietà BrandName in Directory.Build.props
            Title = $"{Branding.Name} — Manifest Editor";
            HeaderTitle.Text = Loc.T("Manifest Editor");
            SubtitleText.Text = Loc.F("Editor del manifest.json per {0}", Branding.Name);

            _config = EditorConfig.Load();
            ComponentsList.ItemsSource = ComponentRows;
            ExtrasList.ItemsSource = ExtraRows;
            RefreshUpdaterFields();

            // aggiornamento in tempo reale della lista mentre si digita nel
            // pannello di dettaglio (la lista mostra chiave/nome/versione/cartella)
            TxtCompKey.TextChanged += OnComponentFieldChanged;
            TxtCompName.TextChanged += OnComponentFieldChanged;
            TxtCompVersion.TextChanged += OnComponentFieldChanged;
            TxtCompFolder.TextChanged += OnComponentFieldChanged;
            TxtExtraKey.TextChanged += OnExtraFieldChanged;
            TxtExtraName.TextChanged += OnExtraFieldChanged;
            CmbExtraAction.SelectionChanged += OnExtraFieldChanged;
        }

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

            Log(Loc.T("Manifest Editor avviato. Apri un manifest esistente o creane uno nuovo."));
        }

        private void Log(string message)
        {
            TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
            TxtLog.ScrollToEnd();
        }

        // ---------- Ricaricamento dell'interfaccia dal modello ----------

        private void RefreshUpdaterFields()
        {
            TxtUpdaterVersion.Text = _manifest.UpdaterVersion ?? "";
            TxtUpdaterUrl.Text = _manifest.UpdaterUrl ?? "";
            TxtUpdaterDescription.Text = _manifest.UpdaterDescription ?? "";
        }

        private void RefreshComponentList()
        {
            ComponentRows.Clear();
            if (_manifest.Components == null) return;
            foreach (var pair in _manifest.Components)
            {
                ComponentRows.Add(new ComponentRow
                {
                    Key = pair.Key,
                    Name = pair.Value?.Name,
                    Version = pair.Value?.Version,
                    Folder = pair.Value?.Folder,
                    Data = pair.Value
                });
            }
        }

        private void RefreshExtraList()
        {
            ExtraRows.Clear();
            if (_manifest.Extras == null) return;
            foreach (var pair in _manifest.Extras)
            {
                ExtraRows.Add(new ExtraRow
                {
                    Key = pair.Key,
                    Name = pair.Value?.Name,
                    Action = pair.Value?.Action,
                    Data = pair.Value
                });
            }
        }

        // Trasferisce i campi dell'interfaccia nel modello (chiamato prima di salvare/caricare)
        private void ApplyUpdaterFields()
        {
            _manifest.UpdaterVersion = TxtUpdaterVersion.Text.Trim();
            _manifest.UpdaterUrl = TxtUpdaterUrl.Text.Trim();
            _manifest.UpdaterDescription = TxtUpdaterDescription.Text;
        }

        // Applica il pannello di dettaglio alla riga corrente (chiamata quando
        // si cambia selezione e prima di salvare/caricare). Una chiave vuota o
        // duplicata non blocca: si mantiene la precedente e viene loggato.
        private void CommitComponentDetail()
        {
            var row = _currentComponent;
            if (row?.Data == null) return;

            string newKey = TxtCompKey.Text.Trim();
            if (newKey != row.Key)
            {
                if (newKey.Length == 0 || _manifest.Components.ContainsKey(newKey))
                {
                    Log(Loc.F("Chiave '{0}' vuota o duplicata: mantengo '{1}'.", newKey, row.Key));
                    newKey = row.Key;
                }
                else
                {
                    _manifest.Components.Remove(row.Key);
                    _manifest.Components[newKey] = row.Data;
                    row.Key = newKey;
                }
            }
            TxtCompKey.Text = row.Key;

            var d = row.Data;
            d.Name = TxtCompName.Text.Trim();
            d.Description = TxtCompDescription.Text;
            d.Version = TxtCompVersion.Text.Trim();
            d.Folder = TxtCompFolder.Text.Trim();
            d.VersionFile = string.IsNullOrWhiteSpace(TxtCompVersionFile.Text) ? null : TxtCompVersionFile.Text.Trim();
            d.DownloadUrl = TxtCompDownloadUrl.Text.Trim();
            d.PreserveFiles = TxtCompPreserveFiles.Text
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList();

            row.Name = d.Name;
            row.Version = d.Version;
            row.Folder = d.Folder;
        }

        private void CommitExtraDetail()
        {
            var row = _currentExtra;
            if (row?.Data == null) return;

            string newKey = TxtExtraKey.Text.Trim();
            if (newKey != row.Key)
            {
                if (newKey.Length == 0 || _manifest.Extras.ContainsKey(newKey))
                {
                    Log(Loc.F("Chiave '{0}' vuota o duplicata: mantengo '{1}'.", newKey, row.Key));
                    newKey = row.Key;
                }
                else
                {
                    _manifest.Extras.Remove(row.Key);
                    _manifest.Extras[newKey] = row.Data;
                    row.Key = newKey;
                }
            }
            TxtExtraKey.Text = row.Key;

            var d = row.Data;
            d.Name = TxtExtraName.Text.Trim();
            d.Description = TxtExtraDescription.Text;
            d.Action = (CmbExtraAction.SelectedItem as ComboBoxItem)?.Tag as string;
            d.DownloadUrl = TxtExtraDownloadUrl.Text.Trim();
            d.GuideUrl = string.IsNullOrWhiteSpace(TxtExtraGuideUrl.Text) ? null : TxtExtraGuideUrl.Text.Trim();

            row.Name = d.Name;
            row.Action = d.Action;
        }

        private void LoadComponentDetail(ComponentRow row)
        {
            _loadingDetail = true;
            try
            {
                ComponentDetail.IsEnabled = row != null;
                if (row?.Data == null)
                {
                    TxtCompKey.Text = ""; TxtCompName.Text = ""; TxtCompDescription.Text = "";
                    TxtCompVersion.Text = ""; TxtCompFolder.Text = ""; TxtCompVersionFile.Text = "";
                    TxtCompDownloadUrl.Text = ""; TxtCompPreserveFiles.Text = "";
                    return;
                }
                var d = row.Data;
                TxtCompKey.Text = row.Key;
                TxtCompName.Text = d.Name ?? "";
                TxtCompDescription.Text = d.Description ?? "";
                TxtCompVersion.Text = d.Version ?? "";
                TxtCompFolder.Text = d.Folder ?? "";
                TxtCompVersionFile.Text = d.VersionFile ?? "";
                TxtCompDownloadUrl.Text = d.DownloadUrl ?? "";
                TxtCompPreserveFiles.Text = string.Join(Environment.NewLine, d.PreserveFiles ?? new List<string>());
            }
            finally
            {
                _loadingDetail = false;
            }
        }

        private void LoadExtraDetail(ExtraRow row)
        {
            _loadingDetail = true;
            try
            {
                ExtraDetail.IsEnabled = row != null;
                if (row?.Data == null)
                {
                    TxtExtraKey.Text = ""; TxtExtraName.Text = ""; TxtExtraDescription.Text = "";
                    CmbExtraAction.SelectedIndex = -1;
                    TxtExtraDownloadUrl.Text = ""; TxtExtraGuideUrl.Text = "";
                    return;
                }
                var d = row.Data;
                TxtExtraKey.Text = row.Key;
                TxtExtraName.Text = d.Name ?? "";
                TxtExtraDescription.Text = d.Description ?? "";
                // Azione: salva come / installer / solo guida (action assente o
                // "none" nel JSON = extra con solo pulsante guida)
                CmbExtraAction.SelectedIndex = d.Action == "run_installer" ? 1 :
                                                d.Action == "save_as" ? 0 : 2;
                TxtExtraDownloadUrl.Text = d.DownloadUrl ?? "";
                TxtExtraGuideUrl.Text = d.GuideUrl ?? "";
            }
            finally
            {
                _loadingDetail = false;
            }
        }

        // ---------- Toolbar ----------

        private void BtnNew_Click(object sender, RoutedEventArgs e)
        {
            if (!IsEmptyState() && !ConfirmDiscardChanges()) return;
            _manifest = new RootManifest();
            _loadedFrom = null;
            // manifest sostituito: nessun commit della vecchia riga in editing
            _currentComponent = null;
            _currentExtra = null;
            RefreshUpdaterFields();
            RefreshComponentList();
            RefreshExtraList();
            LoadComponentDetail(null);
            LoadExtraDetail(null);
            Log(Loc.T("Nuovo manifest vuoto creato."));
        }

        private void BtnOpenFile_Click(object sender, RoutedEventArgs e)
        {
            if (!IsEmptyState() && !ConfirmDiscardChanges()) return;

            var dlg = new OpenFileDialog
            {
                Title = Loc.T("Apri manifest.json"),
                Filter = "File JSON|*.json|Tutti i file|*.*"
            };
            if (dlg.ShowDialog(this) != true) return;

            try
            {
                string json = File.ReadAllText(dlg.FileName);
                var manifest = JsonConvert.DeserializeObject<RootManifest>(json);
                if (manifest == null)
                {
                    MessageBox.Show(this, Loc.T("Il file non contiene un manifest valido."), Loc.T("Errore"), MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                _manifest = manifest;
                _loadedFrom = dlg.FileName;
                EnsureDictionaries();
                _currentComponent = null;
                _currentExtra = null;
                RefreshUpdaterFields();
                RefreshComponentList();
                RefreshExtraList();
                LoadComponentDetail(null);
                LoadExtraDetail(null);
                Log(Loc.F("Manifest caricato da: {0} ({1} componenti, {2} extra).", dlg.FileName, _manifest.Components.Count, _manifest.Extras.Count));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, Loc.F("Impossibile leggere il file: {0}", ex.Message), Loc.T("Errore"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnDownloadUrl_Click(object sender, RoutedEventArgs e)
        {
            if (!IsEmptyState() && !ConfirmDiscardChanges()) return;

            string url = _config.PublicManifestUrl;
            if (string.IsNullOrWhiteSpace(url))
            {
                MessageBox.Show(this, Loc.T("Configura prima l'URL pubblico del manifest nelle Impostazioni."), Loc.T("Impostazioni mancanti"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                Log(Loc.F("Scaricamento del manifest da: {0}", url));
                string json = await _httpClient.GetStringAsync(url);
                var manifest = JsonConvert.DeserializeObject<RootManifest>(json);
                if (manifest == null) throw new InvalidOperationException("contenuto non valido");

                _manifest = manifest;
                _loadedFrom = url;
                EnsureDictionaries();
                _currentComponent = null;
                _currentExtra = null;
                RefreshUpdaterFields();
                RefreshComponentList();
                RefreshExtraList();
                LoadComponentDetail(null);
                LoadExtraDetail(null);
                Log(Loc.F("Manifest scaricato ({0} componenti, {1} extra).", _manifest.Components.Count, _manifest.Extras.Count));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, Loc.F("Download fallito: {0}", ex.Message), Loc.T("Errore"), MessageBoxButton.OK, MessageBoxImage.Error);
                Log(Loc.F("Errore download: {0}", ex.Message));
            }
        }

        private async void BtnDownloadWebDav_Click(object sender, RoutedEventArgs e)
        {
            if (!IsEmptyState() && !ConfirmDiscardChanges()) return;
            if (string.IsNullOrWhiteSpace(_config.ServerUrl))
            {
                MessageBox.Show(this, Loc.T("Configura prima il server WebDAV nelle Impostazioni."), Loc.T("Impostazioni mancanti"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                Log(Loc.F("Scaricamento del manifest da WebDAV ({0})...", _config.RemotePath));
                string json;
                using (var dav = new WebDavClient(_config.ServerUrl, _config.Username, _config.Password))
                {
                    json = await dav.DownloadFileAsync(_config.RemotePath);
                }

                var manifest = JsonConvert.DeserializeObject<RootManifest>(json);
                if (manifest == null) throw new InvalidOperationException("contenuto non valido");

                _manifest = manifest;
                _loadedFrom = $"WebDAV: {_config.RemotePath}";
                EnsureDictionaries();
                _currentComponent = null;
                _currentExtra = null;
                RefreshUpdaterFields();
                RefreshComponentList();
                RefreshExtraList();
                LoadComponentDetail(null);
                LoadExtraDetail(null);
                Log(Loc.F("Manifest scaricato da WebDAV ({0} componenti, {1} extra).", _manifest.Components.Count, _manifest.Extras.Count));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, Loc.F("Download WebDAV fallito: {0}", ex.Message), Loc.T("Errore"), MessageBoxButton.OK, MessageBoxImage.Error);
                Log(Loc.F("Errore download WebDAV: {0}", ex.Message));
            }
        }

        private void BtnSaveFile_Click(object sender, RoutedEventArgs e)
        {
            string target = _loadedFrom;
            if (target != null && target.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                target = null; // non si può salvare su un URL: chiedi un file
            }
            if (target == null || !File.Exists(target))
            {
                var dlg = new SaveFileDialog
                {
                    Title = Loc.T("Salva manifest.json"),
                    Filter = "File JSON|*.json",
                    FileName = "manifest.json"
                };
                if (dlg.ShowDialog(this) != true) return;
                target = dlg.FileName;
            }

            if (!TryBuildValidatedManifest(out string json)) return;

            try
            {
                File.WriteAllText(target, json);
                _loadedFrom = target;
                Log(Loc.F("Manifest salvato in: {0}", target));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, Loc.F("Salvataggio fallito: {0}", ex.Message), Loc.T("Errore"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnUploadWebDav_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_config.ServerUrl))
            {
                MessageBox.Show(this, Loc.T("Configura prima il server WebDAV nelle Impostazioni."), Loc.T("Impostazioni mancanti"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!TryBuildValidatedManifest(out string json)) return;

            try
            {
                Log(Loc.F("Caricamento del manifest su WebDAV ({0})...", _config.RemotePath));
                using (var dav = new WebDavClient(_config.ServerUrl, _config.Username, _config.Password))
                {
                    await dav.UploadFileAsync(_config.RemotePath, json);
                }
                Log(Loc.T("Manifest caricato su Nextcloud con successo."));
                MessageBox.Show(this, Loc.T("Manifest caricato sul server con successo."), Loc.T("Caricamento completato"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, Loc.F("Caricamento WebDAV fallito: {0}", ex.Message), Loc.T("Errore"), MessageBoxButton.OK, MessageBoxImage.Error);
                Log(Loc.F("Errore upload WebDAV: {0}", ex.Message));
            }
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SettingsDialog(_config) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                _config = dlg.Config;
                Log(Loc.T("Impostazioni aggiornate."));
            }
        }

        // Trasferisce l'interfaccia nel modello, valida e serializza.
        // Restituisce false (e mostra gli errori) se la validazione fallisce.
        private bool TryBuildValidatedManifest(out string json)
        {
            json = null;
            try
            {
                ApplyUpdaterFields();
                CommitComponentDetail();
                CommitExtraDetail();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Loc.T("Errore"), MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            var errors = ManifestValidator.Validate(_manifest);
            if (errors.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine(Loc.T("Il manifest contiene errori, correggili prima di continuare:"));
                sb.AppendLine();
                foreach (string err in errors.Take(15))
                {
                    sb.AppendLine("• " + err);
                }
                if (errors.Count > 15) sb.AppendLine(Loc.F("... e altri {0} errori.", errors.Count - 15));
                MessageBox.Show(this, sb.ToString(), Loc.T("Validazione fallita"), MessageBoxButton.OK, MessageBoxImage.Warning);
                Log(Loc.F("Validazione fallita: {0} errori.", errors.Count));
                return false;
            }

            json = JsonConvert.SerializeObject(_manifest, Formatting.Indented);
            return true;
        }

        private void EnsureDictionaries()
        {
            if (_manifest.Components == null) _manifest.Components = new Dictionary<string, ComponentData>();
            if (_manifest.Extras == null) _manifest.Extras = new Dictionary<string, ExtraData>();
        }

        /// <summary>Vero quando non c'è nulla da perdere: nessun manifest
        /// caricato, liste vuote e campi dell'updater ancora intonsi.</summary>
        private bool IsEmptyState()
        {
            return _loadedFrom == null
                && ComponentRows.Count == 0
                && ExtraRows.Count == 0
                && string.IsNullOrWhiteSpace(TxtUpdaterVersion.Text)
                && string.IsNullOrWhiteSpace(TxtUpdaterUrl.Text)
                && string.IsNullOrWhiteSpace(TxtUpdaterDescription.Text);
        }

        private bool ConfirmDiscardChanges()
        {
            var result = MessageBox.Show(this,
                Loc.T("Le modifiche non salvate andranno perse. Continuare?"),
                Loc.T("Conferma"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            return result == MessageBoxResult.Yes;
        }

        // ---------- Liste e dettagli ----------

        private void ComponentsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            CommitComponentDetail(); // applica le modifiche della riga appena lasciata
            _currentComponent = ComponentsList.SelectedItem as ComponentRow;
            LoadComponentDetail(_currentComponent);
        }

        private void ExtrasList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            CommitExtraDetail();
            _currentExtra = ExtrasList.SelectedItem as ExtraRow;
            LoadExtraDetail(_currentExtra);
        }

        // Aggiorna modello e riga della lista ad ogni modifica del pannello.
        // La chiave viene rinominata solo quando valida (non vuota e non
        // duplicata): finché non lo è, la riga mantiene la chiave precedente.
        private void OnComponentFieldChanged(object sender, RoutedEventArgs e)
        {
            if (_loadingDetail) return;
            var row = _currentComponent;
            if (row?.Data == null) return;

            string newKey = TxtCompKey.Text.Trim();
            if (newKey != row.Key && newKey.Length > 0 && !_manifest.Components.ContainsKey(newKey))
            {
                _manifest.Components.Remove(row.Key);
                _manifest.Components[newKey] = row.Data;
                row.Key = newKey;
            }

            row.Data.Name = TxtCompName.Text.Trim();
            row.Data.Version = TxtCompVersion.Text.Trim();
            row.Data.Folder = TxtCompFolder.Text.Trim();
            row.Name = row.Data.Name;
            row.Version = row.Data.Version;
            row.Folder = row.Data.Folder;
        }

        private void OnExtraFieldChanged(object sender, RoutedEventArgs e)
        {
            if (_loadingDetail) return;
            var row = _currentExtra;
            if (row?.Data == null) return;

            string newKey = TxtExtraKey.Text.Trim();
            if (newKey != row.Key && newKey.Length > 0 && !_manifest.Extras.ContainsKey(newKey))
            {
                _manifest.Extras.Remove(row.Key);
                _manifest.Extras[newKey] = row.Data;
                row.Key = newKey;
            }

            row.Data.Name = TxtExtraName.Text.Trim();
            row.Data.Action = (CmbExtraAction.SelectedItem as ComboBoxItem)?.Tag as string;
            row.Name = row.Data.Name;
            row.Action = row.Data.Action;
        }

        private void BtnAddComponent_Click(object sender, RoutedEventArgs e)
        {
            string baseKey = "nuovo_componente";
            string key = baseKey;
            int n = 2;
            while (_manifest.Components.ContainsKey(key))
            {
                key = baseKey + "_" + n++;
            }

            var data = new ComponentData
            {
                Name = Loc.T("Nuovo Componente"),
                Description = "",
                Version = "1.0.0",
                Folder = "",
                DownloadUrl = ""
            };
            _manifest.Components[key] = data;
            RefreshComponentList();
            var row = ComponentRows.FirstOrDefault(r => r.Key == key);
            if (row != null) ComponentsList.SelectedItem = row;
            Log(Loc.F("Aggiunto componente '{0}'.", key));
        }

        private void BtnDuplicateComponent_Click(object sender, RoutedEventArgs e)
        {
            var row = ComponentsList.SelectedItem as ComponentRow;
            if (row?.Data == null) return;

            string baseKey = row.Key + "_copia";
            string key = baseKey;
            int n = 2;
            while (_manifest.Components.ContainsKey(key))
            {
                key = baseKey + n++;
            }

            var copy = JsonConvert.DeserializeObject<ComponentData>(JsonConvert.SerializeObject(row.Data));
            _manifest.Components[key] = copy;
            RefreshComponentList();
            var newRow = ComponentRows.FirstOrDefault(r => r.Key == key);
            if (newRow != null) ComponentsList.SelectedItem = newRow;
            Log(Loc.F("Duplicato il componente '{0}' come '{1}'.", row.Key, key));
        }

        private void BtnRemoveComponent_Click(object sender, RoutedEventArgs e)
        {
            var row = ComponentsList.SelectedItem as ComponentRow;
            if (row == null) return;

            _manifest.Components.Remove(row.Key);
            // azzera la riga corrente PRIMA del refresh: il cambio selezione
            // che ne deriva non deve ri-applicare (e ri-aggiungere) la riga rimossa
            _currentComponent = null;
            RefreshComponentList();
            LoadComponentDetail(null);
            Log(Loc.F("Rimosso il componente '{0}'.", row.Key));
        }

        private void BtnAddExtra_Click(object sender, RoutedEventArgs e)
        {
            string baseKey = "nuovo_extra";
            string key = baseKey;
            int n = 2;
            while (_manifest.Extras.ContainsKey(key))
            {
                key = baseKey + "_" + n++;
            }

            var data = new ExtraData
            {
                Name = Loc.T("Nuova Utility"),
                Description = "",
                Action = "save_as",
                DownloadUrl = ""
            };
            _manifest.Extras[key] = data;
            RefreshExtraList();
            var row = ExtraRows.FirstOrDefault(r => r.Key == key);
            if (row != null) ExtrasList.SelectedItem = row;
            Log(Loc.F("Aggiunto extra '{0}'.", key));
        }

        private void BtnDuplicateExtra_Click(object sender, RoutedEventArgs e)
        {
            var row = ExtrasList.SelectedItem as ExtraRow;
            if (row?.Data == null) return;

            string baseKey = row.Key + "_copia";
            string key = baseKey;
            int n = 2;
            while (_manifest.Extras.ContainsKey(key))
            {
                key = baseKey + n++;
            }

            var copy = JsonConvert.DeserializeObject<ExtraData>(JsonConvert.SerializeObject(row.Data));
            _manifest.Extras[key] = copy;
            RefreshExtraList();
            var newRow = ExtraRows.FirstOrDefault(r => r.Key == key);
            if (newRow != null) ExtrasList.SelectedItem = newRow;
            Log(Loc.F("Duplicato l'extra '{0}' come '{1}'.", row.Key, key));
        }

        private void BtnRemoveExtra_Click(object sender, RoutedEventArgs e)
        {
            var row = ExtrasList.SelectedItem as ExtraRow;
            if (row == null) return;

            _manifest.Extras.Remove(row.Key);
            _currentExtra = null;
            RefreshExtraList();
            LoadExtraDetail(null);
            Log(Loc.F("Rimosso l'extra '{0}'.", row.Key));
        }
    }

    // --- RIGHE DELLE LISTE (notificano i cambi: la lista a sinistra si
    // aggiorna appena le modifiche del pannello vengono applicate) ---

    public class ComponentRow : INotifyPropertyChanged
    {
        public ComponentData Data { get; set; }

        private string _key;
        public string Key { get => _key; set { _key = value; OnPropertyChanged(nameof(Key)); } }

        private string _name;
        public string Name { get => _name; set { _name = value; OnPropertyChanged(nameof(Name)); } }

        private string _version;
        public string Version { get => _version; set { _version = value; OnPropertyChanged(nameof(Version)); } }

        private string _folder;
        public string Folder { get => _folder; set { _folder = value; OnPropertyChanged(nameof(Folder)); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class ExtraRow : INotifyPropertyChanged
    {
        public ExtraData Data { get; set; }

        private string _key;
        public string Key { get => _key; set { _key = value; OnPropertyChanged(nameof(Key)); } }

        private string _name;
        public string Name { get => _name; set { _name = value; OnPropertyChanged(nameof(Name)); } }

        private string _action;
        public string Action { get => _action; set { _action = value; OnPropertyChanged(nameof(Action)); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
