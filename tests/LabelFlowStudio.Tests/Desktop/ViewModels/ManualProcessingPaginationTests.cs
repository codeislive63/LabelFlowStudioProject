using LabelFlowStudio.Application.BoxProcessing;
using LabelFlowStudio.Application.BoxProcessing.Contracts;
using LabelFlowStudio.Application.BoxProcessing.Weight;
using LabelFlowStudio.Application.Tests.Infrastructure;
using LabelFlowStudio.Core.Models;
using LabelFlowStudio.Desktop.ViewModels;
using LabelFlowStudio.Devices.BoxScanner;
using Microsoft.Extensions.Logging.Abstractions;
using System.ComponentModel;
using System.Reflection;

namespace LabelFlowStudio.Application.Tests.Desktop.ViewModels;

public sealed class ManualProcessingPaginationTests
{
    [Theory]
    [InlineData(0, 0, 0, 0, 0)]
    [InlineData(1, 1, 1, 1, 1)]
    [InlineData(10, 1, 10, 1, 10)]
    [InlineData(11, 2, 10, 1, 10)]
    [InlineData(55, 6, 10, 1, 10)]
    public void Projection_CalculatesCountsAndFirstPageRange(
        int recordCount,
        int expectedPages,
        int expectedVisibleCount,
        int expectedRangeStart,
        int expectedRangeEnd)
    {
        StaTestRunner.Run(() =>
        {
            using var work = CreateWorkViewModel();
            using var manual = new ManualProcessingViewModel(work);

            AddRecords(work, recordCount);

            Assert.Equal(10, manual.PageSize);
            Assert.Equal([10, 25, 50], manual.PageSizeOptions);
            Assert.Equal(recordCount, manual.TotalItems);
            Assert.Equal(expectedPages, manual.TotalPages);
            Assert.Equal(expectedVisibleCount, manual.PagedRecords.Count);
            Assert.Same(manual.PagedRecords, manual.VisibleRecords);
            Assert.Equal(expectedRangeStart, manual.RangeStart);
            Assert.Equal(expectedRangeEnd, manual.RangeEnd);
            Assert.Equal(recordCount > 0, manual.HasItems);
        });
    }

    [Fact]
    public void EmptyProjection_HasStablePageAndDisabledNavigation()
    {
        StaTestRunner.Run(() =>
        {
            using var work = CreateWorkViewModel();
            using var manual = new ManualProcessingViewModel(work);

            Assert.Equal(1, manual.CurrentPage);
            Assert.Equal(0, manual.TotalPages);
            Assert.Equal("Показано 0 из 0", manual.RangeText);
            Assert.Empty(manual.PageNavigationItems);
            Assert.False(manual.FirstPageCommand.CanExecute(null));
            Assert.False(manual.PreviousPageCommand.CanExecute(null));
            Assert.False(manual.NextPageCommand.CanExecute(null));
            Assert.False(manual.LastPageCommand.CanExecute(null));

            manual.NavigateToPage(100);

            Assert.Equal(1, manual.CurrentPage);
            Assert.Empty(manual.PagedRecords);
        });
    }

    [Fact]
    public void NavigationCommands_RespectBoundariesAndKeepPartialLastPage()
    {
        StaTestRunner.Run(() =>
        {
            using var work = CreateWorkViewModel();
            using var manual = new ManualProcessingViewModel(work);
            AddRecords(work, 55);

            Assert.False(manual.FirstPageCommand.CanExecute(null));
            Assert.False(manual.PreviousPageCommand.CanExecute(null));
            Assert.True(manual.NextPageCommand.CanExecute(null));
            Assert.True(manual.LastPageCommand.CanExecute(null));

            manual.PreviousPageCommand.Execute(null);
            Assert.Equal(1, manual.CurrentPage);

            manual.NextPageCommand.Execute(null);
            Assert.Equal(2, manual.CurrentPage);
            Assert.Equal("011", manual.PagedRecords[0].Artnr);
            Assert.Equal("Показано 11–20 из 55", manual.RangeText);

            manual.LastPageCommand.Execute(null);
            Assert.Equal(6, manual.CurrentPage);
            Assert.Equal(5, manual.PagedRecords.Count);
            Assert.Equal("051", manual.PagedRecords[0].Artnr);
            Assert.Equal("055", manual.PagedRecords[^1].Artnr);
            Assert.False(manual.NextPageCommand.CanExecute(null));
            Assert.False(manual.LastPageCommand.CanExecute(null));

            manual.NavigateToPage(100);
            Assert.Equal(6, manual.CurrentPage);

            manual.FirstPageCommand.Execute(null);
            Assert.Equal(1, manual.CurrentPage);

            manual.NavigateToPage(-100);
            Assert.Equal(1, manual.CurrentPage);
        });
    }

    [Fact]
    public void PageSize_PreservesTheCurrentPositionWithinNewPageBounds()
    {
        StaTestRunner.Run(() =>
        {
            using var work = CreateWorkViewModel();
            using var manual = new ManualProcessingViewModel(work);
            AddRecords(work, 55);
            manual.NavigateToPage(4);

            manual.PageSize = 25;

            Assert.Equal(2, manual.CurrentPage);
            Assert.Equal(3, manual.TotalPages);
            Assert.Equal(25, manual.PagedRecords.Count);
            Assert.Equal(26, manual.RangeStart);
            Assert.Equal(50, manual.RangeEnd);

            manual.PageSize = 10;

            Assert.Equal(3, manual.CurrentPage);
            Assert.Equal(6, manual.TotalPages);
            Assert.Equal(10, manual.PagedRecords.Count);
            Assert.Equal(21, manual.RangeStart);
            Assert.Equal(30, manual.RangeEnd);
        });
    }

    [Fact]
    public void PageSize_RejectsUnsupportedValuesWithoutChangingProjection()
    {
        StaTestRunner.Run(() =>
        {
            using var work = CreateWorkViewModel();
            using var manual = new ManualProcessingViewModel(work);
            AddRecords(work, 11);

            Assert.Throws<ArgumentOutOfRangeException>(() => manual.PageSize = 20);

            Assert.Equal(10, manual.PageSize);
            Assert.Equal(2, manual.TotalPages);
            Assert.Equal(10, manual.PagedRecords.Count);
        });
    }

    [Fact]
    public void PageSize_SequentialChangesRecalculatePagesAndDisplayedRange()
    {
        StaTestRunner.Run(() =>
        {
            using var work = CreateWorkViewModel();
            using var manual = new ManualProcessingViewModel(work);
            AddRecords(work, 55);

            manual.PageSize = 25;

            Assert.Equal(25, manual.PageSize);
            Assert.Equal(3, manual.TotalPages);
            Assert.Equal(1, manual.CurrentPage);
            Assert.Equal("Показано 1–25 из 55", manual.RangeText);
            Assert.Equal("025", manual.PagedRecords[^1].Artnr);

            manual.PageSize = 50;

            Assert.Equal(50, manual.PageSize);
            Assert.Equal(2, manual.TotalPages);
            Assert.Equal(1, manual.CurrentPage);
            Assert.Equal("Показано 1–50 из 55", manual.RangeText);
            Assert.Equal("050", manual.PagedRecords[^1].Artnr);
        });
    }

    [Fact]
    public void Projection_IgnoresCommandInputAndResetsForReplacementRecordSet()
    {
        StaTestRunner.Run(() =>
        {
            using var work = CreateWorkViewModel();
            using var manual = new ManualProcessingViewModel(work);
            AddRecords(work, 55, "4430558");
            manual.NavigateToPage(4);

            manual.TenamInput = "4430559";
            work.Tenam = manual.TenamInput;

            Assert.Equal(4, manual.CurrentPage);
            Assert.Equal("031", manual.PagedRecords[0].Artnr);
            Assert.Equal("4430559", manual.TenamInput);

            manual.NavigateToPage(3);
            work.Records.Clear();
            AddRecords(work, 11, "4430559");

            Assert.Equal(1, manual.CurrentPage);
            Assert.Equal(2, manual.TotalPages);
            Assert.Equal(10, manual.PagedRecords.Count);
            Assert.All(manual.PagedRecords, record => Assert.Equal("4430559", record.Tenam));
        });
    }

    [Fact]
    public void PageNavigationItems_ProvideNumberWindowAndNonInteractiveEllipses()
    {
        StaTestRunner.Run(() =>
        {
            using var work = CreateWorkViewModel();
            using var manual = new ManualProcessingViewModel(work);
            AddRecords(work, 125);
            manual.NavigateToPage(7);

            Assert.Equal(
                ["1", "…", "6", "7", "8", "…", "13"],
                manual.PageNavigationItems.Select(item => item.Text));
            Assert.Equal(2, manual.PageNavigationItems.Count(item => item.IsEllipsis));
            Assert.All(
                manual.PageNavigationItems.Where(item => item.IsEllipsis),
                item => Assert.Null(item.Command));

            var current = Assert.Single(manual.PageNavigationItems, item => item.IsCurrent);
            Assert.Equal(7, current.PageNumber);
            Assert.NotNull(current.Command);
            Assert.False(current.Command!.CanExecute(null));

            var pageEight = Assert.Single(manual.PageNavigationItems, item => item.PageNumber == 8);
            pageEight.Command!.Execute(null);

            Assert.Equal(8, manual.CurrentPage);
        });
    }

    [Fact]
    public void PageNavigationItems_KeepCompactBoundaryContextAtWindowEdges()
    {
        StaTestRunner.Run(() =>
        {
            using var work = CreateWorkViewModel();
            using var manual = new ManualProcessingViewModel(work);
            AddRecords(work, 125);

            Assert.Equal(
                ["1", "2", "3", "…", "13"],
                manual.PageNavigationItems.Select(item => item.Text));

            manual.LastPageCommand.Execute(null);

            Assert.Equal(
                ["1", "…", "11", "12", "13"],
                manual.PageNavigationItems.Select(item => item.Text));
            Assert.True(manual.PageNavigationItems[^1].IsCurrent);
        });
    }

    [Fact]
    public void Projection_ClampsCurrentPageWhenCollectionShrinksWithoutReset()
    {
        StaTestRunner.Run(() =>
        {
            using var work = CreateWorkViewModel();
            using var manual = new ManualProcessingViewModel(work);
            AddRecords(work, 55);
            manual.LastPageCommand.Execute(null);

            while (work.Records.Count > 11)
            {
                work.Records.RemoveAt(work.Records.Count - 1);
            }

            Assert.Equal(11, manual.TotalItems);
            Assert.Equal(2, manual.TotalPages);
            Assert.Equal(2, manual.CurrentPage);
            Assert.Single(manual.PagedRecords);
            Assert.Equal("011", manual.PagedRecords[0].Artnr);
            Assert.Equal("Показано 11–11 из 11", manual.RangeText);
            Assert.False(manual.NextPageCommand.CanExecute(null));
        });
    }

    [Fact]
    public void Sorting_IsAppliedBeforePagingAndNeverReordersFullWorkRecords()
    {
        StaTestRunner.Run(() =>
        {
            using var work = CreateWorkViewModel();
            using var manual = new ManualProcessingViewModel(work);

            for (var index = 30; index >= 1; index--)
            {
                work.Records.Add(CreateRecord(index));
            }

            manual.ApplySort(nameof(LabelRecord.Artnr), ListSortDirection.Ascending);
            manual.NavigateToPage(2);

            Assert.Equal("011", manual.PagedRecords[0].Artnr);
            Assert.Equal("020", manual.PagedRecords[^1].Artnr);
            Assert.Equal("030", work.Records[0].Artnr);
            Assert.Equal("001", work.Records[^1].Artnr);
            Assert.Equal(nameof(LabelRecord.Artnr), manual.SortPropertyName);
            Assert.Equal(ListSortDirection.Ascending, manual.SortDirection);

            work.Records.Add(CreateRecord(0));

            Assert.Equal("010", manual.PagedRecords[0].Artnr);
            Assert.Equal("019", manual.PagedRecords[^1].Artnr);
            Assert.Equal("000", work.Records[^1].Artnr);
            Assert.Equal(nameof(LabelRecord.Artnr), manual.SortPropertyName);
            Assert.Equal(ListSortDirection.Ascending, manual.SortDirection);

            manual.ClearSort();

            Assert.Equal(1, manual.CurrentPage);
            Assert.Equal("030", manual.PagedRecords[0].Artnr);
            Assert.Null(manual.SortPropertyName);
            Assert.Null(manual.SortDirection);
        });
    }

    [Fact]
    public void Sorting_HandlesNullableDecimalColumnsInBothDirections()
    {
        StaTestRunner.Run(() =>
        {
            using var work = CreateWorkViewModel();
            using var manual = new ManualProcessingViewModel(work);
            work.Records.Add(new LabelRecord { Artnr = "two", Brutto = 2m });
            work.Records.Add(new LabelRecord { Artnr = "none", Brutto = null });
            work.Records.Add(new LabelRecord { Artnr = "one", Brutto = 1m });

            manual.ApplySort(nameof(LabelRecord.Brutto), ListSortDirection.Ascending);

            Assert.Equal([null, 1m, 2m], manual.PagedRecords.Select(record => record.Brutto));

            manual.ApplySort(nameof(LabelRecord.Brutto), ListSortDirection.Descending);

            Assert.Equal([2m, 1m, null], manual.PagedRecords.Select(record => record.Brutto));
        });
    }

    [Fact]
    public Task PrintCommandsRetainFullLoadedResponseWhenDisplayMovesToAnotherPage() =>
        StaTestRunner.RunAsync(async () =>
        {
            var allRecords = Enumerable.Range(1, 55)
                .Select(CreateRecord)
                .ToArray();
            var service = new CapturingProcessingService(
                new BoxProcessingResponse(
                    BoxProcessingStatus.Success,
                    "Данные загружены",
                    allRecords,
                    1m,
                    PrintPlan.None));

            using var work = CreateWorkViewModel(service);
            using var manual = new ManualProcessingViewModel(work);
            work.Tenam = "4430558";

            work.LoadRecordsCommand.Execute(null);

            await service.Called.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await WaitHelpers.WaitUntilAsync(() => !work.IsBusy, TimeSpan.FromSeconds(2));
            manual.NavigateToPage(4);

            Assert.Equal(55, work.Records.Count);
            Assert.Equal(10, manual.PagedRecords.Count);
            Assert.Equal("031", manual.PagedRecords[0].Artnr);
            Assert.True(work.OpenEndLabelPreviewCommand.CanExecute(null));
            Assert.True(work.OpenStuffingSheetPreviewCommand.CanExecute(null));

            var endLabelResponse = ReadPrivateResponse(work, "_lastSuccessfulResponse");
            var stuffingSheetResponse = ReadPrivateResponse(work, "_lastLoadedResponse");

            Assert.Equal(55, endLabelResponse.Records.Count);
            Assert.Equal(55, stuffingSheetResponse.Records.Count);
            Assert.Same(allRecords, endLabelResponse.Records);
            Assert.Same(allRecords, stuffingSheetResponse.Records);
        });

    [Fact]
    public void Pagination_DoesNotReplaceOrTruncateBusinessRecordCollection()
    {
        StaTestRunner.Run(() =>
        {
            using var work = CreateWorkViewModel();
            var originalCollection = work.Records;
            using var manual = new ManualProcessingViewModel(work);
            AddRecords(work, 55);

            manual.NavigateToPage(4);

            Assert.Same(originalCollection, manual.Work.Records);
            Assert.Equal(55, manual.Work.Records.Count);
            Assert.Equal(10, manual.PagedRecords.Count);
            Assert.Equal("031", manual.PagedRecords[0].Artnr);
            Assert.Equal("001", manual.Work.Records[0].Artnr);
            Assert.Equal("055", manual.Work.Records[^1].Artnr);
        });
    }

    private static MainViewModel CreateWorkViewModel(
        IBoxProcessingService? processingService = null) =>
        new(
            processingService ?? new NoOpProcessingService(),
            new NoOpWeightService(),
            new FakeScanner(),
            NullLogger<MainViewModel>.Instance);

    private static BoxProcessingResponse ReadPrivateResponse(MainViewModel work, string fieldName)
    {
        var field = typeof(MainViewModel).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(field);
        return Assert.IsType<BoxProcessingResponse>(field.GetValue(work));
    }

    private static void AddRecords(MainViewModel work, int count, string tenam = "4430558")
    {
        for (var index = 1; index <= count; index++)
        {
            var record = CreateRecord(index);
            record.Tenam = tenam;
            work.Records.Add(record);
        }
    }

    private static LabelRecord CreateRecord(int index) =>
        new()
        {
            Artnr = index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture)
        };

    private sealed class NoOpProcessingService : IBoxProcessingService
    {
        public Task<BoxProcessingResponse> ProcessAsync(
            BoxProcessingRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new BoxProcessingResponse(
                    BoxProcessingStatus.Success,
                    "Готово",
                    [],
                    null,
                    PrintPlan.None));
    }

    private sealed class CapturingProcessingService(BoxProcessingResponse response)
        : IBoxProcessingService
    {
        public TaskCompletionSource Called { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<BoxProcessingResponse> ProcessAsync(
            BoxProcessingRequest request,
            CancellationToken cancellationToken)
        {
            Called.TrySetResult();
            return Task.FromResult(response);
        }
    }

    private sealed class NoOpWeightService : IBoxWeightService
    {
        public Task<BoxWeightUpdateResult> UpdateWeightAsync(
            string tenam,
            decimal weight,
            CancellationToken cancellationToken) =>
            Task.FromResult(BoxWeightUpdateResult.Success());
    }

    private sealed class FakeScanner : IBoxScanner
    {
        public event EventHandler<BoxNumberReceivedEventArgs>? BoxNumberReceived
        {
            add { }
            remove { }
        }

        public bool IsRunning { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            IsRunning = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            IsRunning = false;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }
}
