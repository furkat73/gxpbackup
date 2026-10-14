using System.ComponentModel;

namespace MySqlBackupUtility.Models;

/// <summary>Строка чек-листа баз данных на вкладке «Подключения».</summary>
public sealed class DbCheckItem : INotifyPropertyChanged
{
    public string Name { get; set; } = "";

    private bool _isChecked;
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) return;
            _isChecked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
