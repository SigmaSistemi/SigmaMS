using ICSharpCode.AvalonEdit.CodeCompletion;
using Microsoft.Data.SqlClient;
using SigmaMS.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace SigmaMS.Editor {
    public class SqlCompletionProvider {
        private readonly string _connectionString;
        private List<DatabaseObjectInfo> _allObjects = new List<DatabaseObjectInfo>();
        private DateTime _lastCacheUpdate = DateTime.MinValue;
        private readonly TimeSpan _cacheValidityPeriod = TimeSpan.FromMinutes(30);

        public SqlCompletionProvider(string connectionString) {
            _connectionString = connectionString;
        }

        public async Task<List<ICompletionData>> GetCompletionDataAsync(string searchTerm) {
            // Aggiorna la cache se necessario
            if (DateTime.Now - _lastCacheUpdate > _cacheValidityPeriod) {
                await RefreshDatabaseObjectsAsync();
            }

            var completionData = new List<ICompletionData>();

            // Se non c'è termine di ricerca, mostra i più comuni
            if (string.IsNullOrWhiteSpace(searchTerm)) {
                // Mostra le prime 50 tabelle ordinate alfabeticamente
                var topTables = _allObjects
                    .Where(o => o.Type == "Table")
                    .OrderBy(o => o.Name)
                    .Take(50)
                    .ToList();

                foreach (var obj in topTables) {
                    completionData.Add(new SqlCompletionData(obj.FullName, obj.Description, GetCompletionType(obj)));
                }

                return completionData;
            }

            // Ricerca intelligente basata sul termine inserito
            var searchLower = searchTerm.ToLowerInvariant();

            // Usa un Dictionary per evitare duplicati dalla raccolta iniziale
            var uniqueMatches = new Dictionary<string, (DatabaseObjectInfo obj, int priority, int secondaryScore)>();

            // Debug: verifica quanti oggetti totali abbiamo
            System.Diagnostics.Debug.WriteLine($"IntelliSense DEBUG: Totale oggetti in cache: {_allObjects.Count}");

            // Processa tutti gli oggetti UNA SOLA VOLTA
            foreach (var obj in _allObjects) {
                var objNameLower = obj.Name.ToLowerInvariant();
                var parentNameLower = obj.ParentName?.ToLowerInvariant() ?? string.Empty;

                int priority = int.MaxValue;
                int secondaryScore = int.MaxValue;
                bool isMatch = false;

                // 1. Prima priorità: oggetti che INIZIANO con il termine cercato
                if (objNameLower.StartsWith(searchLower)) {
                    priority = 1;
                    secondaryScore = obj.Name.Length; // I nomi più corti hanno precedenza
                    isMatch = true;
                }
                // 2. Seconda priorità: oggetti che CONTENGONO il termine cercato
                else if (objNameLower.Contains(searchLower)) {
                    priority = 2;
                    secondaryScore = objNameLower.IndexOf(searchLower) * 1000 + obj.Name.Length;
                    isMatch = true;
                }
                // 3. Terza priorità: colonne dove il PARENT contiene il termine
                else if (obj.Type == "Column" && !string.IsNullOrEmpty(parentNameLower) &&
                         parentNameLower.Contains(searchLower)) {
                    priority = 3;
                    secondaryScore = parentNameLower.IndexOf(searchLower) * 1000 + obj.Name.Length;
                    isMatch = true;
                }

                if (isMatch) {
                    // Crea una chiave univoca che tenga conto del nome E del tipo di oggetto
                    var uniqueKey = $"{obj.Type}|{obj.Schema}|{obj.Name}";

                    // Se l'oggetto non esiste ancora, aggiungilo
                    // Se esiste, mantieni quello con priorità migliore
                    if (!uniqueMatches.ContainsKey(uniqueKey) ||
                        (priority < uniqueMatches[uniqueKey].priority) ||
                        (priority == uniqueMatches[uniqueKey].priority && secondaryScore < uniqueMatches[uniqueKey].secondaryScore)) {

                        uniqueMatches[uniqueKey] = (obj, priority, secondaryScore);
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine($"IntelliSense DEBUG: Trovati {uniqueMatches.Count} match unici per '{searchTerm}'");

            // Ordina i risultati finali
            var sortedResults = uniqueMatches.Values
                .OrderBy(r => r.priority) // Prima per priorità
                .ThenBy(r => r.secondaryScore) // Poi per qualità del match
                .ThenBy(r => GetTypePriority(r.obj.Type)) // Poi per tipo di oggetto
                .ThenBy(r => r.obj.Name) // Infine alfabeticamente
                .Take(100) // Limita i risultati
                .ToList();

            // Converti in completion data
            var processedDisplayTexts = new HashSet<string>(); // Ulteriore controllo sui testi visualizzati

            foreach (var result in sortedResults) {
                var obj = result.obj;
                var displayText = GetDisplayText(obj, searchTerm);

                // Verifica che non abbiamo già questo testo nella lista
                if (!processedDisplayTexts.Contains(displayText)) {
                    completionData.Add(new SqlCompletionData(displayText, obj.Description, GetCompletionType(obj)));
                    processedDisplayTexts.Add(displayText);
                }
            }

            System.Diagnostics.Debug.WriteLine($"IntelliSense: Restituiti {completionData.Count} elementi unici per '{searchTerm}'");

            // Debug dettagliato per CGMOVICONT
            if (searchTerm.ToUpper().Contains("CGMOVI")) {
                System.Diagnostics.Debug.WriteLine($"=== DEBUG CGMOVI ===");
                System.Diagnostics.Debug.WriteLine($"Termine ricerca: '{searchTerm}'");
                System.Diagnostics.Debug.WriteLine($"Match trovati nel dizionario: {uniqueMatches.Count}");

                foreach (var kvp in uniqueMatches.Where(m => m.Key.Contains("CGMOVI"))) {
                    System.Diagnostics.Debug.WriteLine($"  Key: {kvp.Key}, Obj: {kvp.Value.obj.Name}, Priority: {kvp.Value.priority}");
                }

                System.Diagnostics.Debug.WriteLine($"Elementi finali nella completion:");
                foreach (var item in completionData) {
                    if (item.Text.Contains("CGMOVI")) {
                        System.Diagnostics.Debug.WriteLine($"  Text: {item.Text}");
                    }
                }
                System.Diagnostics.Debug.WriteLine($"===================");
            }

            return completionData;
        }

        private string GetDisplayText(DatabaseObjectInfo obj, string searchTerm) {
            // Per garantire coerenza, usa sempre la stessa logica per lo stesso oggetto

            // Per le colonne, usa sempre solo il nome della colonna
            if (obj.Type == "Column") {
                return obj.Name;
            }

            // Per gli altri oggetti, usa il nome semplice se è nello schema dbo, altrimenti il FullName
            if (obj.Schema == "dbo") {
                return obj.Name;
            } else {
                return obj.FullName;
            }
        }

        private int GetTypePriority(string type) {
            return type switch {
                "Table" => 1,
                "View" => 2,
                "Column" => 3,
                "StoredProcedure" => 4,
                "Function" => 5,
                "Trigger" => 6,
                _ => 10
            };
        }

        private async Task RefreshDatabaseObjectsAsync() {
            try {
                var newObjects = new List<DatabaseObjectInfo>();

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Carica tabelle
                await LoadTablesAsync(connection, newObjects);

                // Carica colonne
                await LoadColumnsAsync(connection, newObjects);

                // Carica viste
                await LoadViewsAsync(connection, newObjects);

                // Carica stored procedures
                await LoadStoredProceduresAsync(connection, newObjects);

                // Carica funzioni
                await LoadFunctionsAsync(connection, newObjects);

                // Carica trigger
                await LoadTriggersAsync(connection, newObjects);

                // CONTROLLO FINALE PER DUPLICATI
                // Raggruppa per chiave unica e mantieni solo un elemento per gruppo
                var uniqueObjects = newObjects
                    .GroupBy(obj => $"{obj.Type}|{obj.Schema}|{obj.Name}|{obj.ParentName}")
                    .Select(g => g.First()) // Prendi solo il primo di ogni gruppo
                    .ToList();

                var duplicatesRemoved = newObjects.Count - uniqueObjects.Count;
                if (duplicatesRemoved > 0) {
                    System.Diagnostics.Debug.WriteLine($"ATTENZIONE: Rimossi {duplicatesRemoved} duplicati durante il caricamento cache IntelliSense");
                }

                // Sostituisci la collezione esistente solo se il caricamento è andato a buon fine
                _allObjects = uniqueObjects;
                _lastCacheUpdate = DateTime.Now;

                System.Diagnostics.Debug.WriteLine($"Cache IntelliSense aggiornata: {_allObjects.Count} oggetti unici caricati");

                // Debug specifico per CGMOVI
                var cgmoviObjects = _allObjects.Where(o => o.Name.ToUpper().Contains("CGMOVI")).ToList();
                if (cgmoviObjects.Any()) {
                    System.Diagnostics.Debug.WriteLine($"=== OGGETTI CGMOVI IN CACHE ===");
                    foreach (var obj in cgmoviObjects) {
                        System.Diagnostics.Debug.WriteLine($"  {obj.Type}: {obj.Schema}.{obj.Name} (Parent: {obj.ParentName})");
                    }
                    System.Diagnostics.Debug.WriteLine($"===============================");
                }

            } catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"Errore nel refresh oggetti database: {ex.Message}");
            }
        }

        private async Task LoadTablesAsync(SqlConnection connection, List<DatabaseObjectInfo> objects) {
            var query = @"
                SELECT 
                    s.name as schema_name,
                    t.name as table_name,
                    t.modify_date
                FROM sys.tables t
                INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                WHERE t.is_ms_shipped = 0
                AND s.name NOT IN ('sys', 'INFORMATION_SCHEMA')
                ORDER BY s.name, t.name";

            using var command = new SqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync()) {
                var schemaName = reader.GetString("schema_name");
                var tableName = reader.GetString("table_name");
                var modifyDate = reader.GetDateTime("modify_date");

                objects.Add(new DatabaseObjectInfo {
                    Name = tableName,
                    FullName = $"{schemaName}.{tableName}",
                    Schema = schemaName,
                    Type = "Table",
                    Description = $"📋 Tabella: {schemaName}.{tableName} (modificata: {modifyDate:dd/MM/yyyy})",
                    ModifyDate = modifyDate
                });
            }
        }

        private async Task LoadColumnsAsync(SqlConnection connection, List<DatabaseObjectInfo> objects) {
            var query = @"
                SELECT 
                    s.name as schema_name,
                    t.name as table_name,
                    c.name as column_name,
                    ty.name as data_type,
                    c.max_length,
                    c.precision,
                    c.scale,
                    c.is_nullable
                FROM sys.columns c
                INNER JOIN sys.tables t ON c.object_id = t.object_id
                INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                INNER JOIN sys.types ty ON c.user_type_id = ty.user_type_id
                WHERE t.is_ms_shipped = 0
                AND s.name NOT IN ('sys', 'INFORMATION_SCHEMA')
                ORDER BY s.name, t.name, c.column_id";

            using var command = new SqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync()) {
                var schemaName = reader.GetString("schema_name");
                var tableName = reader.GetString("table_name");
                var columnName = reader.GetString("column_name");
                var dataType = reader.GetString("data_type");
                var maxLength = reader.GetInt16("max_length");
                var isNullable = reader.GetBoolean("is_nullable");

                var typeDescription = dataType;
                if (maxLength > 0 && (dataType == "varchar" || dataType == "nvarchar" || dataType == "char" || dataType == "nchar")) {
                    typeDescription += maxLength == -1 ? "(MAX)" : $"({maxLength})";
                }

                objects.Add(new DatabaseObjectInfo {
                    Name = columnName,
                    FullName = $"{schemaName}.{tableName}.{columnName}",
                    Schema = schemaName,
                    ParentName = tableName,
                    Type = "Column",
                    Description = $"📝 Campo: {columnName} ({typeDescription}{(isNullable ? ", NULL" : ", NOT NULL")}) in {schemaName}.{tableName}",
                    DataType = typeDescription
                });
            }
        }

        private async Task LoadViewsAsync(SqlConnection connection, List<DatabaseObjectInfo> objects) {
            var query = @"
                SELECT 
                    s.name as schema_name,
                    v.name as view_name,
                    v.modify_date
                FROM sys.views v
                INNER JOIN sys.schemas s ON v.schema_id = s.schema_id
                WHERE v.is_ms_shipped = 0
                AND s.name NOT IN ('sys', 'INFORMATION_SCHEMA')
                ORDER BY s.name, v.name";

            using var command = new SqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync()) {
                var schemaName = reader.GetString("schema_name");
                var viewName = reader.GetString("view_name");
                var modifyDate = reader.GetDateTime("modify_date");

                objects.Add(new DatabaseObjectInfo {
                    Name = viewName,
                    FullName = $"{schemaName}.{viewName}",
                    Schema = schemaName,
                    Type = "View",
                    Description = $"👁️ Vista: {schemaName}.{viewName} (modificata: {modifyDate:dd/MM/yyyy})",
                    ModifyDate = modifyDate
                });
            }
        }

        private async Task LoadStoredProceduresAsync(SqlConnection connection, List<DatabaseObjectInfo> objects) {
            var query = @"
                SELECT 
                    s.name as schema_name,
                    p.name as procedure_name,
                    p.modify_date
                FROM sys.procedures p
                INNER JOIN sys.schemas s ON p.schema_id = s.schema_id
                WHERE p.is_ms_shipped = 0
                AND p.name NOT LIKE 'sp[_]%'
                AND s.name NOT IN ('sys', 'INFORMATION_SCHEMA')
                ORDER BY s.name, p.name";

            using var command = new SqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync()) {
                var schemaName = reader.GetString("schema_name");
                var procedureName = reader.GetString("procedure_name");
                var modifyDate = reader.GetDateTime("modify_date");

                objects.Add(new DatabaseObjectInfo {
                    Name = procedureName,
                    FullName = $"{schemaName}.{procedureName}",
                    Schema = schemaName,
                    Type = "StoredProcedure",
                    Description = $"⚙️ Stored Procedure: {schemaName}.{procedureName} (modificata: {modifyDate:dd/MM/yyyy})",
                    ModifyDate = modifyDate
                });
            }
        }

        private async Task LoadFunctionsAsync(SqlConnection connection, List<DatabaseObjectInfo> objects) {
            var query = @"
                SELECT 
                    s.name as schema_name,
                    o.name as function_name,
                    o.modify_date,
                    o.type_desc
                FROM sys.objects o
                INNER JOIN sys.schemas s ON o.schema_id = s.schema_id
                WHERE o.type IN ('FN', 'IF', 'TF')
                AND o.is_ms_shipped = 0
                AND s.name NOT IN ('sys', 'INFORMATION_SCHEMA')
                ORDER BY s.name, o.name";

            using var command = new SqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync()) {
                var schemaName = reader.GetString("schema_name");
                var functionName = reader.GetString("function_name");
                var modifyDate = reader.GetDateTime("modify_date");
                var typeDesc = reader.GetString("type_desc");

                objects.Add(new DatabaseObjectInfo {
                    Name = functionName,
                    FullName = $"{schemaName}.{functionName}",
                    Schema = schemaName,
                    Type = "Function",
                    Description = $"🔢 Funzione: {schemaName}.{functionName} ({typeDesc}) (modificata: {modifyDate:dd/MM/yyyy})",
                    ModifyDate = modifyDate
                });
            }
        }

        private async Task LoadTriggersAsync(SqlConnection connection, List<DatabaseObjectInfo> objects) {
            var query = @"
                SELECT 
                    s.name as schema_name,
                    t.name as trigger_name,
                    t.modify_date,
                    OBJECT_NAME(t.parent_id) as table_name
                FROM sys.triggers t
                INNER JOIN sys.objects o ON t.parent_id = o.object_id
                INNER JOIN sys.schemas s ON o.schema_id = s.schema_id
                WHERE t.is_ms_shipped = 0
                AND s.name NOT IN ('sys', 'INFORMATION_SCHEMA')
                ORDER BY s.name, t.name";

            using var command = new SqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync()) {
                var schemaName = reader.GetString("schema_name");
                var triggerName = reader.GetString("trigger_name");
                var tableName = reader.GetString("table_name");
                var modifyDate = reader.GetDateTime("modify_date");

                objects.Add(new DatabaseObjectInfo {
                    Name = triggerName,
                    FullName = $"{schemaName}.{triggerName}",
                    Schema = schemaName,
                    ParentName = tableName,
                    Type = "Trigger",
                    Description = $"⚡ Trigger: {schemaName}.{triggerName} su {tableName} (modificato: {modifyDate:dd/MM/yyyy})",
                    ModifyDate = modifyDate
                });
            }
        }

        private CompletionType GetCompletionType(DatabaseObjectInfo obj) {
            return obj.Type switch {
                "Table" => CompletionType.Table,
                "Column" => CompletionType.Column,
                "View" => CompletionType.View,
                "StoredProcedure" => CompletionType.StoredProcedure,
                "Function" => CompletionType.Function,
                "Trigger" => CompletionType.Trigger,
                _ => CompletionType.Unknown
            };
        }
    }

    // Classe per contenere le informazioni degli oggetti database
    public class DatabaseObjectInfo {
        public string Name { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Schema { get; set; } = string.Empty;
        public string? ParentName { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? DataType { get; set; }
        public DateTime? ModifyDate { get; set; }
    }
}