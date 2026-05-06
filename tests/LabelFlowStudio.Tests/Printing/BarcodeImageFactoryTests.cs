using LabelFlowStudio.Printing;

namespace LabelFlowStudio.Application.Tests.Printing;

public sealed class BarcodeImageFactoryTests
{
    [Fact]
    public void CreateCode128_Throws_WhenTenamIsEmpty()
    {
        Assert.Throws<ArgumentException>(() => BarcodeImageFactory.CreateCode128("", width: 100, height: 40));
    }

    [Fact]
    public void CreateCode128_Throws_WhenWidthIsInvalid()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BarcodeImageFactory.CreateCode128("123", width: 0, height: 40));
    }

    [Fact]
    public void CreateCode128_Throws_WhenHeightIsInvalid()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BarcodeImageFactory.CreateCode128("123", width: 100, height: 0));
    }
}
