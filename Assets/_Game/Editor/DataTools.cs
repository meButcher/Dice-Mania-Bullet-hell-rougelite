using System.IO;
using DiceMania.Data;
using UnityEditor;
using UnityEngine;

namespace DiceMania.EditorTools
{
    /// <summary>Editor helpers for the CSV design sheets.</summary>
    public static class DataTools
    {
        const string Folder = "Assets/_Game/Resources/Data";

        [MenuItem("Dice Mania/Data/Validate Sheets", priority = 40)]
        public static void Validate()
        {
            AssetDatabase.Refresh();
            GameDatabase.Load();
            if (CsvTable.Warnings.Count == 0) Debug.Log("[DiceMania Data] All sheets OK.");
            else Debug.LogWarning($"[DiceMania Data] {CsvTable.Warnings.Count} warning(s):\n" + string.Join("\n", CsvTable.Warnings));
        }

        /// <summary>Command line: -executeMethod DiceMania.EditorTools.DataTools.ValidateBatch (exit code 1 on warnings).</summary>
        public static void ValidateBatch()
        {
            Validate();
            EditorApplication.Exit(CsvTable.Warnings.Count == 0 ? 0 : 1);
        }

        [MenuItem("Dice Mania/Data/Open Data Folder", priority = 41)]
        public static void OpenFolder() => EditorUtility.RevealInFinder(Path.GetFullPath(Folder + "/Config.csv"));

        [MenuItem("Dice Mania/Data/Reload In Play Mode (F5)", priority = 42)]
        public static void Reload()
        {
            AssetDatabase.Refresh();
            Debug.Log("[DiceMania Data] Press F5 in the Game view to hot-reload sheets while playing.");
        }
    }
}
