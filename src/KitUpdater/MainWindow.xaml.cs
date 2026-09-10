using KitUpdater.Shared;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace KitUpdater
{
    public partial class MainWindow : Window
    {
        // --- CHIAMATE DI SISTEMA PER LA BARRA DEL TITOLO SCURA ---
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        // ---------------------------------------------------------

        // URL del manifest di default: incorporato in fase di build dalla
        // proprietà ManifestUrl (Directory.Build.props del builder). Il file
        // updater.config.json accanto all'exe, se presente, ha la precedenza.

        private readonly string _baseDir = AppDomain.CurrentDomain.BaseDirectory;
        private readonly string _localManifestPath;
        private readonly string _tempFolder;
        private readonly string _updaterExePath;
        private readonly string _updaterVersion;

        private string _remoteManifestUrl = Branding.DefaultManifestUrl;

        private readonly HttpClient _httpClient = new HttpClient();

        private RootManifest _localManifest = new RootManifest();
        private RootManifest _remoteManifest = new RootManifest();

        // Processi (es. run.exe) che erano in esecuzione e vanno riavviati a fine aggiornamento
        private readonly List<string> _processesToRestart = new List<string>();

        // LE DUE LISTE PRINCIPALI
        public ObservableCollection<ComponentViewModel> ViewModels { get; set; }
        public ObservableCollection<ExtraViewModel> ExtraViewModels { get; set; } = new ObservableCollection<ExtraViewModel>();

        public MainWindow()
        {
            InitializeComponent();

            // La versione dell'updater viene letta dall'assembly invece che da una costante
            _updaterVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

            // Nome del brand: "KitUpdater" di default, personalizzabile in
            // fase di build con la proprietà BrandName in Directory.Build.props.
            // Il titolo della finestra mostra anche la versione (l'intestazione
            // grande resta solo col nome).
            Title = $"{Branding.Name} {_updaterVersion}";
            HeaderTitle.Text = Branding.Name;

            ViewModels = new ObservableCollection<ComponentViewModel>();
            _localManifestPath = Path.Combine(_baseDir, "manifest.json");
            _tempFolder = Path.Combine(_baseDir, ".temp");
            _updaterExePath = System.Reflection.Assembly.GetExecutingAssembly().Location;

            // icona personalizzata: app.ico accanto all'exe, se presente
            AppIcon.TryOverride(this, _baseDir);

            // logo personalizzato del builder (incorporato in fase di build)
            LoadBrandingLogo();

            LoadConfig();

            // Collega le liste all'interfaccia
            ComponentsList.ItemsSource = ViewModels;
            ExtrasList.ItemsSource = ExtraViewModels;
        }

        private void LoadConfig()
        {            try
            {
                string configPath = Path.Combine(_baseDir, "updater.config.json");
                if (File.Exists(configPath))
                {
                    var config = JObject.Parse(File.ReadAllText(configPath));
                    string url = (string)config["manifest_url"];
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        _remoteManifestUrl = url;
                    }
                }
            }
            catch (Exception ex)
            {
                Log(Loc.F("Errore nella lettura di updater.config.json, uso l'URL di default: {0}", ex.Message));
            }
        }

        // Logo del builder: logo.custom.png viene incorporato come risorsa in
        // fase di build solo se presente; altrimenti resta cat.png dallo XAML.
        private void LoadBrandingLogo()
        {
            try
            {
                var resource = Application.GetResourceStream(new Uri("pack://application:,,,/logo.custom.png"));
                if (resource == null) return;
                var image = new System.Windows.Media.Imaging.BitmapImage();
                image.BeginInit();
                image.StreamSource = resource.Stream;
                image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                image.EndInit();
                HeaderLogo.Source = image;
            }
            catch { /* risorsa non incorporata: resta il logo di default */ }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Abilita la barra del titolo scura di Windows 10/11
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

            CleanupOldFiles();

            // Lancia in automatico il controllo all'apertura del programma
            BtnCheck_Click(null, null);
        }

        private void Log(string message)
        {
            TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
            TxtLog.ScrollToEnd();
        }

        // Elimina i resti (file .old) di aggiornamenti precedenti, saltando quelli ancora in uso
        private void CleanupOldFiles()
        {
            try
            {
                foreach (string file in Directory.EnumerateFiles(_baseDir, "*.old", SearchOption.AllDirectories))
                {
                    try
                    {
                        File.Delete(file);
                        Log(Loc.F("Rimosso file residuo: {0}", Path.GetFileName(file)));
                    }
                    catch { /* ancora in uso: verrà eliminato al prossimo avvio */ }
                }
            }
            catch { /* nessuna cartella o accesso negato: ignora */ }
        }

        private async Task<bool> CheckAndPerformSelfUpdate()
        {
            if (!string.IsNullOrEmpty(_remoteManifest.UpdaterVersion) && IsNewerVersion(_remoteManifest.UpdaterVersion, _updaterVersion))
            {
                UpdateDialog popup = new UpdateDialog(
                    Loc.F("È disponibile una nuova versione dell'Updater (v{0}).\nPer garantire il corretto funzionamento, è consigliato aggiornare ora.", _remoteManifest.UpdaterVersion),
                    _remoteManifest.UpdaterDescription
                );
                popup.Owner = this;
                popup.ShowDialog();

                if (popup.Result == true)
                {
                    Log(Loc.T("Download del nuovo Updater in corso..."));
                    try
                    {
                        string currentExe = _updaterExePath;
                        string newExe = Path.Combine(_baseDir, "Updater_New.exe");
                        string batPath = Path.Combine(_baseDir, "update.bat");

                        var response = await _httpClient.GetAsync(_remoteManifest.UpdaterUrl);
                        response.EnsureSuccessStatusCode();
                        using (var fs = new FileStream(newExe, FileMode.Create))
                        {
                            await response.Content.CopyToAsync(fs);
                        }

                        string exeName = Path.GetFileName(currentExe);
                        string batScript = $@"
@echo off
timeout /t 2 /nobreak > NUL
del ""{exeName}""
ren ""Updater_New.exe"" ""{exeName}""
start """" ""{exeName}""
del ""%~f0""
";
                        File.WriteAllText(batPath, batScript);

                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
                        {
                            FileName = batPath,
                            UseShellExecute = true,
                            CreateNoWindow = true,
                            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                        });

                        Application.Current.Shutdown();
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Log(Loc.F("Errore durante l'aggiornamento dell'Updater: {0}", ex.Message));
                        MessageBox.Show(Loc.T("Impossibile aggiornare l'Updater. Controlla il log."), Loc.T("Errore"), MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            return false;
        }

        private async void BtnCheck_Click(object sender, RoutedEventArgs e)
        {
            SetButtonsState(false);
            ViewModels.Clear();

            try
            {
                Log(Loc.T("Caricamento manifest locale..."));
                if (File.Exists(_localManifestPath))
                {
                    string localJson = File.ReadAllText(_localManifestPath);
                    _localManifest = JsonConvert.DeserializeObject<RootManifest>(localJson) ?? new RootManifest();
                    if (_localManifest.Components == null) _localManifest.Components = new Dictionary<string, ComponentData>();
                    if (_localManifest.Extras == null) _localManifest.Extras = new Dictionary<string, ExtraData>();
                }

                Log(Loc.T("Scaricamento manifest remoto..."));
                string remoteJson = await _httpClient.GetStringAsync(_remoteManifestUrl);
                _remoteManifest = JsonConvert.DeserializeObject<RootManifest>(remoteJson) ?? new RootManifest();

                bool isUpdatingSelf = await CheckAndPerformSelfUpdate();
                if (isUpdatingSelf) return;

                if (_remoteManifest.Components != null)
                {
                    foreach (var remote in _remoteManifest.Components)
                    {
                        string key = remote.Key;
                        var remoteComp = remote.Value;

                        string localVer = ResolveLocalVersion(key, remoteComp, out bool isInstalled);

                        string status = !isInstalled ? Loc.T("Nuovo Componente") :
                                        (IsNewerVersion(remoteComp.Version, localVer) ? Loc.T("Da Aggiornare") : Loc.T("Aggiornato"));

                        ViewModels.Add(new ComponentViewModel
                        {
                            Key = key,
                            Name = remoteComp.Name,
                            Description = remoteComp.Description,
                            LocalVersion = isInstalled ? localVer : Loc.T("Non installato"),
                            RemoteVersion = remoteComp.Version,
                            Status = status,
                            Data = remoteComp
                        });
                    }
                }

                ExtraViewModels.Clear();
                if (_remoteManifest.Extras != null)
                {
                    foreach (var extra in _remoteManifest.Extras)
                    {
                        ExtraViewModels.Add(new ExtraViewModel { Data = extra.Value });
                    }
                }

                Log(Loc.T("Controllo completato."));
            }
            catch (Exception ex)
            {
                Log(Loc.F("Errore durante il controllo: {0}", ex.Message));
            }
            finally
            {
                RefreshUIState();
            }
        }

        // Determina la versione installata di un componente:
        // 1) versione del file indicato da "version_file" (se configurato e presente);
        // 2) altrimenti la versione registrata nel manifest locale;
        // 3) altrimenti il componente non risulta installato.
        private string ResolveLocalVersion(string key, ComponentData remoteComp, out bool isInstalled)
        {
            isInstalled = false;

            if (!string.IsNullOrEmpty(remoteComp.VersionFile))
            {
                try
                {
                    string versionFilePath = Path.Combine(
                        ResolveTargetFolder(remoteComp.Folder),
                        remoteComp.VersionFile.Replace("/", "\\"));

                    if (File.Exists(versionFilePath))
                    {
                        string fileVersion = FileVersionInfo.GetVersionInfo(versionFilePath).FileVersion;
                        if (!string.IsNullOrWhiteSpace(fileVersion))
                        {
                            isInstalled = true;
                            return fileVersion.Trim();
                        }
                    }
                }
                catch { /* file non leggibile o percorso non valido: passa al fallback */ }
            }

            if (_localManifest.Components != null && _localManifest.Components.ContainsKey(key))
            {
                string localVersion = _localManifest.Components[key].Version;
                if (!string.IsNullOrEmpty(localVersion))
                {
                    isInstalled = true;
                    return localVersion;
                }
            }

            return "0.0.0";
        }

        // Confronto di versione semantico: "1.10.0" > "1.9.0", "1.0" == "1.0.0".
        // Se una delle due stringhe non è numerica si ricade sul confronto semplice.
        private static bool IsNewerVersion(string remote, string local)
        {
            int[] remoteParts, localParts;
            if (TryParseVersion(remote, out remoteParts) && TryParseVersion(local, out localParts))
            {
                for (int i = 0; i < 4; i++)
                {
                    if (remoteParts[i] != localParts[i]) return remoteParts[i] > localParts[i];
                }
                return false;
            }
            return !string.Equals(remote, local, StringComparison.Ordinal);
        }

        private static bool TryParseVersion(string version, out int[] parts)
        {
            parts = null;
            if (string.IsNullOrWhiteSpace(version)) return false;

            // Ignora eventuali metadati dopo "-" o "+" (es. "1.2.0-beta")
            string numeric = version.Trim().Split('-', '+')[0];
            string[] tokens = numeric.Split('.');
            if (tokens.Length == 0 || tokens.Length > 4) return false;

            var result = new int[4];
            for (int i = 0; i < tokens.Length; i++)
            {
                if (!int.TryParse(tokens[i], out result[i]) || result[i] < 0) return false;
            }
            parts = result;
            return true;
        }

        private void ComponentsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshUIState();
        }

        private void RefreshUIState()
        {
            BtnCheck.IsEnabled = true;

            ComponentsList.IsHitTestVisible = true;
            ComponentsList.Opacity = 1.0;
            ExtrasList.IsHitTestVisible = true;
            ExtrasList.Opacity = 1.0;

            bool hasUpdates = ViewModels.Any(v => v.Status != Loc.T("Aggiornato"));
            BtnUpdate.IsEnabled = hasUpdates;

            var selected = ComponentsList.SelectedItem as ComponentViewModel;
            BtnUpdateSelected.IsEnabled = selected != null && selected.Status != Loc.T("Aggiornato");
        }

        private void SetButtonsState(bool enabled)
        {
            BtnCheck.IsEnabled = enabled;
            BtnUpdate.IsEnabled = enabled;
            BtnUpdateSelected.IsEnabled = enabled;

            ComponentsList.IsHitTestVisible = enabled;
            ComponentsList.Opacity = enabled ? 1.0 : 0.6;

            ExtrasList.IsHitTestVisible = enabled;
            ExtrasList.Opacity = enabled ? 1.0 : 0.6;
        }

        private async void BtnUpdateAll_Click(object sender, RoutedEventArgs e)
        {
            var toUpdate = ViewModels.Where(v => v.Status != Loc.T("Aggiornato")).ToList();
            await PerformUpdate(toUpdate);
        }

        private async void BtnUpdateSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = ComponentsList.SelectedItem as ComponentViewModel;
            if (selected != null)
            {
                await PerformUpdate(new List<ComponentViewModel> { selected });
            }
        }

        private async void BtnExtraAction_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is ExtraViewModel extra)
            {
                try
                {
                    if (extra.Data.Action == "save_as")
                    {
                        SaveFileDialog saveFileDialog = new SaveFileDialog
                        {
                            FileName = Path.GetFileName(new Uri(extra.Data.DownloadUrl).LocalPath),
                            Title = Loc.F("Salva {0}", extra.Name)
                        };

                        if (saveFileDialog.ShowDialog() == true)
                        {
                            extra.Status = Loc.T("Download...");
                            var response = await _httpClient.GetAsync(extra.Data.DownloadUrl);
                            response.EnsureSuccessStatusCode();
                            using (var fs = new FileStream(saveFileDialog.FileName, FileMode.Create))
                            {
                                await response.Content.CopyToAsync(fs);
                            }
                            extra.Status = Loc.T("Fatto!");
                            Log(Loc.F("{0} salvato correttamente.", extra.Name));
                        }
                    }
                    else if (extra.Data.Action == "run_installer")
                    {
                        extra.Status = Loc.T("Download...");
                        if (!Directory.Exists(_tempFolder)) Directory.CreateDirectory(_tempFolder);

                        string tempFile = Path.Combine(_tempFolder, Path.GetFileName(new Uri(extra.Data.DownloadUrl).LocalPath));

                        var response = await _httpClient.GetAsync(extra.Data.DownloadUrl);
                        response.EnsureSuccessStatusCode();
                        using (var fs = new FileStream(tempFile, FileMode.Create))
                        {
                            await response.Content.CopyToAsync(fs);
                        }

                        extra.Status = Loc.T("In esecuzione...");
                        Log(Loc.F("Avvio di {0}...", extra.Name));

                        var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = tempFile,
                            UseShellExecute = true
                        });

                        // Aspetta che l'installer venga chiuso senza bloccare l'interfaccia grafica
                        if (process != null)
                        {
                            Log(Loc.F("In attesa del termine dell'installazione di {0}...", extra.Name));
                            await Task.Run(() => process.WaitForExit());

                            // A installazione finita, elimina il file
                            if (File.Exists(tempFile))
                            {
                                File.Delete(tempFile);
                                Log(Loc.T("File di installazione temporaneo rimosso."));
                            }
                        }

                        extra.Status = Loc.T("Completato");
                    }
                }
                catch (Exception ex)
                {
                    extra.Status = Loc.T("Errore");
                    Log(Loc.F("Errore con {0}: {1}", extra.Name, ex.Message));
                }
            }
        }

        private void BtnOpenGuide_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is ExtraViewModel extra && !string.IsNullOrEmpty(extra.Data.GuideUrl))
            {
                try
                {
                    System.Diagnostics.Process.Start(extra.Data.GuideUrl);
                    Log(Loc.F("Aperta la guida per {0}.", extra.Name));
                }
                catch (Exception ex)
                {
                    Log(Loc.F("Errore apertura guida: {0}", ex.Message));
                }
            }
        }

        private async Task PerformUpdate(List<ComponentViewModel> toUpdate)
        {
            SetButtonsState(false);
            MainProgress.Value = 0;
            _processesToRestart.Clear();

            if (!Directory.Exists(_tempFolder)) Directory.CreateDirectory(_tempFolder);

            int total = toUpdate.Count;
            int current = 0;

            foreach (var item in toUpdate)
            {
                item.Status = Loc.T("Download...");
                Log(Loc.F("Scaricamento di {0}...", item.Name));

                try
                {
                    string zipPath = Path.Combine(_tempFolder, $"{item.Key}.zip");

                    var response = await _httpClient.GetAsync(item.Data.DownloadUrl);
                    response.EnsureSuccessStatusCode();

                    using (var fs = new FileStream(zipPath, FileMode.Create))
                    {
                        await response.Content.CopyToAsync(fs);
                    }

                    item.Status = Loc.T("Estrazione...");
                    Log(Loc.F("Estrazione di {0}...", item.Name));

                    string targetFolder = ResolveTargetFolder(item.Data.Folder);
                    if (!Directory.Exists(targetFolder)) Directory.CreateDirectory(targetFolder);

                    using (ZipArchive archive = ZipFile.OpenRead(zipPath))
                    {
                        foreach (ZipArchiveEntry entry in archive.Entries)
                        {
                            if (string.IsNullOrEmpty(entry.Name)) continue;

                            string destPath = ResolveEntryPath(targetFolder, entry.FullName);

                            if (ShouldPreserveFile(entry.FullName, item.Data.PreserveFiles) && File.Exists(destPath))
                            {
                                Log(Loc.F("  -> Preservato: {0}", entry.FullName));
                                continue;
                            }

                            ExtractEntryWithReplace(entry, destPath);
                        }
                    }

                    _localManifest.Components[item.Key] = item.Data;
                    item.LocalVersion = item.Data.Version;
                    item.Status = Loc.T("Aggiornato");
                    Log(Loc.F("{0} aggiornato con successo.", item.Name));
                }
                catch (Exception ex)
                {
                    item.Status = Loc.T("Errore");
                    Log(Loc.F("Errore con {0}: {1}", item.Name, ex.Message));
                }

                current++;
                MainProgress.Value = (double)current / total * 100;
            }

            RestartUpdatedProcesses();

            string newLocalJson = JsonConvert.SerializeObject(_localManifest, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(_localManifestPath, newLocalJson);

            if (Directory.Exists(_tempFolder)) Directory.Delete(_tempFolder, true);

            Log(Loc.T("Operazione completata."));
            RefreshUIState();
        }

        // Risolve la cartella di destinazione di un componente rispetto alla cartella dell'updater.
        // "" , "." e "./" indicano la cartella dell'exe stesso (per aggiornare es. run.exe).
        // Percorsi assoluti o con risalite ("..") vengono rifiutati.
        private string ResolveTargetFolder(string folder)
        {
            string normalized = (folder ?? "").Replace("/", "\\").Trim();
            if (normalized == ".") normalized = "";

            if (Path.IsPathRooted(normalized) || normalized.Contains(".."))
            {
                throw new InvalidOperationException(Loc.F("Percorso componente non valido: '{0}'", folder));
            }

            string baseFull = Path.GetFullPath(_baseDir).TrimEnd('\\');
            string full = Path.GetFullPath(Path.Combine(baseFull, normalized));
            if (full != baseFull && !full.StartsWith(baseFull + "\\", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(Loc.F("Percorso componente fuori dalla cartella base: '{0}'", folder));
            }
            return full;
        }

        // Percorso di destinazione di una voce dello zip, con la stessa protezione contro
        // risalite fuori dalla cartella di destinazione (zip slip).
        private string ResolveEntryPath(string targetFolder, string entryFullName)
        {
            string normalized = entryFullName.Replace("/", "\\");
            if (normalized.Contains(".."))
            {
                throw new InvalidOperationException(Loc.F("Voce dello zip non valida: '{0}'", entryFullName));
            }

            string targetFull = Path.GetFullPath(targetFolder).TrimEnd('\\');
            string destPath = Path.GetFullPath(Path.Combine(targetFull, normalized));
            if (destPath != targetFull && !destPath.StartsWith(targetFull + "\\", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(Loc.F("Voce dello zip fuori dalla cartella di destinazione: '{0}'", entryFullName));
            }
            return destPath;
        }

        // Estrae una voce sovrascrivendo il file esistente. Se il file è in uso
        // (es. un exe in esecuzione), lo rinomina in .old — Windows lo consente anche
        // mentre il processo gira — e poi estrae il nuovo. Se un processo risultava
        // in esecuzione da quel file, viene terminato e riavviato a fine batch.
        private void ExtractEntryWithReplace(ZipArchiveEntry entry, string destPath)
        {
            // L'exe dell'updater stesso non va mai toccato qui: passa dall'autoaggiornamento.
            if (string.Equals(destPath, _updaterExePath, StringComparison.OrdinalIgnoreCase))
            {
                Log(Loc.F("  -> Saltato {0}: è l'exe dell'updater (usa l'autoaggiornamento).", Path.GetFileName(destPath)));
                return;
            }

            bool wasLocked = false;
            try
            {
                entry.ExtractToFile(destPath, true);
            }
            catch (IOException)
            {
                wasLocked = true;
                string oldPath = destPath + ".old";
                try { if (File.Exists(oldPath)) File.Delete(oldPath); } catch { }
                File.Move(destPath, oldPath);
                entry.ExtractToFile(destPath, true);
                Log(Loc.F("  -> {0} era in uso: versione precedente spostata in .old", Path.GetFileName(destPath)));
            }

            if (wasLocked)
            {
                string processPath = destPath + ".old";
                if (KillProcessRunningFrom(processPath))
                {
                    _processesToRestart.Add(destPath);
                    Log(Loc.F("  -> Processo {0} terminato, verrà riavviato al termine.", Path.GetFileNameWithoutExtension(destPath)));
                }
            }
        }

        // Termina i processi in esecuzione a partire da filePath (stesso exe e stesso percorso).
        private bool KillProcessRunningFrom(string filePath)
        {
            string processName = Path.GetFileNameWithoutExtension(filePath);
            bool killed = false;
            foreach (var proc in Process.GetProcessesByName(processName))
            {
                try
                {
                    if (string.Equals(proc.MainModule?.FileName, filePath, StringComparison.OrdinalIgnoreCase))
                    {
                        proc.Kill();
                        killed = true;
                    }
                }
                catch { /* processo già terminato o inaccessibile */ }
                finally { proc.Dispose(); }
            }
            return killed;
        }

        private void RestartUpdatedProcesses()
        {
            foreach (string path in _processesToRestart)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
                        Log(Loc.F("Riavviato: {0}", Path.GetFileName(path)));
                    }
                }
                catch (Exception ex)
                {
                    Log(Loc.F("Impossibile riavviare {0}: {1}", Path.GetFileName(path), ex.Message));
                }
            }
            _processesToRestart.Clear();
        }

        private bool ShouldPreserveFile(string filePath, List<string> rules)
        {
            if (rules == null || rules.Count == 0) return false;

            string normalizedPath = filePath.Replace("/", "\\");

            foreach (var rule in rules)
            {
                string normalizedRule = rule.Replace("/", "\\");

                if (normalizedRule.Contains("*"))
                {
                    string regexPattern = "^" + Regex.Escape(normalizedRule).Replace("\\*", ".*") + "$";
                    if (Regex.IsMatch(normalizedPath, regexPattern, RegexOptions.IgnoreCase))
                    {
                        return true;
                    }
                }
                else
                {
                    if (normalizedPath.Equals(normalizedRule, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }

    // --- CLASSI DI SUPPORTO ---

    public class ComponentViewModel : INotifyPropertyChanged
    {
        public string Key { get; set; }
        public ComponentData Data { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

        private string _localVersion;
        public string LocalVersion { get => _localVersion; set { _localVersion = value; OnPropertyChanged(nameof(LocalVersion)); } }

        public string RemoteVersion { get; set; }

        private string _status;
        public string Status { get => _status; set { _status = value; OnPropertyChanged(nameof(Status)); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class ExtraViewModel : INotifyPropertyChanged
    {
        public ExtraData Data { get; set; }
        public string Name => Data.Name;
        public string Description => Data.Description;

        public string ActionButtonText => Data.Action == "run_installer"
            ? Loc.T("Scarica ed Esegui")
            : Loc.T("Salva file...");

        public Visibility ActionButtonVisibility => (Data.Action == "run_installer" || Data.Action == "save_as") ? Visibility.Visible : Visibility.Collapsed;
        public Visibility GuideButtonVisibility => !string.IsNullOrEmpty(Data.GuideUrl) ? Visibility.Visible : Visibility.Collapsed;

        private string _status = "";
        public string Status { get => _status; set { _status = value; OnPropertyChanged(nameof(Status)); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
