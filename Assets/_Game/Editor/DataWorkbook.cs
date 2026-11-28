using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DiceMania.Data;
using UnityEditor;
using UnityEngine;

namespace DiceMania.EditorTools
{
    /// <summary>
    /// The design spreadsheet: <c>DiceMania_Data.xlsx</c> in the project folder has every data sheet as a tab.
    /// While the editor is open the two stay in sync both ways:
    ///  - save the workbook in Excel / LibreOffice -> the changed tabs are written to Assets/_Game/Resources/Data/*.csv
    ///    (the files the game reads), and a running game reloads them;
    ///  - a CSV changes (code change, git pull, text editor) -> the workbook is rewritten from the CSVs.
    /// Neither side ever overwrites an edit made on the other side since the last sync: that is reported as a
    /// conflict and waits for "Import (spreadsheet wins)" or "Export (CSVs win)".
    /// </summary>
    [InitializeOnLoad]
    public static class DataWorkbook
    {
        public const string WorkbookFile = "DiceMania_Data.xlsx";
        public const string CsvFolder = "Assets/_Game/Resources/Data";
        const string StateFile = "Library/DiceManiaWorkbook.sync";
        const string Tag = "[DiceMania Sheets] ";

        /// <summary>Tab order (any other CSV in the folder follows, alphabetically).</summary>
        public static readonly string[] Order = { "Config", "Characters", "Faces", "Weapons", "Passives", "Relics", "Shop", "Enemies", "Patterns", "Waves", "Meta" };

        static readonly Dictionary<string, string> TabColors = new Dictionary<string, string>
        {
            { "Config", "7F8C9D" }, { "Characters", "F2C23E" }, { "Faces", "FF3EF2" }, { "Weapons", "3EF2E0" },
            { "Passives", "6FA8FF" }, { "Relics", "FF8A3E" }, { "Shop", "FFD86B" }, { "Enemies", "FF4F4F" },
            { "Patterns", "9B5BFF" }, { "Waves", "5CD65C" }, { "Meta", "35C2B0" },
        };

        static readonly Dictionary<string, string> TabNotes = new Dictionary<string, string>
        {
            { "Config", "Global tuning: XP curve, dice rolls and combos, Morph rules, gold, spawn limits, game feel (hit-stop, shake, recoil...)" },
            { "Characters", "Playable heroes: HP, speed, start weapon, rolls, and their starting dice (Dice = face|face|face|face|face|face, a second die after ;)" },
            { "Faces", "Dice faces = the abilities you click / tap to use: damage, count, radius, duration, Cooldown, Uses, Auto (hold to repeat), growth per level" },
            { "Weapons", "Auto weapons and their evolutions, plus how each volley feels (Recoil, Kick, Shake, Shell)" },
            { "Passives", "Level-up passive items: which stat and how much per level" },
            { "Relics", "Dice Forge relics (permanent bonuses for the run)" },
            { "Shop", "Dice Forge offers: weight, base cost, cost growth per wave, first wave" },
            { "Enemies", "Enemy stats, movement, bullet pattern, drops, splitting, boss phases, and what you become when you Morph into them" },
            { "Patterns", "Bullet patterns (Aimed / Spread / Ring / Spiral / Random / Summon)" },
            { "Waves", "Each wave: length, spawn rates, enemy mix (id:weight|id:weight), elites, HP / damage scaling, boss, events" },
            { "Meta", "Permanent upgrades bought with chips between runs" },
        };

        public static string WorkbookPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", WorkbookFile));
        static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        static string CsvPath(string sheet) => Path.Combine(ProjectRoot, CsvFolder, sheet + ".csv");
        static string AssetPath(string sheet) => CsvFolder + "/" + sheet + ".csv";

        // ------------------------------------------------------------------ menu

        [MenuItem("Dice Mania/Data/Open Spreadsheet (DiceMania_Data.xlsx)", priority = 30)]
        public static void OpenSpreadsheet()
        {
            if (!File.Exists(WorkbookPath)) Export(force: true);
            EditorUtility.OpenWithDefaultApp(WorkbookPath);
        }

        [MenuItem("Dice Mania/Data/Spreadsheet: Import (spreadsheet wins)", priority = 31)]
        public static void ImportMenu() => Import(force: true);

        [MenuItem("Dice Mania/Data/Spreadsheet: Export (CSVs win)", priority = 32)]
        public static void ExportMenu() => Export(force: true);

        /// <summary>Command line: -executeMethod DiceMania.EditorTools.DataWorkbook.ExportBatch (CSVs -> workbook).</summary>
        public static void ExportBatch() => EditorApplication.Exit(Export(force: true) ? 0 : 1);

        /// <summary>Command line: -executeMethod DiceMania.EditorTools.DataWorkbook.ImportBatch (workbook -> CSVs).</summary>
        public static void ImportBatch() => EditorApplication.Exit(Import(force: true) >= 0 ? 0 : 1);

        // ------------------------------------------------------------------ import / export

        /// <summary>Workbook -> CSVs. Returns how many CSVs changed, or -1 when the workbook could not be read.</summary>
        public static int Import(bool force)
        {
            List<SheetData> sheets;
            try { sheets = XlsxIO.Read(WorkbookPath); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                if (force) Debug.LogWarning(Tag + "Could not read " + WorkbookFile + ": " + e.Message);
                return -1; // locked or half-saved: the watcher tries again in a moment
            }
            catch (Exception e)
            {
                // Say it once per version of the file; keep checking (it may have been caught half-saved).
                long stamp = File.GetLastWriteTimeUtc(WorkbookPath).Ticks;
                if (force || stamp != badStamp)
                    Debug.LogError(Tag + WorkbookFile + " could not be read as .xlsx (" + e.Message + "). Save it as an Excel Workbook (.xlsx).");
                badStamp = stamp;
                return -1;
            }

            var st = LoadState();
            var changed = new List<string>();
            var conflicts = new List<string>();
            var unknown = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Tab names come from the file, so they never become paths: a tab is imported only when it names a CSV
            // that already exists in the data folder, and that file's own name is used from here on.
            var known = SheetNames();
            foreach (var sh in sheets)
            {
                if (IsNotesTab(sh.Name)) continue;
                string name = known.FirstOrDefault(n => string.Equals(n, sh.Name, StringComparison.OrdinalIgnoreCase));
                if (name == null) { unknown.Add(sh.Name); continue; }
                string path = CsvPath(name);
                seen.Add(name);
                string current = File.ReadAllText(path);
                string next = SheetToCsv(sh, current.Contains("\r\n") ? "\r\n" : "\n");
                if (next == current) continue;
                if (!force && st != null && st.CsvHash.TryGetValue(name, out var h) && h != Hash(current))
                {
                    conflicts.Add(name); // the CSV was edited too since the last sync: don't overwrite it
                    continue;
                }
                File.WriteAllText(path, next, new UTF8Encoding(false));
                changed.Add(name);
            }
            var missing = SheetNames().Where(n => !seen.Contains(n)).ToList();

            foreach (var n in changed) AssetDatabase.ImportAsset(AssetPath(n), ImportAssetOptions.ForceUpdate);
            var snap = Snapshot();
            if (conflicts.Count > 0 && st != null)
            {
                snap.Conflict = true;
                foreach (var n in conflicts) if (st.CsvHash.TryGetValue(n, out var old)) snap.CsvHash[n] = old;
            }
            SaveState(snap);

            if (changed.Count > 0)
            {
                Debug.Log(Tag + "Spreadsheet imported: " + string.Join(", ", changed.Select(n => n + ".csv")) + " updated" +
                          (EditorApplication.isPlaying ? " - reloading the running game." : "."));
                if (EditorApplication.isPlaying) GameDatabase.ReloadRequested = true; // GameDirector reloads on its next frame
                else
                {
                    GameDatabase.Load();
                    if (CsvTable.Warnings.Count > 0)
                        Debug.LogWarning(Tag + CsvTable.Warnings.Count + " data warning(s) after the import:\n" + string.Join("\n", CsvTable.Warnings));
                }
            }
            else if (force) Debug.Log(Tag + "Spreadsheet and CSVs already match.");
            if (conflicts.Count > 0)
                Debug.LogWarning(Tag + "Not imported: " + string.Join(", ", conflicts) + " - the CSV was also changed outside the spreadsheet. " +
                                 "Choose Dice Mania > Data > Spreadsheet: Import (spreadsheet wins) or Export (CSVs win). Auto-sync is paused until then.");
            if (unknown.Count > 0)
                Debug.LogWarning(Tag + "Tabs with no matching CSV were skipped: " + string.Join(", ", unknown) +
                                 ". The game reads only: " + string.Join(", ", SheetNames()) + " (a tab named README or starting with _ is for notes).");
            if (missing.Count > 0)
                Debug.LogWarning(Tag + "The spreadsheet has no tab for: " + string.Join(", ", missing) + " (those CSVs were left as they are).");
            return changed.Count;
        }

        /// <summary>CSVs -> workbook. False when the workbook is open in Excel (locked) or was edited and not imported yet.</summary>
        public static bool Export(bool force)
        {
            var st = LoadState();
            if (!force && st != null && File.Exists(WorkbookPath) && !SameStamp(st, WorkbookPath)) return false;
            try { XlsxIO.Write(WorkbookPath, BuildTabs()); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                if (force || !lockedWarned)
                    Debug.LogWarning(Tag + "Could not update " + WorkbookFile + " (" + e.Message + "). If it is open in Excel, close it and Unity will add the CSV changes.");
                lockedWarned = true;
                return false;
            }
            lockedWarned = false;
            SaveState(Snapshot());
            Debug.Log(Tag + WorkbookFile + " updated from the CSVs.");
            return true;
        }

        /// <summary>A tab's rows as CSV text: data rows padded to the header width, notes and blank rows kept as they are.</summary>
        public static string SheetToCsv(SheetData sh, string newline)
        {
            var rows = sh.Rows;
            int width = 0;
            if (rows.Count > 0)
                for (int c = rows[0].Length - 1; c >= 0; c--)
                    if (!string.IsNullOrEmpty(rows[0][c])) { width = c + 1; break; }
            var outRows = new List<string[]>(rows.Count);
            for (int r = 0; r < rows.Count; r++)
            {
                var row = rows[r];
                int last = -1;
                for (int c = row.Length - 1; c >= 0; c--)
                    if (!string.IsNullOrEmpty(row[c])) { last = c; break; }
                if (last < 0) { outRows.Add(new[] { "" }); continue; }
                bool note = r > 0 && (row[0] ?? "").TrimStart().StartsWith("#", StringComparison.Ordinal);
                int n = note ? last + 1 : Math.Max(width, last + 1);
                var cells = new string[n];
                for (int c = 0; c < n; c++) cells[c] = c < row.Length ? row[c] ?? "" : "";
                outRows.Add(cells);
            }
            return CsvText.Write(outRows, newline);
        }

        public static bool IsNotesTab(string name) =>
            string.Equals(name, "README", StringComparison.OrdinalIgnoreCase) || name.StartsWith("_", StringComparison.Ordinal);

        /// <summary>Every sheet the game has, in tab order.</summary>
        public static List<string> SheetNames()
        {
            var dir = Path.Combine(ProjectRoot, CsvFolder);
            var files = Directory.Exists(dir)
                ? Directory.GetFiles(dir, "*.csv").Select(Path.GetFileNameWithoutExtension).ToList()
                : new List<string>();
            var list = Order.Where(n => files.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();
            list.AddRange(files.Where(f => !Order.Contains(f, StringComparer.OrdinalIgnoreCase)).OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
            return list;
        }

        /// <summary>The README tab followed by one tab per CSV.</summary>
        public static List<XlsxIO.Tab> BuildTabs()
        {
            var tabs = new List<XlsxIO.Tab> { ReadmeTab() };
            foreach (var name in SheetNames())
            {
                var sd = new SheetData(name) { Rows = CsvText.Parse(File.ReadAllText(CsvPath(name))) };
                tabs.Add(new XlsxIO.Tab
                {
                    Sheet = sd,
                    TabColor = TabColors.TryGetValue(name, out var col) ? col : null,
                    FreezeFirstColumn = sd.Rows.Count > 0 && sd.Rows[0].Length > 6,
                });
            }
            return tabs;
        }

        static XlsxIO.Tab ReadmeTab()
        {
            var lines = new List<(string, int)>
            {
                ("Dice Mania - design spreadsheet", XlsxIO.Style.Title),
                ("Every number in the game comes from these tabs: abilities, dice, weapons, enemies, waves, shop, meta upgrades and game feel.", XlsxIO.Style.Body),
                ("", XlsxIO.Style.Body),
                ("How to edit", XlsxIO.Style.Heading),
                ("1.  Change any cell under a tab's dark header row. Keep the header names: the game finds columns by name, so their order does not matter and you can add your own note columns.", XlsxIO.Style.Body),
                ("2.  Add a row to add a thing (an enemy, a wave, a face...). The first column is its Id; other tabs refer to it by that Id.", XlsxIO.Style.Body),
                ("3.  A row whose first cell starts with # is a note; the game skips it.", XlsxIO.Style.Body),
                ("4.  Lists in one cell use |  (Characters > Dice: six face ids for one die, then ; and six more for a second die. Waves > Mix: enemy:weight|enemy:weight).", XlsxIO.Style.Body),
                ("5.  Save (keep the name DiceMania_Data.xlsx and the Excel Workbook format). With the Unity editor open, the game data updates a moment later; if the game is running in Play mode it reloads on the spot.", XlsxIO.Style.Body),
                ("6.  Unity also keeps this file up to date the other way: when a CSV in Assets/_Game/Resources/Data changes, this workbook is rewritten (close it in Excel so it can be).", XlsxIO.Style.Body),
                ("", XlsxIO.Style.Body),
                ("Default die (the face you roll is your ability: click / tap to use it, then it recharges)", XlsxIO.Style.Heading),
                ("1 Bullet  -  2 Duplicate  -  3 Melee  -  4 Heal  -  5 Devour  -  6 Refresh (refills your rolls). The Mimic hero rolls Morph on 6 instead of Refresh.", XlsxIO.Style.Body),
                ("Faces tab: Cooldown = seconds between uses (CooldownPerLevel changes it per level), Uses = how many times before it is spent (0 = unlimited), Auto = 1 keeps using it while the button is held.", XlsxIO.Style.Body),
                ("", XlsxIO.Style.Body),
                ("Tabs", XlsxIO.Style.Heading),
            };
            foreach (var name in SheetNames())
                lines.Add((name + "  -  " + (TabNotes.TryGetValue(name, out var note) ? note : "game data"), XlsxIO.Style.Body));
            lines.Add(("", XlsxIO.Style.Body));
            lines.Add(("If both sides were edited", XlsxIO.Style.Heading));
            lines.Add(("If a CSV changed outside this file while you were editing the same tab here, Unity overwrites neither and says so in the Console. " +
                       "Pick Dice Mania > Data > Spreadsheet: Import (spreadsheet wins) or Export (CSVs win).", XlsxIO.Style.Body));
            lines.Add(("Dice Mania > Data > Validate Sheets lists typos (unknown ids, text in number columns, missing columns).", XlsxIO.Style.Body));

            var sd = new SheetData("README");
            foreach (var (text, _) in lines) sd.Rows.Add(new[] { text });
            return new XlsxIO.Tab { Sheet = sd, Readme = true, TabColor = "1F2A44", RowStyles = lines.Select(l => l.Item2).ToArray() };
        }

        // ------------------------------------------------------------------ automatic sync (editor only)

        static double nextTick;
        static long badStamp;
        static bool lockedWarned, conflictWarned;
        static readonly Dictionary<string, (long ticks, long size, string hash)> hashCache = new Dictionary<string, (long, long, string)>();

        static DataWorkbook()
        {
            if (Application.isBatchMode) return; // builds and test runs never touch the workbook
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now < nextTick) return;
            nextTick = now + 1.5;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer) return;
            try { AutoSync(); }
            catch (Exception e)
            {
                Debug.LogWarning(Tag + "Sync failed: " + e.Message);
                nextTick = now + 15;
            }
        }

        static void AutoSync()
        {
            var st = LoadState();
            bool wbExists = File.Exists(WorkbookPath);
            if (st == null)
            {
                if (!wbExists) { Export(force: true); return; }
                // First run on this machine: take the current pair as the starting point and say if they disagree.
                var diff = Differences();
                SaveState(Snapshot());
                if (diff.Count > 0)
                    Debug.LogWarning(Tag + WorkbookFile + " and the CSVs differ for: " + string.Join(", ", diff) +
                                     ". Use Dice Mania > Data > Spreadsheet: Import (spreadsheet wins) or Export (CSVs win) to line them up.");
                return;
            }
            if (st.Conflict)
            {
                if (!conflictWarned) Debug.LogWarning(Tag + "Spreadsheet auto-sync is paused because of a conflict - see the earlier message.");
                conflictWarned = true;
                return;
            }
            conflictWarned = false;
            if (!wbExists) { Export(force: true); return; }
            if (!SameStamp(st, WorkbookPath)) { Import(force: false); return; }
            foreach (var n in SheetNames())
                if (!st.CsvHash.TryGetValue(n, out var h) || h != CachedHash(n)) { Export(force: false); return; }
        }

        /// <summary>Tabs whose content differs from their CSV (empty when the pair is in sync).</summary>
        public static List<string> Differences()
        {
            var list = new List<string>();
            var sheets = XlsxIO.Read(WorkbookPath).Where(s => !IsNotesTab(s.Name)).ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var n in SheetNames())
            {
                if (!sheets.TryGetValue(n, out var sh)) { list.Add(n); continue; }
                string cur = File.ReadAllText(CsvPath(n));
                if (SheetToCsv(sh, cur.Contains("\r\n") ? "\r\n" : "\n") != cur) list.Add(n);
            }
            return list;
        }

        // ------------------------------------------------------------------ sync state (Library/, per machine)

        sealed class SyncState
        {
            public long WbTicks, WbSize;
            public bool Conflict;
            public readonly Dictionary<string, string> CsvHash = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        static bool SameStamp(SyncState st, string path)
        {
            var fi = new FileInfo(path);
            return fi.Exists && fi.LastWriteTimeUtc.Ticks == st.WbTicks && fi.Length == st.WbSize;
        }

        static SyncState Snapshot()
        {
            var st = new SyncState();
            var fi = new FileInfo(WorkbookPath);
            if (fi.Exists) { st.WbTicks = fi.LastWriteTimeUtc.Ticks; st.WbSize = fi.Length; }
            foreach (var n in SheetNames()) st.CsvHash[n] = CachedHash(n);
            return st;
        }

        static string CachedHash(string sheet)
        {
            var fi = new FileInfo(CsvPath(sheet));
            if (!fi.Exists) return "";
            long t = fi.LastWriteTimeUtc.Ticks, s = fi.Length;
            if (hashCache.TryGetValue(sheet, out var c) && c.ticks == t && c.size == s) return c.hash;
            string h = Hash(File.ReadAllText(fi.FullName));
            hashCache[sheet] = (t, s, h);
            return h;
        }

        static string Hash(string text)
        {
            using (var sha = SHA1.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "");
        }

        static SyncState LoadState()
        {
            string path = Path.Combine(ProjectRoot, StateFile);
            if (!File.Exists(path)) return null;
            var st = new SyncState();
            foreach (var line in File.ReadAllLines(path))
            {
                var p = line.Split('|');
                if (p.Length == 3 && p[0] == "wb") { long.TryParse(p[1], out st.WbTicks); long.TryParse(p[2], out st.WbSize); }
                else if (p.Length == 2 && p[0] == "conflict") st.Conflict = p[1] == "1";
                else if (p.Length == 3 && p[0] == "csv") st.CsvHash[p[1]] = p[2];
            }
            return st;
        }

        static void SaveState(SyncState st)
        {
            var sb = new StringBuilder();
            sb.Append("wb|").Append(st.WbTicks).Append('|').Append(st.WbSize).Append('\n');
            sb.Append("conflict|").Append(st.Conflict ? "1" : "0").Append('\n');
            foreach (var kv in st.CsvHash) sb.Append("csv|").Append(kv.Key).Append('|').Append(kv.Value).Append('\n');
            string path = Path.Combine(ProjectRoot, StateFile);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString());
        }
    }
}
