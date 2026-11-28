using System;
using System.Collections.Generic;
using UnityEngine;

namespace DiceMania.Data
{
    public enum StatType
    {
        None, MaxHP, MoveSpeed, Might, Cooldown, Area, ProjectileCount, ProjectileSpeed, Duration,
        PickupRadius, Armor, Regen, Luck, Growth, Greed, RollCharges, RollRecharge, FacePower,
        Thorns, Revival, Interest, DoublesChance,
        Count
    }

    public enum WeaponKind { PipShot = 0, Orbit = 1, Cannon = 2, CardFan = 3, ChipRing = 4, Aura = 5 }
    public enum EnemyBehaviour { Chaser = 0, Keeper = 1, Turret = 2, Rusher = 3, Swarm = 4, Boss = 5 }
    public enum PatternType { None = 0, Aimed = 1, Spread = 2, Ring = 3, Spiral = 4, Random = 5, Summon = 6 }
    public enum ShopType { FaceUpgrade, NewFace, LoadFace, WeaponUpgrade, Relic, Heal, RollCharge, ExtraDie }

    /// <summary>
    /// Mesh ids used by the instanced renderer. Keep in sync with RenderAssets.Init.
    /// Glow = soft additive blob (flashes, halos, mist), Streak = soft additive line (trails, sparks, cracks),
    /// Splat = opaque floor blob (blood, scorch marks).
    /// </summary>
    public enum ShapeId : byte { Sphere = 0, Cube = 1, Diamond = 2, Disc = 3, Ring = 4, Glow = 5, Streak = 6, Splat = 7 }

    public static class Lvl
    {
        /// <summary>base + perLevel * (level - 1)</summary>
        public static float At(float b, float per, int level) => b + per * Mathf.Max(0, level - 1);
        public static int AtInt(float b, float per, int level) => Mathf.FloorToInt(At(b, per, level) + 0.0001f);
    }

    public class CharacterDef
    {
        public string Id, Name, Description, StartWeapon;
        public float MaxHP, MoveSpeed, PickupRadius, Might, Armor, Luck;
        public int StartRolls, UnlockCost;
        public List<string[]> Dice = new List<string[]>();
        public Color Color;
    }

    public class FaceDef
    {
        public string Id, Name, Description;
        public Color Color;
        public int MaxLevel, UnlockCost, ShopWeight;
        public float Value, ValuePerLevel, Count, CountPerLevel, Radius, RadiusPerLevel, Duration, DurationPerLevel, HpCost;
        // Rolled face = current ability, used by clicking / tapping. Cooldown between uses; Uses > 0 = spent after N uses;
        // Auto = holding the button keeps using it whenever the cooldown is ready.
        public float Cooldown, CooldownPerLevel;
        public int Uses;
        public bool Auto;

        public float ValueAt(int l) => Lvl.At(Value, ValuePerLevel, l);
        public int CountAt(int l) => Lvl.AtInt(Count, CountPerLevel, l);
        public float RadiusAt(int l) => Lvl.At(Radius, RadiusPerLevel, l);
        public float DurationAt(int l) => Lvl.At(Duration, DurationPerLevel, l);
        public float CooldownAt(int l) => Mathf.Max(0f, Lvl.At(Cooldown, CooldownPerLevel, l));

        public string Describe(int level)
        {
            return Description
                .Replace("{value}", ValueAt(level).ToString("0.##"))
                .Replace("{count}", CountAt(level).ToString())
                .Replace("{radius}", RadiusAt(level).ToString("0.#"))
                .Replace("{duration}", DurationAt(level).ToString("0.#"))
                .Replace("{hpcost}", HpCost.ToString("0"))
                .Replace("{cooldown}", CooldownAt(level).ToString("0.##"));
        }
    }

    public class WeaponDef
    {
        public string Id, Name, Description, EvolvesFrom, EvolveRequires;
        public WeaponKind Kind;
        public int MaxLevel, Weight;
        public float Damage, DamagePerLevel, Cooldown, CooldownReductionPerLevel, Count, CountPerLevel, Speed,
            Pierce, PiercePerLevel, Area, AreaPerLevel, Life, Knockback;
        // Feel: how hard the shot pushes the hero back, kicks the camera, shakes the screen, and which shell it ejects.
        public float Recoil, Kick, Shake;
        public int Shell;
        public Color Color;
        public bool IsEvolution => !string.IsNullOrEmpty(EvolvesFrom);
    }

    public class PassiveDef
    {
        public string Id, Name, Description;
        public StatType Stat;
        public float ValuePerLevel;
        public int MaxLevel, Weight;
    }

    public class RelicDef
    {
        public string Id, Name, Description;
        public StatType Stat;
        public float Value;
        public int Cost;
    }

    public class ShopDef
    {
        public ShopType Type;
        public int Weight, BaseCost, CostPerWave, MinWave;
        public string Description;
    }

    public class PatternDef
    {
        public string Id, SummonEnemy;
        public PatternType Type;
        public int Count, Bursts;
        public float Spread, Spin, BurstInterval, Cooldown, SpeedMult;
    }

    public class BossPhaseDef
    {
        public PatternDef Pattern;
        public float Duration;
    }

    public class EnemyDef
    {
        public int Index;
        public string Id, Name, SplitInto;
        public EnemyBehaviour Behaviour;
        public ShapeId Shape;
        public Color Color;
        public float Size, HP, Speed, ContactDamage, FireCooldown, BulletSpeed, BulletDamage, Range, PreferredRange, GoldChance;
        public int XP, Gold, Score, SplitCount;
        public PatternDef Pattern, CopyPattern;
        public List<BossPhaseDef> BossPhases = new List<BossPhaseDef>();
        public float CopyDamageMult, CopyContactMult, CopySpeedMult, CopyArmor;
        public bool IsBoss => Behaviour == EnemyBehaviour.Boss;
    }

    public class WaveDef
    {
        public int Wave, MaxAlive, EventCount;
        public float Duration, SpawnRateStart, SpawnRateEnd, EliteChance, HpMult, DamageMult, SpeedMult, EventEvery;
        public string Boss, EventEnemy, EventShape;
        public List<(string id, float weight)> Mix = new List<(string, float)>();
        public bool IsBossWave => !string.IsNullOrEmpty(Boss);
    }

    public class MetaDef
    {
        public string Id, Name, Description;
        public StatType Stat;
        public float ValuePerLevel, CostGrowth;
        public int MaxLevel, BaseCost;
        public int CostAt(int currentLevel) => Mathf.RoundToInt(BaseCost * Mathf.Pow(CostGrowth, currentLevel));
    }
}
