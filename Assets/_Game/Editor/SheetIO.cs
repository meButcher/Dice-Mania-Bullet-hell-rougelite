using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace DiceMania.EditorTools
{
    /// <summary>One tab of the design spreadsheet: its name and the text of every cell, row by row.</summary>
    public sealed class SheetData
    {
        public string Name;
        public List<string[]> Rows = new List<string[]>();
        public SheetData(string name) { Name = name; }
    }

    /// <summary>
    /// CSV text and rows, kept faithful (comment rows, blank rows and quoting survive), so a CSV that goes into the
    /// spreadsheet and back comes out byte for byte the same.
    /// </summary>
    public static class CsvText
    {
        public static List<string[]> Parse(string text)
        {
            var rows = new List<string[]>();
            if (string.IsNullOrEmpty(text)) return rows;
            if (text[0] == '﻿') text = text.Substring(1);
            char delim = DetectDelimiter(text);
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
                else if (ch == '\n') { cur.Add(sb.ToString()); sb.Length = 0; rows.Add(cur.ToArray()); cur.Clear(); }
                else sb.Append(ch);
            }
            if (sb.Length > 0 || cur.Count > 0) { cur.Add(sb.ToString()); rows.Add(cur.ToArray()); }
            return rows;
        }

        /// <summary>Comma separated; a field is quoted only when it has to be (comma, quote or line break).</summary>
        public static string Write(IEnumerable<string[]> rows, string newline = "\n")
        {
            var sb = new StringBuilder();
            foreach (var r in rows)
            {
                for (int c = 0; c < r.Length; c++)
                {
                    if (c > 0) sb.Append(',');
                    string s = r[c] ?? "";
                    if (s.IndexOfAny(QuoteChars) < 0) sb.Append(s);
                    else sb.Append('"').Append(s.Replace("\"", "\"\"")).Append('"');
                }
                sb.Append(newline);
            }
            return sb.ToString();
        }

        static readonly char[] QuoteChars = { ',', '"', '\r', '\n' };

        static char DetectDelimiter(string text)
        {
            int end = text.IndexOf('\n');
            string header = end >= 0 ? text.Substring(0, end) : text;
            int commas = 0, semis = 0;
            foreach (var c in header) { if (c == ',') commas++; else if (c == ';') semis++; }
            return semis > commas ? ';' : ',';
        }
    }

    /// <summary>
    /// Reads and writes .xlsx workbooks (Office Open XML) with nothing but zip + XML, so no plugin is needed.
    /// Reading understands what Excel, LibreOffice and Google Sheets save (shared strings, rich text, booleans,
    /// formulas' cached values, gaps); writing produces a styled workbook with frozen headers and filters.
    /// </summary>
    public static class XlsxIO
    {
        const string NsMain = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        const string NsRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        const string NsPkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";
        const string Xml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n";
        static readonly DateTimeOffset FixedTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        /// <summary>A tab to write, with its look.</summary>
        public sealed class Tab
        {
            public SheetData Sheet;
            public string TabColor;          // "RRGGBB" or null
            public bool Readme;              // one wide wrapped column of notes instead of a data table
            public int[] RowStyles;          // Readme only: Style per row
            public bool FreezeFirstColumn;   // keep the Id column visible on wide tabs
        }

        /// <summary>Cell styles (indexes into styles.xml cellXfs).</summary>
        public static class Style
        {
            public const int Default = 0, Header = 1, Text = 2, Number = 3, Title = 4, Body = 5, Comment = 6, Heading = 7, IdText = 8;
        }

        // ================================================================== read

        public static List<SheetData> Read(string path)
        {
            // Share everything: Excel keeps the file open while you work, and reading must still succeed.
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                return Read(fs);
        }

        public static List<SheetData> Read(Stream stream)
        {
            XNamespace m = NsMain, r = NsRel, pr = NsPkgRel;
            var result = new List<SheetData>();
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, true))
            {
                string wbPath = "xl/workbook.xml";
                var rootRels = Load(zip, "_rels/.rels");
                if (rootRels?.Root != null)
                    foreach (var rel in rootRels.Root.Elements(pr + "Relationship"))
                        if (((string)rel.Attribute("Type") ?? "").EndsWith("/officeDocument", StringComparison.Ordinal))
                            wbPath = Resolve("", (string)rel.Attribute("Target"));
                var wb = Load(zip, wbPath);
                if (wb?.Root == null) throw new InvalidDataException("not a spreadsheet (no workbook part)");

                string wbDir = DirOf(wbPath);
                var targets = new Dictionary<string, string>();
                string sstPath = null;
                var wbRels = Load(zip, wbDir + "_rels/" + wbPath.Substring(wbDir.Length) + ".rels");
                if (wbRels?.Root != null)
                    foreach (var rel in wbRels.Root.Elements(pr + "Relationship"))
                    {
                        string id = (string)rel.Attribute("Id"), type = (string)rel.Attribute("Type") ?? "";
                        string target = Resolve(wbDir, (string)rel.Attribute("Target"));
                        if (id != null) targets[id] = target;
                        if (type.EndsWith("/sharedStrings", StringComparison.Ordinal)) sstPath = target;
                    }

                var sst = new List<string>();
                var sstDoc = sstPath != null ? Load(zip, sstPath) : null;
                if (sstDoc?.Root != null)
                    foreach (var si in sstDoc.Root.Elements(m + "si")) sst.Add(RichText(si, m));

                var sheets = wb.Root.Element(m + "sheets");
                if (sheets == null) return result;
                foreach (var sh in sheets.Elements(m + "sheet"))
                {
                    string name = (string)sh.Attribute("name") ?? "Sheet" + (result.Count + 1);
                    string rid = (string)sh.Attribute(r + "id");
                    if (rid == null || !targets.TryGetValue(rid, out var part)) continue;
                    var doc = Load(zip, part);
                    if (doc?.Root == null) continue;
                    result.Add(ReadSheet(name, doc.Root, sst, m));
                }
            }
            return result;
        }

        static SheetData ReadSheet(string name, XElement root, List<string> sst, XNamespace m)
        {
            var sheet = new SheetData(name);
            var data = root.Element(m + "sheetData");
            if (data == null) return sheet;
            int nextRow = 1;
            foreach (var row in data.Elements(m + "row"))
            {
                int rn = int.TryParse((string)row.Attribute("r"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int rv) ? rv : nextRow;
                nextRow = rn + 1;
                var cells = new List<string>();
                int nextCol = 0;
                foreach (var c in row.Elements(m + "c"))
                {
                    string at = (string)c.Attribute("r");
                    int col = at != null ? ColumnIndex(at) : nextCol;
                    if (col < 0) col = nextCol;
                    nextCol = col + 1;
                    while (cells.Count <= col) cells.Add("");
                    cells[col] = CellText(c, sst, m);
                }
                while (sheet.Rows.Count < rn - 1) sheet.Rows.Add(new string[0]);
                sheet.Rows.Add(cells.ToArray());
            }
            while (sheet.Rows.Count > 0 && IsBlank(sheet.Rows[sheet.Rows.Count - 1])) sheet.Rows.RemoveAt(sheet.Rows.Count - 1);
            return sheet;
        }

        static string CellText(XElement c, List<string> sst, XNamespace m)
        {
            string t = (string)c.Attribute("t") ?? "n";
            string v = c.Element(m + "v")?.Value;
            switch (t)
            {
                case "s":
                    return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) && i >= 0 && i < sst.Count ? sst[i] : "";
                case "inlineStr":
                {
                    var isEl = c.Element(m + "is");
                    return isEl != null ? RichText(isEl, m) : v ?? "";
                }
                case "b":
                    return v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) ? "1" : "0";
                case "str":
                case "e":
                case "d":
                    return v ?? "";
                default:
                    if (string.IsNullOrEmpty(v)) return "";
                    return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? FormatNumber(d) : v;
            }
        }

        /// <summary>Concatenated text of a string item: plain &lt;t&gt; or rich-text runs (phonetic hints skipped).</summary>
        static string RichText(XElement si, XNamespace m)
        {
            var sb = new StringBuilder();
            foreach (var t in si.Descendants(m + "t"))
                if (t.Parent == null || t.Parent.Name != m + "rPh") sb.Append(t.Value);
            return sb.ToString();
        }

        /// <summary>
        /// Excel stores doubles with up to 17 digits (0.1 can come back as 0.10000000000000001); 15 significant
        /// digits is what the sheet itself shows, and it turns those back into the number that was typed.
        /// </summary>
        public static string FormatNumber(double d) => d.ToString("G15", CultureInfo.InvariantCulture);

        /// <summary>True when the text is a number that survives the trip into a numeric cell and back unchanged.</summary>
        public static bool IsNumber(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length > 24) return false;
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return false;
            if (double.IsNaN(d) || double.IsInfinity(d)) return false;
            return FormatNumber(d) == s;
        }

        static bool IsBlank(string[] row)
        {
            foreach (var s in row) if (!string.IsNullOrEmpty(s)) return false;
            return true;
        }

        /// <summary>"AB12" -> 27 (zero-based column).</summary>
        public static int ColumnIndex(string cellRef)
        {
            int col = 0, n = 0;
            foreach (char ch in cellRef)
            {
                char u = char.ToUpperInvariant(ch);
                if (u < 'A' || u > 'Z') break;
                col = col * 26 + (u - 'A' + 1);
                n++;
            }
            return n == 0 ? -1 : col - 1;
        }

        public static string ColumnName(int col)
        {
            var sb = new StringBuilder();
            for (col++; col > 0; col = (col - 1) / 26) sb.Insert(0, (char)('A' + (col - 1) % 26));
            return sb.ToString();
        }

        static XDocument Load(ZipArchive zip, string path)
        {
            var e = zip.GetEntry(path);
            if (e == null)
                foreach (var x in zip.Entries)
                    if (string.Equals(x.FullName, path, StringComparison.OrdinalIgnoreCase)) { e = x; break; }
            if (e == null) return null;
            using (var s = e.Open()) return XDocument.Load(s);
        }

        static string DirOf(string path)
        {
            int i = path.LastIndexOf('/');
            return i < 0 ? "" : path.Substring(0, i + 1);
        }

        /// <summary>Relationship target relative to <paramref name="baseDir"/> (or absolute "/xl/..."), with ".." folded.</summary>
        static string Resolve(string baseDir, string target)
        {
            if (string.IsNullOrEmpty(target)) return "";
            target = target.Replace('\\', '/');
            string full = target.StartsWith("/", StringComparison.Ordinal) ? target.Substring(1) : baseDir + target;
            var parts = new List<string>();
            foreach (var p in full.Split('/'))
            {
                if (p == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); }
                else if (p.Length > 0 && p != ".") parts.Add(p);
            }
            return string.Join("/", parts);
        }

        // ================================================================== write

        /// <summary>Write the workbook next to <paramref name="path"/> first, then swap it in (fails if Excel has it locked).</summary>
        public static void Write(string path, IList<Tab> tabs)
        {
            string tmp = path + ".tmp";
            try
            {
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                    Write(fs, tabs);
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            finally
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
        }

        public static void Write(Stream stream, IList<Tab> tabs)
        {
            int n = tabs.Count;
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            {
                var ct = new StringBuilder(Xml);
                ct.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
                ct.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
                ct.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
                ct.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
                ct.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
                for (int i = 1; i <= n; i++)
                    ct.Append($"<Override PartName=\"/xl/worksheets/sheet{i}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
                ct.Append("<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>");
                ct.Append("<Override PartName=\"/docProps/app.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.extended-properties+xml\"/>");
                ct.Append("</Types>");
                Put(zip, "[Content_Types].xml", ct.ToString());

                Put(zip, "_rels/.rels", Xml +
                    "<Relationships xmlns=\"" + NsPkgRel + "\">" +
                    "<Relationship Id=\"rId1\" Type=\"" + NsRel + "/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                    "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/>" +
                    "<Relationship Id=\"rId3\" Type=\"" + NsRel + "/extended-properties\" Target=\"docProps/app.xml\"/>" +
                    "</Relationships>");

                Put(zip, "docProps/core.xml", Xml +
                    "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" " +
                    "xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" " +
                    "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
                    "<dc:title>Dice Mania design data</dc:title><dc:creator>Dice Mania</dc:creator></cp:coreProperties>");

                var app = new StringBuilder(Xml);
                app.Append("<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\" ");
                app.Append("xmlns:vt=\"http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes\"><Application>Dice Mania</Application></Properties>");
                Put(zip, "docProps/app.xml", app.ToString());

                var wb = new StringBuilder(Xml);
                wb.Append("<workbook xmlns=\"" + NsMain + "\" xmlns:r=\"" + NsRel + "\">");
                wb.Append("<bookViews><workbookView activeTab=\"0\"/></bookViews><sheets>");
                for (int i = 0; i < n; i++)
                    wb.Append($"<sheet name=\"{Esc(tabs[i].Sheet.Name)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>");
                wb.Append("</sheets>");
                var names = new StringBuilder();
                for (int i = 0; i < n; i++)
                {
                    string range = FilterRange(tabs[i]);
                    if (range == null) continue;
                    names.Append($"<definedName name=\"_xlnm._FilterDatabase\" localSheetId=\"{i}\" hidden=\"1\">")
                         .Append(Esc(SheetRefName(tabs[i].Sheet.Name) + "!" + Absolute(range))).Append("</definedName>");
                }
                if (names.Length > 0) wb.Append("<definedNames>").Append(names).Append("</definedNames>");
                wb.Append("</workbook>");
                Put(zip, "xl/workbook.xml", wb.ToString());

                var rels = new StringBuilder(Xml);
                rels.Append("<Relationships xmlns=\"" + NsPkgRel + "\">");
                for (int i = 1; i <= n; i++)
                    rels.Append($"<Relationship Id=\"rId{i}\" Type=\"{NsRel}/worksheet\" Target=\"worksheets/sheet{i}.xml\"/>");
                rels.Append($"<Relationship Id=\"rId{n + 1}\" Type=\"{NsRel}/styles\" Target=\"styles.xml\"/>");
                rels.Append("</Relationships>");
                Put(zip, "xl/_rels/workbook.xml.rels", rels.ToString());

                Put(zip, "xl/styles.xml", StylesXml);
                for (int i = 0; i < n; i++) Put(zip, $"xl/worksheets/sheet{i + 1}.xml", SheetXml(tabs[i], i == 0));
            }
        }

        static void Put(ZipArchive zip, string name, string content)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Optimal);
            e.LastWriteTime = FixedTime; // same CSVs -> same bytes, so re-exporting never shows up as a change in git
            using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false))) w.Write(content);
        }

        static string FilterRange(Tab tab)
        {
            var rows = tab.Sheet.Rows;
            if (tab.Readme || rows.Count < 2 || rows[0].Length == 0) return null;
            return "A1:" + ColumnName(Width(tab) - 1) + rows.Count;
        }

        static int Width(Tab tab)
        {
            int w = 1;
            foreach (var r in tab.Sheet.Rows) w = Math.Max(w, r.Length);
            return w;
        }

        static string Absolute(string range)
        {
            var sb = new StringBuilder();
            foreach (var part in range.Split(':'))
            {
                if (sb.Length > 0) sb.Append(':');
                int k = 0;
                while (k < part.Length && char.IsLetter(part[k])) k++;
                sb.Append('$').Append(part, 0, k).Append('$').Append(part, k, part.Length - k);
            }
            return sb.ToString();
        }

        static string SheetRefName(string name)
        {
            foreach (char ch in name)
                if (!char.IsLetterOrDigit(ch) && ch != '_') return "'" + name.Replace("'", "''") + "'";
            return name;
        }

        static string SheetXml(Tab tab, bool selected)
        {
            var rows = tab.Sheet.Rows;
            int width = Width(tab);
            var sb = new StringBuilder(Xml);
            sb.Append("<worksheet xmlns=\"" + NsMain + "\" xmlns:r=\"" + NsRel + "\">");
            if (!string.IsNullOrEmpty(tab.TabColor)) sb.Append($"<sheetPr><tabColor rgb=\"FF{tab.TabColor}\"/></sheetPr>");
            sb.Append($"<dimension ref=\"A1:{ColumnName(width - 1)}{Math.Max(1, rows.Count)}\"/>");
            sb.Append("<sheetViews><sheetView workbookViewId=\"0\"").Append(selected ? " tabSelected=\"1\"" : "").Append(tab.Readme ? " showGridLines=\"0\"" : "").Append('>');
            if (!tab.Readme && rows.Count > 1)
            {
                if (tab.FreezeFirstColumn)
                    sb.Append("<pane xSplit=\"1\" ySplit=\"1\" topLeftCell=\"B2\" activePane=\"bottomRight\" state=\"frozen\"/>" +
                              "<selection pane=\"topRight\"/><selection pane=\"bottomLeft\"/><selection pane=\"bottomRight\" activeCell=\"B2\" sqref=\"B2\"/>");
                else
                    sb.Append("<pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/>" +
                              "<selection pane=\"bottomLeft\" activeCell=\"A2\" sqref=\"A2\"/>");
            }
            sb.Append("</sheetView></sheetViews>");

            sb.Append("<cols>");
            for (int c = 0; c < width; c++)
            {
                double w = tab.Readme ? 118 : ColumnWidth(rows, c);
                sb.Append($"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{w.ToString("0.##", CultureInfo.InvariantCulture)}\" customWidth=\"1\"/>");
            }
            sb.Append("</cols><sheetData>");
            for (int r = 0; r < rows.Count; r++)
            {
                var row = rows[r];
                if (IsBlank(row)) continue;
                bool comment = !tab.Readme && r > 0 && (row[0] ?? "").TrimStart().StartsWith("#", StringComparison.Ordinal);
                sb.Append($"<row r=\"{r + 1}\">");
                for (int c = 0; c < row.Length; c++)
                {
                    string v = row[c] ?? "";
                    if (v.Length == 0) continue;
                    string at = ColumnName(c) + (r + 1);
                    int style;
                    if (tab.Readme) style = tab.RowStyles != null && r < tab.RowStyles.Length ? tab.RowStyles[r] : Style.Body;
                    else if (r == 0) style = Style.Header;
                    else if (comment) style = Style.Comment;
                    else if (IsNumber(v)) { sb.Append($"<c r=\"{at}\" s=\"{Style.Number}\"><v>{v}</v></c>"); continue; }
                    else style = c == 0 ? Style.IdText : Style.Text;
                    sb.Append($"<c r=\"{at}\" s=\"{style}\" t=\"inlineStr\"><is><t");
                    if (v.Length != v.Trim().Length || v.IndexOf('\n') >= 0) sb.Append(" xml:space=\"preserve\"");
                    sb.Append('>').Append(Esc(v)).Append("</t></is></c>");
                }
                sb.Append("</row>");
            }
            sb.Append("</sheetData>");
            string filter = FilterRange(tab);
            if (filter != null) sb.Append($"<autoFilter ref=\"{filter}\"/>");
            sb.Append("<pageMargins left=\"0.7\" right=\"0.7\" top=\"0.75\" bottom=\"0.75\" header=\"0.3\" footer=\"0.3\"/>");
            sb.Append("</worksheet>");
            return sb.ToString();
        }

        static double ColumnWidth(List<string[]> rows, int c)
        {
            int len = 0;
            for (int r = 0; r < rows.Count; r++)
            {
                if (c >= rows[r].Length || string.IsNullOrEmpty(rows[r][c])) continue;
                if (r > 0 && (rows[r][0] ?? "").TrimStart().StartsWith("#", StringComparison.Ordinal)) continue; // notes may overflow
                foreach (var line in rows[r][c].Split('\n')) len = Math.Max(len, line.Length + (r == 0 ? 3 : 0)); // header: bold + filter button
            }
            return Math.Min(70, Math.Max(7, len * 1.05 + 1.5));
        }

        static string Esc(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            foreach (char ch in s)
            {
                switch (ch)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    default:
                        if (ch < 0x20 && ch != '\t' && ch != '\n' && ch != '\r') break; // not allowed in XML
                        sb.Append(ch);
                        break;
                }
            }
            return sb.ToString();
        }

        // Fonts: 0 Arial 10, 1 header (bold white), 2 title, 3 note (grey italic), 4 heading, 5 id (bold).
        // Fills: 2 header band. Borders: 1 header underline. cellXfs follow Style.*; "@" (text) keeps typed ids as text.
        const string StylesXml = Xml +
            "<styleSheet xmlns=\"" + NsMain + "\">" +
            "<fonts count=\"6\">" +
            "<font><sz val=\"10\"/><name val=\"Arial\"/><family val=\"2\"/></font>" +
            "<font><b/><sz val=\"10\"/><color rgb=\"FFFFFFFF\"/><name val=\"Arial\"/><family val=\"2\"/></font>" +
            "<font><b/><sz val=\"16\"/><color rgb=\"FF1F2A44\"/><name val=\"Arial\"/><family val=\"2\"/></font>" +
            "<font><i/><sz val=\"10\"/><color rgb=\"FF7F7F7F\"/><name val=\"Arial\"/><family val=\"2\"/></font>" +
            "<font><b/><sz val=\"11\"/><color rgb=\"FF1F2A44\"/><name val=\"Arial\"/><family val=\"2\"/></font>" +
            "<font><b/><sz val=\"10\"/><name val=\"Arial\"/><family val=\"2\"/></font>" +
            "</fonts>" +
            "<fills count=\"3\">" +
            "<fill><patternFill patternType=\"none\"/></fill>" +
            "<fill><patternFill patternType=\"gray125\"/></fill>" +
            "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF2F3B52\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
            "</fills>" +
            "<borders count=\"2\">" +
            "<border><left/><right/><top/><bottom/><diagonal/></border>" +
            "<border><left/><right/><top/><bottom style=\"thin\"><color rgb=\"FF9AA3B5\"/></bottom><diagonal/></border>" +
            "</borders>" +
            "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
            "<cellXfs count=\"9\">" +
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
            "<xf numFmtId=\"49\" fontId=\"1\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\"/></xf>" +
            "<xf numFmtId=\"49\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
            "<xf numFmtId=\"49\" fontId=\"2\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\"/>" +
            "<xf numFmtId=\"49\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\" applyAlignment=\"1\"><alignment vertical=\"top\" wrapText=\"1\"/></xf>" +
            "<xf numFmtId=\"49\" fontId=\"3\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\"/>" +
            "<xf numFmtId=\"49\" fontId=\"4\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\"/>" +
            "<xf numFmtId=\"49\" fontId=\"5\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\"/>" +
            "</cellXfs>" +
            "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
            "<dxfs count=\"0\"/><tableStyles count=\"0\" defaultTableStyle=\"TableStyleMedium2\" defaultPivotStyle=\"PivotStyleLight16\"/>" +
            "</styleSheet>";
    }
}
