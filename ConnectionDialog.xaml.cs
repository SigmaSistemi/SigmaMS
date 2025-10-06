using SigmaMS.Models;
using SigmaMS.Services;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;

namespace SigmaMS.Dialogs {
    public partial class ConnectionDialog : Window {
        private readonly DataService _dataService;

        public DatabaseConnection? Connection { get; private set; }

        public ConnectionDialog(DatabaseConnection? connection = null) {
            InitializeComponent();
            _dataService = new DataService();

            if (connection != null) {
                LoadConnection(connection);
                Title = "Modifica Connessione";
            } else {
                Title = "Nuova Connessione";
            }

            UpdateCredentialsVisibility();
        }

        private void LoadConnection(DatabaseConnection connection) {
            txtName.Text = connection.Name;
            txtServer.Text = connection.Server;
            txtDatabase.Text = connection.Database;
            chkIntegratedSecurity.IsChecked = connection.IntegratedSecurity;
            txtUsername.Text = connection.Username;
            txtPassword.Password = connection.Password;
        }

        private void ChkIntegratedSecurity_Changed(object sender, RoutedEventArgs e) {
            UpdateCredentialsVisibility();
        }

        private void UpdateCredentialsVisibility() {
            //pnlCredentials.Visibility = chkIntegratedSecurity.IsChecked == true ?
            //    Visibility.Collapsed : Visibility.Visible;
        }

        private async void BtnTest_Click(object sender, RoutedEventArgs e) {
            try {
                btnTest.IsEnabled = false;
                txtTestResult.Text = "Test in corso...";
                txtTestResult.Foreground = System.Windows.Media.Brushes.Blue;

                var testConnection = CreateConnectionFromForm();
                if (testConnection == null) {
                    txtTestResult.Text = "Compila tutti i campi obbligatori";
                    txtTestResult.Foreground = System.Windows.Media.Brushes.Red;
                    return;
                }

                bool success = await _dataService.TestConnectionAsync(testConnection.ConnectionString);

                if (success) {
                    txtTestResult.Text = "✓ Connessione riuscita";
                    txtTestResult.Foreground = System.Windows.Media.Brushes.Green;
                } else {
                    txtTestResult.Text = "✗ Connessione fallita";
                    txtTestResult.Foreground = System.Windows.Media.Brushes.Red;
                }
            } catch (Exception ex) {
                txtTestResult.Text = $"✗ Errore: {ex.Message}";
                txtTestResult.Foreground = System.Windows.Media.Brushes.Red;
            } finally {
                btnTest.IsEnabled = true;
            }
        }

        private void BtnOK_Click(object sender, RoutedEventArgs e) {
            try {
                var connection = CreateConnectionFromForm();
                if (connection == null) {
                    MessageBox.Show("Compila tutti i campi obbligatori.", "Errore",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrWhiteSpace(connection.Name)) {
                    MessageBox.Show("Il nome della connessione è obbligatorio.", "Errore",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtName.Focus();
                    return;
                }

                if (string.IsNullOrWhiteSpace(connection.Server)) {
                    MessageBox.Show("Il server è obbligatorio.", "Errore",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtServer.Focus();
                    return;
                }

                Connection = connection;
                DialogResult = true;
            } catch (Exception ex) {
                MessageBox.Show($"Errore nella validazione: {ex.Message}", "Errore",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) {
            DialogResult = false;
        }

        private DatabaseConnection? CreateConnectionFromForm() {
            try {
                return new DatabaseConnection {
                    Name = txtName.Text.Trim(),
                    Server = txtServer.Text.Trim(),
                    Database = txtDatabase.Text.Trim(),
                    IntegratedSecurity = chkIntegratedSecurity.IsChecked == true,
                    Username = txtUsername.Text.Trim(),
                    Password = txtPassword.Password
                };
            } catch {
                return null;
            }
        }
    }
}