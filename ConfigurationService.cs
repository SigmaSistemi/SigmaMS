using Newtonsoft.Json;
using SigmaMS.Models;
using System.IO;

namespace SigmaMS.Services {
    public class ConfigurationService {
        private const string ConfigFileName = "SQLManagerConfig.json";
        private const string FiltersFileName = "DatabaseFilters.json";
        private readonly string _configPath;
        private readonly string _filtersPath;

        public ConfigurationService() {
            var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SQLManagerWPF");
            Directory.CreateDirectory(appDataPath);

            _configPath = Path.Combine(appDataPath, ConfigFileName);
            _filtersPath = Path.Combine(appDataPath, FiltersFileName);
        }

        public async Task<List<DatabaseConnection>> LoadConnectionsAsync() {
            try {
                if (!File.Exists(_configPath))
                    return new List<DatabaseConnection>();

                var json = await File.ReadAllTextAsync(_configPath);
                var connections = JsonConvert.DeserializeObject<List<DatabaseConnection>>(json);
                return connections ?? new List<DatabaseConnection>();
            } catch (Exception ex) {
                throw new Exception($"Errore nel caricamento delle connessioni: {ex.Message}", ex);
            }
        }

        public async Task SaveConnectionsAsync(List<DatabaseConnection> connections) {
            try {
                var json = JsonConvert.SerializeObject(connections, Formatting.Indented);
                await File.WriteAllTextAsync(_configPath, json);
            } catch (Exception ex) {
                throw new Exception($"Errore nel salvataggio delle connessioni: {ex.Message}", ex);
            }
        }

        public async Task ExportConfigurationAsync(string filePath) {
            try {
                var config = new {
                    Connections = await LoadConnectionsAsync(),
                    ExportDate = DateTime.Now
                };

                var json = JsonConvert.SerializeObject(config, Formatting.Indented);
                await File.WriteAllTextAsync(filePath, json);
            } catch (Exception ex) {
                throw new Exception($"Errore nell'esportazione della configurazione: {ex.Message}", ex);
            }
        }

        public async Task ImportConfigurationAsync(string filePath) {
            try {
                var json = await File.ReadAllTextAsync(filePath);
                var config = JsonConvert.DeserializeAnonymousType(json, new {
                    Connections = new List<DatabaseConnection>(),
                    ExportDate = DateTime.Now
                });

                if (config?.Connections != null)
                    await SaveConnectionsAsync(config.Connections);
            } catch (Exception ex) {
                throw new Exception($"Errore nell'importazione della configurazione: {ex.Message}", ex);
            }
        }

        public async Task UpdateLastUsedDatabaseAsync(string connectionName, string databaseName) {
            try {
                var connections = await LoadConnectionsAsync();
                var connection = connections.FirstOrDefault(c => c.Name == connectionName);

                if (connection != null) {
                    connection.LastUsedDatabase = databaseName;
                    await SaveConnectionsAsync(connections);
                }
            } catch (Exception ex) {
                throw new Exception($"Errore nell'aggiornamento dell'ultimo database utilizzato: {ex.Message}", ex);
            }
        }

        public async Task<string?> GetLastUsedDatabaseAsync(string connectionName) {
            try {
                var connections = await LoadConnectionsAsync();
                var connection = connections.FirstOrDefault(c => c.Name == connectionName);

                return connection?.LastUsedDatabase;
            } catch (Exception ex) {
                // In caso di errore, ritorna null per non bloccare l'applicazione
                System.Diagnostics.Debug.WriteLine($"Errore nel recupero dell'ultimo database: {ex.Message}");
                return null;
            }
        }

        public async Task<List<DatabaseFilterSettings>> LoadFilterSettingsAsync() {
            try {
                if (!File.Exists(_filtersPath))
                    return new List<DatabaseFilterSettings>();

                var json = await File.ReadAllTextAsync(_filtersPath);
                var filters = JsonConvert.DeserializeObject<List<DatabaseFilterSettings>>(json);
                return filters ?? new List<DatabaseFilterSettings>();
            } catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"Errore nel caricamento dei filtri: {ex.Message}");
                return new List<DatabaseFilterSettings>();
            }
        }

        public async Task SaveFilterSettingsAsync(List<DatabaseFilterSettings> filterSettings) {
            try {
                var json = JsonConvert.SerializeObject(filterSettings, Formatting.Indented);
                await File.WriteAllTextAsync(_filtersPath, json);
            } catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"Errore nel salvataggio dei filtri: {ex.Message}");
            }
        }

        public async Task SaveFiltersForDatabaseAsync(string connectionName, string databaseName,
            string filter1, string filter2, string filter3, string lastSelectedObjectType = "") {
            try {
                var allFilters = await LoadFilterSettingsAsync();

                // Rimuovi eventuali filtri esistenti per questa combinazione
                allFilters.RemoveAll(f => f.ConnectionName == connectionName && f.DatabaseName == databaseName);

                // Aggiungi i nuovi filtri (salva sempre, anche se tutti vuoti, per ricordare il tipo di oggetto)
                allFilters.Add(new DatabaseFilterSettings {
                    ConnectionName = connectionName,
                    DatabaseName = databaseName,
                    Filter1 = filter1?.Trim() ?? string.Empty,
                    Filter2 = filter2?.Trim() ?? string.Empty,
                    Filter3 = filter3?.Trim() ?? string.Empty,
                    LastSelectedObjectType = lastSelectedObjectType?.Trim() ?? string.Empty,
                    LastUsed = DateTime.Now
                });

                await SaveFilterSettingsAsync(allFilters);
            } catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"Errore nel salvataggio filtri database: {ex.Message}");
            }
        }

        public async Task<DatabaseFilterSettings?> GetFiltersForDatabaseAsync(string connectionName, string databaseName) {
            try {
                var allFilters = await LoadFilterSettingsAsync();
                return allFilters.FirstOrDefault(f => f.ConnectionName == connectionName && f.DatabaseName == databaseName);
            } catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"Errore nel recupero filtri database: {ex.Message}");
                return null;
            }
        }
    }
}