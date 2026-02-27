using LabelFlowStudio.Core.Models;

namespace LabelFlowStudio.Application.Tests.Core;

public sealed class LabelRecordTests
{
    [Fact]
    public void DeliveryCity_PrefersRecipientCity_AndNormalizesPrefix()
    {
        var record = new LabelRecord
        {
            Lfaempfort1 = "  г. Минск ",
            Gport1 = "Minsk fallback"
        };

        Assert.Equal("Минск", record.DeliveryCity);
    }

    [Fact]
    public void DeliveryCity_UsesGpbezCity_WhenRecipientCityEmpty()
    {
        var record = new LabelRecord
        {
            Lfaempfort1 = " ",
            Gport1 = "  город   Тула  "
        };

        Assert.Equal("Тула", record.DeliveryCity);
    }

    [Fact]
    public void DeliveryStreet_NormalizesCommasAndSpaces()
    {
        var record = new LabelRecord
        {
            Lfaempfstrasse = "  Main,,   st 1,, "
        };

        Assert.Equal("Main,   st 1", record.DeliveryStreet);
    }
}