using KerwaKasse.Core.Models;
using System;

namespace KerwaKasse.MVVM.Model
{
    /// <summary>An event as shown in the event selector on the info page: name, key figures and whether it
    /// is the event the till currently books into.</summary>
    public class EventListItem
    {
        public EventListItem(EventDatabase source, bool isActive)
        {
            Source = source;
            IsActive = isActive;
        }

        public EventDatabase Source { get; }
        public string Name => Source.Name;
        public string FilePath => Source.FilePath;
        public bool IsActive { get; }

        /// <summary>One line under the name: "39 Produkte · 5.058 Verkäufe · 29.07.2022 – 26.07.2026".</summary>
        public string Details => Source.Orders == 0
            ? $"{ProductsText} · noch keine Verkäufe"
            : $"{ProductsText} · {OrdersText}" + (PeriodText.Length > 0 ? $" · {PeriodText}" : "");

        /// <summary>The same as a sentence part: "39 Produkte und 5.058 Verkäufe (29.07.2022 – 26.07.2026)".</summary>
        public string ContentsText => Source.Orders == 0
            ? $"{ProductsText} und noch keine Verkäufe"
            : $"{ProductsText} und {OrdersText}" + (PeriodText.Length > 0 ? $" ({PeriodText})" : "");

        private string ProductsText => Count(Source.Products, "Produkt", "Produkte");
        private string OrdersText => Count(Source.Orders, "Verkauf", "Verkäufe");

        private string PeriodText
        {
            get
            {
                if (Source.FirstOrder is not DateTime first || Source.LastOrder is not DateTime last)
                    return string.Empty;
                return first.Date == last.Date ? $"{first:dd.MM.yyyy}" : $"{first:dd.MM.yyyy} – {last:dd.MM.yyyy}";
            }
        }

        private static string Count(int n, string singular, string plural) => $"{n:N0} {(n == 1 ? singular : plural)}";
    }
}
