using MuhendisKit.Core.Hakedis;

namespace MuhendisKit.Core.Tests;

public class HakedisTests
{
    static Job Sample()
    {
        var a = new PozItem { PozNo = "15.250.1011", Description = "Seramik kaplama", Unit = "m²", UnitPrice = 100.00m };
        var b = new PozItem { PozNo = "25.120.1001", Description = "Panel radyatör", Unit = "adet", UnitPrice = 2500.50m };
        return new Job
        {
            TenderMonth = "2025-01",
            ContractAmount = 100_000m,
            B = 0.90m,
            Items = [a, b],
            Lines =
            [
                new() { ItemId = a.Id, Period = 1, Description = "Zemin", Width = 2, Length = 15 },
                new() { ItemId = b.Id, Period = 1, Description = "Kat 1", Fixed = 2 },
                new() { ItemId = a.Id, Period = 2, Description = "Zemin 2", Count = 2, Width = 2, Length = 5 },
                new() { ItemId = b.Id, Period = 2, Description = "İade", Fixed = 1, Deduct = true },
            ],
            Periods =
            [
                new() { No = 1, Month = "2025-03" },
                new() { No = 2, Month = "2025-05", AllowanceMonth = "2025-04" },
            ],
            Indices = new() { ["2025-01"] = 3000m, ["2025-03"] = 3150m, ["2025-04"] = 3240m, ["2025-05"] = 3300m },
        };
    }

    [Fact]
    public void FirstPeriod()
    {
        var s = Calculator.Period(Sample(), 1);
        Assert.Equal(8001.00m, s.ThisAmount);
        Assert.Equal(1.050m, s.Pn);
        Assert.Equal(360.05m, s.PriceDiff);    // 8001 × 0.90 × 0.05 = 360.045
        Assert.Equal(8361.05m, s.Subtotal);
        Assert.Equal(1672.21m, s.Kdv);
        Assert.Equal(79.26m, Assert.Single(s.Deductions).Amount);
        Assert.Equal(9954.00m, s.Payable);
        Assert.Empty(s.Warnings);
    }

    [Fact]
    public void SecondPeriodUsesSmallerAllowanceIndexAndAccumulates()
    {
        var s = Calculator.Period(Sample(), 2);
        Assert.Equal(7500.50m, s.CumAmount);
        Assert.Equal(8001.00m, s.PrevAmount);
        Assert.Equal(-500.50m, s.ThisAmount);
        Assert.Equal(3240m, s.Gn);
        Assert.Equal(1.080m, s.Pn);
        Assert.Equal(-36.04m, s.PriceDiff);
        Assert.Equal(324.01m, s.CumPriceDiff);
        var radiator = s.Items.Single(i => i.PozNo == "25.120.1001");
        Assert.Equal(2m, radiator.PrevQty);
        Assert.Equal(-1m, radiator.ThisQty);
    }

    [Fact]
    public void MissingIndexSkipsPriceDifferenceWithWarning()
    {
        var job = Sample();
        job.Indices.Remove("2025-03");
        var s = Calculator.Period(job, 1);
        Assert.Null(s.Pn);
        Assert.Equal(0m, s.PriceDiff);
        Assert.Contains(s.Warnings, w => w.Contains("Endeks"));
    }

    [Fact]
    public void ExcelExportWritesAllSheets()
    {
        string path = Path.Combine(Path.GetTempPath(), $"mk-test-{Guid.NewGuid():n}.xlsx");
        try
        {
            ExcelExport.Write(Sample(), "Test işi", 2, path);
            using var wb = new ClosedXML.Excel.XLWorkbook(path);
            Assert.Equal(["İcmal", "Hakediş Raporu", "Yeşil Defter", "Fiyat Farkı"], wb.Worksheets.Select(w => w.Name));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CatalogParsesTurkishHeadersAndNumbers()
    {
        var rows = new List<string[]>
        {
            new[] { "ÇEVRE, ŞEHİRCİLİK VE İKLİM DEĞİŞİKLİĞİ BAKANLIĞI", "", "", "" },
            new[] { "Poz No", "Tanımı", "Birim Fiyatı (TL)", "Birimi" },
            new[] { "25.120.1001", "Panel radyatör", "1.234,56", "adet" },
            new[] { "", "ara başlık", "", "" },
            new[] { "25.305.7150", "PE 100 boru", "87,10", "m" },
        };
        var items = PozCatalog.Parse(rows, "test");
        Assert.Equal(2, items.Count);
        Assert.Equal(new CatalogPoz("25.120.1001", "Panel radyatör", "adet", 1234.56m, "test"), items[0]);
        Assert.Equal(87.10m, items[1].UnitPrice);
    }

    [Fact]
    public void EvdsResponseParses()
    {
        string json = """{"totalCount":2,"items":[{"Tarih":"2025-1","TP_TUFE1YI_T1":"3861.33","UNIXTIME":{}},{"Tarih":"2025-10","TP_TUFE1YI_T1":null}]}""";
        var r = Evds.Parse(json, "TP.TUFE1YI.T1");
        Assert.Equal(3861.33m, Assert.Single(r).Value);
        Assert.Equal("2025-01", r.Keys.Single());
    }
}
