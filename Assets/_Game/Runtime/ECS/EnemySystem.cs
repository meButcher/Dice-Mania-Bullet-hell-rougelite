using DiceMania.Data;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace DiceMania.ECS
{
    /// <summary>Enemy steering behaviours, bullet patterns and boss phase cycling.</summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(WeaponSystem))]
    [BurstCompile]
    public partial struct EnemySystem : ISystem
    {
        Random rng;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GameClock>();
            state.RequireForUpdate<PlayerTag>();
            state.RequireForUpdate<Prefabs>();
            rng = Random.CreateFromIndex(4242);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var clock = SystemAPI.GetSingleton<GameClock>();
            if (clock.Running == 0) return;
            float dt = SystemAPI.Time.DeltaTime;
            float time = clock.Time;
            var pf = SystemAPI.GetSingleton<Prefabs>();
            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
            var requests = SystemAPI.GetSingletonBuffer<SpawnRequest>();
            var sfx = SystemAPI.GetSingletonBuffer<SfxEvent>();

            var player = SystemAPI.GetSingletonEntity<PlayerTag>();
            float2 ppos = SystemAPI.GetComponent<Pos>(player).Value;
            var copy = SystemAPI.GetComponent<CopyForm>(player);
            int disguisedAs = copy.Time > 0f ? copy.EnemyDef : -1;
            float lim = clock.ArenaHalf - 0.5f;
            bool anyShot = false;
            var noPlayerTpl = new Projectile();

            // ---------------- Bosses: cycle phases (writes the active pattern into Shooter) ----------------
            foreach (var (boss, gun, hp, phases, pos) in SystemAPI.Query<RefRW<BossState>, RefRW<Shooter>, RefRO<Health>, DynamicBuffer<BossPhase>, RefRO<Pos>>())
            {
                if (phases.Length == 0) continue;
                ref var b = ref boss.ValueRW;
                if (b.Enraged == 0 && hp.ValueRO.Value < hp.ValueRO.Max * 0.5f) b.Enraged = 1;
                b.Timer -= dt * (b.Enraged != 0 ? 1.3f : 1f);
                if (b.Timer <= 0f)
                {
                    b.Index = (b.Index + 1) % phases.Length;
                    var ph = phases[b.Index];
                    b.Timer = ph.Duration;
                    var g = ph.Gun;
                    if (b.Enraged != 0) g.Cooldown *= 0.7f;
                    g.Timer = 0.6f; // telegraph pause between phases
                    gun.ValueRW = g;
                }
                // Summon patterns: queue spawn requests for the GameDirector.
                ref var s = ref gun.ValueRW;
                if (s.Pattern == (byte)PatternType.Summon)
                {
                    s.Timer -= dt;
                    if (s.Timer <= 0f)
                    {
                        s.Timer = s.Cooldown;
                        requests.Add(new SpawnRequest { Def = s.SummonDef, Pos = pos.ValueRO.Value, Count = s.Count });
                    }
                }
            }

            // ---------------- All enemies: move + shoot ----------------
            foreach (var (enemy, pos, gun, hp, vis) in SystemAPI.Query<RefRW<Enemy>, RefRW<Pos>, RefRW<Shooter>, RefRO<Health>, RefRW<Visual>>())
            {
                if (hp.ValueRO.Value <= 0f) continue;
                ref var e = ref enemy.ValueRW;
                ref var v = ref vis.ValueRW;

                e.Push *= math.exp(-6f * dt);
                if (e.FreezeTimer > 0f)
                {
                    e.FreezeTimer -= dt;
                    v.Color = new float4(0.55f, 0.85f, 1f, 0.6f);
                    pos.ValueRW.Value = math.clamp(pos.ValueRO.Value + e.Push * dt, -lim, lim);
                    continue;
                }
                v.Color = e.BaseColor;

                float2 p = pos.ValueRO.Value;
                float2 to = ppos - p;
                float dist = math.length(to);
                float2 dir = dist > 1e-4f ? to / dist : new float2(1, 0);
                float2 desired = float2.zero;
                e.StateTimer += dt;

                switch ((EnemyBehaviour)e.Behaviour)
                {
                    case EnemyBehaviour.Chaser:
                        desired = dir * e.Speed;
                        break;
                    case EnemyBehaviour.Keeper:
                    {
                        float2 side = new float2(-dir.y, dir.x) * (math.sin(e.Seed + time * 0.7f) > 0 ? 1f : -1f);
                        if (dist > e.PreferredRange + 1f) desired = dir * e.Speed;
                        else if (dist < e.PreferredRange - 1f) desired = -dir * e.Speed * 0.8f + side * e.Speed * 0.3f;
                        else desired = side * e.Speed * 0.6f;
                        break;
                    }
                    case EnemyBehaviour.Turret:
                        desired = dist > e.PreferredRange ? dir * e.Speed : float2.zero;
                        break;
                    case EnemyBehaviour.Rusher:
                    {
                        // Chase, and every ~3s dash at 3x speed for 0.5s.
                        float cycle = math.fmod(e.StateTimer + e.Seed, 3f);
                        bool dashing = cycle > 2.5f;
                        desired = dir * e.Speed * (dashing ? 3f : 1f);
                        if (dashing) v.Color = new float4(1f, 0.9f, 0.4f, 1f);
                        break;
                    }
                    case EnemyBehaviour.Swarm:
                    {
                        float2 side = new float2(-dir.y, dir.x);
                        desired = (dir + side * math.sin(time * 4f + e.Seed) * 0.6f) * e.Speed;
                        break;
                    }
                    case EnemyBehaviour.Boss:
                    {
                        float2 side = new float2(-dir.y, dir.x);
                        if (dist > e.PreferredRange) desired = dir * e.Speed;
                        else desired = side * e.Speed * 0.5f;
                        break;
                    }
                }

                pos.ValueRW.Value = math.clamp(p + (desired + e.Push) * dt, -lim, lim);
                v.RotY = -math.atan2(dir.y, dir.x);

                // Shooting (enemies of the type the player copied are fooled and hold fire).
                ref var g = ref gun.ValueRW;
                if (g.Pattern == (byte)PatternType.None || g.Pattern == (byte)PatternType.Summon) continue;
                bool canFire = dist <= g.Range && e.Def != disguisedAs;
                if (SpawnUtil.TickShooter(ref g, dt, canFire))
                {
                    float4 bc = e.Boss != 0 ? new float4(1f, 0.35f, 0.85f, 1f) : new float4(1f, 0.2f, 0.25f, 1f);
                    float size = e.Boss != 0 ? 0.42f : 0.3f;
                    SpawnUtil.FireVolley(ecb, pf, ref g, pos.ValueRO.Value, dir, false, clock.EnemyBulletLife, bc, size, ref rng, noPlayerTpl);
                    anyShot = true;
                }
            }
            if (anyShot) sfx.Add(new SfxEvent { Id = Sfx.EnemyShoot });
        }
    }
}
