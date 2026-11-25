using Unity.Entities;
using Unity.Mathematics;

namespace DiceMania.ECS
{
    // ------------------------------------------------------------------
    // Spatial / rendering. The game plays on the XZ plane: Pos.Value = (x, z).
    // ------------------------------------------------------------------
    public struct Pos : IComponentData { public float2 Value; }
    public struct Vel : IComponentData { public float2 Value; }
    public struct Radius : IComponentData { public float Value; }

    /// <summary>Everything with Pos + Visual is drawn by InstancedRenderSystem.</summary>
    public struct Visual : IComponentData
    {
        public byte Mesh;       // ShapeId
        public float3 Scale;
        public float Y;         // height above ground
        public float RotY;      // radians
        public float Spin;      // radians / second
        public float4 Color;    // opaque: rgb + emission in a; additive meshes: rgb * a
        public float Flash;     // 0..1 white hit flash
    }

    // ------------------------------------------------------------------
    // Player
    // ------------------------------------------------------------------
    public struct PlayerTag : IComponentData { }

    public struct PlayerData : IComponentData
    {
        public float Speed;
        public float2 MoveInput;   // written by the GameDirector each frame
        public float2 AimDir;      // normalized, toward mouse
        public float2 AimPoint;    // world point under the mouse
        public float InvulnTimer;
        public float4 BaseColor;
    }

    public struct Health : IComponentData { public float Value; public float Max; }

    /// <summary>Active while the player is transformed by the Copy face.</summary>
    public struct CopyForm : IComponentData
    {
        public float Time;          // remaining seconds; 0 = inactive
        public float TotalTime;
        public int EnemyDef;
        public byte Mesh;
        public float Size;
        public float4 Color;
        public float SpeedMult;
        public float Armor;
        public float ContactDps;
        public Shooter Gun;         // enemy pattern re-used as player bullets
        public float BulletDamage;
        public float BulletSpeed;
    }

    /// <summary>Auto-weapon slots (resolved stats) on the player entity.</summary>
    public struct WeaponSlot : IBufferElementData
    {
        public byte Kind;           // WeaponKind
        public float Damage;
        public float Cooldown;
        public float Timer;
        public int Count;
        public float Speed;
        public int Pierce;
        public float Area;
        public float Life;
        public float Knockback;
        public float4 Color;
        public byte Evolved;
        public float Angle;
    }

    // ------------------------------------------------------------------
    // Enemies
    // ------------------------------------------------------------------
    public struct Enemy : IComponentData
    {
        public int Def;
        public byte Behaviour;      // EnemyBehaviour
        public float Speed;
        public float ContactDamage;
        public float PreferredRange;
        public float FreezeTimer;
        public float StateTimer;
        public float2 Push;
        public byte Elite;
        public byte Boss;
        public int Xp;
        public float GoldChance;
        public int Gold;
        public int Score;
        public int SplitDef;        // -1 = none
        public int SplitCount;
        public float4 BaseColor;
        public float Seed;
    }

    /// <summary>Bullet emitter. Used by enemies, bosses and by the player while copied.</summary>
    public struct Shooter : IComponentData
    {
        public byte Pattern;        // PatternType
        public int Count;
        public float Spread;        // degrees
        public float Spin;          // degrees per shot (spiral)
        public float BulletSpeed;
        public float Damage;
        public float Cooldown;
        public float Timer;
        public float Angle;         // degrees, running angle for ring/spiral
        public int Bursts;
        public int BurstLeft;
        public float BurstInterval;
        public float BurstTimer;
        public float Range;
        public int SummonDef;       // for Summon patterns
    }

    public struct BossPhase : IBufferElementData
    {
        public Shooter Gun;
        public float Duration;
    }

    public struct BossState : IComponentData
    {
        public int Index;
        public float Timer;
        public byte Enraged;
        public float BaseFireMult;
    }

    // ------------------------------------------------------------------
    // Projectiles, zones, pickups, particles
    // ------------------------------------------------------------------
    public static class ProjKind
    {
        public const byte Normal = 0, Explode = 1, Copy = 2, Orbit = 3;
    }

    public struct Projectile : IComponentData
    {
        public byte Kind;
        public float Damage;
        public float Life;
        public int Pierce;
        public float Knockback;
        public Entity LastHit;
        public float HitCooldown;
        public byte Homing;
        public float TurnRate;      // radians / second
        public float Speed;
        public Entity Target;       // copy shot target
        public float Area;          // explosion radius
    }

    public struct EnemyShot : IComponentData { public float Damage; public float Life; public float Radius; }

    public static class ZoneFlags
    {
        public const byte SingleTick = 1, ConsumeBullets = 2, Freeze = 4, Heal = 8, Grow = 16, Aura = 32, ConsumeEnemies = 64;
    }

    public struct DamageZone : IComponentData
    {
        public float Damage;        // per tick (heal per tick for Heal zones)
        public float Interval;
        public float Timer;
        public float Radius;
        public float Life;          // <0 = infinite
        public float Delay;
        public byte Follow;
        public byte Flags;
        public float GrowRate;
        public float MaxRadius;
        public float FreezeTime;
        public float Lifesteal;
        public float Knockback;
        public float4 Color;
    }

    public struct Orbiter : IComponentData { public float Angle; public float Distance; public float AngularSpeed; }

    public struct CloneUnit : IComponentData
    {
        public float Life;
        public float FireTimer;
        public float FireCooldown;
        public float Damage;
        public float OrbitAngle;
    }

    public static class PickupKind { public const byte Xp = 0, Gold = 1, Heal = 2, Chest = 3; }

    public struct Pickup : IComponentData { public byte Kind; public float Value; public byte Magnet; }

    public struct Particle : IComponentData { public float Life; public float MaxLife; public float Drag; public float StartScale; }

    /// <summary>Tag for static arena props (ground stones, fires, walls).</summary>
    public struct Decor : IComponentData { }
}
