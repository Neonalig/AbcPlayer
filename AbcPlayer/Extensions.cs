using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AbcPlayer {
    public static class Extensions {
#pragma warning disable CA1820 // Test for empty strings using string length
        public static bool IsEmpty(this string str) => str != null && str == string.Empty;
#pragma warning restore CA1820 // Test for empty strings using string length

        public static bool IsNullOrWhiteSpace(this string str) => string.IsNullOrWhiteSpace(str);

        public static string Truncate(this string str, int places, string delimiter = "...") => str == null ? string.Empty : str.Length <= places ? str : str.Substring(0, places - 3) + delimiter;

        public static string RegexReplace(this string str, string pattern, string replacement = "", RegexOptions options = RegexOptions.None, TimeSpan? timeout = null) => new Regex(pattern, options, timeout ?? Regex.InfiniteMatchTimeout).Replace(str, replacement);

        public static string Join(this IEnumerable<string> strIenum) => strIenum.Aggregate("", (current, str) => current + str);

        public static string MergeLines(this IEnumerable<string> str) => str.Join().RegexReplace(@"(\r\n|\r|\n)");

        public static string MergeLines(this string str) => str.RegexReplace(@"(\r\n|\r|\n)");
    }
}
