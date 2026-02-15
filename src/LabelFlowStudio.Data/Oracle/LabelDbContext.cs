using LabelFlowStudio.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace LabelFlowStudio.Data.Oracle;

public sealed class LabelDbContext : DbContext
{
    public LabelDbContext(DbContextOptions<LabelDbContext> options)
        : base(options) 
    { 
    }

    public DbSet<LabelRecord> LabelRecords => Set<LabelRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<LabelRecord>(entity =>
        {
            entity.HasNoKey();
            entity.ToView("LIST_FOR_TEKARTON_V", "MLSOFT");

            entity.Property(e => e.Tenam).HasColumnName("TENAM");
            entity.Property(e => e.Artnr).HasColumnName("ARTNR");
            entity.Property(e => e.Artbez).HasColumnName("ARTBEZ");
            entity.Property(e => e.Bstchgnam5).HasColumnName("BSTCHGNAM5");
            entity.Property(e => e.Bstmg).HasColumnName("BSTMG").HasPrecision(18, 3);
            entity.Property(e => e.Aufid).HasColumnName("AUFID");
            entity.Property(e => e.Gpplz).HasColumnName("GPPLZ");
            entity.Property(e => e.Gpbez).HasColumnName("GPBEZ");
            entity.Property(e => e.Lndnam).HasColumnName("LNDNAM");
            entity.Property(e => e.Gport1).HasColumnName("GPORT1");
            entity.Property(e => e.Gpstrasse).HasColumnName("GPSTRASSE");
            entity.Property(e => e.Lfakdnr).HasColumnName("LFAKDNR");
            entity.Property(e => e.Adres).HasColumnName("ADRES");
            entity.Property(e => e.Brutto).HasColumnName("BRUTTO").HasPrecision(18, 3);
            entity.Property(e => e.Tesortnr).HasColumnName("TESORTNR").HasPrecision(18, 3);
            entity.Property(e => e.Lfaempfkdnr).HasColumnName("LFAEMPFKDNR");
            entity.Property(e => e.Market).HasColumnName("MARKET");
            entity.Property(e => e.CountBst).HasColumnName("COUNTBST").HasPrecision(18, 3);
            entity.Property(e => e.SumBst).HasColumnName("SUM_BST").HasPrecision(18, 3);

            entity.Property(e => e.Lfaempfort1).HasColumnName("LFAEMPFORT1");
            entity.Property(e => e.Lfaempfstrasse).HasColumnName("LFAEMPFSTRASSE");
        });
    }
}
