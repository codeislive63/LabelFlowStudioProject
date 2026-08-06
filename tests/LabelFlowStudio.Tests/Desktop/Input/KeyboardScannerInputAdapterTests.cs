using LabelFlowStudio.Desktop.Input;

namespace LabelFlowStudio.Application.Tests.Desktop.Input;

public sealed class KeyboardScannerInputAdapterTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 8, 6, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_UsesExistingScannerTimingDefaults()
    {
        var adapter = new KeyboardScannerInputAdapter();

        Assert.Equal(TimeSpan.FromMilliseconds(900), adapter.BufferTimeout);
        Assert.Equal(TimeSpan.FromMilliseconds(60), adapter.AutomaticMaxInterKeyDelay);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsNonPositiveBufferTimeout(int milliseconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new KeyboardScannerInputAdapter(bufferTimeout: TimeSpan.FromMilliseconds(milliseconds)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsNonPositiveAutomaticInterKeyDelay(int milliseconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new KeyboardScannerInputAdapter(automaticMaxInterKeyDelay: TimeSpan.FromMilliseconds(milliseconds)));
    }

    [Fact]
    public void ProcessTextInput_AutomaticMode_BuffersDigitsWithinInterKeyLimit()
    {
        var adapter = new KeyboardScannerInputAdapter();

        var first = adapter.ProcessTextInput("44", KeyboardScannerInputMode.Automatic, StartedAt);
        var second = adapter.ProcessTextInput(
            "3",
            KeyboardScannerInputMode.Automatic,
            StartedAt.AddMilliseconds(60));

        Assert.Equal(KeyboardScannerInputOutcome.BufferUpdated, first.Outcome);
        Assert.True(first.ShouldHandle);
        Assert.Equal("44", first.Buffer);
        Assert.Equal(KeyboardScannerInputOutcome.BufferUpdated, second.Outcome);
        Assert.True(second.ShouldHandle);
        Assert.Equal("443", second.Buffer);
    }

    [Fact]
    public void ProcessTextInput_AutomaticMode_DiscardsRemainderOfSlowCandidateUntilBufferTimeout()
    {
        var adapter = new KeyboardScannerInputAdapter();
        adapter.ProcessTextInput("4", KeyboardScannerInputMode.Automatic, StartedAt);

        var slowInput = adapter.ProcessTextInput(
            "5",
            KeyboardScannerInputMode.Automatic,
            StartedAt.AddMilliseconds(61));
        var nextCandidate = adapter.ProcessTextInput(
            "6",
            KeyboardScannerInputMode.Automatic,
            StartedAt.AddMilliseconds(62));
        var candidateAfterTimeout = adapter.ProcessTextInput(
            "7",
            KeyboardScannerInputMode.Automatic,
            StartedAt.AddMilliseconds(900));

        Assert.Equal(KeyboardScannerInputOutcome.BufferCleared, slowInput.Outcome);
        Assert.True(slowInput.ShouldHandle);
        Assert.Empty(slowInput.Buffer);
        Assert.Empty(nextCandidate.Buffer);
        Assert.Equal("7", candidateAfterTimeout.Buffer);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("A")]
    [InlineData("12A")]
    public void ProcessTextInput_AutomaticMode_ConsumesNonDigitInput(string text)
    {
        var adapter = new KeyboardScannerInputAdapter();
        adapter.ProcessTextInput("44", KeyboardScannerInputMode.Automatic, StartedAt);

        var result = adapter.ProcessTextInput(
            text,
            KeyboardScannerInputMode.Automatic,
            StartedAt.AddMilliseconds(10));

        Assert.Equal(KeyboardScannerInputOutcome.Consumed, result.Outcome);
        Assert.True(result.ShouldHandle);
        Assert.Equal("44", result.Buffer);
    }

    [Fact]
    public void ProcessTextInput_ManualMode_IgnoresNonDigitsWithoutChangingBuffer()
    {
        var adapter = new KeyboardScannerInputAdapter();
        adapter.ProcessTextInput("44", KeyboardScannerInputMode.Manual, StartedAt);

        var result = adapter.ProcessTextInput(
            "A",
            KeyboardScannerInputMode.Manual,
            StartedAt.AddMilliseconds(10));

        Assert.Equal(KeyboardScannerInputOutcome.Ignored, result.Outcome);
        Assert.False(result.ShouldHandle);
        Assert.Equal("44", result.Buffer);
    }

    [Fact]
    public void ProcessInput_WhenCallerDeclinesCapture_DoesNotHandleOrMutateState()
    {
        var adapter = new KeyboardScannerInputAdapter();
        adapter.ProcessTextInput("44", KeyboardScannerInputMode.Manual, StartedAt);

        var textResult = adapter.ProcessTextInput(
            "3",
            KeyboardScannerInputMode.Manual,
            StartedAt.AddSeconds(2),
            shouldCapture: false);
        var keyResult = adapter.ProcessKeyDown(
            KeyboardScannerKeyKind.Enter,
            KeyboardScannerInputMode.Manual,
            StartedAt.AddSeconds(2),
            shouldCapture: false);

        Assert.Equal(KeyboardScannerInputOutcome.Ignored, textResult.Outcome);
        Assert.False(textResult.ShouldHandle);
        Assert.Equal("44", textResult.Buffer);
        Assert.Equal(KeyboardScannerInputOutcome.Ignored, keyResult.Outcome);
        Assert.False(keyResult.ShouldHandle);
        Assert.Equal("44", keyResult.Buffer);
    }

    [Theory]
    [InlineData(KeyboardScannerInputMode.Manual)]
    [InlineData(KeyboardScannerInputMode.Automatic)]
    public void ProcessKeyDown_Enter_CompletesAndClearsBufferedScan(KeyboardScannerInputMode mode)
    {
        var adapter = new KeyboardScannerInputAdapter();
        adapter.ProcessTextInput("4430558", mode, StartedAt);

        var result = adapter.ProcessKeyDown(
            KeyboardScannerKeyKind.Enter,
            mode,
            StartedAt.AddMilliseconds(10));

        Assert.Equal(KeyboardScannerInputOutcome.ScanCompleted, result.Outcome);
        Assert.True(result.ShouldHandle);
        Assert.Equal("4430558", result.CompletedScan);
        Assert.Empty(result.Buffer);
        Assert.Empty(adapter.Buffer);
    }

    [Fact]
    public void ProcessKeyDown_WithEmptyBuffer_PreservesModeSpecificHandling()
    {
        var manualAdapter = new KeyboardScannerInputAdapter();
        var automaticAdapter = new KeyboardScannerInputAdapter();

        var manual = manualAdapter.ProcessKeyDown(
            KeyboardScannerKeyKind.Enter,
            KeyboardScannerInputMode.Manual,
            StartedAt);
        var automatic = automaticAdapter.ProcessKeyDown(
            KeyboardScannerKeyKind.Enter,
            KeyboardScannerInputMode.Automatic,
            StartedAt);

        Assert.False(manual.ShouldHandle);
        Assert.True(automatic.ShouldHandle);
        Assert.Null(manual.CompletedScan);
        Assert.Null(automatic.CompletedScan);
    }

    [Fact]
    public void ProcessKeyDown_OtherKey_PreservesModeSpecificHandling()
    {
        var manualAdapter = new KeyboardScannerInputAdapter();
        var automaticAdapter = new KeyboardScannerInputAdapter();

        var manual = manualAdapter.ProcessKeyDown(
            KeyboardScannerKeyKind.Other,
            KeyboardScannerInputMode.Manual,
            StartedAt);
        var automatic = automaticAdapter.ProcessKeyDown(
            KeyboardScannerKeyKind.Other,
            KeyboardScannerInputMode.Automatic,
            StartedAt);

        Assert.False(manual.ShouldHandle);
        Assert.True(automatic.ShouldHandle);
    }

    [Fact]
    public void ExpireBuffer_ClearsAtNineHundredMilliseconds()
    {
        var adapter = new KeyboardScannerInputAdapter();
        adapter.ProcessTextInput("44", KeyboardScannerInputMode.Manual, StartedAt);

        var beforeTimeout = adapter.ExpireBuffer(StartedAt.AddMilliseconds(899));
        var atTimeout = adapter.ExpireBuffer(StartedAt.AddMilliseconds(900));

        Assert.Equal(KeyboardScannerInputOutcome.Ignored, beforeTimeout.Outcome);
        Assert.Equal("44", beforeTimeout.Buffer);
        Assert.Equal(KeyboardScannerInputOutcome.BufferCleared, atTimeout.Outcome);
        Assert.False(atTimeout.ShouldHandle);
        Assert.Empty(atTimeout.Buffer);
    }

    [Fact]
    public void ProcessTextInput_AfterBufferTimeout_StartsNewCandidate()
    {
        var adapter = new KeyboardScannerInputAdapter();
        adapter.ProcessTextInput("44", KeyboardScannerInputMode.Manual, StartedAt);

        var result = adapter.ProcessTextInput(
            "3",
            KeyboardScannerInputMode.Manual,
            StartedAt.AddMilliseconds(900));

        Assert.Equal(KeyboardScannerInputOutcome.BufferUpdated, result.Outcome);
        Assert.Equal("3", result.Buffer);
    }

    [Fact]
    public void Reset_DiscardsOnlyUnacceptedCandidate()
    {
        var adapter = new KeyboardScannerInputAdapter();
        adapter.ProcessTextInput("4430", KeyboardScannerInputMode.Automatic, StartedAt);

        var reset = adapter.Reset();
        var enterAfterReset = adapter.ProcessKeyDown(
            KeyboardScannerKeyKind.Enter,
            KeyboardScannerInputMode.Automatic,
            StartedAt.AddMilliseconds(10));

        Assert.Equal(KeyboardScannerInputOutcome.BufferCleared, reset.Outcome);
        Assert.Empty(reset.Buffer);
        Assert.Null(reset.CompletedScan);
        Assert.Null(enterAfterReset.CompletedScan);
        Assert.Empty(adapter.Buffer);
    }
}
