namespace LabelFlowStudio.Core.Models;

public sealed class LabelRecord
{
    public string Tenam { get; set; } = string.Empty;
    public string Artnr { get; set; } = string.Empty;
    public string Artbez { get; set; } = string.Empty;
    public string Bstchgnam5 { get; set; } = string.Empty;
    public decimal? Bstmg { get; set; }
    public string Aufid { get; set; } = string.Empty;
    public string Gpplz { get; set; } = string.Empty;
    public string Gpbez { get; set; } = string.Empty;
    public string Lndnam { get; set; } = string.Empty;
    public string Gport1 { get; set; } = string.Empty;
    public string Gpstrasse { get; set; } = string.Empty;
    public string Lfakdnr { get; set; } = string.Empty;
    public string Adres { get; set; } = string.Empty;
    public decimal? Brutto { get; set; }
    public decimal? Tesortnr { get; set; }
    public string Lfaempfkdnr { get; set; } = string.Empty;
    public string? Market { get; set; }
    public decimal? CountBst { get; set; }
    public decimal? SumBst { get; set; }

    public string? Lfaempfort1 { get; set; }
    public string? Lfaempfstrasse { get; set; }


    public string? DeliveryCityRaw => !string.IsNullOrWhiteSpace(Lfaempfort1) ? Lfaempfort1 : Gport1;
    public string? DeliveryStreetRaw => !string.IsNullOrWhiteSpace(Lfaempfstrasse) ? Lfaempfstrasse : Gpstrasse;

    public string? DeliveryCity => NormalizeCity(DeliveryCityRaw);
    public string? DeliveryStreet => NormalizeStreet(DeliveryStreetRaw);

    private static string? NormalizeCity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var v = NormalizeSpaces(value);

        v = System.Text.RegularExpressions.Regex.Replace(
            v,
            @"^\s*(город|г\.|г)\s",
            "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );

        return v.Trim();
    }

    private static string? NormalizeStreet(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var v = NormalizeSpaces(value);

        while (v.Contains(",,"))
        {
            v = v.Replace(",,", ",");
        }

        v = v.Trim().TrimEnd(',').Trim();

        return v;
    }

    private static string NormalizeSpaces(string input)
    {
        return System.Text.RegularExpressions.Regex.Replace(input, @"\s", " ").Trim();
    }
}
