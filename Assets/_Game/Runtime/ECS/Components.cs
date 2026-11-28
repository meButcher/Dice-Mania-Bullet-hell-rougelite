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
        public byte Fx;         // VisualFx flags: extra glow instances drawn with this entity
        public float3 Scale;
        public float Y;         // height above ground
        public float RotY;      // radians
        public float Spin;      // radians / second
        public float4 Color;    // opaque: rgb + emission in a; additive meshes: rgb * a
        public float Flash;     // 0..1 white hit flash (1 = pure white silhouette)
        public float SquashAmp; // jelly wobble amplitude, set on hits (0 = none)
        public float SquashT;   // seconds since the wobble started
        public float Heading;   // radians (same convention as RotY): direction of travel, used by trails
        public float TrailLen;  // world units of glowing tail drawn behind (VisualFx.Trail)
    }

    public static class VisualFx
    {
        public const byte Trail = 1, Halo = 2;
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
        public float2 Recoil;      // velocity impulse from shooting / getting hit, decays quickly
    }

    /// <summary>The little gun the hero carries: aims at the target, kicks back and flashes on every shot.</summary>
    public struct PlayerGun : IComponentData
    {
        public float2 Dir;
        public float Kick;          // 1 right after a shot, decays to 0
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
        public float Recoil;        // hero push-back speed per volley
        public float Kick;          // camera kick per volley
        public float Shake;         // screen vibration per volley
        public byte Shell;          // 0 none, 1 small casing, 2 big casing
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
        public const byte SingleTick = 1, ConsumeBullets = 2, Freeze = 4, Heal = 8, Grow = 16, Aura = 32, ConsumeEnemies = 64,
            Cone = 128; // only hits enemies within ConeCos of Dir (melee)
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
        public byte Vfx;            // ZoneVfx: effect spawned by CombatSystem when the zone goes off
        public byte VfxState;       // 0 = nothing yet, 1 = pre-effect running (meteor falling), 2 = done
        public byte VfxFlip;        // mirror the effect (melee swings alternate direction)
        public float2 Dir;          // ZoneFlags.Cone: facing
        public float ConeCos;       // ZoneFlags.Cone: cos(half angle)
    }

    public static class ZoneVfx
    {
        public const byte None = 0, Explosion = 1, Meteor = 2, Frost = 3, Devour = 4, Mend = 5, Aegis = 6, Slash = 7;
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

    /// <summary>Behaviour flags for <see cref="Particle"/>.</summary>
    public static class PFlags
    {
        public const ushort
            Shrink = 1,         // scale -> 0 over the whole life
            ShrinkLate = 2,     // keep full size, shrink during the last 0.4 s
            Streak = 4,         // face the velocity and stretch with speed (sparks)
            PopIn = 8,          // grow from 0 with a little overshoot when it starts moving
            PopOut = 16,        // burst into cubes of its colour when it dies (corpses, ice spikes)
            Grow = 32,          // scale eases from 1x to GrowTo x over the life (shockwaves, mist)
            Fade = 64,          // alpha fades out with life (additive meshes)
            Land = 128,         // stops dead when it lands (droplets, embers)
            Hidden = 256,       // invisible while frozen (delayed spawns)
            Clink = 512,        // plays the shell "ping" when it bounces hard
            Ice = 1024;         // plays the shatter sound when it pops
    }

    /// <summary>
    /// Every short-lived visual: sparks, debris, shells, splats, flashes, shockwaves, thrown corpses, ice spikes.
    /// Height lives in Visual.Y; Vel is the ground-plane velocity, VelY the vertical one.
    /// </summary>
    public struct Particle : IComponentData
    {
        public float Life;
        public float MaxLife;
        public float Drag;
        public float3 BaseScale;
        public float VelY;
        public float Gravity;
        public float Bounce;        // fraction of vertical speed kept when it hits the floor
        public float Freeze;        // seconds held still (white) before moving: the kill "beat" or a spawn delay
        public float Age;           // seconds since it started moving
        public float GrowTo;
        public float Alpha0;
        public ushort Flags;        // PFlags
    }

    /// <summary>Tag for static arena props (ground stones, fires, walls).</summary>
    public struct Decor : IComponentData { }
}
