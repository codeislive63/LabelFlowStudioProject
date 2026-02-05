using System.Collections.ObjectModel;
using LabelFlowStudio.Core.Abstractions;
using LabelFlowStudio.Core.Models;
using LabelFlowStudio.Desktop.Commands;

namespace LabelFlowStudio.Desktop.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly ILabelRepository _labelRepository;

    private string _tenam = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isBusy;

    public MainViewModel(ILabelRepository labelRepository)
    {
        _labelRepository = labelRepository ?? throw new ArgumentNullException(nameof(labelRepository));

        Records = new ObservableCollection<LabelRecord>();
        LoadRecordsCommand = new AsyncCommand(LoadRecordsAsync, CanLoadRecords, HandleCommandException);
    }

    public string Tenam
    {
        get => _tenam;
        set
        {
            if (SetProperty(ref _tenam, value))
            {
                LoadRecordsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public ObservableCollection<LabelRecord> Records { get; }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            SetProperty(ref _statusMessage, value);
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                LoadRecordsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public AsyncCommand LoadRecordsCommand { get; }

    private bool CanLoadRecords()
    {
        if (IsBusy)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(Tenam);
    }

    private async Task LoadRecordsAsync()
    {
        IsBusy = true;

        try
        {
            StatusMessage = "Загрузка...";
            Records.Clear();

            IReadOnlyList<LabelRecord> records =
                await _labelRepository.GetByTenamAsync(Tenam, CancellationToken.None);

            foreach (LabelRecord record in records)
            {
                Records.Add(record);
            }

            if (records.Count == 0)
            {
                StatusMessage = "Данных не найдено.";
            }
            else
            {
                StatusMessage = $"Загружено строк: {records.Count}.";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void HandleCommandException(Exception exception)
    {
        StatusMessage = exception.Message;
    }
}
