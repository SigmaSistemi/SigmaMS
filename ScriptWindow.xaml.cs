using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Search;
using Microsoft.Win32;
using PoorMansTSqlFormatterRedux;
using PoorMansTSqlFormatterRedux.Formatters;
using PoorMansTSqlFormatterRedux.Interfaces;
using SigmaMS.Editor;
using SigmaMS.Services;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SigmaMS {
    public partial class ScriptWindow : Window {
        private readonly string _objectName;
        private readonly string _script;
        private readonly string _connectionString;
        private readonly DataService _dataService;
        private SearchPanel _searchPanel;
        private SqlCompletionProvider _completionProvider;
        private CompletionWindow _completionWindow;
        private bool _isStoredProcedure;

        private QueryResultsWindow _currentResultsWindow;
        private bool _reuseResultsWindow = true; // Preferenza utente (default: riusa)

        private ISqlTokenizer _tokenizer;
        private ISqlTreeFormatter _formatter;

        public ScriptWindow(string objectName, string script, string connectionString = null) {
            InitializeComponent();

            _objectName = objectName;
            _script = script;
            _connectionString = connectionString;
            _dataService = new DataService();

            // Determina se è una stored procedure
            _isStoredProcedure = IsStoredProcedureScript(script);

            InitializeWindow();
            InitializeEditor();
            InitializeCompletion();
            InitializeSqlFormatter();
        }

        private bool IsStoredProcedureScript(string script) {
            if (string.IsNullOrWhiteSpace(script)) return false;

            // Verifica se lo script contiene CREATE PROCEDURE o ALTER PROCEDURE
            var upperScript = script.ToUpperInvariant();
            return upperScript.Contains("CREATE PROCEDURE") || upperScript.Contains("ALTER PROCEDURE");
        }

        private void InitializeWindow() {
            Title = $"Script - {_objectName}";
            txtObjectName.Text = _objectName;
            txtScript.Text = _script;

            // Aggiungi evento per aggiornare il pulsante commenta quando cambia la selezione
            txtScript.TextArea.SelectionChanged += (s, e) => UpdateCommentButtonText();

            // Il pulsante Esegui è attivo se c'è una connessione disponibile
            btnExecute.IsEnabled = !string.IsNullOrEmpty(_connectionString);

            // Aggiorna il testo del pulsante commenta inizialmente
            UpdateCommentButtonText();

            // Mostra il pulsante Test solo per le stored procedure
            if (_isStoredProcedure) {
                btnTest.Visibility = Visibility.Visible;
            } else {
                btnTest.Visibility = Visibility.Collapsed;
            }

            // Inizializza la modalità risultati
            chkReuseResultsWindow.IsChecked = _reuseResultsWindow;
            UpdateResultsModeDisplay();

            UpdateLineCount();
        }

        private void AddTestButton() {
            // Trova il Grid che contiene i pulsanti (prima riga dell'header)
            var headerGrid = FindName("HeaderGrid") as Grid;
            if (headerGrid == null) {
                // Se non troviamo il grid specifico, aggiungiamo il pulsante tramite codice
                // Dobbiamo modificare il XAML per includere il pulsante Test
                CreateTestButton();
            }
        }

        private void CreateTestButton() {
            // Crea il pulsante Test dinamicamente
            var testButton = new Button {
                Name = "btnTest",
                Width = 100,
                Height = 30,
                Margin = new Thickness(0, 0, 8, 0),
                Content = "🧪 Test SP",
                ToolTip = "Genera script di test per la stored procedure"
            };

            testButton.Click += BtnTest_Click;

            // Trova il Grid che contiene i pulsanti esistenti
            // Dovremmo cercare nel Visual Tree, ma per semplicità aggiungiamo alla toolbar
            // Nota: Questo richiede di modificare anche il XAML per una migliore integrazione
        }

        private void BtnTest_Click(object sender, RoutedEventArgs e) {
            try {
                if (!_isStoredProcedure) {
                    MessageBox.Show("Questa funzione è disponibile solo per le stored procedure.", "Info",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var currentScript = txtScript.Text;
                var testScript = GenerateTestScript(currentScript);

                if (!string.IsNullOrEmpty(testScript)) {
                    txtScript.Text = testScript;

                    // Feedback visivo
                    var originalContent = (sender as Button)?.Content;
                    if (sender is Button btn) {
                        btn.Content = "✅ Test generato!";

                        var timer = new System.Windows.Threading.DispatcherTimer {
                            Interval = TimeSpan.FromSeconds(1.5)
                        };
                        timer.Tick += (s, args) => {
                            btn.Content = originalContent;
                            timer.Stop();
                        };
                        timer.Start();
                    }
                } else {
                    MessageBox.Show("Impossibile generare lo script di test. Verificare che il formato della stored procedure sia corretto.", "Errore",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            } catch (Exception ex) {
                MessageBox.Show($"Errore nella generazione dello script di test: {ex.Message}", "Errore",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string GenerateTestScript(string originalScript) {
            try {
                var result = new List<string>(); // Usa List<string> invece di StringBuilder
                var lines = originalScript.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

                bool foundProcedureDeclaration = false;
                bool parametersProcessed = false;
                var parameters = new List<string>();
                var procedureBodyLines = new List<string>();
                var currentParameterBuilder = new StringBuilder();
                bool isInMultiLineParameter = false;

                for (int i = 0; i < lines.Length; i++) {
                    var line = lines[i];
                    var trimmedLine = line.Trim();
                    var upperLine = trimmedLine.ToUpperInvariant();

                    // Trova la dichiarazione della stored procedure
                    if ((upperLine.Contains("CREATE PROCEDURE") || upperLine.Contains("ALTER PROCEDURE")) && !foundProcedureDeclaration) {
                        foundProcedureDeclaration = true;

                        var lineAfterProcedure = trimmedLine;
                        var procIndex = Math.Max(lineAfterProcedure.IndexOf("CREATE PROCEDURE", StringComparison.OrdinalIgnoreCase),
                                                lineAfterProcedure.IndexOf("ALTER PROCEDURE", StringComparison.OrdinalIgnoreCase));

                        var afterProc = lineAfterProcedure.Substring(procIndex);
                        var parts = afterProc.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 2) {
                            var remainingText = string.Join(" ", parts.Skip(2));
                            if (remainingText.Trim().StartsWith("@")) {
                                currentParameterBuilder.Append(remainingText);
                                isInMultiLineParameter = true;
                            }
                        }
                        continue;
                    }

                    // Se abbiamo trovato la dichiarazione, processa i parametri
                    if (foundProcedureDeclaration && !parametersProcessed) {
                        if (upperLine == "AS" || (upperLine.StartsWith("AS ") && !upperLine.StartsWith("@"))) {
                            if (currentParameterBuilder.Length > 0) {
                                var param = ExtractParameter(currentParameterBuilder.ToString());
                                if (!string.IsNullOrEmpty(param)) {
                                    parameters.Add(param);
                                }
                                currentParameterBuilder.Clear();
                            }
                            parametersProcessed = true;
                            continue;
                        }

                        if (trimmedLine.StartsWith("@")) {
                            if (currentParameterBuilder.Length > 0) {
                                var param = ExtractParameter(currentParameterBuilder.ToString());
                                if (!string.IsNullOrEmpty(param)) {
                                    parameters.Add(param);
                                }
                            }

                            currentParameterBuilder.Clear();
                            currentParameterBuilder.Append(trimmedLine);
                            isInMultiLineParameter = !trimmedLine.TrimEnd().EndsWith(",") && !upperLine.Contains("AS");

                            if (!isInMultiLineParameter) {
                                var param = ExtractParameter(currentParameterBuilder.ToString());
                                if (!string.IsNullOrEmpty(param)) {
                                    parameters.Add(param);
                                }
                                currentParameterBuilder.Clear();
                            }
                        } else if (isInMultiLineParameter && currentParameterBuilder.Length > 0) {
                            currentParameterBuilder.Append(" " + trimmedLine);

                            if (trimmedLine.TrimEnd().EndsWith(",") || upperLine.Contains("AS")) {
                                isInMultiLineParameter = false;
                            }
                        }
                        continue;
                    }

                    // Dopo aver processato i parametri, aggiungi il resto come corpo della procedure
                    if (parametersProcessed) {
                        procedureBodyLines.Add(lines[i]); // Mantieni l'indentazione originale
                    }
                }

                // Processa l'ultimo parametro se rimasto in sospeso
                if (currentParameterBuilder.Length > 0) {
                    var param = ExtractParameter(currentParameterBuilder.ToString());
                    if (!string.IsNullOrEmpty(param)) {
                        parameters.Add(param);
                    }
                }

                // Costruisci lo script di test mantenendo la formattazione
                if (parameters.Count > 0) {
                    // Aggiungi le dichiarazioni dei parametri
                    foreach (var param in parameters) {
                        result.Add($"DECLARE {param} = NULL");
                    }

                    result.Add(""); // Una riga vuota
                    result.Add("-- SP Code");

                    // Aggiungi il corpo della stored procedure mantenendo la formattazione originale
                    result.AddRange(procedureBodyLines);
                } else {
                    // Se non troviamo parametri, potrebbe essere una SP senza parametri
                    bool skipNextLines = false;
                    bool foundAs = false;

                    foreach (var line in lines) {
                        var upperLine = line.Trim().ToUpperInvariant();

                        if (upperLine.Contains("CREATE PROCEDURE") || upperLine.Contains("ALTER PROCEDURE")) {
                            skipNextLines = true;
                            continue;
                        }

                        if (skipNextLines && (upperLine == "AS" || upperLine.StartsWith("AS "))) {
                            foundAs = true;
                            skipNextLines = false;
                            result.Add("-- SP Code (nessun parametro)");
                            continue;
                        }

                        if (!skipNextLines && foundAs) {
                            result.Add(line);
                        }
                    }

                    if (result.Count == 0) {
                        return originalScript;
                    }
                }

                // IMPORTANTE: Usa string.Join invece di StringBuilder per mantenere la formattazione
                return string.Join("\r\n", result);
            } catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"Errore nella generazione script test: {ex.Message}");
                return string.Empty;
            }
        }

        //private string GenerateTestScript(string originalScript) {
        //    try {
        //        var result = new StringBuilder();
        //        var lines = originalScript.Split('\n');

        //        bool foundProcedureDeclaration = false;
        //        bool parametersProcessed = false;
        //        var parameters = new List<string>();
        //        var procedureBodyLines = new List<string>();
        //        var currentParameterBuilder = new StringBuilder();
        //        bool isInMultiLineParameter = false;

        //        for (int i = 0; i < lines.Length; i++) {
        //            var line = lines[i];
        //            var trimmedLine = line.Trim();
        //            var upperLine = trimmedLine.ToUpperInvariant();

        //            // Trova la dichiarazione della stored procedure
        //            if ((upperLine.Contains("CREATE PROCEDURE") || upperLine.Contains("ALTER PROCEDURE")) && !foundProcedureDeclaration) {
        //                foundProcedureDeclaration = true;

        //                // Se la riga contiene anche parametri, estraili
        //                var lineAfterProcedure = trimmedLine;
        //                var procIndex = Math.Max(lineAfterProcedure.IndexOf("CREATE PROCEDURE", StringComparison.OrdinalIgnoreCase),
        //                                        lineAfterProcedure.IndexOf("ALTER PROCEDURE", StringComparison.OrdinalIgnoreCase));

        //                // Trova il nome della procedure
        //                var afterProc = lineAfterProcedure.Substring(procIndex);
        //                var parts = afterProc.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        //                if (parts.Length > 2) {
        //                    // Se ci sono più di 2 parti, potrebbe esserci un parametro sulla stessa riga
        //                    var remainingText = string.Join(" ", parts.Skip(2));
        //                    if (remainingText.Trim().StartsWith("@")) {
        //                        currentParameterBuilder.Append(remainingText);
        //                        isInMultiLineParameter = true;
        //                    }
        //                }
        //                continue;
        //            }

        //            // Se abbiamo trovato la dichiarazione, processa i parametri
        //            if (foundProcedureDeclaration && !parametersProcessed) {
        //                // Controlla se la riga contiene "AS" che indica la fine dei parametri
        //                if (upperLine == "AS" || (upperLine.StartsWith("AS ") && !upperLine.StartsWith("@"))) {
        //                    // Processa l'ultimo parametro se ce n'è uno in costruzione
        //                    if (currentParameterBuilder.Length > 0) {
        //                        var param = ExtractParameter(currentParameterBuilder.ToString());
        //                        if (!string.IsNullOrEmpty(param)) {
        //                            parameters.Add(param);
        //                        }
        //                        currentParameterBuilder.Clear();
        //                    }
        //                    parametersProcessed = true;
        //                    continue;
        //                }

        //                // Se la riga inizia con @ è un nuovo parametro
        //                if (trimmedLine.StartsWith("@")) {
        //                    // Processa il parametro precedente se ce n'è uno
        //                    if (currentParameterBuilder.Length > 0) {
        //                        var param = ExtractParameter(currentParameterBuilder.ToString());
        //                        if (!string.IsNullOrEmpty(param)) {
        //                            parameters.Add(param);
        //                        }
        //                    }

        //                    // Inizia il nuovo parametro
        //                    currentParameterBuilder.Clear();
        //                    currentParameterBuilder.Append(trimmedLine);
        //                    isInMultiLineParameter = !trimmedLine.TrimEnd().EndsWith(",") && !upperLine.Contains("AS");

        //                    // Se il parametro è completo su una riga, processalo subito
        //                    if (!isInMultiLineParameter) {
        //                        var param = ExtractParameter(currentParameterBuilder.ToString());
        //                        if (!string.IsNullOrEmpty(param)) {
        //                            parameters.Add(param);
        //                        }
        //                        currentParameterBuilder.Clear();
        //                    }
        //                } else if (isInMultiLineParameter && currentParameterBuilder.Length > 0) {
        //                    // Continua il parametro su più righe
        //                    currentParameterBuilder.Append(" " + trimmedLine);

        //                    // Controlla se il parametro è completo
        //                    if (trimmedLine.TrimEnd().EndsWith(",") || upperLine.Contains("AS")) {
        //                        isInMultiLineParameter = false;
        //                    }
        //                }
        //                continue;
        //            }

        //            // Dopo aver processato i parametri, aggiungi il resto come corpo della procedure
        //            if (parametersProcessed) {
        //                procedureBodyLines.Add(lines[i]); // Mantieni l'indentazione originale
        //            }
        //        }

        //        // Processa l'ultimo parametro se rimasto in sospeso
        //        if (currentParameterBuilder.Length > 0) {
        //            var param = ExtractParameter(currentParameterBuilder.ToString());
        //            if (!string.IsNullOrEmpty(param)) {
        //                parameters.Add(param);
        //            }
        //        }

        //        // Costruisci lo script di test
        //        if (parameters.Count > 0) {
        //            // Aggiungi le dichiarazioni dei parametri
        //            foreach (var param in parameters) {
        //                result.AppendLine($"DECLARE {param} = NULL");
        //            }

        //            result.AppendLine();
        //            result.AppendLine("-- SP Code");

        //            // Aggiungi il corpo della stored procedure
        //            foreach (var bodyLine in procedureBodyLines) {
        //                result.AppendLine(bodyLine);
        //            }
        //        } else {
        //            // Se non troviamo parametri, potrebbe essere una SP senza parametri
        //            // Rimuovi comunque la dichiarazione CREATE/ALTER PROCEDURE
        //            bool skipNextLines = false;
        //            bool foundAs = false;

        //            foreach (var line in lines) {
        //                var upperLine = line.Trim().ToUpperInvariant();

        //                if (upperLine.Contains("CREATE PROCEDURE") || upperLine.Contains("ALTER PROCEDURE")) {
        //                    skipNextLines = true;
        //                    continue;
        //                }

        //                if (skipNextLines && (upperLine == "AS" || upperLine.StartsWith("AS "))) {
        //                    foundAs = true;
        //                    skipNextLines = false;
        //                    result.AppendLine("-- SP Code (nessun parametro)");
        //                    continue;
        //                }

        //                if (!skipNextLines && foundAs) {
        //                    result.AppendLine(line);
        //                }
        //            }

        //            // Se non abbiamo trovato niente, restituisci lo script originale
        //            if (result.Length == 0) {
        //                return originalScript;
        //            }
        //        }

        //        return result.ToString();
        //    } catch (Exception ex) {
        //        System.Diagnostics.Debug.WriteLine($"Errore nella generazione script test: {ex.Message}");
        //        return string.Empty;
        //    }
        //}

        private string ExtractParameter(string parameterLine) {
            try {
                // Rimuovi spazi extra, virgole finali e eventuali commenti
                var cleanLine = parameterLine.Trim().TrimEnd(',');

                // Rimuovi eventuali commenti inline
                var commentIndex = cleanLine.IndexOf("--");
                if (commentIndex >= 0) {
                    cleanLine = cleanLine.Substring(0, commentIndex).Trim();
                }

                // Pattern più robusto per estrarre nome parametro e tipo
                var match = Regex.Match(cleanLine, @"(@\w+)\s+([^=]+?)(?:\s*=\s*[^,]*)?(?:\s*,\s*)?$", RegexOptions.IgnoreCase);

                if (match.Success) {
                    var paramName = match.Groups[1].Value.Trim();
                    var paramType = match.Groups[2].Value.Trim();

                    // Pulisci il tipo da eventuali caratteri indesiderati
                    paramType = Regex.Replace(paramType, @"\s+", " "); // Normalizza spazi multipli

                    return $"{paramName} {paramType}";
                }

                // Fallback con pattern più semplice
                var simplMatch = Regex.Match(cleanLine, @"(@\w+)\s+(.+)", RegexOptions.IgnoreCase);
                if (simplMatch.Success) {
                    var paramName = simplMatch.Groups[1].Value.Trim();
                    var paramType = simplMatch.Groups[2].Value.Trim();

                    // Rimuovi eventuali valori di default e virgole
                    if (paramType.Contains("=")) {
                        paramType = paramType.Substring(0, paramType.IndexOf("=")).Trim();
                    }
                    paramType = paramType.TrimEnd(',').Trim();

                    return $"{paramName} {paramType}";
                }

                return string.Empty;
            } catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"Errore nell'estrazione parametro: {ex.Message}");
                return string.Empty;
            }
        }

        //private string GenerateTestScript(string originalScript) {
        //    try {
        //        var result = new StringBuilder();
        //        var lines = originalScript.Split('\n');

        //        bool foundProcedureDeclaration = false;
        //        bool parametersProcessed = false;
        //        var parameters = new List<string>();
        //        var procedureBodyLines = new List<string>();

        //        for (int i = 0; i < lines.Length; i++) {
        //            var line = lines[i].Trim();
        //            var upperLine = line.ToUpperInvariant();

        //            // Trova la dichiarazione della stored procedure
        //            if (upperLine.Contains("CREATE PROCEDURE") || upperLine.Contains("ALTER PROCEDURE")) {
        //                foundProcedureDeclaration = true;
        //                continue; // Salta questa riga
        //            }

        //            // Se abbiamo trovato la dichiarazione, processa i parametri
        //            if (foundProcedureDeclaration && !parametersProcessed) {
        //                // Controlla se la riga contiene "AS" che indica la fine dei parametri
        //                if (upperLine.Contains("AS") && !upperLine.StartsWith("@")) {
        //                    parametersProcessed = true;
        //                    continue; // Salta la riga "AS"
        //                }

        //                // Se la riga inizia con @ è un parametro
        //                if (line.StartsWith("@")) {
        //                    var parameter = ExtractParameter(line);
        //                    if (!string.IsNullOrEmpty(parameter)) {
        //                        parameters.Add(parameter);
        //                    }
        //                }
        //                continue;
        //            }

        //            // Dopo aver processato i parametri, aggiungi il resto come corpo della procedure
        //            if (parametersProcessed) {
        //                procedureBodyLines.Add(lines[i]); // Mantieni l'indentazione originale
        //            }
        //        }

        //        // Costruisci lo script di test
        //        if (parameters.Count > 0) {
        //            // Aggiungi le dichiarazioni dei parametri
        //            foreach (var param in parameters) {
        //                result.AppendLine($"DECLARE {param} = NULL");
        //            }

        //            result.AppendLine();
        //            result.AppendLine("-- SP Code");

        //            // Aggiungi il corpo della stored procedure
        //            foreach (var bodyLine in procedureBodyLines) {
        //                result.AppendLine(bodyLine);
        //            }
        //        } else {
        //            // Se non troviamo parametri, restituisci lo script originale
        //            return originalScript;
        //        }

        //        return result.ToString();
        //    } catch (Exception ex) {
        //        System.Diagnostics.Debug.WriteLine($"Errore nella generazione script test: {ex.Message}");
        //        return string.Empty;
        //    }
        //}

        //private string ExtractParameter(string parameterLine) {
        //    try {
        //        // Rimuovi spazi extra e virgole finali
        //        var cleanLine = parameterLine.Trim().TrimEnd(',');

        //        // Pattern per estrarre nome parametro e tipo
        //        var match = Regex.Match(cleanLine, @"(@\w+)\s+(.+)", RegexOptions.IgnoreCase);

        //        if (match.Success) {
        //            var paramName = match.Groups[1].Value;
        //            var paramType = match.Groups[2].Value.Trim();

        //            // Rimuovi eventuali valori di default
        //            if (paramType.Contains("=")) {
        //                paramType = paramType.Substring(0, paramType.IndexOf("=")).Trim();
        //            }

        //            return $"{paramName} {paramType}";
        //        }

        //        return string.Empty;
        //    } catch (Exception ex) {
        //        System.Diagnostics.Debug.WriteLine($"Errore nell'estrazione parametro: {ex.Message}");
        //        return string.Empty;
        //    }
        //}

        //// ... resto del codice rimane invariato ...

        private void InitializeEditor() {
            // Installa il pannello di ricerca integrato di AvalonEdit
            _searchPanel = SearchPanel.Install(txtScript);

            // Configura il syntax highlighting per SQL
            SetupSqlHighlighting();

            // Configura l'editor
            txtScript.Options.ConvertTabsToSpaces = true;
            txtScript.Options.IndentationSize = 4;
            txtScript.Options.EnableRectangularSelection = true;
            txtScript.Options.EnableTextDragDrop = true;
            txtScript.Options.ShowEndOfLine = false;
            txtScript.Options.ShowTabs = false;
            txtScript.Options.ShowSpaces = false;

            // Imposta il focus sull'editor
            txtScript.Focus();
        }

        private void InitializeCompletion() {
            if (!string.IsNullOrEmpty(_connectionString)) {
                _completionProvider = new SqlCompletionProvider(_connectionString);

                // Aggiungi eventi per l'autocompletamento
                txtScript.TextArea.TextEntering += TextArea_TextEntering;
                txtScript.TextArea.TextEntered += TextArea_TextEntered;
                txtScript.TextArea.KeyDown += TextArea_KeyDown;
            }
        }

        private void InitializeSqlFormatter() {
            try {
                _tokenizer = new PoorMansTSqlFormatterRedux.Tokenizers.TSqlStandardTokenizer();
                _formatter = new PoorMansTSqlFormatterRedux.Formatters.TSqlStandardFormatter(new TSqlStandardFormatterOptions {
                    // Configurazione formattazione
                    IndentString = "    ", // 4 spazi per indentazione
                    SpacesPerTab = 4,
                    MaxLineWidth = 120,
                    ExpandCommaLists = true,
                    TrailingCommas = true,
                    SpaceAfterExpandedComma = true,
                    ExpandBooleanExpressions = true,
                    ExpandCaseStatements = true,
                    ExpandBetweenConditions = true,
                    ExpandInLists = true,
                    BreakJoinOnSections = false,
                    UppercaseKeywords = true, // IMPORTANTE: converte in maiuscolo
                    //BreakBeforeJoinType = true,
                    
                    NewStatementLineBreaks = 2,
                    NewClauseLineBreaks = 1
                });
            } catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"Errore inizializzazione SQL formatter: {ex.Message}");
                _tokenizer = null;
                _formatter = null;
            }
        }

        private void TextArea_KeyDown(object sender, KeyEventArgs e) {
            // Ctrl+Down Arrow per forzare l'autocompletamento come hai richiesto
            if (e.Key == Key.Down && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) {
                e.Handled = true;
                ShowCompletionWindow();
            }
            // Ctrl+Space per autocompletamento standard
            else if (e.Key == Key.Space && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) {
                e.Handled = true;
                ShowCompletionWindow();
            }
        }

        private void TextArea_TextEntering(object sender, TextCompositionEventArgs e) {
            // Chiudi la finestra di completamento quando vengono digitati caratteri non validi per nomi oggetti
            if (e.Text.Length > 0 && _completionWindow != null) {
                if (!char.IsLetterOrDigit(e.Text[0]) && e.Text[0] != '_' && e.Text[0] != '.') {
                    _completionWindow.CompletionList.RequestInsertion(e);
                }
            }
        }

        private async void TextArea_TextEntered(object sender, TextCompositionEventArgs e) {
            // Mostra autocompletamento dopo aver digitato almeno 3 caratteri consecutivi (come CGMOVI)
            if (char.IsLetterOrDigit(e.Text[0]) || e.Text[0] == '_') {
                var currentWord = GetCurrentWord();
                if (currentWord.Length >= 3) {  // Aumentato a 3 per evitare troppi suggerimenti
                    ShowCompletionWindow();
                }
            }
        }

        private async void ShowCompletionWindow() {
            if (_completionProvider == null) return;

            try {
                // Chiudi la finestra esistente se è aperta
                if (_completionWindow != null) {
                    _completionWindow.Close();
                    _completionWindow = null;
                }

                var currentWord = GetCurrentWord();
                var completionData = await _completionProvider.GetCompletionDataAsync(currentWord);

                if (completionData.Count > 0) {
                    // Calcola l'offset di inizio della parola corrente
                    var textArea = txtScript.TextArea;
                    var document = textArea.Document;
                    var caretOffset = textArea.Caret.Offset;

                    var startOffset = caretOffset;
                    while (startOffset > 0) {
                        var prevChar = document.GetCharAt(startOffset - 1);
                        if (!char.IsLetterOrDigit(prevChar) && prevChar != '_') {
                            break;
                        }
                        startOffset--;
                    }

                    _completionWindow = new CompletionWindow(textArea) {
                        MaxHeight = 300,
                        MaxWidth = 600,
                        ResizeMode = ResizeMode.CanResizeWithGrip,
                        StartOffset = startOffset,  // Imposta l'offset di inizio
                        EndOffset = caretOffset,     // Imposta l'offset di fine,
                        BorderThickness = new Thickness(0),
                        Width = 200
                    };

                    // Personalizza l'aspetto della finestra
                    _completionWindow.Background = System.Windows.Media.Brushes.White;
                    _completionWindow.BorderBrush = System.Windows.Media.Brushes.Gray;
                    _completionWindow.BorderThickness = new Thickness(1);

                    // Aggiungi i dati di completamento
                    foreach (var data in completionData) {
                        _completionWindow.CompletionList.CompletionData.Add(data);
                    }

                    // Configura il comportamento
                    _completionWindow.CompletionList.IsFiltering = true;
                    _completionWindow.CloseWhenCaretAtBeginning = true;

                    // Pre-seleziona il primo elemento se c'è una corrispondenza esatta parziale
                    if (!string.IsNullOrEmpty(currentWord)) {
                        if (currentWord.Length >= 4) {
                            var exactMatch = _completionWindow.CompletionList.CompletionData
                                .FirstOrDefault(cd => cd.Text.StartsWith(currentWord, StringComparison.OrdinalIgnoreCase));
                            if (exactMatch != null) {
                                _completionWindow.CompletionList.SelectedItem = exactMatch;
                            }
                        }
                    }

                    // Gestisce la chiusura automatica
                    _completionWindow.Closed += (s, e) => _completionWindow = null;

                    // Mostra la finestra
                    _completionWindow.Show();
                }
            } catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"Errore nell'autocompletamento: {ex.Message}");
            }
        }

        private string GetCurrentWord() {
            var textArea = txtScript.TextArea;
            var document = textArea.Document;
            var offset = textArea.Caret.Offset;

            // Trova l'inizio della parola corrente (supporta caratteri tipici dei nomi oggetti SQL)
            var startOffset = offset;
            while (startOffset > 0) {
                var prevChar = document.GetCharAt(startOffset - 1);
                if (!char.IsLetterOrDigit(prevChar) && prevChar != '_') {
                    break;
                }
                startOffset--;
            }

            // Trova la fine della parola corrente
            var endOffset = offset;
            while (endOffset < document.TextLength) {
                var currentChar = document.GetCharAt(endOffset);
                if (!char.IsLetterOrDigit(currentChar) && currentChar != '_') {
                    break;
                }
                endOffset++;
            }

            if (startOffset < endOffset) {
                return document.GetText(startOffset, endOffset - startOffset);
            }

            return string.Empty;
        }

        private void SetupSqlHighlighting() {
            try {
                // Prova prima a caricare la definizione SQL incorporata
                using (var stream = typeof(ICSharpCode.AvalonEdit.TextEditor).Assembly
                    .GetManifestResourceStream("ICSharpCode.AvalonEdit.Highlighting.Resources.SQL.xshd")) {
                    if (stream != null) {
                        using (var reader = new System.Xml.XmlTextReader(stream)) {
                            txtScript.SyntaxHighlighting = ICSharpCode.AvalonEdit.Highlighting.Xshd.HighlightingLoader.Load(reader,
                                ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance);
                        }
                        return;
                    }
                }

                // Se non trova la risorsa incorporata, prova dal HighlightingManager
                var highlightingManager = ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance;
                txtScript.SyntaxHighlighting = highlightingManager.GetDefinition("SQL");

                // Se ancora non funziona, crea una definizione di base
                if (txtScript.SyntaxHighlighting == null) {
                    CreateBasicSqlHighlighting();
                }
            } catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"Errore nel caricamento syntax highlighting: {ex.Message}");
                // Fallback a definizione di base
                CreateBasicSqlHighlighting();
            }
        }

        private void CreateBasicSqlHighlighting() {
            try {
                // Crea una definizione di highlighting SQL di base
                var xshd = @"<?xml version=""1.0""?>
<SyntaxDefinition name=""SQL"" xmlns=""http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008"">
    <Color name=""Comment"" foreground=""Green"" />
    <Color name=""String"" foreground=""Red"" />
    <Color name=""Keyword"" foreground=""Blue"" fontWeight=""bold"" />
    <Color name=""Number"" foreground=""DarkBlue"" />
    
    <RuleSet>
        <Span color=""Comment"" begin=""--"" />
        <Span color=""Comment"" multiline=""true"" begin=""/\*"" end=""\*/"" />
        <Span color=""String"" begin=""'"" end=""'"" />
        <Span color=""String"" begin=""&quot;"" end=""&quot;"" />
        
        <Keywords color=""Keyword"">
            <Word>SELECT</Word>
            <Word>FROM</Word>
            <Word>WHERE</Word>
            <Word>INSERT</Word>
            <Word>UPDATE</Word>
            <Word>DELETE</Word>
            <Word>CREATE</Word>
            <Word>ALTER</Word>
            <Word>DROP</Word>
            <Word>TABLE</Word>
            <Word>VIEW</Word>
            <Word>PROCEDURE</Word>
            <Word>FUNCTION</Word>
            <Word>TRIGGER</Word>
            <Word>INDEX</Word>
            <Word>AND</Word>
            <Word>OR</Word>
            <Word>NOT</Word>
            <Word>NULL</Word>
            <Word>IS</Word>
            <Word>IN</Word>
            <Word>LIKE</Word>
            <Word>BETWEEN</Word>
            <Word>ORDER</Word>
            <Word>BY</Word>
            <Word>GROUP</Word>
            <Word>HAVING</Word>
            <Word>UNION</Word>
            <Word>JOIN</Word>
            <Word>INNER</Word>
            <Word>LEFT</Word>
            <Word>RIGHT</Word>
            <Word>FULL</Word>
            <Word>OUTER</Word>
            <Word>ON</Word>
            <Word>AS</Word>
            <Word>CASE</Word>
            <Word>WHEN</Word>
            <Word>THEN</Word>
            <Word>ELSE</Word>
            <Word>END</Word>
            <Word>IF</Word>
            <Word>BEGIN</Word>
            <Word>DECLARE</Word>
            <Word>SET</Word>
            <Word>PRINT</Word>
            <Word>RETURN</Word>
            <Word>EXEC</Word>
            <Word>EXECUTE</Word>
            <Word>INT</Word>
            <Word>VARCHAR</Word>
            <Word>NVARCHAR</Word>
            <Word>CHAR</Word>
            <Word>NCHAR</Word>
            <Word>TEXT</Word>
            <Word>NTEXT</Word>
            <Word>DATETIME</Word>
            <Word>DATE</Word>
            <Word>TIME</Word>
            <Word>TIMESTAMP</Word>
            <Word>DECIMAL</Word>
            <Word>NUMERIC</Word>
            <Word>FLOAT</Word>
            <Word>REAL</Word>
            <Word>MONEY</Word>
            <Word>BIT</Word>
            <Word>BINARY</Word>
            <Word>VARBINARY</Word>
            <Word>IMAGE</Word>
            <Word>UNIQUEIDENTIFIER</Word>
        </Keywords>
        
        <Rule foreground=""DarkBlue"">
            \b0[xX][0-9a-fA-F]+  # hex number
            |    \b
            (    \d+(\.[0-9]+)?   #number with optional floating point
            |    \.[0-9]+         #or just starting with floating point
            )
            ([eE][+-]?[0-9]+)?     # optional exponent
        </Rule>
    </RuleSet>
</SyntaxDefinition>";

                using (var reader = new System.IO.StringReader(xshd))
                using (var xmlReader = System.Xml.XmlReader.Create(reader)) {
                    txtScript.SyntaxHighlighting = ICSharpCode.AvalonEdit.Highlighting.Xshd.HighlightingLoader.Load(xmlReader,
                        ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance);
                }
            } catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"Errore nella creazione syntax highlighting personalizzato: {ex.Message}");
            }
        }

        private void UpdateLineCount() {
            var lines = txtScript.Text.Split('\n').Length;
            var chars = txtScript.Text.Length;
            txtLineCount.Text = $"Righe: {lines} | Caratteri: {chars}";
        }

        private void TxtScript_TextChanged(object sender, EventArgs e) {
            UpdateLineCount();
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e) {
            try {
                // Se c'è testo selezionato, copia solo quello, altrimenti tutto
                var textToCopy = !string.IsNullOrEmpty(txtScript.SelectedText)
                    ? txtScript.SelectedText
                    : txtScript.Text;

                Clipboard.SetText(textToCopy);

                // Feedback visivo temporaneo
                var originalContent = btnCopy.Content;
                btnCopy.Content = "✅ Copiato!";

                // Ripristina il testo del bottone dopo 1.5 secondi
                var timer = new System.Windows.Threading.DispatcherTimer {
                    Interval = TimeSpan.FromSeconds(1.5)
                };
                timer.Tick += (s, args) => {
                    btnCopy.Content = originalContent;
                    timer.Stop();
                };
                timer.Start();
            } catch (Exception ex) {
                MessageBox.Show($"Errore nella copia: {ex.Message}", "Errore",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e) {
            try {
                var dialog = new SaveFileDialog {
                    Filter = "File SQL|*.sql|File di testo|*.txt|Tutti i file|*.*",
                    Title = "Salva Script",
                    FileName = $"{_objectName}.sql"
                };

                if (dialog.ShowDialog() == true) {
                    File.WriteAllText(dialog.FileName, txtScript.Text);

                    // Feedback visivo temporaneo
                    var originalContent = btnSave.Content;
                    btnSave.Content = "✅ Salvato!";

                    var timer = new System.Windows.Threading.DispatcherTimer {
                        Interval = TimeSpan.FromSeconds(1.5)
                    };
                    timer.Tick += (s, args) => {
                        btnSave.Content = originalContent;
                        timer.Stop();
                    };
                    timer.Start();
                }
            } catch (Exception ex) {
                MessageBox.Show($"Errore nel salvataggio: {ex.Message}", "Errore",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnComment_Click(object sender, RoutedEventArgs e) {
            try {
                ToggleComments();
                UpdateCommentButtonText();
            } catch (Exception ex) {
                MessageBox.Show($"Errore nel commento: {ex.Message}", "Errore",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ToggleComments() {
            if (txtScript.Document == null) return;

            var document = txtScript.Document;
            var selection = txtScript.SelectionLength > 0;

            int startLine, endLine;

            if (selection) {
                // Se c'è una selezione, lavora sulle righe selezionate
                startLine = document.GetLineByOffset(txtScript.SelectionStart).LineNumber;
                endLine = document.GetLineByOffset(txtScript.SelectionStart + txtScript.SelectionLength).LineNumber;
            } else {
                // Se non c'è selezione, lavora sulla riga corrente
                startLine = endLine = document.GetLineByOffset(txtScript.CaretOffset).LineNumber;
            }

            var isCommenting = DetermineCommentAction(startLine, endLine);

            using (document.RunUpdate()) {
                for (int lineNum = startLine; lineNum <= endLine; lineNum++) {
                    var line = document.GetLineByNumber(lineNum);
                    var lineText = document.GetText(line.Offset, line.Length);

                    if (isCommenting) {
                        // Commenta la riga
                        if (!string.IsNullOrWhiteSpace(lineText)) {
                            document.Insert(line.Offset, "-- ");
                        }
                    } else {
                        // Decommenta la riga
                        if (lineText.TrimStart().StartsWith("--")) {
                            var trimmedStart = lineText.Length - lineText.TrimStart().Length;
                            var commentIndex = lineText.IndexOf("--", trimmedStart);
                            if (commentIndex >= 0) {
                                // Rimuovi "-- " (incluso lo spazio se presente)
                                var toRemove = lineText.Substring(commentIndex, 2);
                                if (commentIndex + 2 < lineText.Length && lineText[commentIndex + 2] == ' ') {
                                    toRemove = lineText.Substring(commentIndex, 3);
                                }
                                document.Remove(line.Offset + commentIndex, toRemove.Length);
                            }
                        }
                    }
                }
            }
        }

        private bool DetermineCommentAction(int? startLine = null, int? endLine = null) {
            if (txtScript.Document == null) return true;

            var document = txtScript.Document;
            var selection = txtScript.SelectionLength > 0;

            int start, end;

            if (startLine.HasValue && endLine.HasValue) {
                start = startLine.Value;
                end = endLine.Value;
            } else if (selection) {
                start = document.GetLineByOffset(txtScript.SelectionStart).LineNumber;
                end = document.GetLineByOffset(txtScript.SelectionStart + txtScript.SelectionLength).LineNumber;
            } else {
                start = end = document.GetLineByOffset(txtScript.CaretOffset).LineNumber;
            }

            // Conta le righe commentate e non commentate
            int commentedLines = 0;
            int nonEmptyLines = 0;

            for (int lineNum = start; lineNum <= end; lineNum++) {
                var line = document.GetLineByNumber(lineNum);
                var lineText = document.GetText(line.Offset, line.Length).Trim();

                if (!string.IsNullOrWhiteSpace(lineText)) {
                    nonEmptyLines++;
                    if (lineText.StartsWith("--")) {
                        commentedLines++;
                    }
                }
            }

            // Se la maggior parte delle righe non vuote è commentata, decommenta
            // Altrimenti, commenta
            return commentedLines < nonEmptyLines / 2.0;
        }

        private void UpdateCommentButtonText() {
            if (btnComment != null) {
                var isCommenting = DetermineCommentAction();
                btnComment.Content = isCommenting ? "💬 Commenta" : "💬 Decommenta";
            }
        }

        private void ChkWordWrap_Changed(object sender, RoutedEventArgs e) {
            if (txtScript != null) {
                txtScript.WordWrap = chkWordWrap.IsChecked == true;
            }
        }

        private void BtnFind_Click(object sender, RoutedEventArgs e) {
            // AvalonEdit ha già un pannello di ricerca integrato
            // Si attiva con Ctrl+F, ma possiamo aprirlo programmaticamente
            if (_searchPanel != null) {
                _searchPanel.Open();
            }
        }

        //private async void BtnExecute_Click(object sender, RoutedEventArgs e) {
        //    if (string.IsNullOrEmpty(_connectionString)) {
        //        MessageBox.Show("Connessione database non disponibile.", "Errore",
        //            MessageBoxButton.OK, MessageBoxImage.Warning);
        //        return;
        //    }

        //    try {
        //        btnExecute.IsEnabled = false;
        //        btnExecute.Content = "⏳ Esecuzione...";

        //        // Prendi il testo selezionato, o tutto il testo se nessuna selezione
        //        var queryToExecute = !string.IsNullOrEmpty(txtScript.SelectedText)
        //            ? txtScript.SelectedText
        //            : txtScript.Text;

        //        if (string.IsNullOrWhiteSpace(queryToExecute)) {
        //            MessageBox.Show("Nessuna query da eseguire. Scrivi del codice SQL nell'editor.", "Info",
        //                MessageBoxButton.OK, MessageBoxImage.Information);
        //            return;
        //        }

        //        // Esegui la query
        //        var results = await _dataService.ExecuteMultipleQueriesAsync(_connectionString, queryToExecute);

        //        // Gestisci la finestra dei risultati
        //        await ShowResultsAsync(results, queryToExecute);

        //    } catch (Exception ex) {
        //        MessageBox.Show($"Errore nell'esecuzione della query:\n\n{ex.Message}", "Errore",
        //            MessageBoxButton.OK, MessageBoxImage.Error);
        //    } finally {
        //        btnExecute.IsEnabled = true;
        //        btnExecute.Content = "▶️ Esegui";
        //    }
        //}

        private async void BtnExecute_Click(object sender, RoutedEventArgs e) {
            if (string.IsNullOrEmpty(_connectionString)) {
                MessageBox.Show("Connessione database non disponibile.", "Errore",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try {
                btnExecute.IsEnabled = false;
                btnExecute.Content = "⏳ Esecuzione...";

                // Prendi il testo selezionato, o tutto il testo se nessuna selezione
                var queryToExecute = !string.IsNullOrEmpty(txtScript.SelectedText)
                    ? txtScript.SelectedText
                    : txtScript.Text;

                if (string.IsNullOrWhiteSpace(queryToExecute)) {
                    MessageBox.Show("Nessuna query da eseguire. Scrivi del codice SQL nell'editor.", "Info",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // IMPORTANTE: Esegui il testo ESATTAMENTE come appare nell'editor
                // NESSUNA normalizzazione, NESSUNA modifica
                var results = await _dataService.ExecuteMultipleQueriesAsync(_connectionString, queryToExecute);

                // Mostra i risultati
                await ShowResultsAsync(results, queryToExecute);

            } catch (Exception ex) {
                MessageBox.Show($"Errore nell'esecuzione della query:\n\n{ex.Message}", "Errore",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            } finally {
                btnExecute.IsEnabled = true;
                btnExecute.Content = "▶️ Esegui";
            }
        }

        private async Task ShowResultsAsync(List<QueryResult> results, string query) {
            // Controlla se la finestra dei risultati esiste ancora e se l'utente vuole riusarla
            if (_reuseResultsWindow && _currentResultsWindow != null) {
                try {
                    // Verifica se la finestra è ancora valida
                    if (_currentResultsWindow.IsLoaded && !_currentResultsWindow.IsClosed) {
                        // Aggiorna la finestra esistente
                        _currentResultsWindow.UpdateResults(results, query);

                        // Porta la finestra in primo piano
                        if (_currentResultsWindow.WindowState == WindowState.Minimized) {
                            _currentResultsWindow.WindowState = WindowState.Normal;
                        }
                        _currentResultsWindow.Activate();

                        return;
                    } else {
                        // La finestra è stata chiusa, rimuovi il riferimento
                        _currentResultsWindow = null;
                    }
                } catch {
                    // In caso di errore, rimuovi il riferimento e crea una nuova finestra
                    _currentResultsWindow = null;
                }
            }

            // Crea una nuova finestra dei risultati
            _currentResultsWindow = new QueryResultsWindow(results, query) {
                Owner = this
            };

            // Gestisci la chiusura della finestra
            _currentResultsWindow.Closed += (s, e) => {
                _currentResultsWindow = null;
            };

            _currentResultsWindow.Show();
        }

        // Aggiorna il metodo ToggleResultsWindowReuse per sincronizzare con la checkbox
        private void ToggleResultsWindowReuse() {
            _reuseResultsWindow = !_reuseResultsWindow;
            chkReuseResultsWindow.IsChecked = _reuseResultsWindow;
            UpdateResultsModeDisplay();

            // Feedback visivo
            var message = _reuseResultsWindow ?
                "✅ Finestra risultati verrà riutilizzata" :
                "🔄 Verrà creata una nuova finestra risultati";

            ShowTemporaryMessage(message);
        }

        private void ShowTemporaryMessage(string message) {
            // Se hai una status bar, mostra il messaggio temporaneamente
            // Altrimenti usa il titolo della finestra
            var originalTitle = Title;
            Title = $"{originalTitle} - {message}";

            var timer = new System.Windows.Threading.DispatcherTimer {
                Interval = TimeSpan.FromSeconds(2)
            };
            timer.Tick += (s, e) => {
                Title = originalTitle;
                timer.Stop();
            };
            timer.Start();
        }

        // Gestione dei tasti scorciatoia
        protected override void OnKeyDown(KeyEventArgs e) {
            // F5 per eseguire
            if (e.Key == Key.F5) {
                BtnExecute_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            // Ctrl+F5 per alternare modalità finestra risultati
            else if (e.Key == Key.F5 && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) {
                ToggleResultsWindowReuse();
                e.Handled = true;
            }
            // Ctrl+Shift+F per formattare
            else if (e.Key == Key.F &&
                     (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
                     (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift) {
                FormatSelectedOrAllSql();
                e.Handled = true;
            }
            // Ctrl+/ per commentare/decommentare
            else if (e.Key == Key.OemQuestion &&
                     (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) {
                BtnComment_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            // Ctrl+F per la ricerca
            else if (e.Key == Key.F &&
                    (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) {
                BtnFind_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            // Ctrl+S per salvare
            else if (e.Key == Key.S &&
                     (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) {
                BtnSave_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            // Ctrl+A per selezionare tutto
            else if (e.Key == Key.A &&
                     (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) {
                txtScript.SelectAll();
                e.Handled = true;
            }
            // Escape per chiudere
            else if (e.Key == Key.Escape) {
                Close();
                e.Handled = true;
            }

            base.OnKeyDown(e);
        }

        // Cleanup quando la finestra viene chiusa
        protected override void OnClosed(EventArgs e) {
            // Chiudi la finestra di completamento se è aperta
            if (_completionWindow != null) {
                _completionWindow.Close();
                _completionWindow = null;
            }

            // Chiudi la finestra dei risultati se è aperta (opzionale)
            if (_currentResultsWindow != null && !_currentResultsWindow.IsClosed) {
                _currentResultsWindow.Close();
                _currentResultsWindow = null;
            }

            base.OnClosed(e);
        }

        public void SetCursorAfterSelect() {
            // Usa il Dispatcher per assicurarsi che la finestra sia completamente caricata
            Dispatcher.BeginInvoke(new Action(() => {
                try {
                    var text = txtScript.Text;
                    var selectIndex = text.IndexOf("SELECT ", StringComparison.OrdinalIgnoreCase);

                    if (selectIndex >= 0) {
                        // Posiziona il cursore dopo "SELECT " (7 caratteri)
                        var cursorPosition = selectIndex + 7;

                        // Assicurati che la posizione sia valida
                        if (cursorPosition <= text.Length) {
                            txtScript.CaretOffset = cursorPosition;

                            // Forza il focus sull'editor e scorre alla posizione
                            txtScript.Focus();
                            txtScript.TextArea.Caret.BringCaretToView();

                            // Opzionale: seleziona il testo dopo SELECT per facilitare la digitazione
                            // txtScript.Select(cursorPosition, 0);
                        }
                    }
                } catch (Exception ex) {
                    System.Diagnostics.Debug.WriteLine($"Errore nel posizionamento cursore: {ex.Message}");
                    // In caso di errore, metti semplicemente il focus sull'editor
                    txtScript.Focus();
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void ChkReuseResultsWindow_Changed(object sender, RoutedEventArgs e) {
            _reuseResultsWindow = chkReuseResultsWindow.IsChecked == true;
            UpdateResultsModeDisplay();
        }

        private void UpdateResultsModeDisplay() {
            if (txtResultsMode != null) {
                txtResultsMode.Text = _reuseResultsWindow ?
                    "Modalità: Riutilizza finestra" :
                    "Modalità: Nuova finestra";
            }

            // Aggiorna anche il tooltip del pulsante Execute
            if (btnExecute != null) {
                var baseTooltip = _connectionString != null ?
                    "Esegui query SQL (F5)" :
                    "Connessione database non disponibile";

                var modeText = _reuseResultsWindow ? "riutilizza" : "crea nuova";
                btnExecute.ToolTip = $"{baseTooltip}\nModalità: {modeText} finestra risultati\nCambia modalità: Ctrl+F5";
            }
        }

        //private string FormatSqlCode(string sqlCode) {
        //    if (_tokenizer == null || _formatter == null || string.IsNullOrWhiteSpace(sqlCode)) {
        //        return sqlCode;
        //    }

        //    try {
        //        // Tokenizza il codice SQL
        //        var tokenized = _tokenizer.TokenizeSQL(sqlCode);

        //        // Converte in albero strutturato
        //        var parsed = new PoorMansTSqlFormatterRedux.Parsers.TSqlStandardParser().ParseSQL(tokenized);

        //        // Formatta l'albero
        //        var formatted = _formatter.FormatSQLTree(parsed);

        //        return formatted;
        //    } catch (Exception ex) {
        //        System.Diagnostics.Debug.WriteLine($"Errore nella formattazione SQL: {ex.Message}");
        //        // In caso di errore, restituisce il codice originale
        //        return sqlCode;
        //    }
        //}

        private string FormatSqlCode(string sqlCode) {
            try {
                return FormatSqlCodeAsync(sqlCode).GetAwaiter().GetResult();
            } catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"Errore nella formattazione sincrona: {ex.Message}");
                return sqlCode;
            }
        }

        //private async Task<string> FormatSqlCodeAsync(string sqlCode) {
        //    if (string.IsNullOrWhiteSpace(sqlCode)) {
        //        return sqlCode;
        //    }

        //    try {
        //        // Step 1: Formattazione con PoorMansTSqlFormatter
        //        string formattedSql = await Task.Run(() => {
        //            if (_tokenizer == null || _formatter == null) {
        //                return sqlCode;
        //            }

        //            try {
        //                var tokenized = _tokenizer.TokenizeSQL(sqlCode);
        //                var parsed = new PoorMansTSqlFormatterRedux.Parsers.TSqlStandardParser().ParseSQL(tokenized);
        //                return _formatter.FormatSQLTree(parsed);
        //            } catch {
        //                return sqlCode;
        //            }
        //        });

        //        // Step 2: Converti TUTTO in maiuscolo, preservando solo stringhe e commenti
        //        formattedSql = ConvertToUppercasePreservingStringsAndComments(formattedSql);

        //        return formattedSql;
        //    } catch (Exception ex) {
        //        System.Diagnostics.Debug.WriteLine($"Errore nella formattazione SQL: {ex.Message}");

        //        // Fallback: almeno converti in maiuscolo
        //        try {
        //            return ConvertToUppercasePreservingStringsAndComments(sqlCode);
        //        } catch {
        //            return sqlCode;
        //        }
        //    }
        //}

        private async Task<string> FormatSqlCodeAsync(string sqlCode) {
            if (string.IsNullOrWhiteSpace(sqlCode)) {
                return sqlCode;
            }

            try {
                // Determina se si tratta di uno script di definizione oggetto database
                var upperSql = sqlCode.Trim().ToUpperInvariant();
                bool isObjectDefinition = upperSql.StartsWith("CREATE PROCEDURE") ||
                                         upperSql.StartsWith("ALTER PROCEDURE") ||
                                         upperSql.StartsWith("CREATE FUNCTION") ||
                                         upperSql.StartsWith("ALTER FUNCTION") ||
                                         upperSql.StartsWith("CREATE VIEW") ||
                                         upperSql.StartsWith("ALTER VIEW") ||
                                         upperSql.StartsWith("CREATE TRIGGER") ||
                                         upperSql.StartsWith("ALTER TRIGGER");

                if (isObjectDefinition) {
                    // Per gli script di definizione oggetti, applica SOLO la conversione maiuscolo
                    // SENZA ALCUNA formattazione che potrebbe aggiungere righe vuote
                    return ConvertToUppercasePreservingStringsAndComments(sqlCode);
                }

                // Per le query normali (SELECT, INSERT, UPDATE, DELETE), applica la formattazione completa
                string formattedSql = await Task.Run(() => {
                    if (_tokenizer == null || _formatter == null) {
                        // Se non c'è il formatter, applica solo la conversione maiuscolo
                        return ConvertToUppercasePreservingStringsAndComments(sqlCode);
                    }

                    try {
                        var tokenized = _tokenizer.TokenizeSQL(sqlCode);
                        var parsed = new PoorMansTSqlFormatterRedux.Parsers.TSqlStandardParser().ParseSQL(tokenized);
                        var formatted = _formatter.FormatSQLTree(parsed);

                        // Rimuovi righe vuote eccessive (più di 2 consecutive)
                        formatted = Regex.Replace(formatted, @"\r?\n\s*\r?\n\s*\r?\n\s*", "\r\n\r\n", RegexOptions.Multiline);

                        return formatted;
                    } catch {
                        // In caso di errore, usa solo la conversione maiuscolo
                        return ConvertToUppercasePreservingStringsAndComments(sqlCode);
                    }
                });

                // Step 2: Converti in maiuscolo preservando stringhe e commenti
                formattedSql = ConvertToUppercasePreservingStringsAndComments(formattedSql);

                return formattedSql;
            } catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"Errore nella formattazione SQL: {ex.Message}");

                // Fallback: solo conversione maiuscolo
                try {
                    return ConvertToUppercasePreservingStringsAndComments(sqlCode);
                } catch {
                    return sqlCode;
                }
            }
        }

        //private string ConvertToUppercasePreservingStringsAndComments(string sql) {
        //    if (string.IsNullOrWhiteSpace(sql)) return sql;

        //    try {
        //        var result = sql;
        //        var preservedContent = new List<(int start, int length, string content)>();

        //        // Step 1: Trova e salva stringhe letterali (tra apici singoli)
        //        var stringMatches = Regex.Matches(result, @"'(?:[^']|'')*'", RegexOptions.Singleline);
        //        foreach (Match match in stringMatches.Cast<Match>().Reverse()) {
        //            preservedContent.Add((match.Index, match.Length, match.Value));
        //        }

        //        // Step 2: Trova e salva commenti SQL (-- commento)
        //        var lineCommentMatches = Regex.Matches(result, @"--.*$", RegexOptions.Multiline);
        //        foreach (Match match in lineCommentMatches.Cast<Match>().Reverse()) {
        //            preservedContent.Add((match.Index, match.Length, match.Value));
        //        }

        //        // Step 3: Trova e salva commenti multi-linea (/* commento */)
        //        var blockCommentMatches = Regex.Matches(result, @"/\*.*?\*/", RegexOptions.Singleline);
        //        foreach (Match match in blockCommentMatches.Cast<Match>().Reverse()) {
        //            preservedContent.Add((match.Index, match.Length, match.Value));
        //        }

        //        // Step 4: Sostituisci il contenuto da preservare con placeholder temporanei
        //        var placeholders = new Dictionary<string, string>();
        //        var placeholderIndex = 0;

        //        foreach (var item in preservedContent.OrderByDescending(x => x.start)) {
        //            var placeholder = $"__PRESERVE_{placeholderIndex++}__";
        //            placeholders[placeholder] = item.content;
        //            result = result.Remove(item.start, item.length).Insert(item.start, placeholder);
        //        }

        //        // Step 5: Converti tutto in maiuscolo
        //        result = result.ToUpperInvariant();

        //        // Step 6: Ripristina il contenuto preservato
        //        foreach (var kvp in placeholders) {
        //            result = result.Replace(kvp.Key, kvp.Value);
        //        }

        //        return result;
        //    } catch (Exception ex) {
        //        System.Diagnostics.Debug.WriteLine($"Errore nella conversione maiuscolo: {ex.Message}");

        //        // Fallback estremo: conversione semplice (potrebbero alterarsi stringhe e commenti)
        //        return sql.ToUpperInvariant();
        //    }
        //}

        private string ConvertToUppercasePreservingStringsAndComments(string sql) {
            if (string.IsNullOrWhiteSpace(sql)) return sql;

            try {
                var result = sql;
                var preservedContent = new List<(int start, int length, string content)>();

                // Step 1: Trova e salva stringhe letterali (tra apici singoli)
                var stringMatches = Regex.Matches(result, @"'(?:[^']|'')*'", RegexOptions.Singleline);
                foreach (Match match in stringMatches.Cast<Match>().Reverse()) {
                    preservedContent.Add((match.Index, match.Length, match.Value));
                }

                // Step 2: Trova e salva commenti SQL (-- commento)
                var lineCommentMatches = Regex.Matches(result, @"--.*$", RegexOptions.Multiline);
                foreach (Match match in lineCommentMatches.Cast<Match>().Reverse()) {
                    preservedContent.Add((match.Index, match.Length, match.Value));
                }

                // Step 3: Trova e salva commenti multi-linea (/* commento */)
                var blockCommentMatches = Regex.Matches(result, @"/\*.*?\*/", RegexOptions.Singleline);
                foreach (Match match in blockCommentMatches.Cast<Match>().Reverse()) {
                    preservedContent.Add((match.Index, match.Length, match.Value));
                }

                // Step 4: Sostituisci il contenuto da preservare con placeholder temporanei
                var placeholders = new Dictionary<string, string>();
                var placeholderIndex = 0;

                foreach (var item in preservedContent.OrderByDescending(x => x.start)) {
                    var placeholder = $"__PRESERVE_{placeholderIndex++}__";
                    placeholders[placeholder] = item.content;
                    result = result.Remove(item.start, item.length).Insert(item.start, placeholder);
                }

                // Step 5: Converti tutto in maiuscolo
                result = result.ToUpperInvariant();

                // Step 6: NUOVO - Applica la formattazione speciale per le funzioni
                result = FormatSpecificFunctions(result);

                // Step 7: Ripristina il contenuto preservato
                foreach (var kvp in placeholders) {
                    result = result.Replace(kvp.Key, kvp.Value);
                }

                return result;
            } catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"Errore nella conversione maiuscolo: {ex.Message}");
                return sql.ToUpperInvariant();
            }
        }

        private async void FormatSelectedOrAllSql() {
            try {
                string textToFormat;
                int startOffset;
                int length;

                // Se c'è testo selezionato, formatta solo quello
                if (!string.IsNullOrEmpty(txtScript.SelectedText)) {
                    textToFormat = txtScript.SelectedText;
                    startOffset = txtScript.SelectionStart;
                    length = txtScript.SelectionLength;
                } else {
                    // Altrimenti formatta tutto il testo
                    textToFormat = txtScript.Text;
                    startOffset = 0;
                    length = txtScript.Text.Length;
                }

                if (string.IsNullOrWhiteSpace(textToFormat)) {
                    ShowTemporaryMessage("ℹ️ Nessun testo da formattare");
                    return;
                }

                // Mostra feedback che la formattazione è in corso
                btnFormat.Content = "⏳ Formattazione...";
                btnFormat.IsEnabled = false;

                try {
                    // Salva la posizione del cursore
                    var originalCaretOffset = txtScript.CaretOffset;

                    // Formatta il codice (ora async)
                    var formattedText = await FormatSqlCodeAsync(textToFormat);

                    if (formattedText != textToFormat) {
                        // Sostituisce il testo con la versione formattata
                        txtScript.Document.Replace(startOffset, length, formattedText);

                        // Cerca di riposizionare il cursore in una posizione sensata
                        var newCaretOffset = Math.Min(originalCaretOffset, txtScript.Document.TextLength);
                        txtScript.CaretOffset = newCaretOffset;

                        // Feedback positivo
                        ShowTemporaryMessage("✅ Codice SQL formattato e convertito in maiuscolo!");
                    } else {
                        ShowTemporaryMessage("ℹ️ Il codice è già formattato correttamente");
                    }
                } finally {
                    // Ripristina il pulsante
                    btnFormat.Content = "🎨 Format";
                    btnFormat.IsEnabled = true;
                }

            } catch (Exception ex) {
                btnFormat.Content = "🎨 Format";
                btnFormat.IsEnabled = true;

                MessageBox.Show($"Errore nella formattazione del codice SQL:\n\n{ex.Message}", "Errore",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnFormat_Click(object sender, RoutedEventArgs e) {
            FormatSelectedOrAllSql();
        }

        private string FormatSpecificFunctions(string sql) {
            try {
                // Lista delle funzioni che vuoi formattare come PascalCase
                var functionsToFormat = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                    { "COUNT", "Count" },
                    { "SUM", "Sum" },
                    { "AVG", "Avg" },
                    { "MIN", "Min" },
                    { "MAX", "Max" },
                    { "ISNULL", "IsNull" },
                    { "COALESCE", "Coalesce" },
                    { "NULLIF", "NullIf" },
                    { "LEN", "Len" },
                    { "LTRIM", "LTrim" },
                    { "RTRIM", "RTrim" },
                    { "TRIM", "Trim" },
                    { "UPPER", "Upper" },
                    { "LOWER", "Lower" },
                    { "SUBSTRING", "Substring" },
                    { "REPLACE", "Replace" },
                    { "CHARINDEX", "CharIndex" },
                    { "PATINDEX", "PatIndex" },
                    { "LEFT", "Left" },
                    { "RIGHT", "Right" },
                    { "CONCAT", "Concat" },
                    { "CAST", "Cast" },
                    { "CONVERT", "Convert" },
                    { "GETDATE", "GetDate" },
                    { "DATEADD", "DateAdd" },
                    { "DATEDIFF", "DateDiff" },
                    { "DATEPART", "DatePart" },
                    { "YEAR", "Year" },
                    { "MONTH", "Month" },
                    { "DAY", "Day" },
                    { "ROUND", "Round" },
                    { "CEILING", "Ceiling" },
                    { "FLOOR", "Floor" },
                    { "ABS", "Abs" }
                };

                var result = sql;

                foreach (var function in functionsToFormat) {
                    var upperFunction = function.Key;
                    var pascalFunction = function.Value;

                    // Pattern per trovare la funzione seguita da parentesi aperta
                    // Usa word boundary per evitare di sostituire parti di parole
                    var pattern = $@"\b{Regex.Escape(upperFunction)}\s*\(";
                    var replacement = $"{pascalFunction}(";

                    result = Regex.Replace(result, pattern, replacement, RegexOptions.IgnoreCase);
                }

                return result;
            } catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"Errore nella formattazione funzioni: {ex.Message}");
                return sql; // Ritorna il testo originale in caso di errore
            }
        }
    }
}