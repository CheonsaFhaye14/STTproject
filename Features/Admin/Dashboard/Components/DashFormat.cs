using System.Globalization;
using System.Text.RegularExpressions;

namespace STTproject.Features.Admin.Dashboard.Components
{
    /// <summary>Formatting shared by the dashboard cards and panels.</summary>
    public static class DashFormat
    {
        private static readonly CultureInfo Ph = new("en-PH");
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>₱1,234,567.89</summary>
        public static string Peso(decimal value) => value.ToString("C2", Ph);

        /// <summary>₱1.23M / ₱456.7K / ₱89 — for cards, bars and rankings.</summary>
        public static string PesoShort(decimal value)
        {
            var a = Math.Abs(value);
            var s = a >= 1_000_000m ? "₱" + (a / 1_000_000m).ToString("0.00", Inv) + "M"
                  : a >= 1_000m ? "₱" + (a / 1_000m).ToString("0.0", Inv) + "K"
                  : "₱" + a.ToString("0", Inv);
            return value < 0 ? "−" + s : s;
        }

        public static string Number(int value) => value.ToString("N0", Inv);

        /// <summary>Number for inline CSS (always a dot decimal separator).</summary>
        public static string Css(double value) => value.ToString("0.##", Inv);

        /// <summary>"UnmappedCustomer" → "Unmapped customer".</summary>
        public static string Humanize(string pascal)
        {
            if (string.IsNullOrWhiteSpace(pascal)) return pascal;
            var words = Regex.Replace(pascal.Trim(), "(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ");
            return char.ToUpper(words[0]) + words[1..].ToLower();
        }

        /// <summary>Today / 1 day / 9 days</summary>
        public static string Waiting(DateTime since)
        {
            var days = (DateTime.Today - since.Date).Days;
            return days <= 0 ? "Today" : days == 1 ? "1 day" : $"{days} days";
        }

        /// <summary>Nov 9, 10:42</summary>
        public static string DateTimeShort(DateTime d) => d.ToString("MMM d, HH:mm", Inv);

        /// <summary>Nov 15 (this year) or Jan 5, 2027</summary>
        public static string DateShort(DateTime d) =>
            d.ToString(d.Year == DateTime.Today.Year ? "MMM d" : "MMM d, yyyy", Inv);

        public static string MonthName(int month) => Inv.DateTimeFormat.GetMonthName(month);
        public static string MonthAbbr(int month) => Inv.DateTimeFormat.GetAbbreviatedMonthName(month);

        /// <summary>ImportBatch.BatchStatus → tag css class and label.</summary>
        public static (string Css, string Text) BatchStatus(string status) => status switch
        {
            "PartiallyCommitted" => ("t-part", "Partial"),
            "Failed" => ("t-bad", "Failed"),
            "Prepared" => ("t-prep", "Prepared"),
            "Committed" => ("t-ok", "Committed"),
            "Cancelled" => ("t-grey", "Cancelled"),
            _ => ("t-grey", status)
        };
    }
}
