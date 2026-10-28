namespace MuhendisKit.Core.Hakedis;

/// <summary>A construction contract followed through its progress payments (hakediş). Saved inside the project file.</summary>
public sealed class Job
{
    public string Owner { get; set; } = "";         // idare
    public string Contractor { get; set; } = "";    // yüklenici
    public string ContractNo { get; set; } = "";
    public string TenderMonth { get; set; } = "";   // "yyyy-MM", base month for G0
    public decimal ContractAmount { get; set; }
    public decimal B { get; set; } = 0.90m;         // sabit katsayı (sözleşmedeki değer)
    public int PnDigits { get; set; } = 3;
    public decimal KdvRate { get; set; } = 20;      // %
    public List<Deduction> Deductions { get; set; } = [new() { Name = "Damga vergisi", RatePercent = 0.948m }];
    public List<PozItem> Items { get; set; } = [];
    public List<MeasureLine> Lines { get; set; } = [];
    public List<Period> Periods { get; set; } = [];
    /// <summary>Yİ-ÜFE (or the index named in the contract) by month "yyyy-MM".</summary>
    public Dictionary<string, decimal> Indices { get; set; } = [];
}

public sealed class PozItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public string PozNo { get; set; } = "";
    public string Description { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal UnitPrice { get; set; }
}

/// <summary>One row of the measurement sheet (ataşman / yeşil defter).</summary>
public sealed class MeasureLine
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public string ItemId { get; set; } = "";
    public int Period { get; set; } = 1;
    public string Description { get; set; } = "";
    public decimal Count { get; set; } = 1;
    public decimal? Width { get; set; }
    public decimal? Length { get; set; }
    public decimal? Height { get; set; }
    /// <summary>Quantity entered directly (e.g. from the drawing); overrides the dimensions.</summary>
    public decimal? Fixed { get; set; }
    public bool Deduct { get; set; }
    public string Source { get; set; } = "";

    public decimal Quantity =>
        Math.Round((Fixed ?? Count * (Width ?? 1) * (Length ?? 1) * (Height ?? 1)) * (Deduct ? -1 : 1), 3);
}

public sealed class Period
{
    public int No { get; set; }
    public string Date { get; set; } = "";          // "yyyy-MM-dd"
    public string Month { get; set; } = "";         // uygulama ayı "yyyy-MM"
    public string? AllowanceMonth { get; set; }     // ödenek ayı; the smaller index of the two is used
    public string Note { get; set; } = "";
}

public sealed class Deduction
{
    public string Name { get; set; } = "";
    public decimal RatePercent { get; set; }
    /// <summary>"tutar" = amount before VAT, "kdv" = the VAT itself (tevkifat), "toplam" = amount with VAT.</summary>
    public string Base { get; set; } = "tutar";
}
