namespace Dcs.Or.Gov.Azure.Azure.Relay.Http.Listener
{
    static class StringExtension
    {
        /// <summary>
        /// Ensures the given string ends with the requested pattern. If it does no allocations are performed.
        /// </summary>
        public static string EnsureEndsWith(this string s, string value, StringComparison comparisonType = StringComparison.Ordinal)
        {
            if (!string.IsNullOrEmpty(s) && s.EndsWith(value, comparisonType))
            {
                return s;
            }

            return s + value;
        }
    }
}