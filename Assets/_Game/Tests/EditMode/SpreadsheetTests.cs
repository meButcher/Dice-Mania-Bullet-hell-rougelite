using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using DiceMania.EditorTools;
using NUnit.Framework;

namespace DiceMania.Tests
{
    /// <summary>The design spreadsheet (DiceMania_Data.xlsx) and the CSVs the game reads must carry the same data.</summary>
    public class SpreadsheetTests
    {
        [Test]
        public void EveryCsvSurvivesTheSpreadsheetRoundTrip()
        {
            var tabs = DataWorkbook.BuildTabs();
            var ms = new MemoryStream();
            XlsxIO.Write(ms, tabs);
            ms.Position = 0;
            var sheets = XlsxIO.Read(ms);

            Assert.AreEqual(tabs.Count, sheets.Count);
            Assert.AreEqual("README", sheets[0].Name);
            Assert.IsTrue(DataWorkbook.IsNotesTab(sheets[0].Name));
            var names = DataWorkbook.SheetNames();
            CollectionAssert.IsSubsetOf(new[] { "Config", "Characters", "Faces", "Weapons", "Enemies", "Waves" }, names);
            foreach (var name in names)
            {
                string path = Path.Combine(UnityEngine.Application.dataPath, "..", DataWorkbook.CsvFolder, name + ".csv");
                string original = File.ReadAllText(path);
                var sheet = sheets.First(s => s.Name == name);
                string back = DataWorkbook.SheetToCsv(sheet, original.Contains("\r\n") ? "\r\n" : "\n");
                Assert.AreEqual(original, back, $"{name}.csv changed on the way through the spreadsheet");
            }
        }

        [Test]
        public void ReaderUnderstandsWhatExcelSaves()
        {
            // The shapes Excel / LibreOffice write that our own writer never does: shared strings, rich-text runs,
            // phonetic hints, 17-digit doubles, booleans, cached formula results, absolute part paths, skipped cells and rows.
            var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                const string main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                const string rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
                const string pkg = "http://schemas.openxmlformats.org/package/2006/relationships";
                Put(zip, "_rels/.rels", $"<Relationships xmlns=\"{pkg}\"><Relationship Id=\"r1\" Type=\"{rel}/officeDocument\" Target=\"/xl/workbook.xml\"/></Relationships>");
                Put(zip, "xl/workbook.xml", $"<workbook xmlns=\"{main}\" xmlns:r=\"{rel}\"><sheets><sheet name=\"README\" sheetId=\"2\" r:id=\"rId2\"/><sheet name=\"Faces\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
                Put(zip, "xl/_rels/workbook.xml.rels",
                    $"<Relationships xmlns=\"{pkg}\">" +
                    $"<Relationship Id=\"rId1\" Type=\"{rel}/worksheet\" Target=\"/xl/worksheets/sheet1.xml\"/>" +
                    $"<Relationship Id=\"rId2\" Type=\"{rel}/worksheet\" Target=\"worksheets/../worksheets/sheet2.xml\"/>" +
                    $"<Relationship Id=\"rId3\" Type=\"{rel}/sharedStrings\" Target=\"sharedStrings.xml\"/></Relationships>");
                Put(zip, "xl/sharedStrings.xml",
                    $"<sst xmlns=\"{main}\"><si><t>Id</t></si><si><r><t>Va</t></r><r><rPr><b/></rPr><t>lue</t></r></si>" +
                    "<si><t>bolt</t></si><si><t>Melee, sharp</t><rPh sb=\"0\" eb=\"1\"><t>X</t></rPh></si></sst>");
                Put(zip, "xl/worksheets/sheet1.xml",
                    $"<worksheet xmlns=\"{main}\"><sheetData>" +
                    "<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"B1\" t=\"s\"><v>1</v></c><c r=\"D1\" t=\"inlineStr\"><is><t>Auto</t></is></c></row>" +
                    "<row r=\"3\"><c r=\"A3\" t=\"s\"><v>2</v></c><c r=\"B3\"><v>0.10000000000000001</v></c><c r=\"D3\" t=\"b\"><v>1</v></c></row>" +
                    "<row r=\"4\"><c r=\"A4\" t=\"s\"><v>3</v></c><c r=\"C4\" t=\"str\"><f>LOWER(\"X\")</f><v>x</v></c><c r=\"D4\" s=\"2\"/></row>" +
                    "<row r=\"9\" customFormat=\"1\"/>" +
                    "</sheetData></worksheet>");
                Put(zip, "xl/worksheets/sheet2.xml", $"<worksheet xmlns=\"{main}\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>notes</t></is></c></row></sheetData></worksheet>");
            }
            ms.Position = 0;
            var sheets = XlsxIO.Read(ms);
            Assert.AreEqual(2, sheets.Count);
            var faces = sheets.Single(s => s.Name == "Faces");
            Assert.AreEqual(4, faces.Rows.Count, "trailing empty rows are dropped");
            Assert.AreEqual("Id,Value,,Auto\n\nbolt,0.1,,1\n\"Melee, sharp\",,x,\n", DataWorkbook.SheetToCsv(faces, "\n"));
            Assert.AreEqual("notes", sheets.Single(s => s.Name == "README").Rows[0][0]);
        }

        [Test]
        public void NumbersStayNumbersAndTextStaysText()
        {
            foreach (var n in new[] { "25", "0.45", "-0.03", "1.75", "100000", "0" }) Assert.IsTrue(XlsxIO.IsNumber(n), n);
            foreach (var t in new[] { "", "007", "1e3", "#3EF2E0", "NaN", "Infinity", " 5", "5 ", "1|2", "0.50", "bolt" }) Assert.IsFalse(XlsxIO.IsNumber(t), t);
            Assert.AreEqual("0.3", XlsxIO.FormatNumber(0.1 + 0.2));
            Assert.AreEqual("A", XlsxIO.ColumnName(0));
            Assert.AreEqual("Z", XlsxIO.ColumnName(25));
            Assert.AreEqual("AA", XlsxIO.ColumnName(26));
            Assert.AreEqual("AAA", XlsxIO.ColumnName(702));
            for (int c = 0; c < 800; c += 7) Assert.AreEqual(c, XlsxIO.ColumnIndex(XlsxIO.ColumnName(c) + "12"));
        }

        [Test]
        public void CsvTextKeepsNotesQuotesAndBlankLines()
        {
            const string text = "Id,Description\n# a note, with a comma\n\nbolt,\"Fires, fast \"\"bolts\"\"\"\nmelee,\"two\nlines\"\n";
            var rows = CsvText.Parse(text);
            Assert.AreEqual(5, rows.Count);
            Assert.AreEqual("Fires, fast \"bolts\"", rows[3][1]);
            Assert.AreEqual("two\nlines", rows[4][1]);
            Assert.AreEqual(text, CsvText.Write(rows));
        }

        [Test]
        public void ProjectSpreadsheetMatchesTheCsvs()
        {
            if (!File.Exists(DataWorkbook.WorkbookPath)) Assert.Ignore(DataWorkbook.WorkbookFile + " not found");
            var diff = DataWorkbook.Differences();
            Assert.IsEmpty(diff, $"{DataWorkbook.WorkbookFile} and the CSVs disagree on: {string.Join(", ", diff)}. " +
                                 "Open the project in Unity (it syncs them) or run Dice Mania > Data > Spreadsheet: Import / Export.");
        }

        static void Put(ZipArchive zip, string name, string xml)
        {
            using (var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false))) w.Write(xml);
        }
    }
}
