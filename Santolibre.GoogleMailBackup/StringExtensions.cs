using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Santolibre.GoogleMailBackup
{
    public static class StringExtensions
    {
        public static byte[] UrlSafeBase64StringToByteArray(this string value)
        {
            value = value.Replace('_', '/').Replace('-', '+');
            switch (value.Length % 4)
            {
                case 2: value += "=="; break;
                case 3: value += "="; break;
            }
            return Convert.FromBase64String(value);
        }

        public static string ToValidFileName(this string fileName)
        {
            var invalidChars = new string(Path.GetInvalidFileNameChars());
            var escapedInvalidChars = Regex.Escape(invalidChars);
            var invalidRegex = string.Format(@"([{0}]*\.+$)|([{0}]+)", escapedInvalidChars);
            return Regex.Replace(fileName, invalidRegex, " ").Replace("  ", " ");
        }
    }
}
