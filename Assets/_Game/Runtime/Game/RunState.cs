using System.Collections.Generic;
using DiceMania.Data;
using UnityEngine;

namespace DiceMania.Game
{
    public class FaceSlot
    {
        public FaceDef Face;
        public int Level = 1;
        public int Weight = 1;
        public FaceSlot Clone() => new FaceSlot { Face = Face, Level = Level, Weight = Weight };
    }

    public class Die
    {
        public readonly FaceSlot[] Slots = new FaceSlot[6];
        public int TotalWeight
        {
            get { int w = 0; foreach (var s in Slots) w += Mathf.Max(0, s.Weight); return w; }
        }
        public float Chance(int slot) => TotalWeight > 0 ? Slots[slot].Weight / (float)TotalWeight : 0f;

        public static Die FromIds(string[] ids)
        {
            var d = new Die();
            for (int i = 0; i < 6; i++)
            {
                string id = ids != null && ids.Length > 0 ? ids[i % ids.Length] : "bolt";
                d.Slots[i] = new FaceSlot { Face = GameDatabase.Face(id) ?? GameDatabase.Faces[0] };
            }
            return d;
        }
    }

    public class OwnedWeapon { public WeaponDef Def; public int Level = 1; }
    public class OwnedPassive { public PassiveDef Def; public int Level = 1; }

    /// <summary>Everything about the current run that lives outside ECS.</summary>
    public class RunState
    {
        public CharacterDef Character;
        public readonly List<Die> Dice = new List<Die>();
        public readonly List<OwnedWeapon> Weapons = new List<OwnedWeapon>();
        public readonly List<OwnedPassive> Passives = new List<OwnedPassive>();
        public readonly List<RelicDef> Relics = new List<RelicDef>();
        public readonly List<int> PipHistory = new List<int>();

        public int Gold, Level = 1, Score, Kills, Wave, RollsLeft, BonusRolls, CopyCharges, CopyLevel = 1, RevivalsLeft, BossesKilled;
        public float Xp, XpToNext, RunTime, RollRechargeTimer, DamageTaken;
        public bool Endless;

        readonly float[] bonus = new float[(int)StatType.Count];
        public Dictionary<string, int> MetaLevels = new Dictionary<string, int>();

        public float Bonus(StatType t) => bonus[(int)t];

        public void RecalculateStats()
        {
            for (int i = 0; i < bonus.Length; i++) bonus[i] = 0f;
            foreach (var p in Passives) bonus[(int)p.Def.Stat] += p.Def.ValuePerLevel * p.Level;
            foreach (var r in Relics) bonus[(int)r.Stat] += r.Value;
            foreach (var m in GameDatabase.Meta)
                if (MetaLevels.TryGetValue(m.Id, out int lv) && lv > 0) bonus[(int)m.Stat] += m.ValuePerLevel * lv;
        }

        // ---- Resolved stats ----
        public float MaxHP => Character.MaxHP + Bonus(StatType.MaxHP);
        public float MoveSpeed => Character.MoveSpeed * (1f + Bonus(StatType.MoveSpeed));
        public float Might => Character.Might * (1f + Bonus(StatType.Might));
        public float CooldownMult => Mathf.Clamp(1f - Bonus(StatType.Cooldown), 0.25f, 1f);
        public float Area => 1f + Bonus(StatType.Area);
        public int ProjectileCount => Mathf.RoundToInt(Bonus(StatType.ProjectileCount));
        public float ProjectileSpeed => 1f + Bonus(StatType.ProjectileSpeed);
        public float Duration => 1f + Bonus(StatType.Duration);
        public float PickupRadius => Character.PickupRadius * (1f + Bonus(StatType.PickupRadius));
        public float Armor => Character.Armor + Bonus(StatType.Armor);
        public float Regen => Bonus(StatType.Regen);
        public float Luck => Mathf.Clamp01(Character.Luck + Bonus(StatType.Luck));
        public float Growth => 1f + Bonus(StatType.Growth);
        public float Greed => Bonus(StatType.Greed);
        public float FacePower => 1f + Bonus(StatType.FacePower);
        public float Thorns => Bonus(StatType.Thorns);
        public float Interest => GameDatabase.Cfg("InterestRate", 0.1f) + Bonus(StatType.Interest);
        public float EchoChance => Mathf.Clamp01(Bonus(StatType.DoublesChance));
        public float RollRechargeMult => 1f + Bonus(StatType.RollRecharge);
        public int RollsPerWave => Character.StartRolls + Mathf.RoundToInt(Bonus(StatType.RollCharges)) + BonusRolls;
        public int Revivals => Mathf.RoundToInt(Bonus(StatType.Revival));

        public static float XpForLevel(int level)
        {
            float b = GameDatabase.Cfg("XpBase", 6), g = GameDatabase.Cfg("XpGrowth", 5), q = GameDatabase.Cfg("XpQuadratic", 0.5f);
            int l = Mathf.Max(0, level - 1);
            return b + g * l + q * l * l;
        }

        public OwnedWeapon FindWeapon(string id) => Weapons.Find(w => w.Def.Id == id);
        public OwnedPassive FindPassive(string id) => Passives.Find(p => p.Def.Id == id);

        public int MaxFaceLevel(string faceId)
        {
            int best = 0;
            foreach (var d in Dice) foreach (var s in d.Slots) if (s.Face.Id == faceId && s.Level > best) best = s.Level;
            return best;
        }
    }
}
