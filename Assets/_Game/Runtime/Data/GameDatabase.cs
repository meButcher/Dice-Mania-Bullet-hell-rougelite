using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace DiceMania.Data
{
    /// <summary>
    /// Loads every design sheet from Resources/Data/*.csv.
    /// Edit the CSVs (Excel / Google Sheets / any text editor) - the game reads them at startup,
    /// and in the Editor you can press F5 during play to hot-reload them.
    /// </summary>
    public static class GameDatabase
    {
        public const string Folder = "Data/";

        public static readonly Dictionary<string, float> Config = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        public static readonly List<CharacterDef> Characters = new List<CharacterDef>();
        public static readonly List<FaceDef> Faces = new List<FaceDef>();
        public static readonly List<WeaponDef> Weapons = new List<WeaponDef>();
        public static readonly List<PassiveDef> Passives = new List<PassiveDef>();
        public static readonly List<RelicDef> Relics = new List<RelicDef>();
        public static readonly List<ShopDef> Shop = new List<ShopDef>();
        public static readonly List<PatternDef> Patterns = new List<PatternDef>();
        public static readonly List<EnemyDef> Enemies = new List<EnemyDef>();
        public static readonly List<WaveDef> Waves = new List<WaveDef>();
        public static readonly List<MetaDef> Meta = new List<MetaDef>();

        public static bool Loaded { get; private set; }
        public static int Version { get; private set; }

        public static float Cfg(string key, float def)
        {
            if (Config.TryGetValue(key, out float v)) return v;
            CsvTable.Warn($"[Config] missing key '{key}', using {def}");
            Config[key] = def;
            return def;
        }

        public static T Find<T>(List<T> list, string id, Func<T, string> getId) where T : class
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(getId(list[i]), id, StringComparison.OrdinalIgnoreCase)) return list[i];
            return null;
        }

        public static FaceDef Face(string id) => Find(Faces, id, f => f.Id);
        public static WeaponDef Weapon(string id) => Find(Weapons, id, w => w.Id);
        public static PassiveDef Passive(string id) => Find(Passives, id, p => p.Id);
        public static RelicDef Relic(string id) => Find(Relics, id, r => r.Id);
        public static EnemyDef Enemy(string id) => Find(Enemies, id, e => e.Id);
        public static PatternDef Pattern(string id) => Find(Patterns, id, p => p.Id);
        public static CharacterDef Character(string id) => Find(Characters, id, c => c.Id);
        public static MetaDef MetaUpgrade(string id) => Find(Meta, id, m => m.Id);

        public static WaveDef WaveFor(int wave)
        {
            if (Waves.Count == 0) return null;
            for (int i = 0; i < Waves.Count; i++) if (Waves[i].Wave == wave) return Waves[i];
            // Endless: reuse the last non-boss wave.
            for (int i = Waves.Count - 1; i >= 0; i--) if (!Waves[i].IsBossWave) return Waves[i];
            return Waves[Waves.Count - 1];
        }

        static CsvTable LoadTable(string name)
        {
            var ta = Resources.Load<TextAsset>(Folder + name);
            if (ta == null)
            {
                CsvTable.Warn($"Sheet Resources/{Folder}{name}.csv not found");
                return CsvTable.Parse(name, "");
            }
            return CsvTable.Parse(name, ta.text);
        }

        /// <summary>Parse from raw text (used by tests / validators).</summary>
        public static void LoadFromTexts(Func<string, string> getText)
        {
            Build(n => CsvTable.Parse(n, getText(n) ?? ""));
        }

        public static void Load()
        {
            Build(LoadTable);
        }

        static void Build(Func<string, CsvTable> get)
        {
            CsvTable.Warnings.Clear();
            Config.Clear(); Characters.Clear(); Faces.Clear(); Weapons.Clear(); Passives.Clear(); Relics.Clear();
            Shop.Clear(); Patterns.Clear(); Enemies.Clear(); Waves.Clear(); Meta.Clear();

            var t = get("Config");
            for (int r = 0; r < t.RowCount; r++) Config[t.Str(r, "Key")] = t.Float(r, "Value");

            t = get("Patterns");
            for (int r = 0; r < t.RowCount; r++)
            {
                Patterns.Add(new PatternDef
                {
                    Id = t.Str(r, "Id"),
                    Type = ParseEnum(t, r, "Type", PatternType.None),
                    Count = t.Int(r, "Count"),
                    Spread = t.Float(r, "Spread"),
                    Spin = t.Float(r, "Spin"),
                    Bursts = Mathf.Max(1, t.Int(r, "Bursts", 1)),
                    BurstInterval = t.Float(r, "BurstInterval"),
                    Cooldown = t.Float(r, "Cooldown"),
                    SpeedMult = t.Float(r, "SpeedMult", 1),
                    SummonEnemy = t.Str(r, "SummonEnemy"),
                });
            }

            t = get("Faces");
            for (int r = 0; r < t.RowCount; r++)
            {
                Faces.Add(new FaceDef
                {
                    Id = t.Str(r, "Id"), Name = t.Str(r, "Name"), Description = t.Str(r, "Description"),
                    Color = t.Color(r, "Color", UnityEngine.Color.white),
                    MaxLevel = Mathf.Max(1, t.Int(r, "MaxLevel", 5)),
                    Value = t.Float(r, "Value"), ValuePerLevel = t.Float(r, "ValuePerLevel"),
                    Count = t.Float(r, "Count"), CountPerLevel = t.Float(r, "CountPerLevel"),
                    Radius = t.Float(r, "Radius"), RadiusPerLevel = t.Float(r, "RadiusPerLevel"),
                    Duration = t.Float(r, "Duration"), DurationPerLevel = t.Float(r, "DurationPerLevel"),
                    HpCost = t.Float(r, "HpCost"), UnlockCost = t.Int(r, "UnlockCost"), ShopWeight = t.Int(r, "ShopWeight", 5),
                });
            }

            t = get("Weapons");
            for (int r = 0; r < t.RowCount; r++)
            {
                Weapons.Add(new WeaponDef
                {
                    Id = t.Str(r, "Id"), Name = t.Str(r, "Name"), Description = t.Str(r, "Description"),
                    Kind = ParseEnum(t, r, "Kind", WeaponKind.PipShot),
                    MaxLevel = Mathf.Max(1, t.Int(r, "MaxLevel", 8)),
                    Damage = t.Float(r, "Damage"), DamagePerLevel = t.Float(r, "DamagePerLevel"),
                    Cooldown = t.Float(r, "Cooldown"), CooldownReductionPerLevel = t.Float(r, "CooldownReductionPerLevel"),
                    Count = t.Float(r, "Count"), CountPerLevel = t.Float(r, "CountPerLevel"),
                    Speed = t.Float(r, "Speed"), Pierce = t.Float(r, "Pierce"), PiercePerLevel = t.Float(r, "PiercePerLevel"),
                    Area = t.Float(r, "Area"), AreaPerLevel = t.Float(r, "AreaPerLevel"),
                    Life = t.Float(r, "Life"), Knockback = t.Float(r, "Knockback"),
                    Recoil = t.HasColumn("Recoil") ? t.Float(r, "Recoil") : 0f,
                    Kick = t.HasColumn("Kick") ? t.Float(r, "Kick") : 0f,
                    Shake = t.HasColumn("Shake") ? t.Float(r, "Shake") : 0f,
                    Shell = t.HasColumn("Shell") ? t.Int(r, "Shell") : 0,
                    Color = t.Color(r, "Color", UnityEngine.Color.white),
                    EvolvesFrom = t.Str(r, "EvolvesFrom"), EvolveRequires = t.Str(r, "EvolveRequires"),
                    Weight = t.Int(r, "Weight", 5),
                });
            }

            t = get("Passives");
            for (int r = 0; r < t.RowCount; r++)
            {
                Passives.Add(new PassiveDef
                {
                    Id = t.Str(r, "Id"), Name = t.Str(r, "Name"), Description = t.Str(r, "Description"),
                    Stat = ParseEnum(t, r, "Stat", StatType.None), ValuePerLevel = t.Float(r, "ValuePerLevel"),
                    MaxLevel = Mathf.Max(1, t.Int(r, "MaxLevel", 5)), Weight = t.Int(r, "Weight", 5),
                });
            }

            t = get("Relics");
            for (int r = 0; r < t.RowCount; r++)
            {
                Relics.Add(new RelicDef
                {
                    Id = t.Str(r, "Id"), Name = t.Str(r, "Name"), Description = t.Str(r, "Description"),
                    Stat = ParseEnum(t, r, "Stat", StatType.None), Value = t.Float(r, "Value"), Cost = t.Int(r, "Cost", 30),
                });
            }

            t = get("Shop");
            for (int r = 0; r < t.RowCount; r++)
            {
                Shop.Add(new ShopDef
                {
                    Type = ParseEnum(t, r, "Type", ShopType.Heal), Weight = t.Int(r, "Weight", 1),
                    BaseCost = t.Int(r, "BaseCost"), CostPerWave = t.Int(r, "CostPerWave"), MinWave = t.Int(r, "MinWave", 1),
                    Description = t.Str(r, "Description"),
                });
            }

            t = get("Enemies");
            for (int r = 0; r < t.RowCount; r++)
            {
                var e = new EnemyDef
                {
                    Index = Enemies.Count,
                    Id = t.Str(r, "Id"), Name = t.Str(r, "Name"),
                    Behaviour = ParseEnum(t, r, "Behaviour", EnemyBehaviour.Chaser),
                    Shape = ParseEnum(t, r, "Shape", ShapeId.Sphere),
                    Color = t.Color(r, "Color", UnityEngine.Color.red),
                    Size = t.Float(r, "Size", 1), HP = t.Float(r, "HP", 10), Speed = t.Float(r, "Speed", 3),
                    ContactDamage = t.Float(r, "ContactDamage"),
                    FireCooldown = t.Float(r, "FireCooldown", 2), BulletSpeed = t.Float(r, "BulletSpeed", 8),
                    BulletDamage = t.Float(r, "BulletDamage", 5), Range = t.Float(r, "Range", 15),
                    PreferredRange = t.Float(r, "PreferredRange"),
                    XP = t.Int(r, "XP", 1), GoldChance = t.Float(r, "GoldChance"), Gold = t.Int(r, "Gold", 1),
                    Score = t.Int(r, "Score", 10), SplitInto = t.Str(r, "SplitInto"), SplitCount = t.Int(r, "SplitCount"),
                    CopyDamageMult = t.Float(r, "CopyDamageMult", 2), CopyContactMult = t.Float(r, "CopyContactMult", 1),
                    CopySpeedMult = t.Float(r, "CopySpeedMult", 1), CopyArmor = t.Float(r, "CopyArmor"),
                };
                e.Pattern = Pattern(t.Str(r, "Pattern", "none")) ?? Pattern("none");
                e.CopyPattern = Pattern(t.Str(r, "CopyPattern", "none")) ?? e.Pattern;
                var bp = t.Str(r, "BossPatterns");
                if (!string.IsNullOrEmpty(bp))
                {
                    foreach (var part in bp.Split('|'))
                    {
                        var kv = part.Split(':');
                        var p = Pattern(kv[0].Trim());
                        if (p == null) { CsvTable.Warn($"[Enemies] {e.Id}: unknown boss pattern '{kv[0]}'"); continue; }
                        float dur = kv.Length > 1 ? ParseF(kv[1], 4) : 4;
                        e.BossPhases.Add(new BossPhaseDef { Pattern = p, Duration = dur });
                    }
                }
                Enemies.Add(e);
            }

            t = get("Waves");
            for (int r = 0; r < t.RowCount; r++)
            {
                var w = new WaveDef
                {
                    Wave = t.Int(r, "Wave"), Duration = t.Float(r, "Duration", 60),
                    SpawnRateStart = t.Float(r, "SpawnRateStart", 1), SpawnRateEnd = t.Float(r, "SpawnRateEnd", 2),
                    MaxAlive = t.Int(r, "MaxAlive", 50), EliteChance = t.Float(r, "EliteChance"),
                    HpMult = t.Float(r, "HpMult", 1), DamageMult = t.Float(r, "DamageMult", 1), SpeedMult = t.Float(r, "SpeedMult", 1),
                    Boss = t.Str(r, "Boss"), EventEvery = t.Float(r, "EventEvery"), EventEnemy = t.Str(r, "EventEnemy"),
                    EventCount = t.Int(r, "EventCount"), EventShape = t.Str(r, "EventShape", "ring"),
                };
                foreach (var part in t.Str(r, "Mix").Split('|'))
                {
                    if (string.IsNullOrWhiteSpace(part)) continue;
                    var kv = part.Split(':');
                    var id = kv[0].Trim();
                    if (Enemy(id) == null) { CsvTable.Warn($"[Waves] wave {w.Wave}: unknown enemy '{id}'"); continue; }
                    w.Mix.Add((id, kv.Length > 1 ? ParseF(kv[1], 1) : 1));
                }
                if (w.IsBossWave && Enemy(w.Boss) == null) CsvTable.Warn($"[Waves] wave {w.Wave}: unknown boss '{w.Boss}'");
                Waves.Add(w);
            }
            Waves.Sort((a, b) => a.Wave.CompareTo(b.Wave));

            t = get("Meta");
            for (int r = 0; r < t.RowCount; r++)
            {
                Meta.Add(new MetaDef
                {
                    Id = t.Str(r, "Id"), Name = t.Str(r, "Name"), Description = t.Str(r, "Description"),
                    Stat = ParseEnum(t, r, "Stat", StatType.None), ValuePerLevel = t.Float(r, "ValuePerLevel"),
                    MaxLevel = Mathf.Max(1, t.Int(r, "MaxLevel", 1)), BaseCost = t.Int(r, "BaseCost", 10),
                    CostGrowth = t.Float(r, "CostGrowth", 1.5f),
                });
            }

            t = get("Characters");
            for (int r = 0; r < t.RowCount; r++)
            {
                var c = new CharacterDef
                {
                    Id = t.Str(r, "Id"), Name = t.Str(r, "Name"), Description = t.Str(r, "Description"),
                    MaxHP = t.Float(r, "MaxHP", 100), MoveSpeed = t.Float(r, "MoveSpeed", 7), PickupRadius = t.Float(r, "PickupRadius", 2.5f),
                    StartWeapon = t.Str(r, "StartWeapon", "pipshot"), StartRolls = t.Int(r, "StartRolls", 5),
                    Might = t.Float(r, "Might", 1), Armor = t.Float(r, "Armor"), Luck = t.Float(r, "Luck"),
                    Color = t.Color(r, "Color", new Color(0.23f, 0.66f, 1f)), UnlockCost = t.Int(r, "UnlockCost"),
                };
                foreach (var die in t.Str(r, "Dice", "bolt|copy|mend|twin|devour|reroll").Split(';'))
                {
                    var faces = die.Split('|');
                    for (int i = 0; i < faces.Length; i++)
                    {
                        faces[i] = faces[i].Trim();
                        if (Face(faces[i]) == null) CsvTable.Warn($"[Characters] {c.Id}: unknown face '{faces[i]}'");
                    }
                    c.Dice.Add(faces);
                }
                if (Weapon(c.StartWeapon) == null) CsvTable.Warn($"[Characters] {c.Id}: unknown start weapon '{c.StartWeapon}'");
                Characters.Add(c);
            }

            foreach (var w in Weapons)
            {
                if (w.IsEvolution)
                {
                    if (Weapon(w.EvolvesFrom) == null) CsvTable.Warn($"[Weapons] {w.Id}: EvolvesFrom '{w.EvolvesFrom}' unknown");
                    if (Passive(w.EvolveRequires) == null) CsvTable.Warn($"[Weapons] {w.Id}: EvolveRequires '{w.EvolveRequires}' is not a passive id");
                }
            }

            Loaded = true;
            Version++;
            Debug.Log($"[DiceMania Data] Loaded v{Version}: {Characters.Count} characters, {Faces.Count} faces, {Weapons.Count} weapons, " +
                      $"{Passives.Count} passives, {Relics.Count} relics, {Enemies.Count} enemies, {Waves.Count} waves, warnings: {CsvTable.Warnings.Count}");
        }

        static float ParseF(string s, float def)
        {
            return float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : def;
        }

        static T ParseEnum<T>(CsvTable t, int r, string col, T def) where T : struct
        {
            var s = t.Str(r, col, null);
            if (s == null) return def;
            if (Enum.TryParse(s, true, out T v)) return v;
            CsvTable.Warn($"[{t.Name}] row {r + 2} column '{col}': '{s}' is not one of {string.Join("/", Enum.GetNames(typeof(T)))}");
            return def;
        }
    }
}
