using System.Windows;

namespace SigmaMS {
    public partial class App : Application {
        protected override void OnStartup(StartupEventArgs e) {
            base.OnStartup(e);

            // Gestione eccezioni globali
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            DevExpress.Xpf.Grid.GridControl.AllowInfiniteGridSize = true;
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e) {
            MessageBox.Show($"Errore non gestito nell'applicazione:\n\n{e.Exception.Message}\n\nDettagli:\n{e.Exception}",
                "Errore", MessageBoxButton.OK, MessageBoxImage.Error);

            // Imposta Handled = true per evitare che l'applicazione si chiuda
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e) {
            if (e.ExceptionObject is Exception ex) {
                MessageBox.Show($"Errore critico nell'applicazione:\n\n{ex.Message}\n\nDettagli:\n{ex}",
                    "Errore Critico", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}