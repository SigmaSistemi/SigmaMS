using Microsoft.Data.SqlClient;
using SigmaMS.Models;
using System.Data;
using System.Text.RegularExpressions;

namespace SigmaMS.Services {
    public class QueryResult {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public TimeSpan ExecutionTime { get; set; }
        public int RowsAffected { get; set; }

        // NUOVO: DataTable per i risultati
        public DataTable ResultData { get; set; } = new DataTable();

        // Proprietà di compatibilità (deprecate ma mantenute per non rompere il codice esistente)
        [Obsolete("Usa ResultData invece")]
        public List<string> Columns { get; set; } = new List<string>();

        [Obsolete("Usa ResultData invece")]
        public List<object[]> Rows { get; set; } = new List<object[]>();
    }

    namespace SigmaMS.Services {
        public class DataService {
            public async Task<List<string>> GetDatabasesAsync(string connectionString) {
                var databases = new List<string>();

                try {
                    using var connection = new SqlConnection(connectionString);
                    await connection.OpenAsync();

                    var query = @"
                    SELECT name 
                    FROM sys.databases 
                    WHERE name NOT IN ('master', 'tempdb', 'model', 'msdb')
                    ORDER BY name";

                    using var command = new SqlCommand(query, connection);
                    using var reader = await command.ExecuteReaderAsync();

                    while (await reader.ReadAsync()) {
                        databases.Add(reader.GetString("name"));
                    }
                } catch (Exception ex) {
                    throw new Exception($"Errore nel recupero dei database: {ex.Message}", ex);
                }

                return databases;
            }

            public async Task<List<DatabaseObject>> GetTablesAsync(string connectionString) {
                var objects = new List<DatabaseObject>();

                try {
                    using var connection = new SqlConnection(connectionString);
                    await connection.OpenAsync();

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
                        objects.Add(new DatabaseObject {
                            Name = reader.GetString("table_name"),
                            Schema = reader.GetString("schema_name"),
                            Type = "Table",
                            ModifyDate = reader.GetDateTime("modify_date")
                        });
                    }
                } catch (Exception ex) {
                    throw new Exception($"Errore nel recupero delle tabelle: {ex.Message}", ex);
                }

                return objects;
            }

            public async Task<List<DatabaseObject>> GetStoredProceduresAsync(string connectionString) {
                var objects = new List<DatabaseObject>();

                try {
                    using var connection = new SqlConnection(connectionString);
                    await connection.OpenAsync();

                    var query = @"
                    SELECT 
                        s.name as schema_name,
                        p.name as procedure_name,
                        p.modify_date
                    FROM sys.procedures p
                    INNER JOIN sys.schemas s ON p.schema_id = s.schema_id
                    WHERE p.is_ms_shipped = 0
                    AND p.name NOT LIKE 'sp[_]%'
                    AND p.name NOT LIKE 'fn[_]%'
                    AND p.name NOT LIKE 'dt[_]%'
                    AND s.name NOT IN ('sys', 'INFORMATION_SCHEMA')
                    ORDER BY s.name, p.name";

                    using var command = new SqlCommand(query, connection);
                    using var reader = await command.ExecuteReaderAsync();

                    while (await reader.ReadAsync()) {
                        objects.Add(new DatabaseObject {
                            Name = reader.GetString("procedure_name"),
                            Schema = reader.GetString("schema_name"),
                            Type = "Stored Procedure",
                            ModifyDate = reader.GetDateTime("modify_date")
                        });
                    }
                } catch (Exception ex) {
                    throw new Exception($"Errore nel recupero delle stored procedure: {ex.Message}", ex);
                }

                return objects;
            }

            public async Task<List<DatabaseObject>> GetViewsAsync(string connectionString) {
                var objects = new List<DatabaseObject>();

                try {
                    using var connection = new SqlConnection(connectionString);
                    await connection.OpenAsync();

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
                        objects.Add(new DatabaseObject {
                            Name = reader.GetString("view_name"),
                            Schema = reader.GetString("schema_name"),
                            Type = "View",
                            ModifyDate = reader.GetDateTime("modify_date")
                        });
                    }
                } catch (Exception ex) {
                    throw new Exception($"Errore nel recupero delle viste: {ex.Message}", ex);
                }

                return objects;
            }

            public async Task<List<DatabaseObject>> GetFunctionsAsync(string connectionString) {
                var objects = new List<DatabaseObject>();

                try {
                    using var connection = new SqlConnection(connectionString);
                    await connection.OpenAsync();

                    var query = @"
                    SELECT 
                        s.name as schema_name,
                        o.name as function_name,
                        o.modify_date,
                        CASE 
                            WHEN o.type = 'FN' THEN 'Scalar Function'
                            WHEN o.type = 'IF' THEN 'Inline Table Function'
                            WHEN o.type = 'TF' THEN 'Table Function'
                            ELSE 'Function'
                        END as function_type
                    FROM sys.objects o
                    INNER JOIN sys.schemas s ON o.schema_id = s.schema_id
                    WHERE o.type IN ('FN', 'IF', 'TF')
                    AND o.is_ms_shipped = 0
                    AND o.name NOT LIKE 'fn[_]%'
                    AND s.name NOT IN ('sys', 'INFORMATION_SCHEMA')
                    ORDER BY s.name, o.name";

                    using var command = new SqlCommand(query, connection);
                    using var reader = await command.ExecuteReaderAsync();

                    while (await reader.ReadAsync()) {
                        objects.Add(new DatabaseObject {
                            Name = reader.GetString("function_name"),
                            Schema = reader.GetString("schema_name"),
                            Type = reader.GetString("function_type"),
                            ModifyDate = reader.GetDateTime("modify_date")
                        });
                    }
                } catch (Exception ex) {
                    throw new Exception($"Errore nel recupero delle funzioni: {ex.Message}", ex);
                }

                return objects;
            }

            public async Task<List<DatabaseObject>> GetTriggersAsync(string connectionString) {
                var objects = new List<DatabaseObject>();

                try {
                    using var connection = new SqlConnection(connectionString);
                    await connection.OpenAsync();

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
                        objects.Add(new DatabaseObject {
                            Name = reader.GetString("trigger_name"),
                            Schema = reader.GetString("schema_name"),
                            Type = "Trigger",
                            ModifyDate = reader.GetDateTime("modify_date")
                        });
                    }
                } catch (Exception ex) {
                    throw new Exception($"Errore nel recupero dei trigger: {ex.Message}", ex);
                }

                return objects;
            }

            public async Task<string> GetObjectScriptAsync(string connectionString, string objectName, string objectType, string schema) {
                try {
                    using var connection = new SqlConnection(connectionString);
                    await connection.OpenAsync();

                    if (objectType.ToLower() == "table") {
                        return await GetTableCreateScriptAsync(connection, objectName, schema);
                    }

                    string query = objectType.ToLower() switch {
                        "stored procedure" => $@"
                        SELECT OBJECT_DEFINITION(OBJECT_ID('{schema}.{objectName}')) as definition",
                        "view" => $@"
                        SELECT OBJECT_DEFINITION(OBJECT_ID('{schema}.{objectName}')) as definition",
                        "scalar function" or "inline table function" or "table function" => $@"
                        SELECT OBJECT_DEFINITION(OBJECT_ID('{schema}.{objectName}')) as definition",
                        "trigger" => $@"
                        SELECT OBJECT_DEFINITION(OBJECT_ID('{schema}.{objectName}')) as definition",
                        _ => throw new ArgumentException($"Tipo oggetto non supportato: {objectType}")
                    };

                    using var command = new SqlCommand(query, connection);
                    var result = await command.ExecuteScalarAsync();

                    var script = result?.ToString() ?? "Impossibile recuperare la definizione dell'oggetto.";

                    // Converti CREATE in ALTER per maggiore praticità (eccetto per le tabelle)
                    return ConvertCreateToAlter(script, objectType);
                } catch (Exception ex) {
                    throw new Exception($"Errore nel recupero dello script: {ex.Message}", ex);
                }
            }

            private async Task<string> GetTableCreateScriptAsync(SqlConnection connection, string tableName, string schema) {
                try {
                    var script = new System.Text.StringBuilder();

                    // Header del commento
                    script.AppendLine($"-- Script CREATE TABLE generato automaticamente");
                    script.AppendLine($"-- Tabella: {schema}.{tableName}");
                    script.AppendLine($"-- Generato il: {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
                    script.AppendLine();

                    // Inizio della definizione della tabella
                    script.AppendLine($"CREATE TABLE [{schema}].[{tableName}] (");

                    // Query per ottenere le colonne
                    var columnQuery = @"
                    SELECT 
                        c.COLUMN_NAME,
                        c.DATA_TYPE,
                        c.CHARACTER_MAXIMUM_LENGTH,
                        c.NUMERIC_PRECISION,
                        c.NUMERIC_SCALE,
                        c.IS_NULLABLE,
                        c.COLUMN_DEFAULT,
                        CASE WHEN pk.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END AS IS_PRIMARY_KEY,
                        CASE WHEN ic.is_identity = 1 THEN 1 ELSE 0 END AS IS_IDENTITY,
                        ISNULL(ic.seed_value, 1) as seed_value,
                        ISNULL(ic.increment_value, 1) as increment_value
                    FROM INFORMATION_SCHEMA.COLUMNS c
                    LEFT JOIN (
                        SELECT ku.COLUMN_NAME
                        FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                        INNER JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE ku
                            ON tc.CONSTRAINT_NAME = ku.CONSTRAINT_NAME
                        WHERE tc.TABLE_SCHEMA = @schema 
                        AND tc.TABLE_NAME = @tableName 
                        AND tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
                    ) pk ON c.COLUMN_NAME = pk.COLUMN_NAME
                    LEFT JOIN sys.identity_columns ic ON ic.object_id = OBJECT_ID(@schema + '.' + @tableName) 
                        AND ic.name = c.COLUMN_NAME
                    WHERE c.TABLE_SCHEMA = @schema 
                    AND c.TABLE_NAME = @tableName
                    ORDER BY c.ORDINAL_POSITION";

                    using var columnCommand = new SqlCommand(columnQuery, connection);
                    columnCommand.Parameters.AddWithValue("@schema", schema);
                    columnCommand.Parameters.AddWithValue("@tableName", tableName);

                    using var reader = await columnCommand.ExecuteReaderAsync();
                    var columns = new List<string>();

                    while (await reader.ReadAsync()) {
                        var columnDef = new System.Text.StringBuilder();
                        columnDef.Append($"    [{reader["COLUMN_NAME"]}] ");

                        // Tipo di dato
                        var dataType = reader["DATA_TYPE"].ToString().ToUpper();
                        columnDef.Append(dataType);

                        // Lunghezza per tipi stringa
                        if (reader["CHARACTER_MAXIMUM_LENGTH"] != DBNull.Value) {
                            var maxLength = Convert.ToInt32(reader["CHARACTER_MAXIMUM_LENGTH"]);
                            columnDef.Append(maxLength == -1 ? "(MAX)" : $"({maxLength})");
                        }
                        // Precisione per tipi numerici
                        else if (reader["NUMERIC_PRECISION"] != DBNull.Value &&
                                 !dataType.Contains("INT") && !dataType.Contains("MONEY")) {
                            var precision = Convert.ToInt32(reader["NUMERIC_PRECISION"]);
                            var scale = reader["NUMERIC_SCALE"] != DBNull.Value ?
                                Convert.ToInt32(reader["NUMERIC_SCALE"]) : 0;

                            if (scale > 0) {
                                columnDef.Append($"({precision},{scale})");
                            } else if (dataType == "DECIMAL" || dataType == "NUMERIC") {
                                columnDef.Append($"({precision})");
                            }
                        }

                        // Identity
                        if (Convert.ToBoolean(reader["IS_IDENTITY"])) {
                            var seed = reader["seed_value"];
                            var increment = reader["increment_value"];
                            columnDef.Append($" IDENTITY({seed},{increment})");
                        }

                        // Nullable
                        if (reader["IS_NULLABLE"].ToString() == "NO") {
                            columnDef.Append(" NOT NULL");
                        } else {
                            columnDef.Append(" NULL");
                        }

                        // Default
                        if (reader["COLUMN_DEFAULT"] != DBNull.Value) {
                            columnDef.Append($" DEFAULT {reader["COLUMN_DEFAULT"]}");
                        }

                        columns.Add(columnDef.ToString());
                    }
                    reader.Close();

                    // Aggiungi le colonne allo script
                    script.AppendLine(string.Join(",\r\n", columns));
                    script.AppendLine(")");

                    return script.ToString();
                } catch (Exception ex) {
                    return $"-- Errore nella generazione dello script CREATE TABLE: {ex.Message}";
                }
            }

            private string ConvertCreateToAlter(string script, string objectType) {
                if (string.IsNullOrWhiteSpace(script) || script.Contains("Impossibile recuperare")) {
                    return script;
                }

                try {
                    // Pattern per diversi tipi di oggetti
                    var patterns = new Dictionary<string, string> {
                    { "stored procedure", @"\bCREATE\s+PROCEDURE\b" },
                    { "view", @"\bCREATE\s+VIEW\b" },
                    { "scalar function", @"\bCREATE\s+FUNCTION\b" },
                    { "inline table function", @"\bCREATE\s+FUNCTION\b" },
                    { "table function", @"\bCREATE\s+FUNCTION\b" },
                    { "trigger", @"\bCREATE\s+TRIGGER\b" }
                };

                    var replacements = new Dictionary<string, string> {
                    { "stored procedure", "ALTER PROCEDURE" },
                    { "view", "ALTER VIEW" },
                    { "scalar function", "ALTER FUNCTION" },
                    { "inline table function", "ALTER FUNCTION" },
                    { "table function", "ALTER FUNCTION" },
                    { "trigger", "ALTER TRIGGER" }
                };

                    var objectTypeLower = objectType.ToLower();

                    if (patterns.ContainsKey(objectTypeLower) && replacements.ContainsKey(objectTypeLower)) {
                        var pattern = patterns[objectTypeLower];
                        var replacement = replacements[objectTypeLower];

                        // Sostituisce CREATE con ALTER (case-insensitive)
                        var convertedScript = Regex.Replace(script, pattern, replacement, RegexOptions.IgnoreCase);

                        // Aggiungi un commento informativo all'inizio
                        var comment = $"-- Script generato automaticamente \r\n-- Oggetto: {objectType}\r\n-- Generato il: {DateTime.Now:dd/MM/yyyy HH:mm:ss}\r\n\r\n";

                        return comment + convertedScript;
                    }

                    return script;
                } catch (Exception ex) {
                    // In caso di errore nella conversione, restituisci lo script originale
                    System.Diagnostics.Debug.WriteLine($"Errore nella conversione CREATE->ALTER: {ex.Message}");
                    return $"-- Errore nella conversione automatica CREATE->ALTER\r\n-- Script originale:\r\n\r\n{script}";
                }
            }

            public async Task<bool> TestConnectionAsync(string connectionString) {
                try {
                    using var connection = new SqlConnection(connectionString);
                    await connection.OpenAsync();
                    return true;
                } catch {
                    return false;
                }
            }

            public async Task<QueryResult> ExecuteQueryAsync(string connectionString, string query) {
                var result = new QueryResult();
                var startTime = DateTime.Now;

                try {
                    using var connection = new SqlConnection(connectionString);
                    await connection.OpenAsync();

                    using var command = new SqlCommand(query, connection);
                    command.CommandTimeout = 30; // 30 secondi timeout

                    // Verifica se è una query SELECT o un comando
                    var trimmedQuery = query.Trim().ToUpper();
                    var isSelect = trimmedQuery.StartsWith("SELECT") ||
                                  trimmedQuery.StartsWith("WITH") ||
                                  trimmedQuery.Contains("SELECT");

                    if (isSelect) {
                        // Query SELECT - restituisce dati
                        using var reader = await command.ExecuteReaderAsync();

                        // Ottieni i nomi delle colonne
                        var columnCount = reader.FieldCount;
                        for (int i = 0; i < columnCount; i++) {
                            result.Columns.Add(reader.GetName(i));
                        }

                        // Leggi i dati
                        while (await reader.ReadAsync()) {
                            var row = new object[columnCount];
                            for (int i = 0; i < columnCount; i++) {
                                row[i] = reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i);
                            }
                            result.Rows.Add(row);
                        }

                        result.Message = $"Query completata. {result.Rows.Count} righe restituite.";
                    } else {
                        // Comando di modifica (INSERT, UPDATE, DELETE, etc.)
                        var rowsAffected = await command.ExecuteNonQueryAsync();
                        result.Message = $"Comando completato. {rowsAffected} righe interessate.";
                        result.RowsAffected = rowsAffected;
                    }

                    result.IsSuccess = true;
                    result.ExecutionTime = DateTime.Now - startTime;

                } catch (Exception ex) {
                    result.IsSuccess = false;
                    result.ErrorMessage = ex.Message;
                    result.ExecutionTime = DateTime.Now - startTime;
                }

                return result;
            }

            public async Task<List<QueryResult>> ExecuteMultipleQueriesAsync(string connectionString, string queries) {
                var results = new List<QueryResult>();

                // Separa le query usando GO come delimitatore
                var individualQueries = SplitSqlQueries(queries);

                foreach (var query in individualQueries) {
                    if (!string.IsNullOrWhiteSpace(query)) {
                        var result = await ExecuteQueryAsync(connectionString, query);
                        results.Add(result);

                        // Se c'è un errore, fermati (opzionale)
                        if (!result.IsSuccess) {
                            break;
                        }
                    }
                }

                return results;
            }

            private List<string> SplitSqlQueries(string sql) {
                var queries = new List<string>();
                var lines = sql.Split('\n');
                var currentQuery = new System.Text.StringBuilder();

                foreach (var line in lines) {
                    var trimmedLine = line.Trim();

                    if (trimmedLine.Equals("GO", StringComparison.OrdinalIgnoreCase)) {
                        // Termina la query corrente
                        if (currentQuery.Length > 0) {
                            queries.Add(currentQuery.ToString());
                            currentQuery.Clear();
                        }
                    } else {
                        currentQuery.AppendLine(line);
                    }
                }

                // Aggiungi l'ultima query se presente
                if (currentQuery.Length > 0) {
                    queries.Add(currentQuery.ToString());
                }

                return queries;
            }
        }
    }

}