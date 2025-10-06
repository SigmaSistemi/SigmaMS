using DevExpress.Utils;
using DevExpress.Xpf.Core;
using DevExpress.Xpf.Grid;
using DevExpress.Xpf.Printing;
using DevExpress.XtraPrinting;
using ICSharpCode.AvalonEdit.Document;
using Microsoft.Win32;
using SigmaMS.Services;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace SigmaMS {
    public partial class QueryResultsWindow : Window {
        private List<QueryResult> _results;
        private string _originalQuery;
        private readonly List<GridControl> _gridControls = new List<GridControl>();

        public bool IsClosed { get; private set; } = false;

        public QueryResultsWindow(List<QueryResult> results, string originalQuery) {
            InitializeComponent();

            _results = new List<QueryResult>(results); // Crea una copia modificabile
            _originalQuery = originalQuery;

            InitializeResults();
        }

        public QueryResultsWindow(QueryResult result, string originalQuery)
            : this(new List<QueryResult> { result }, originalQuery) {
        }

        private void InitializeResults() {
            System.Diagnostics.Debug.WriteLine("=== INIZIO INITIALIZE RESULTS ===");

            if (_results == null || !_results.Any()) {
                System.Diagnostics.Debug.WriteLine("Nessun risultato da visualizzare");
                txtQueryInfo.Text = "Nessun risultato da visualizzare";
                return;
            }

            System.Diagnostics.Debug.WriteLine($"Risultati da processare: {_results.Count}");

            // Informazioni generali
            var successCount = _results.Count(r => r.IsSuccess);
            var errorCount = _results.Count(r => !r.IsSuccess);
            var totalTime = TimeSpan.FromMilliseconds(_results.Sum(r => r.ExecutionTime.TotalMilliseconds));

            txtQueryInfo.Text = $"Query eseguite: {_results.Count} | Successi: {successCount} | Errori: {errorCount}";
            txtExecutionTime.Text = $"Tempo totale: {totalTime.TotalMilliseconds:F0} ms";

            // Crea i tab per ogni risultato
            for (int i = 0; i < _results.Count; i++) {
                System.Diagnostics.Debug.WriteLine($"Creando tab {i + 1} per risultato {i}");
                CreateTabForResult(_results[i], i + 1);
                System.Diagnostics.Debug.WriteLine($"Tab creato. Totale tab: {tabResults.Items.Count}");
            }

            // Aggiorna contatore righe totali
            var totalRows = _results.Where(r => r.IsSuccess && r.ResultData != null)
                                   .Sum(r => r.ResultData.Rows.Count);
            var totalAffected = _results.Where(r => r.IsSuccess).Sum(r => r.RowsAffected);

            if (totalRows > 0) {
                txtRowCount.Text = $"Righe restituite: {totalRows}";
            } else if (totalAffected > 0) {
                txtRowCount.Text = $"Righe interessate: {totalAffected}";
            }

            System.Diagnostics.Debug.WriteLine("=== FINE INITIALIZE RESULTS ===");
        }

        private void CreateTabForResult(QueryResult result, int queryNumber) {
            System.Diagnostics.Debug.WriteLine($"=== CREANDO TAB {queryNumber} ===");

            // Debug per diagnosticare il problema
            //DebugQueryResult(result, queryNumber);

            var tabItem = new DXTabItem();

            if (result.IsSuccess) {
                // Tab per risultati positivi
                if (result.ResultData != null && result.ResultData.Rows.Count > 0) {
                    // Query SELECT con dati - usa DevExpress GridControl
                    System.Diagnostics.Debug.WriteLine($"Creando GridControl per {result.ResultData.Rows.Count} righe");
                    tabItem.Header = $"Risultati {queryNumber} ({result.ResultData.Rows.Count} righe)";

                    var gridControl = CreateDevExpressGrid(result);
                    tabItem.Content = gridControl;

                    System.Diagnostics.Debug.WriteLine($"GridControl creato: {gridControl != null}");
                    System.Diagnostics.Debug.WriteLine($"GridControl.ItemsSource: {gridControl?.ItemsSource != null}");

                } else {
                    // Query di modifica o senza risultati
                    System.Diagnostics.Debug.WriteLine($"Creando pannello messaggio: {result.Message}");
                    tabItem.Header = $"Query {queryNumber} ✅";
                    tabItem.Content = CreateMessagePanel(result.Message, false);
                }
            } else {
                // Tab per errori
                System.Diagnostics.Debug.WriteLine($"Creando pannello errore: {result.ErrorMessage}");
                tabItem.Header = $"Query {queryNumber} ❌";
                tabItem.Content = CreateMessagePanel(result.ErrorMessage, true);
            }

            System.Diagnostics.Debug.WriteLine($"Aggiungendo tab. Header: {tabItem.Header}");
            System.Diagnostics.Debug.WriteLine($"Content type: {tabItem.Content?.GetType().Name}");

            tabResults.Items.Add(tabItem);

            System.Diagnostics.Debug.WriteLine($"Tab aggiunto. Totale: {tabResults.Items.Count}");
            System.Diagnostics.Debug.WriteLine($"=== FINE CREAZIONE TAB {queryNumber} ===");
        }

        private GridControl CreateDevExpressGrid(QueryResult result) {
            System.Diagnostics.Debug.WriteLine($"=== CREANDO DEVEXPRESS GRID ===");
            System.Diagnostics.Debug.WriteLine($"ResultData è null: {result.ResultData == null}");

            if (result.ResultData == null) {
                System.Diagnostics.Debug.WriteLine("ERRORE: ResultData è null!");
                return new GridControl(); // Ritorna un grid vuoto
            }

            System.Diagnostics.Debug.WriteLine($"Righe: {result.ResultData.Rows.Count}");
            System.Diagnostics.Debug.WriteLine($"Colonne: {result.ResultData.Columns.Count}");

            var gridControl = new GridControl {
                Name = $"gridResults_{DateTime.Now.Ticks}",
                AutoGenerateColumns = AutoGenerateColumnsMode.AddNew
            };

            var tableView = new TableView {
                ShowGroupPanel = false,
                AllowEditing = false,
                AllowColumnFiltering = chkEnableFiltering.IsChecked == true,
                AllowGrouping = chkEnableGrouping.IsChecked == true,
                AutoWidth = false,
                BestFitMode = DevExpress.Xpf.Core.BestFitMode.Smart,
                ShowAutoFilterRow = chkEnableFiltering.IsChecked == true,
                ShowFilterPanelMode = ShowFilterPanelMode.Never,
                SearchPanelAllowFilter = false,
                ShowSearchPanelMode = ShowSearchPanelMode.Always
            };

            gridControl.View = tableView;

            try {
                System.Diagnostics.Debug.WriteLine("Assegnando ItemsSource...");
                gridControl.ItemsSource = result.ResultData.DefaultView;

                System.Diagnostics.Debug.WriteLine($"ItemsSource assegnato: {gridControl.ItemsSource != null}");
                System.Diagnostics.Debug.WriteLine($"DefaultView Count: {result.ResultData.DefaultView.Count}");

                _gridControls.Add(gridControl);

                System.Diagnostics.Debug.WriteLine($"Grid aggiunto alla collezione. Totale: {_gridControls.Count}");

            } catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"ERRORE nell'assegnazione ItemsSource: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"StackTrace: {ex.StackTrace}");
            }

            System.Diagnostics.Debug.WriteLine($"=== FINE CREAZIONE DEVEXPRESS GRID ===");

            return gridControl;
        }

        private ScrollViewer CreateMessagePanel(string message, bool isError) {
            var textBlock = new TextBlock {
                Text = message,
                Margin = new Thickness(10),
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 12
            };

            if (isError) {
                textBlock.Foreground = System.Windows.Media.Brushes.Red;
                textBlock.Background = System.Windows.Media.Brushes.LightPink;
                textBlock.Padding = new Thickness(10);
            } else {
                textBlock.Foreground = System.Windows.Media.Brushes.DarkGreen;
                textBlock.Background = System.Windows.Media.Brushes.LightGreen;
                textBlock.Padding = new Thickness(10);
            }

            return new ScrollViewer {
                Content = textBlock,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
            };
        }

        private void BtnExportCsv_Click(object sender, RoutedEventArgs e) {
            try {
                // Trova il primo risultato con dati
                var resultWithData = _results.FirstOrDefault(r => r.IsSuccess &&
                                                                r.ResultData != null &&
                                                                r.ResultData.Rows.Count > 0);
                if (resultWithData == null) {
                    MessageBox.Show("Nessun dato da esportare.", "Info",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var dialog = new SaveFileDialog {
                    Filter = "File CSV|*.csv",
                    Title = "Esporta Risultati",
                    FileName = $"QueryResults_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
                };

                if (dialog.ShowDialog() == true) {
                    ExportToCsv(resultWithData, dialog.FileName);
                    MessageBox.Show("Esportazione completata!", "Successo",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            } catch (Exception ex) {
                MessageBox.Show($"Errore nell'esportazione: {ex.Message}", "Errore",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportToCsv(QueryResult result, string filePath) {
            var csv = new StringBuilder();

            // Header dalle colonne del DataTable
            var columnNames = result.ResultData.Columns.Cast<DataColumn>()
                .Select(column => EscapeCsvField(column.ColumnName));
            csv.AppendLine(string.Join(",", columnNames));

            // Dati dalle righe del DataTable
            foreach (DataRow row in result.ResultData.Rows) {
                var values = row.ItemArray.Select(field => {
                    if (field == null || field == DBNull.Value) {
                        return "";
                    }
                    return EscapeCsvField(field.ToString());
                });
                csv.AppendLine(string.Join(",", values));
            }

            File.WriteAllText(filePath, csv.ToString(), Encoding.UTF8);
        }

        private string EscapeCsvField(string field) {
            if (string.IsNullOrEmpty(field)) return "";

            if (field.Contains(",") || field.Contains("\"") || field.Contains("\n")) {
                return "\"" + field.Replace("\"", "\"\"") + "\"";
            }
            return field;
        }

        private void BtnCopyResults_Click(object sender, RoutedEventArgs e) {
            try {
                var activeGrid = GetActiveGridControl();
                if (activeGrid == null) {
                    var resultWithData = _results.FirstOrDefault(r => r.IsSuccess && r.ResultData.Rows.Count > 0);
                    if (resultWithData == null) {
                        Clipboard.SetText("Nessun dato da copiare.");
                        return;
                    }

                    // Copia dal DataTable
                    var text = new StringBuilder();

                    // Header
                    var columnNames = resultWithData.ResultData.Columns.Cast<DataColumn>()
                        .Select(c => c.ColumnName);
                    text.AppendLine(string.Join("\t", columnNames));

                    // Dati
                    foreach (DataRow row in resultWithData.ResultData.Rows) {
                        var values = row.ItemArray.Select(field => {
                            if (field == null || field == DBNull.Value) return "";
                            return field.ToString();
                        });
                        text.AppendLine(string.Join("\t", values));
                    }

                    Clipboard.SetText(text.ToString());
                } else {
                    // Usa la funzionalità di copia di DevExpress
                    if (activeGrid.View is TableView tableView) {
                        tableView.CopyToClipboard();
                    }
                }

                // Feedback visivo
                ShowCopyFeedback();

            } catch (Exception ex) {
                MessageBox.Show($"Errore nella copia: {ex.Message}", "Errore",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowCopyFeedback() {
            // Feedback visivo temporaneo
            var originalContent = btnCopyResults.Content;
            btnCopyResults.Content = "✅ Copiato!";

            var timer = new System.Windows.Threading.DispatcherTimer {
                Interval = TimeSpan.FromSeconds(1.5)
            };
            timer.Tick += (s, args) => {
                btnCopyResults.Content = originalContent;
                timer.Stop();
            };
            timer.Start();
        }

        private void ChkEnableFiltering_Changed(object sender, RoutedEventArgs e) {
            var isEnabled = chkEnableFiltering.IsChecked == true;

            foreach (var gridControl in _gridControls) {
                if (gridControl.View is TableView tableView) {
                    tableView.AllowColumnFiltering = isEnabled ? true : false;
                    tableView.ShowAutoFilterRow = isEnabled;
                    //tableView.ShowSearchPanel = isEnabled;
                }
            }
        }

        private void ChkEnableGrouping_Changed(object sender, RoutedEventArgs e) {
            var isEnabled = chkEnableGrouping.IsChecked == true;

            foreach (var gridControl in _gridControls) {
                if (gridControl.View is TableView tableView) {
                    tableView.AllowGrouping = isEnabled;
                    tableView.ShowGroupPanel = isEnabled;
                }
            }
        }

        private void BtnExportExcel_Click(object sender, RoutedEventArgs e) {
            try {
                var activeGrid = GetActiveGridControl();
                if (activeGrid == null) {
                    MessageBox.Show("Nessun dato da esportare.", "Info",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var dialog = new SaveFileDialog {
                    Filter = "File Excel|*.xlsx|File Excel 97-2003|*.xls",
                    Title = "Esporta in Excel",
                    FileName = $"QueryResults_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
                };

                if (dialog.ShowDialog() == true) {
                    var options = new XlsxExportOptions {
                        ShowGridLines = true,
                        SheetName = "Query Results"
                    };

                    activeGrid.View.ExportToXlsx(dialog.FileName, options);

                    MessageBox.Show("Esportazione Excel completata!", "Successo",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            } catch (Exception ex) {
                MessageBox.Show($"Errore nell'esportazione Excel: {ex.Message}", "Errore",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private GridControl GetActiveGridControl() {
            if (tabResults.SelectedItem is TabItem selectedTab) {
                return selectedTab.Content as GridControl;
            }
            return _gridControls.FirstOrDefault();
        }

        protected override void OnClosed(EventArgs e) {
            IsClosed = true;
            base.OnClosed(e);
        }

        public void UpdateResults(List<QueryResult> newResults, string newQuery) {
            if (IsClosed) return;

            try {
                System.Diagnostics.Debug.WriteLine("=== INIZIO UPDATE RESULTS ===");
                System.Diagnostics.Debug.WriteLine($"Nuovi risultati: {newResults.Count}");

                // Aggiorna i risultati
                _results.Clear();
                _results.AddRange(newResults);
                _originalQuery = newQuery;

                System.Diagnostics.Debug.WriteLine($"Tab esistenti prima della pulizia: {tabResults.Items.Count}");

                // IMPORTANTE: Prima di pulire, disconnetti i DataSource esistenti
                foreach (var gridControl in _gridControls) {
                    try {
                        gridControl.ItemsSource = null;
                        gridControl.Columns.Clear();
                    } catch (Exception ex) {
                        System.Diagnostics.Debug.WriteLine($"Errore nella pulizia grid: {ex.Message}");
                    }
                }

                // Pulisci i controlli esistenti
                tabResults.Items.Clear();
                _gridControls.Clear();

                System.Diagnostics.Debug.WriteLine($"Tab dopo pulizia: {tabResults.Items.Count}");

                // Forza il garbage collection per liberare le risorse
                GC.Collect();
                GC.WaitForPendingFinalizers();

                // Ricrea l'interfaccia con i nuovi risultati
                System.Diagnostics.Debug.WriteLine("Chiamando InitializeResults...");
                InitializeResults();

                System.Diagnostics.Debug.WriteLine($"Tab dopo InitializeResults: {tabResults.Items.Count}");

                // Aggiorna il titolo della finestra con timestamp
                Title = $"Risultati Query - {DateTime.Now:HH:mm:ss}";

                // Feedback visivo temporaneo
                ShowUpdateFeedback();

                System.Diagnostics.Debug.WriteLine("=== FINE UPDATE RESULTS ===");

            } catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"ERRORE in UpdateResults: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"StackTrace: {ex.StackTrace}");

                MessageBox.Show($"Errore nell'aggiornamento dei risultati: {ex.Message}",
                    "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CleanupAndRecreate() {
            // Disconnetti e pulisci tutti i controlli esistenti
            foreach (var gridControl in _gridControls) {
                try {
                    if (gridControl.ItemsSource is DataView dataView) {
                        dataView.Table?.Dispose();
                    }
                    gridControl.ItemsSource = null;
                    gridControl.Columns.Clear();
                } catch (Exception ex) {
                    System.Diagnostics.Debug.WriteLine($"Errore nella pulizia grid: {ex.Message}");
                }
            }

            // Pulisci le collezioni
            tabResults.Items.Clear();
            _gridControls.Clear();

            // Forza la garbage collection
            GC.Collect();
            GC.WaitForPendingFinalizers();

            // Usa il Dispatcher per assicurarsi che la pulizia sia completata
            Dispatcher.BeginInvoke(() => {
                // Ricrea tutto da capo
                InitializeResults();
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        private void ShowUpdateFeedback() {
            var originalTitle = Title;
            Title = $"{Title} - ✅ Aggiornato!";

            var timer = new System.Windows.Threading.DispatcherTimer {
                Interval = TimeSpan.FromSeconds(1.5)
            };
            timer.Tick += (s, e) => {
                if (!IsClosed) {
                    Title = originalTitle;
                }
                timer.Stop();
            };
            timer.Start();
        }




    }


    // Converter per gestire i valori NULL nelle celle
    public class NullValueConverter : System.Windows.Data.IValueConverter {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
            if (value == null || value == DBNull.Value) {
                return "<NULL>";
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) {
            throw new NotImplementedException();
        }
    }
}