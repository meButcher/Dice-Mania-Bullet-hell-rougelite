using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace DiceMania.Data
{
    /// <summary>
    /// Minimal, forgiving CSV reader for the design spreadsheets.
    /// Handles quotes, escaped quotes, CRLF, UTF-8 BOM, ';' delimiters (Excel in some locales),
    /// blank lines and comment rows starting with '#'.
    /// </summary>
    public sealed class CsvTable
    {
        public readonly string Name;
        public readonly string[] Headers;
        readonly List<string[]> rows = new List<string[]>();
        readonly Dictionary<string, int> columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public static readonly List<string> Warnings = new List<string>();

        public int RowCount => rows.Count;

        CsvTable(string name, List<string[]> all)
        {
            Name = name;
            Headers = all.Count > 0 ? all[0] : Array.Empty<string>();
            for (int i = 0; i < Headers.Length; i++)
            {
                var h = Headers[i].Trim();
                Headers[i] = h;
                if (h.Length > 0 && !columns.ContainsKey(h)) columns[h] = i;
            }
            for (int r = 1; r < all.Count; r++)
            {
                var row = all[r];
                if (row.Length == 0) continue;
                bool empty = true;
                for (int c = 0; c < row.Length; c++) if (!string.IsNullOrWhiteSpace(row[c])) { empty = false; break; }
                if (empty) continue;
                if (row[0].TrimStart().StartsWith("#")) continue;
                rows.Add(row);
            }
        }

        public static CsvTable Parse(string name, string text)
        {
            if (text == null) text = string.Empty;
            if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
            char delim = DetectDelimiter(text);
            var all = new List<string[]>();
            var cur = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else sb.Append(ch);
                }
                else if (ch == '"') inQuotes = true;
                else if (ch == delim) { cur.Add(sb.ToString()); sb.Length = 0; }
                else if (ch == '\r') { }
                else if (ch == '\n') { cur.Add(sb.ToString()); sb.Length = 0; all.Add(cur.ToArray()); cur.Clear(); }
                else sb.Append(ch);
            }
            if (sb.Length > 0 || cur.Count > 0) { cur.Add(sb.ToString()); all.Add(cur.ToArray()); }
            return new CsvTable(name, all);
        }

        static char DetectDelimiter(string text)
        {
            int end = text.IndexOf('\n');
            string header = end >= 0 ? text.Substring(0, end) : text;
            int commas = 0, semis = 0;
            foreach (var c in header) { if (c == ',') commas++; else if (c == ';') semis++; }
            return semis > commas ? ';' : ',';
        }

        public bool HasColumn(string col) => columns.ContainsKey(col);

        public string Str(int row, string col, string def = "")
        {
            if (!columns.TryGetValue(col, out int c))
            {
                Warn($"[{Name}] missing column '{col}'");
                return def;
            }
            var r = rows[row];
            if (c >= r.Length) return def;
            var s = r[c].Trim();
            return s.Length == 0 ? def : s;
        }

        public float Float(int row, string col, float def = 0f)
        {
            var s = Str(row, col, null);
            if (s == null) return def;
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) return v;
            if (float.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            if (s.EndsWith("%") && float.TryParse(s.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v / 100f;
            Warn($"[{Name}] row {row + 2} column '{col}': '{s}' is not a number");
            return def;
        }

        public int Int(int row, string col, int def = 0) => Mathf.RoundToInt(Float(row, col, def));

        public Color Color(int row, string col, Color def)
        {
            var s = Str(row, col, null);
            if (s == null) return def;
            if (!s.StartsWith("#")) s = "#" + s;
            if (ColorUtility.TryParseHtmlString(s, out var c)) return c;
            Warn($"[{Name}] row {row + 2} column '{col}': '{s}' is not a colour (use #RRGGBB)");
            return def;
        }

        public static void Warn(string msg)
        {
            if (Warnings.Count < 200) Warnings.Add(msg);
            Debug.LogWarning("[DiceMania Data] " + msg);
        }
    }
}
