using System.ComponentModel;

namespace SigmaMS.Models {
    public class DatabaseConnection : INotifyPropertyChanged {
        private string _name = string.Empty;
        private string _server = string.Empty;
        private string _database = string.Empty;
        private bool _integratedSecurity = true;
        private string _username = string.Empty;
        private string _password = string.Empty;
        private string _lastUsedDatabase = string.Empty;

        public string Name {
            get => _name;
            set {
                _name = value;
                OnPropertyChanged(nameof(Name));
            }
        }

        public string Server {
            get => _server;
            set {
                _server = value;
                OnPropertyChanged(nameof(Server));
            }
        }

        public string Database {
            get => _database;
            set {
                _database = value;
                OnPropertyChanged(nameof(Database));
            }
        }

        public bool IntegratedSecurity {
            get => _integratedSecurity;
            set {
                _integratedSecurity = value;
                OnPropertyChanged(nameof(IntegratedSecurity));
            }
        }

        public string Username {
            get => _username;
            set {
                _username = value;
                OnPropertyChanged(nameof(Username));
            }
        }

        public string Password {
            get => _password;
            set {
                _password = value;
                OnPropertyChanged(nameof(Password));
            }
        }

        public string LastUsedDatabase {
            get => _lastUsedDatabase;
            set {
                _lastUsedDatabase = value;
                OnPropertyChanged(nameof(LastUsedDatabase));
            }
        }

        public string ConnectionString {
            get {
                if (IntegratedSecurity) {
                    return $"Server={Server};Database={Database};Integrated Security=true;TrustServerCertificate=true;";
                } else {
                    return $"Server={Server};Database={Database};User Id={Username};Password={Password};TrustServerCertificate=true;";
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class DatabaseObject : INotifyPropertyChanged {
        private string _name = string.Empty;
        private string _schema = string.Empty;
        private string _type = string.Empty;
        private DateTime? _modifyDate;

        public string Name {
            get => _name;
            set {
                _name = value;
                OnPropertyChanged(nameof(Name));
            }
        }

        public string Schema {
            get => _schema;
            set {
                _schema = value;
                OnPropertyChanged(nameof(Schema));
            }
        }

        public string Type {
            get => _type;
            set {
                _type = value;
                OnPropertyChanged(nameof(Type));
            }
        }

        public DateTime? ModifyDate {
            get => _modifyDate;
            set {
                _modifyDate = value;
                OnPropertyChanged(nameof(ModifyDate));
            }
        }

        public string FullName => $"{Schema}.{Name}";

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class DatabaseFilterSettings {
        public string ConnectionName { get; set; } = string.Empty;
        public string DatabaseName { get; set; } = string.Empty;
        public string Filter1 { get; set; } = string.Empty;
        public string Filter2 { get; set; } = string.Empty;
        public string Filter3 { get; set; } = string.Empty;
        public string LastSelectedObjectType { get; set; } = string.Empty;
        public DateTime LastUsed { get; set; } = DateTime.Now;
    }
}