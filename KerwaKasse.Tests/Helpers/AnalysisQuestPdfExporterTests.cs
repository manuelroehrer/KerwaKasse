using System.IO;
using KerwaKasse.Core.Models;
using KerwaKasse.Helper;
using KerwaKasse.MVVM.Model;

namespace KerwaKasse.Tests.Helpers;

public class AnalysisQuestPdfExporterTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"KerwaKasseReport_{Guid.NewGuid():N}.pdf");

    private static AnalysisReport Report(bool includeDetails) => new()
    {
        AnalysisName = "Kerwa",
        Period = "09.10.2026 00:00 – 09.10.2026 23:59 Uhr",
        ProductSummary = "alle (3)",
        IncludeDetails = includeDetails,
        Rows =
        [
            new AnalysisSalesRow { Kind = SalesFigureKind.Product, ProductId = 1, Name = "Bratwurst", Amount = 10, Revenue = 35.00m, UnitPrice = 3.50m },
            new AnalysisSalesRow
            {
                Kind = SalesFigureKind.SpecialPrice, ProductId = 1, Name = "Bratwurst (Sonderpreis)", Amount = 3, Revenue = 5.00m,
                Details =
                [
                    new AnalysisDetailRow { Name = "Helfer", Amount = 1, UnitPrice = 1.00m, Revenue = 1.00m },
                    new AnalysisDetailRow { Name = "Ohne Angabe", IsWithoutDescription = true, Amount = 2, UnitPrice = 2.00m, Revenue = 4.00m }
                ]
            },
            new AnalysisSalesRow
            {
                Kind = SalesFigureKind.FreeEntries, Name = "Freie Eingabe", Amount = 3, Revenue = 12.00m, UnitPrice = 4.00m,
                Details = [new AnalysisDetailRow { Name = "Kuchenspende", Amount = 3, UnitPrice = 4.00m, Revenue = 12.00m }]
            }
        ],
        TotalAmount = 16,
        TotalRevenue = 52.00m,
        AppVersion = "2.12.0"
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Export_WritesAPdf_WithOrWithoutTheDetailsPage(bool includeDetails)
    {
        AnalysisQuestPdfExporter.Export(Report(includeDetails), _path);

        var header = File.ReadAllBytes(_path).Take(5).ToArray();
        Assert.Equal("%PDF-"u8.ToArray(), header);
    }

    [Fact]
    public void Export_WithDetails_AddsAPage()
    {
        AnalysisQuestPdfExporter.Export(Report(includeDetails: false), _path);
        int withoutDetails = PageCount(_path);
        AnalysisQuestPdfExporter.Export(Report(includeDetails: true), _path);

        Assert.Equal(withoutDetails + 1, PageCount(_path));
    }

    // Counts the page objects ("/Type /Page", not "/Pages") of a PDF.
    private static int PageCount(string path) =>
        System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(path, System.Text.Encoding.Latin1), @"/Type\s*/Page(?!s)").Count;

    public void Dispose()
    {
        if (File.Exists(_path))
            File.Delete(_path);
    }
}
