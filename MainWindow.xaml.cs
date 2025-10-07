using Microsoft.Win32;
using SigmaMS.Dialogs;
using SigmaMS.Models;
using SigmaMS.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SigmaMS {
    public partial class MainWindow : Window, INotifyPropertyChanged {
        private readonly DataService _dataService;
        private readonly ConfigurationService _configService;
        private ObservableCollection<DatabaseConnection> _connections;
        private List<DatabaseObject> _allObjects;
        private List<DatabaseObject> _filteredObjects;
        private string _currentDatabase = string.Empty;
        private string _currentConnectionName = string.Empty;
        private bool _isLoadingFilters = false;
        private string _currentSelectedObjectType = string.Empty;

        public ObservableCollection<DatabaseConnection> Connections => _connections;

        public MainWindow() {
            InitializeComponent();

            _dataService = new DataService();
            _configService = new ConfigurationService();
            _connections = new ObservableCollection<DatabaseConnection>();
            _allObjects = new List<DatabaseObject>();
            _filteredObjects = new List<DatabaseObject>();

            DataContext = this;
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e) {
            try {
                await LoadConnectionsAsync();
                UpdateStatus("Pronto");
            } catch (Exception ex) {
                MessageBox.Show($"Errore durante l'inizializzazione: {ex.Message}", "Errore",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task LoadConnectionsAsync() {
            try {
                var connections = await _configService.LoadConnectionsAsync();
                _connections.Clear();
                foreach (var conn in connections) {
                    _connections.Add(conn);
                }

                cmbConnections.ItemsSource = _connections;

                if (_connections.Count > 0) {
                    cmbConnections.SelectedIndex = 0;
                }
            } catch (Exception ex) {
                throw new Exception($"Errore nel caricamento delle connessioni: {ex.Message}", ex);
            }
        }

        private async void CmbConnections_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (cmbConnections.SelectedItem is DatabaseConnection connection) {
                try {
                    UpdateStatus("Caricamento database...");
                    _currentConnectionName = connection.Name;

                    var databases = await _dataService.GetDatabasesAsync(connection.ConnectionString);
                    cmbDatabases.ItemsSource = databases;

                    // Cerca l'ultimo database utilizzato per questa connessione
                    var lastUsedDb = await _configService.GetLastUsedDatabaseAsync(connection.Name);

                    if (!string.IsNullOrWhiteSpace(lastUsedDb) && databases.Contains(lastUsedDb)) {
                        // Se l'ultimo database utilizzato esiste ancora, selezionalo
                        cmbDatabases.SelectedItem = lastUsedDb;
                        UpdateStatus($"Selezionato ultimo database utilizzato: {lastUsedDb}");
                    } else if (databases.Count > 0) {
                        // Altrimenti seleziona il primo database disponibile
                        cmbDatabases.SelectedIndex = 0;
                        UpdateStatus("Database caricati");
                    }
                } catch (Exception ex) {
                    MessageBox.Show($"Errore nel caricamento dei database: {ex.Message}", "Errore",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    UpdateStatus("Errore");
                }
            }
        }

        private async void CmbDatabases_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (cmbDatabases.SelectedItem is string database &&
                cmbConnections.SelectedItem is DatabaseConnection connection) {
                try {
                    UpdateStatus("Caricamento oggetti database...");
                    _currentDatabase = database;

                    // Salva questo database come ultimo utilizzato per questa connessione
                    await _configService.UpdateLastUsedDatabaseAsync(connection.Name, database);

                    var dbConnection = new DatabaseConnection {
                        Name = connection.Name,
                        Server = connection.Server,
                        Database = database,
                        IntegratedSecurity = connection.IntegratedSecurity,
                        Username = connection.Username,
                        Password = connection.Password
                    };

                    await LoadDatabaseObjectsAsync(dbConnection.ConnectionString);

                    // Carica i filtri salvati per questo database
                    await LoadSavedFiltersAsync();

                    UpdateStatus($"Caricati oggetti per database: {database}");
                } catch (Exception ex) {
                    MessageBox.Show($"Errore nel caricamento degli oggetti: {ex.Message}", "Errore",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    UpdateStatus("Errore");
                }
            }
        }

        //private async Task LoadDatabaseObjectsAsync(string connectionString) {
        //    _allObjects.Clear();

        //    try {
        //        // Carica tutti i tipi di oggetti - AGGIUNTA: tabelle
        //        var tables = await _dataService.GetTablesAsync(connectionString);
        //        var storedProcs = await _dataService.GetStoredProceduresAsync(connectionString);
        //        var views = await _dataService.GetViewsAsync(connectionString);
        //        var functions = await _dataService.GetFunctionsAsync(connectionString);
        //        var triggers = await _dataService.GetTriggersAsync(connectionString);

        //        // Aggiungi tutti gli oggetti alla collezione
        //        _allObjects.AddRange(tables);
        //        _allObjects.AddRange(storedProcs);
        //        _allObjects.AddRange(views);
        //        _allObjects.AddRange(functions);
        //        _allObjects.AddRange(triggers);

        //        // Applica i filtri e aggiorna la vista
        //        ApplyFilters();
        //        UpdateObjectTypeTree();
        //    } catch (Exception ex) {
        //        throw new Exception($"Errore nel caricamento degli oggetti: {ex.Message}", ex);
        //    }
        //}

        private async Task LoadDatabaseObjectsAsync(string connectionString) {
            _allObjects.Clear();

            try {
                // Carica tutti i tipi di oggetti
                var tables = await _dataService.GetTablesAsync(connectionString);
                var storedProcs = await _dataService.GetStoredProceduresAsync(connectionString);
                var views = await _dataService.GetViewsAsync(connectionString);
                var functions = await _dataService.GetFunctionsAsync(connectionString);
                var triggers = await _dataService.GetTriggersAsync(connectionString);

                // NUOVI tipi di oggetti
                var userTableTypes = await _dataService.GetUserDefinedTableTypesAsync(connectionString);
                var userDataTypes = await _dataService.GetUserDefinedDataTypesAsync(connectionString);
                var synonyms = await _dataService.GetSynonymsAsync(connectionString);

                // Aggiungi tutti gli oggetti alla collezione
                _allObjects.AddRange(tables);
                _allObjects.AddRange(storedProcs);
                _allObjects.AddRange(views);
                _allObjects.AddRange(functions);
                _allObjects.AddRange(triggers);
                _allObjects.AddRange(userTableTypes);
                _allObjects.AddRange(userDataTypes);
                _allObjects.AddRange(synonyms);

                // Applica i filtri e aggiorna la vista
                ApplyFilters();
                UpdateObjectTypeTree();
            } catch (Exception ex) {
                throw new Exception($"Errore nel caricamento degli oggetti: {ex.Message}", ex);
            }
        }

        private void ApplyFilters() {
            _filteredObjects = _allObjects.ToList();

            // Applica Filtro 1
            var filter1 = txtFilter1.Text.Trim();
            if (!string.IsNullOrWhiteSpace(filter1)) {
                if (filter1.EndsWith("%")) {
                    // Filtro con wildcard - inizia con
                    var prefix = filter1.Substring(0, filter1.Length - 1);
                    _filteredObjects = _filteredObjects.Where(o =>
                        o.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
                } else {
                    // Filtro contiene
                    _filteredObjects = _filteredObjects.Where(o =>
                        o.Name.Contains(filter1, StringComparison.OrdinalIgnoreCase)).ToList();
                }
            }

            // Applica Filtro 2
            var filter2 = txtFilter2.Text.Trim();
            if (!string.IsNullOrWhiteSpace(filter2)) {
                if (filter2.EndsWith("%")) {
                    // Filtro con wildcard - inizia con
                    var prefix = filter2.Substring(0, filter2.Length - 1);
                    _filteredObjects = _filteredObjects.Where(o =>
                        o.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
                } else {
                    // Filtro contiene
                    _filteredObjects = _filteredObjects.Where(o =>
                        o.Name.Contains(filter2, StringComparison.OrdinalIgnoreCase)).ToList();
                }
            }

            // Applica Filtro 3
            var filter3 = txtFilter3.Text.Trim();
            if (!string.IsNullOrWhiteSpace(filter3)) {
                if (filter3.EndsWith("%")) {
                    // Filtro con wildcard - inizia con
                    var prefix = filter3.Substring(0, filter3.Length - 1);
                    _filteredObjects = _filteredObjects.Where(o =>
                        o.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
                } else {
                    // Filtro contiene
                    _filteredObjects = _filteredObjects.Where(o =>
                        o.Name.Contains(filter3, StringComparison.OrdinalIgnoreCase)).ToList();
                }
            }

            UpdateFilterStatus();
        }

        //private void UpdateObjectTypeTree() {
        //    tvObjectTypes.Items.Clear();

        //    // Raggruppa per tipo di oggetto
        //    var groupedObjects = _filteredObjects.GroupBy(o => o.Type).OrderBy(g => g.Key);

        //    TreeViewItem? itemToSelect = null;
        //    bool foundLastSelected = false;

        //    foreach (var group in groupedObjects) {
        //        var typeNode = new TreeViewItem {
        //            Header = $"{GetTypeIcon(group.Key)} {group.Key} ({group.Count()})",
        //            Tag = group.Key,
        //            IsExpanded = true
        //        };

        //        tvObjectTypes.Items.Add(typeNode);

        //        // Se questo è il tipo di oggetto selezionato in precedenza, memorizzalo per la selezione
        //        if (group.Key == _currentSelectedObjectType) {
        //            itemToSelect = typeNode;
        //            foundLastSelected = true;
        //        }

        //        // Se non abbiamo ancora un elemento da selezionare, usa il primo
        //        if (itemToSelect == null) {
        //            itemToSelect = typeNode;
        //        }
        //    }

        //    // Seleziona il tipo di oggetto appropriato
        //    if (itemToSelect != null) {
        //        itemToSelect.IsSelected = true;

        //        // Se non abbiamo trovato l'ultimo tipo selezionato, aggiorna la variabile con il primo disponibile
        //        if (!foundLastSelected && itemToSelect.Tag is string firstType) {
        //            _currentSelectedObjectType = firstType;
        //        }
        //    } else {
        //        // Nessun oggetto filtrato
        //        dgObjects.ItemsSource = new List<DatabaseObject>();
        //        UpdateObjectCount();
        //        _currentSelectedObjectType = string.Empty;
        //    }
        //}

        private void UpdateObjectTypeTree() {
            tvObjectTypes.Items.Clear();

            // Raggruppa gli oggetti filtrati per tipo
            var groupedObjects = _filteredObjects.GroupBy(o => o.Type).ToDictionary(g => g.Key, g => g.Count());

            TreeViewItem? itemToSelect = null;
            bool foundLastSelected = false;

            // Aggiungi l'opzione "Tutti" come primo elemento
            var allNode = new TreeViewItem {
                Header = $"📋 Tutti ({_filteredObjects.Count})",
                Tag = "Tutti",
                IsExpanded = true
            };
            tvObjectTypes.Items.Add(allNode);

            // Se "Tutti" è il tipo selezionato o se non c'è selezione, seleziona "Tutti"
            if (_currentSelectedObjectType == "Tutti" || string.IsNullOrEmpty(_currentSelectedObjectType)) {
                itemToSelect = allNode;
                foundLastSelected = true;
            }

            // Lista completa di tutti i tipi di oggetti possibili
            var allObjectTypes = new Dictionary<string, string> {
        { "Table", "🗃️" },
        { "View", "👁️" },
        { "Stored Procedure", "⚙️" },
        { "Scalar Function", "🔢" },
        { "Inline Table Function", "📊" },
        { "Table Function", "📋" },
        { "Trigger", "⚡" },
        { "User Defined Table Type", "📄" },
        { "User Defined Data Type", "🔤" },
        { "Synonym", "🔗" },
        { "Sequence", "🔢" },
        { "Index", "🗂️" },
        { "Constraint", "🔒" },
        { "Schema", "📁" }
    };

            // Crea i nodi per tutti i tipi, anche quelli con 0 risultati
            foreach (var objectType in allObjectTypes) {
                var typeName = objectType.Key;
                var icon = objectType.Value;
                var count = groupedObjects.ContainsKey(typeName) ? groupedObjects[typeName] : 0;

                var typeNode = new TreeViewItem {
                    Header = $"{icon} {typeName} ({count})",
                    Tag = typeName,
                    IsExpanded = true
                };

                // Se non ci sono oggetti di questo tipo, rendi il nodo leggermente diverso visualmente
                if (count == 0) {
                    typeNode.Foreground = System.Windows.Media.Brushes.Gray;
                    typeNode.FontStyle = FontStyles.Italic;
                }

                tvObjectTypes.Items.Add(typeNode);

                // Se questo è il tipo di oggetto selezionato in precedenza, memorizzalo per la selezione
                if (typeName == _currentSelectedObjectType) {
                    itemToSelect = typeNode;
                    foundLastSelected = true;
                }
            }

            // Se non abbiamo trovato l'ultimo tipo selezionato, usa "Tutti" come default
            if (!foundLastSelected) {
                itemToSelect = allNode;
                _currentSelectedObjectType = "Tutti";
            }

            // Seleziona il tipo di oggetto appropriato
            if (itemToSelect != null) {
                itemToSelect.IsSelected = true;

                // Aggiorna la variabile con il tipo selezionato
                if (itemToSelect.Tag is string selectedType) {
                    _currentSelectedObjectType = selectedType;
                }
            }

            // Aggiorna la griglia anche se non ci sono oggetti del tipo selezionato
            UpdateDataGridForSelectedType();
        }

        private void UpdateDataGridForSelectedType() {
            if (!string.IsNullOrEmpty(_currentSelectedObjectType)) {
                if (_currentSelectedObjectType == "Tutti") {
                    // Mostra tutti gli oggetti filtrati senza filtro di tipo
                    dgObjects.ItemsSource = _filteredObjects.OrderBy(o => o.Type).ThenBy(o => o.Name).ToList();
                } else {
                    // Filtra per il tipo specifico selezionato
                    var objectsOfType = _filteredObjects.Where(o => o.Type == _currentSelectedObjectType).OrderBy(o => o.Name).ToList();
                    dgObjects.ItemsSource = objectsOfType;
                }
                UpdateObjectCount();
            } else {
                dgObjects.ItemsSource = new List<DatabaseObject>();
                UpdateObjectCount();
            }
        }

        private string GetTypeIcon(string objectType) {
            return objectType.ToLower() switch {
                "table" => "🗃️",
                "stored procedure" => "⚙️",
                "view" => "👁️",
                "scalar function" => "🔢",
                "inline table function" => "📊",
                "table function" => "📋",
                "trigger" => "⚡",
                _ => "📄"
            };
        }

        //private void TvObjectTypes_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) {
        //    if (tvObjectTypes.SelectedItem is TreeViewItem item && item.Tag is string objectType) {
        //        _currentSelectedObjectType = objectType;

        //        var objectsOfType = _filteredObjects.Where(o => o.Type == objectType).OrderBy(o => o.Name).ToList();
        //        dgObjects.ItemsSource = objectsOfType;
        //        UpdateObjectCount();

        //        // Salva il tipo di oggetto selezionato
        //        SaveCurrentSelectionWithDelay();
        //    }
        //}

        private void TvObjectTypes_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) {
            if (tvObjectTypes.SelectedItem is TreeViewItem item && item.Tag is string objectType) {
                _currentSelectedObjectType = objectType;
                UpdateDataGridForSelectedType();

                // Salva il tipo di oggetto selezionato
                SaveCurrentSelectionWithDelay();
            }
        }
        private void Filter_TextChanged(object sender, TextChangedEventArgs e) {
            // Evita di applicare filtri mentre stiamo caricando i filtri salvati
            if (_isLoadingFilters) return;

            if (_allObjects.Any()) {
                ApplyFilters();
                UpdateObjectTypeTree();

                // Salva i filtri automaticamente dopo un breve ritardo
                SaveFiltersWithDelay();
            }
        }

        private System.Windows.Threading.DispatcherTimer? _saveTimer;
        private System.Windows.Threading.DispatcherTimer? _saveSelectionTimer;

        private void SaveFiltersWithDelay() {
            // Cancella il timer precedente se esiste
            _saveTimer?.Stop();

            // Crea un nuovo timer che salva dopo 2 secondi di inattività
            _saveTimer = new System.Windows.Threading.DispatcherTimer {
                Interval = TimeSpan.FromSeconds(2)
            };

            _saveTimer.Tick += async (s, e) => {
                _saveTimer?.Stop();
                await SaveCurrentFiltersAsync();
            };

            _saveTimer.Start();
        }

        private void SaveCurrentSelectionWithDelay() {
            // Cancella il timer precedente se esiste
            _saveSelectionTimer?.Stop();

            // Crea un nuovo timer per salvare la selezione dopo 1 secondo
            _saveSelectionTimer = new System.Windows.Threading.DispatcherTimer {
                Interval = TimeSpan.FromSeconds(1)
            };

            _saveSelectionTimer.Tick += async (s, e) => {
                _saveSelectionTimer?.Stop();
                await SaveCurrentFiltersAsync(); // Salva tutto insieme
            };

            _saveSelectionTimer.Start();
        }

        private async Task SaveCurrentFiltersAsync() {
            try {
                if (!string.IsNullOrWhiteSpace(_currentConnectionName) && !string.IsNullOrWhiteSpace(_currentDatabase)) {
                    await _configService.SaveFiltersForDatabaseAsync(
                        _currentConnectionName,
                        _currentDatabase,
                        txtFilter1.Text,
                        txtFilter2.Text,
                        txtFilter3.Text,
                        _currentSelectedObjectType
                    );
                }
            } catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"Errore nel salvataggio filtri: {ex.Message}");
            }
        }

        private async Task LoadSavedFiltersAsync() {
            try {
                _isLoadingFilters = true;

                var savedFilters = await _configService.GetFiltersForDatabaseAsync(_currentConnectionName, _currentDatabase);

                if (savedFilters != null) {
                    txtFilter1.Text = savedFilters.Filter1;
                    txtFilter2.Text = savedFilters.Filter2;
                    txtFilter3.Text = savedFilters.Filter3;
                    _currentSelectedObjectType = savedFilters.LastSelectedObjectType;

                    // Applica i filtri caricati
                    ApplyFilters();
                    UpdateObjectTypeTree(); // Questo selezionerà automaticamente il tipo salvato
                }
            } catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"Errore nel caricamento filtri salvati: {ex.Message}");
            } finally {
                _isLoadingFilters = false;
            }
        }

        private async void ClearFilters_Click(object sender, RoutedEventArgs e) {
            txtFilter1.Text = string.Empty;
            txtFilter2.Text = string.Empty;
            txtFilter3.Text = string.Empty;
            _currentSelectedObjectType = string.Empty; // Reset anche il tipo selezionato

            if (_allObjects.Any()) {
                ApplyFilters();
                UpdateObjectTypeTree();
            }

            // Salva immediatamente i filtri vuoti
            await SaveCurrentFiltersAsync();
        }

        private async void SearchInScript_Click(object sender, RoutedEventArgs e) {
            if (cmbConnections.SelectedItem is not DatabaseConnection connection ||
                string.IsNullOrEmpty(_currentDatabase)) {
                MessageBox.Show("Seleziona prima una connessione e un database.", "Attenzione",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Raccoglie tutti i testi di ricerca non vuoti
            var searchTexts = new List<string>();
            if (!string.IsNullOrWhiteSpace(txtFilter1.Text)) searchTexts.Add(txtFilter1.Text.Trim().TrimEnd('%'));
            if (!string.IsNullOrWhiteSpace(txtFilter2.Text)) searchTexts.Add(txtFilter2.Text.Trim().TrimEnd('%'));
            if (!string.IsNullOrWhiteSpace(txtFilter3.Text)) searchTexts.Add(txtFilter3.Text.Trim().TrimEnd('%'));

            if (searchTexts.Count == 0) {
                MessageBox.Show("Inserisci almeno un testo di ricerca in uno dei filtri.", "Attenzione",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try {
                UpdateStatus("🔍 Ricerca nel testo degli script in corso...");
                Mouse.OverrideCursor = Cursors.Wait;
                btnSearchInScript.IsEnabled = false;

                var dbConnection = new DatabaseConnection {
                    Name = connection.Name,
                    Server = connection.Server,
                    Database = _currentDatabase,
                    IntegratedSecurity = connection.IntegratedSecurity,
                    Username = connection.Username,
                    Password = connection.Password
                };

                // Esegui la ricerca per ogni termine e unisci i risultati
                var foundObjects = new List<DatabaseObject>();
                foreach (var searchText in searchTexts) {
                    var results = await _dataService.SearchInScriptTextAsync(dbConnection.ConnectionString, searchText);
                    foundObjects.AddRange(results);
                }

                // Rimuovi duplicati
                foundObjects = foundObjects.GroupBy(o => new { o.Schema, o.Name, o.Type })
                                          .Select(g => g.First())
                                          .ToList();

                if (foundObjects.Count == 0) {
                    MessageBox.Show($"Nessun oggetto trovato contenente il testo cercato.", "Risultato Ricerca",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    UpdateStatus("❌ Nessun risultato");
                } else {
                    // Sostituisci completamente la lista filtrata con i risultati della ricerca
                    _filteredObjects = foundObjects;
                    UpdateObjectTypeTree();
                    UpdateStatus($"✅ Trovati {foundObjects.Count} oggetti");
                }
            } catch (Exception ex) {
                MessageBox.Show($"Errore durante la ricerca nel testo: {ex.Message}", "Errore",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatus("❌ Errore nella ricerca");
            } finally {
                Mouse.OverrideCursor = null;
                btnSearchInScript.IsEnabled = true;

                // Ripristina status dopo 3 secondi
                _ = Task.Delay(3000).ContinueWith(_ =>
                    Dispatcher.BeginInvoke(() => UpdateStatus("Pronto")));
            }
        }

        private void UpdateFilterStatus() {
            var activeFilters = new List<string>();

            if (!string.IsNullOrWhiteSpace(txtFilter1.Text)) activeFilters.Add("F1");
            if (!string.IsNullOrWhiteSpace(txtFilter2.Text)) activeFilters.Add("F2");
            if (!string.IsNullOrWhiteSpace(txtFilter3.Text)) activeFilters.Add("F3");

            if (activeFilters.Any()) {
                txtFilterStatus.Text = $"Filtri attivi: {string.Join(", ", activeFilters)} | {_filteredObjects.Count}/{_allObjects.Count}";
            } else {
                txtFilterStatus.Text = $"Nessun filtro | {_allObjects.Count} oggetti";
            }
        }

        private void UpdateObjectCount() {
            if (dgObjects.ItemsSource is IEnumerable<DatabaseObject> objects) {
                var count = objects.Count();
                txtObjectCount.Text = $"Oggetti: {count}";
            }
        }

        private void UpdateStatus(string message) {
            txtStatus.Text = message;
        }

        // Event handlers per i menu
        private async void NewConnection_Click(object sender, RoutedEventArgs e) {
            var dialog = new ConnectionDialog();
            if (dialog.ShowDialog() == true && dialog.Connection != null) {
                _connections.Add(dialog.Connection);
                await _configService.SaveConnectionsAsync(_connections.ToList());
                cmbConnections.SelectedItem = dialog.Connection;
            }
        }

        private async void EditConnection_Click(object sender, RoutedEventArgs e) {
            if (cmbConnections.SelectedItem is DatabaseConnection connection) {
                var dialog = new ConnectionDialog(connection);
                if (dialog.ShowDialog() == true && dialog.Connection != null) {
                    var index = _connections.IndexOf(connection);
                    _connections[index] = dialog.Connection;
                    await _configService.SaveConnectionsAsync(_connections.ToList());
                }
            }
        }

        private async void DeleteConnection_Click(object sender, RoutedEventArgs e) {
            if (cmbConnections.SelectedItem is DatabaseConnection connection) {
                var result = MessageBox.Show($"Eliminare la connessione '{connection.Name}'?",
                    "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes) {
                    _connections.Remove(connection);
                    await _configService.SaveConnectionsAsync(_connections.ToList());
                }
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e) {
            Close();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) {
            if (cmbConnections.SelectedItem is DatabaseConnection connection &&
                cmbDatabases.SelectedItem is string database) {

                var dbConnection = new DatabaseConnection {
                    Name = connection.Name,
                    Server = connection.Server,
                    Database = database,
                    IntegratedSecurity = connection.IntegratedSecurity,
                    Username = connection.Username,
                    Password = connection.Password
                };

                await LoadDatabaseObjectsAsync(dbConnection.ConnectionString);
            }
        }

        private async void ScriptObject_Click(object sender, RoutedEventArgs e) {
            if (dgObjects.SelectedItem is DatabaseObject obj &&
                cmbConnections.SelectedItem is DatabaseConnection connection &&
                !string.IsNullOrEmpty(_currentDatabase)) {
                try {
                    UpdateStatus($"📋 Caricamento script per {obj.Name}...");
                    Mouse.OverrideCursor = Cursors.Wait;

                    // Crea la connessione al database specifico
                    var dbConnection = new DatabaseConnection {
                        Name = connection.Name,
                        Server = connection.Server,
                        Database = _currentDatabase,  // Database attualmente selezionato
                        IntegratedSecurity = connection.IntegratedSecurity,
                        Username = connection.Username,
                        Password = connection.Password
                    };

                    // Debug: verifica la connection string
                    System.Diagnostics.Debug.WriteLine($"MainWindow - Connection string: {dbConnection.ConnectionString}");

                    var script = await _dataService.GetObjectScriptAsync(
                        dbConnection.ConnectionString, obj.Name, obj.Type, obj.Schema);

                    // CORREZIONE: Passa la connection string al ScriptWindow
                    var scriptWindow = new ScriptWindow(obj.FullName, script, dbConnection.ConnectionString);
                    scriptWindow.Show();

                    UpdateStatus($"✅ Script aperto per {obj.Name}");
                } catch (Exception ex) {
                    MessageBox.Show($"Errore nel recupero dello script: {ex.Message}", "Errore",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    UpdateStatus("❌ Errore nel caricamento script");
                } finally {
                    Mouse.OverrideCursor = null;

                    // Ripristina status dopo 3 secondi
                    _ = Task.Delay(3000).ContinueWith(_ =>
                        Dispatcher.BeginInvoke(() => UpdateStatus("Pronto")));
                }
            }
        }

        private async void NewScript_Click(object sender, RoutedEventArgs e) {
            if (cmbConnections.SelectedItem is DatabaseConnection connection &&
                !string.IsNullOrEmpty(_currentDatabase)) {
                try {
                    // Crea la connessione al database attualmente selezionato
                    var dbConnection = new DatabaseConnection {
                        Name = connection.Name,
                        Server = connection.Server,
                        Database = _currentDatabase,
                        IntegratedSecurity = connection.IntegratedSecurity,
                        Username = connection.Username,
                        Password = connection.Password
                    };

                    // Crea uno script vuoto con template iniziale
                    var initialScript = $@"-- Nuovo Script SQL
-- Database: {_currentDatabase}
-- Server: {connection.Server}
-- Creato il: {DateTime.Now:dd/MM/yyyy HH:mm:ss}

SELECT 

";

                    var scriptWindow = new ScriptWindow($"Nuovo Script - {_currentDatabase}", initialScript, dbConnection.ConnectionString);
                    scriptWindow.Show();
                    scriptWindow.SetCursorAfterSelect();

                    UpdateStatus($"✅ Nuovo script aperto per {_currentDatabase}");
                } catch (Exception ex) {
                    MessageBox.Show($"Errore nell'apertura del nuovo script: {ex.Message}", "Errore",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    UpdateStatus("❌ Errore nell'apertura script");
                }
            } else {
                MessageBox.Show("Seleziona prima una connessione e un database.", "Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void DgObjects_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
            // Doppio click apre lo script
            ScriptObject_Click(sender, new RoutedEventArgs());
        }

        private async void ExportConfig_Click(object sender, RoutedEventArgs e) {
            var dialog = new SaveFileDialog {
                Filter = "File JSON|*.json",
                Title = "Esporta Configurazione"
            };

            if (dialog.ShowDialog() == true) {
                try {
                    await _configService.ExportConfigurationAsync(dialog.FileName);
                    MessageBox.Show("Configurazione esportata con successo.", "Successo",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                } catch (Exception ex) {
                    MessageBox.Show($"Errore nell'esportazione: {ex.Message}", "Errore",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void ImportConfig_Click(object sender, RoutedEventArgs e) {
            var dialog = new OpenFileDialog {
                Filter = "File JSON|*.json",
                Title = "Importa Configurazione"
            };

            if (dialog.ShowDialog() == true) {
                try {
                    await _configService.ImportConfigurationAsync(dialog.FileName);
                    await LoadConnectionsAsync();

                    MessageBox.Show("Configurazione importata con successo.", "Successo",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                } catch (Exception ex) {
                    MessageBox.Show($"Errore nell'importazione: {ex.Message}", "Errore",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}