using Microsoft.Extensions.Options;

namespace LabelFlowStudio.Application.Tests.Infrastructure;

public sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
{
    private readonly T _currentValue;

    public TestOptionsMonitor(T currentValue)
    {
        _currentValue = currentValue;
    }

    public T CurrentValue => _currentValue;

    public T Get(string? name)
    {
        return _currentValue;
    }

    public IDisposable OnChange(Action<T, string?> listener)
    {
        return EmptyDisposable.Instance;
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
