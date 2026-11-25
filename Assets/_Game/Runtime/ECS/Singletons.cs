using Unity.Entities;
using Unity.Mathematics;

namespace DiceMania.ECS
{
    /// <summary>Global simulation switch, created by the GameDirector.</summary>
    public struct GameClock : IComponentData
    {
        public byte Running;
        public float Time;
        public float ArenaHalf;
        public float EnemyBulletLife;
        public float PlayerRadius;
        public float CritMult;
        public float HealPickupChance;
        public float HealPickupAmount;
        public float ChestGold;
        public float MagnetSpeed;
        public float InvulnTime;
    }

    /// <summary>Resolved player stats pushed by the GameDirector when the build changes.</summary>
    public struct PlayerStats : IComponentData
    {
        public float Might;
        public float Area;
        public float ProjSpeed;
        public float Duration;
        public float Luck;
        public float Armor;
        public float Regen;
        public float PickupRadius;
        public float Thorns;
        public float Greed;
    }

    public struct Buffs : IComponentData
    {
        public float Invuln;
        public float GoldRush;
        public byte MagnetPulse;
    }

    /// <summary>Counters written by systems, consumed (and reset) by the GameDirector every frame.</summary>
    public struct GameEvents : IComponentData
    {
        public int Kills;
        public int Score;
        public float Xp;
        public int Gold;
        public int BossKills;
        public int Chests;
        public float DamageTaken;
        public float Healed;
        public byte PlayerDied;
        public byte CopyHit;
        public byte CopyMiss;
        public int CopyHitDef;
        public byte CopyHitElite;
        public float ShakeRequest;
    }

    public struct Prefabs : IComponentData
    {
        public Entity PlayerShot;
        public Entity EnemyShot;
        public Entity Pickup;
        public Entity Particle;
        public Entity Zone;
        public Entity Enemy;
        public Entity Orbiter;
        public Entity Clone;
    }

    public static class NumKind { public const byte Normal = 0, Crit = 1, PlayerHurt = 2, Heal = 3; }

    public struct DamageNumber : IBufferElementData { public float2 Pos; public float Amount; public byte Kind; }

    public struct SpawnRequest : IBufferElementData { public int Def; public float2 Pos; public int Count; public byte Elite; }

    public static class Sfx
    {
        public const byte Shoot = 0, Hit = 1, Kill = 2, Hurt = 3, Pickup = 4, Explode = 5, EnemyShoot = 6, Copy = 7;
    }

    public struct SfxEvent : IBufferElementData { public byte Id; }
}
