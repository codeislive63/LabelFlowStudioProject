using LabelFlowStudio.Application.BoxProcessing;
using LabelFlowStudio.Core.Models;
using LabelFlowStudio.Desktop.Commands;
using System.Collections.ObjectModel;

namespace LabelFlowStudio.Desktop.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly IBoxProcessingService _boxProcessingService;
    private WorkMode _mode = WorkMode.Manual;

    private string _tenam = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isBusy;
    private bool _shouldPrintEndLabels;

    public MainViewModel(IBoxProcessingService boxProcessingService)
    {
        _boxProcessingService = boxProcessingService ?? throw new ArgumentNullException(nameof(boxProcessingService));

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

    public bool ShouldPrintEndLabels
    {
        get => _shouldPrintEndLabels;
        set => SetProperty(ref _shouldPrintEndLabels, value);
    }

    public WorkMode Mode
    {
        get => _mode;
        set => SetProperty(ref _mode, value);
    }

    public bool IsManual
    {
        get => _mode == WorkMode.Manual;
        set
        {
            if (value)
            {
                Mode = WorkMode.Manual;
            }
        }
    }

    public bool IsAutomatic
    {
        get => _mode == WorkMode.Automatic;
        set
        {
            if (value)
            {
                Mode = WorkMode.Automatic;
            }
        }
    }

    public ObservableCollection<LabelRecord> Records { get; }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
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
            StatusMessage = "Загрузка";
            Records.Clear();

            var request = new BoxProcessingRequest(
                Tenam: Tenam,
                Mode: Mode,
                ShouldPrintEndLabels: ShouldPrintEndLabels
            );

            var response = await _boxProcessingService.ProcessAsync(request, CancellationToken.None);

            foreach (var record in response.Records)
            {
                Records.Add(record);
            }

            StatusMessage = response.Message;

            if (response.Status == BoxProcessingStatus.Success)
            {
                Tenam = string.Empty;
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
