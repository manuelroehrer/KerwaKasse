using System;
using System.Collections.Generic;
using KerwaKasse.MVVM.Model;

namespace KerwaKasse.Helper
{
    /// <summary>Snapshot of the currently shown breakdown that the QuestPDF exporter renders into
    /// the report, taken at export time so a later filter change cannot alter the produced file.</summary>
    public class AnalysisReport
    {
        /// <summary>The report heading (user-chosen title, e.g. a saved analysis' name or a free one).</summary>
        public string AnalysisName { get; init; } = string.Empty;

        /// <summary>The analysed period, already formatted for display (e.g. "04.07.2026 00:00 – 04.07.2026 23:59 Uhr").</summary>
        public string Period { get; init; } = string.Empty;

        /// <summary>The product filter summary as shown in the toolbar (e.g. "alle (12)").</summary>
        public string ProductSummary { get; init; } = string.Empty;

        /// <summary>The table rows in the currently chosen sort order.</summary>
        public IReadOnlyList<AnalysisSalesRow> Rows { get; init; } = Array.Empty<AnalysisSalesRow>();

        public int TotalAmount { get; init; }
        public decimal TotalRevenue { get; init; }

        /// <summary>App version for the footer (e.g. "2.8.0").</summary>
        public string AppVersion { get; init; } = string.Empty;
    }
}
