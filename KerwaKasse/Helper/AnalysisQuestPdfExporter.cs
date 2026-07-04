using System;
using System.Globalization;
using KerwaKasse.MVVM.Model;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace KerwaKasse.Helper
{
    /// <summary>Variant A of the analysis PDF export: QuestPDF draws the document straight into a
    /// .pdf file (no printer involved), with a repeating page header and a footer carrying page
    /// numbers. Layout is defined entirely in code, independent of WPF.</summary>
    public static class AnalysisQuestPdfExporter
    {
        private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

        private const string Accent = "#1B4068";
        private const string TextDark = "#1B1E24";
        private const string TextGray = "#6B7280";
        private const string LineGray = "#D5D9DE";
        private const string ZebraGray = "#F4F6F8";

        static AnalysisQuestPdfExporter()
        {
            // QuestPDF is free under its community license (open source / small projects).
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public static void Export(AnalysisReport report, string path)
        {
            Document.Create(container => container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(46);
                page.DefaultTextStyle(t => t.FontSize(10.5f).FontFamily("Segoe UI").FontColor(TextDark));

                // The document header lives in the content (not page.Header()), so it appears on
                // the first page only; the table's own column captions still repeat on every page.
                page.Content().Column(col =>
                {
                    col.Item().Element(c => ComposeHeader(c, report));
                    col.Item().PaddingTop(16).Element(c => ComposeTable(c, report));
                });
                page.Footer().Element(c => ComposeFooter(c, report));
            })).GeneratePdf(path);
        }

        private static void ComposeHeader(IContainer container, AnalysisReport report)
        {
            container.Column(col =>
            {
                col.Item().Text("KERWAKASSE · AUSWERTUNG").FontSize(8.5f).SemiBold().FontColor(TextGray).LetterSpacing(0.08f);
                col.Item().PaddingTop(2).Text(report.AnalysisName).FontSize(19).Bold().FontColor(Accent);
                col.Item().PaddingTop(6).Text($"Zeitraum: {report.Period}").FontColor(TextGray);
                col.Item().Text($"Produkte: {report.ProductSummary}").FontColor(TextGray);
                col.Item().PaddingTop(10).LineHorizontal(1).LineColor(LineGray);
            });
        }

        private static void ComposeTable(IContainer container, AnalysisReport report)
        {
            container.Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(22);  // colour swatch
                    columns.RelativeColumn();    // product name
                    columns.ConstantColumn(70);  // amount
                    columns.ConstantColumn(95);  // revenue
                });

                table.Header(header =>
                {
                    header.Cell().Element(HeaderCell);
                    header.Cell().Element(HeaderCell).Text("Produkt").SemiBold();
                    header.Cell().Element(HeaderCell).AlignRight().Text("Menge").SemiBold();
                    header.Cell().Element(HeaderCell).AlignRight().Text("Umsatz").SemiBold();
                });

                for (int i = 0; i < report.Rows.Count; i++)
                {
                    var row = report.Rows[i];
                    bool zebra = i % 2 == 1;

                    table.Cell().Element(c => BodyCell(c, zebra)).AlignMiddle()
                        .Width(9).Height(9)
                        .Background(Hex(row.ColorBrush, "#D3D3D3"))
                        .Border(0.8f).BorderColor(Hex(row.ColorBorderBrush, LineGray));
                    table.Cell().Element(c => BodyCell(c, zebra)).Text(row.Name);
                    table.Cell().Element(c => BodyCell(c, zebra)).AlignRight().Text(row.Amount.ToString(German));
                    table.Cell().Element(c => BodyCell(c, zebra)).AlignRight().Text(row.Revenue.ToString("C", German));
                }

                table.Cell().Element(TotalCell);
                table.Cell().Element(TotalCell).Text("Gesamt").SemiBold();
                table.Cell().Element(TotalCell).AlignRight().Text(report.TotalAmount.ToString(German)).SemiBold();
                table.Cell().Element(TotalCell).AlignRight().Text(report.TotalRevenue.ToString("C", German)).SemiBold();
            });
        }

        private static void ComposeFooter(IContainer container, AnalysisReport report)
        {
            container.PaddingTop(6).Row(row =>
            {
                row.RelativeItem()
                    .Text($"Erstellt am {DateTime.Now.ToString("dd.MM.yyyy HH:mm", German)} · KerwaKasse {report.AppVersion}")
                    .FontSize(8.5f).FontColor(TextGray);
                row.ConstantItem(90).AlignRight().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(8.5f).FontColor(TextGray));
                    t.Span("Seite ");
                    t.CurrentPageNumber();
                    t.Span(" von ");
                    t.TotalPages();
                });
            });
        }

        private static IContainer HeaderCell(IContainer c) =>
            c.BorderBottom(1).BorderColor(LineGray).PaddingVertical(5).PaddingHorizontal(3);

        private static IContainer BodyCell(IContainer c, bool zebra)
        {
            if (zebra) c = c.Background(ZebraGray);
            return c.PaddingVertical(4.5f).PaddingHorizontal(3);
        }

        private static IContainer TotalCell(IContainer c) =>
            c.BorderTop(1).BorderColor(LineGray).PaddingVertical(6).PaddingHorizontal(3);

        /// <summary>Converts a WPF solid brush (the table's row swatch) into a hex string for QuestPDF.</summary>
        private static string Hex(System.Windows.Media.Brush brush, string fallback) =>
            brush is System.Windows.Media.SolidColorBrush b
                ? $"#{b.Color.R:X2}{b.Color.G:X2}{b.Color.B:X2}"
                : fallback;
    }
}
