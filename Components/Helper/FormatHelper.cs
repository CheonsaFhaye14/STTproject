using System.Globalization;

namespace STTproject.Components.Helper
{
    public static class FormatHelper
    {
        // ALL CAPS
        public static string ToAllCaps(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "";

            return input.ToUpper();
        }

        // Title Case (Proper Name Style)
        public static string ToTitleCase(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "";

            return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(input.ToLower());
        }

        // Location format: City, Province (handles nulls)
        public static string FormatLocation(string city, string province)
        {
            if (string.IsNullOrWhiteSpace(city))
                return province ?? "";

            if (string.IsNullOrWhiteSpace(province))
                return city;

            return $"{city}, {province}";
        }
    }
}