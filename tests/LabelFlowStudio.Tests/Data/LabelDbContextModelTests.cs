using LabelFlowStudio.Core.Models;
using LabelFlowStudio.Data.Oracle;
using Microsoft.EntityFrameworkCore;

namespace LabelFlowStudio.Application.Tests.Data;

public sealed class LabelDbContextModelTests
{
    [Fact]
    public void Model_Configures_LabelRecord_AsKeylessView_WithExpectedMappings()
    {
        var options = new DbContextOptionsBuilder<LabelDbContext>()
            .UseInMemoryDatabase("LabelDbContextModelTests")
            .Options;

        using var context = new LabelDbContext(options);

        var entityType = context.Model.FindEntityType(typeof(LabelRecord));
        Assert.NotNull(entityType);

        Assert.Null(entityType!.FindPrimaryKey());

        Assert.Equal("LIST_FOR_TEKARTON_V", entityType.GetViewName());
        Assert.Equal("MLSOFT", entityType.GetViewSchema());

        var bstmg = entityType.FindProperty(nameof(LabelRecord.Bstmg));
        Assert.NotNull(bstmg);
        Assert.Equal(18, bstmg!.GetPrecision());
        Assert.Equal(3, bstmg.GetScale());
    }
}
