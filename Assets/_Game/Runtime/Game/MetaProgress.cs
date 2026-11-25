using System;
using System.Collections.Generic;
using DiceMania.Data;
using UnityEngine;

namespace DiceMania.Game
{
    /// <summary>Persistent progression saved in PlayerPrefs (IndexedDB on Web builds).</summary>
    [Serializable]
    public class MetaSave
    {
        public int Chips;
        public int TotalRuns;
        public int BestWave;
        public int BestScore;
        public string SelectedCharacter = "roller";
        public List<string> MetaIds = new List<string>();
        public List<int> MetaLevels = new List<int>();
        public List<string> UnlockedCharacters = new List<string>();
        public List<string> UnlockedFaces = new List<string>();
        public bool SfxOn = true;
        public bool SeenTutorial;
    }

    public static class MetaProgress
    {
        const string Key = "dicemania_save_v1";
        public static MetaSave Data = new MetaSave();

        public static void Load()
        {
            try
            {
                var json = PlayerPrefs.GetString(Key, "");
                Data = string.IsNullOrEmpty(json) ? new MetaSave() : JsonUtility.FromJson<MetaSave>(json) ?? new MetaSave();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DiceMania] Save could not be read, starting fresh: " + e.Message);
                Data = new MetaSave();
            }
        }

        public static void Save()
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(Data));
            PlayerPrefs.Save();
        }

        public static void ResetAll()
        {
            Data = new MetaSave();
            Save();
        }

        public static int MetaLevel(string id)
        {
            int i = Data.MetaIds.IndexOf(id);
            return i >= 0 ? Data.MetaLevels[i] : 0;
        }

        public static void SetMetaLevel(string id, int lv)
        {
            int i = Data.MetaIds.IndexOf(id);
            if (i >= 0) Data.MetaLevels[i] = lv;
            else { Data.MetaIds.Add(id); Data.MetaLevels.Add(lv); }
        }

        public static Dictionary<string, int> MetaLevelsDict()
        {
            var d = new Dictionary<string, int>();
            for (int i = 0; i < Data.MetaIds.Count; i++) d[Data.MetaIds[i]] = Data.MetaLevels[i];
            return d;
        }

        public static bool IsCharacterUnlocked(CharacterDef c) => c.UnlockCost <= 0 || Data.UnlockedCharacters.Contains(c.Id);
        public static bool IsFaceUnlocked(FaceDef f) => f.UnlockCost <= 0 || Data.UnlockedFaces.Contains(f.Id);

        public static bool TryBuyMeta(MetaDef m)
        {
            int lv = MetaLevel(m.Id);
            if (lv >= m.MaxLevel) return false;
            int cost = m.CostAt(lv);
            if (Data.Chips < cost) return false;
            Data.Chips -= cost;
            SetMetaLevel(m.Id, lv + 1);
            Save();
            return true;
        }

        public static bool TryUnlockCharacter(CharacterDef c)
        {
            if (IsCharacterUnlocked(c) || Data.Chips < c.UnlockCost) return false;
            Data.Chips -= c.UnlockCost;
            Data.UnlockedCharacters.Add(c.Id);
            Save();
            return true;
        }

        public static bool TryUnlockFace(FaceDef f)
        {
            if (IsFaceUnlocked(f) || Data.Chips < f.UnlockCost) return false;
            Data.Chips -= f.UnlockCost;
            Data.UnlockedFaces.Add(f.Id);
            Save();
            return true;
        }
    }
}
