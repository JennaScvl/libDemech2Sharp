using System;
using System.Globalization;
using System.Text;

namespace Demech2
{
    // The C runtime's parsing as the tasks' data strings use it.
    public static class Libc
    {
        // sscanf's "%f": the leading float of p_text, false if there is none.
        public static bool ScanFloat(string p_text, out float p_value)
        {
            string text = p_text.Trim();
            int end = 0;
            while (end < text.Length && ("+-.eE".IndexOf(text[end]) >= 0 || char.IsDigit(text[end]))) end++;
            while (end > 0 && !float.TryParse(text.Substring(0, end), NumberStyles.Float, CultureInfo.InvariantCulture, out _)) end--;
            p_value = 0;
            return end > 0 && float.TryParse(text.Substring(0, end), NumberStyles.Float, CultureInfo.InvariantCulture, out p_value);
        }

        // sscanf(p_text, "%f,%f,...") into p_values, stopping at the first that doesn't scan (the rest stay 0).
        public static void ScanFloats(string p_text, float[] p_values)
        {
            string[] tokens = p_text.Split(',');
            for (int i = 0; i < p_values.Length && i < tokens.Length; i++)
            {
                if (!ScanFloat(tokens[i], out p_values[i])) return;
            }
        }

        // atoi
        public static int Atoi(string p_text)
        {
            string text = p_text.TrimStart();
            int end = text.Length > 0 && (text[0] == '-' || text[0] == '+') ? 1 : 0;
            int start = end;
            while (end < text.Length && char.IsDigit(text[end])) end++;
            return end == start ? 0 : int.Parse(text.Substring(0, end), CultureInfo.InvariantCulture);
        }

        // A NUL-terminated ASCII string at p_start, cut at p_end.
        public static string CString(byte[] p_data, int p_start, int p_end)
        {
            int i = p_start;
            while (i < p_end && p_data[i] != 0) i++;
            return Encoding.ASCII.GetString(p_data, p_start, i - p_start);
        }
    }
}
