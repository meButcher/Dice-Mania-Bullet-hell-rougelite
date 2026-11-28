using DiceMania.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace DiceMania.ECS
{
    /// <summary>
    /// All collision + damage resolution, using a per-frame spatial hash of enemies:
    /// enemy separation, player projectiles, damage zones, enemy bullets, contact damage,
    /// pickups, deaths and drops. Also emits the hit / kill feel (flash, squash, sparks, thrown corpses, splats,
    /// hit-stop and camera requests). Runs single-threaded under Burst (Web builds are single-threaded anyway).
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(MotionSystem))]
    [BurstCompile]
    public partial struct CombatSystem : ISystem
    {
        const float Cell = 2.5f;
        EntityQuery enemyQ, shotQ, zoneQ;
        Random rng;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GameClock>();
            state.RequireForUpdate<PlayerTag>();
            state.RequireForUpdate<Prefabs>();
            state.RequireForUpdate<Feel>();
            enemyQ = SystemAPI.QueryBuilder().WithAllRW<Enemy, Pos>().WithAllRW<Health, Visual>().WithAll<Radius>().Build();
            shotQ = SystemAPI.QueryBuilder().WithAllRW<Projectile>().WithAll<Pos, Radius, Vel>().Build();
            zoneQ = SystemAPI.QueryBuilder().WithAllRW<DamageZone, Pos>().WithAllRW<Visual>().Build();
            rng = Random.CreateFromIndex(9001);
        }

        static int Key(int x, int y) => (x * 73856093) ^ (y * 19349663);
        static int2 CellOf(float2 p) => (int2)math.floor(p / Cell);

        static bool TryHurt(ref PlayerData pd, ref Health hp, in Buffs buffs, float armor, float raw, float invulnTime, in Feel feel,
            ref GameEvents ev, DynamicBuffer<DamageNumber> nums, DynamicBuffer<SfxEvent> sfx, float2 ppos, float2 from)
        {
            if (pd.InvulnTimer > 0f || buffs.Invuln > 0f || hp.Value <= 0f || raw <= 0f) return false;
            float d = math.max(1f, raw - armor);
            hp.Value -= d;
            pd.InvulnTimer = invulnTime;
            pd.Recoil += SpawnUtil.SafeNormalize(ppos - from, new float2(0f, -1f)) * 9f; // shoved away from the hit
            ev.DamageTaken += d;
            ev.ShakeRequest = math.max(ev.ShakeRequest, 0.4f);
            ev.HitStop = math.max(ev.HitStop, feel.HitStopHurt);
            ev.HitStopForce = 1; // getting hit must always be felt (the invulnerability window already limits how often)
            ev.Hurt = 1f;
            ev.Rumble = math.max(ev.Rumble, 0.8f);
            nums.Add(new DamageNumber { Pos = ppos, Amount = d, Kind = NumKind.PlayerHurt });
            sfx.Add(new SfxEvent { Id = Sfx.Hurt });
            if (hp.Value <= 0f) { hp.Value = 0f; ev.PlayerDied = 1; }
            return true;
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var clock = SystemAPI.GetSingleton<GameClock>();
            if (clock.Running == 0) return;
            float dt = SystemAPI.Time.DeltaTime;
            var stats = SystemAPI.GetSingleton<PlayerStats>();
            var buffs = SystemAPI.GetSingleton<Buffs>();
            var feel = SystemAPI.GetSingleton<Feel>();
            var pf = SystemAPI.GetSingleton<Prefabs>();
            var evRW = SystemAPI.GetSingletonRW<GameEvents>();
            var ev = evRW.ValueRO;
            var nums = SystemAPI.GetSingletonBuffer<DamageNumber>();
            var sfx = SystemAPI.GetSingletonBuffer<SfxEvent>();
            var requests = SystemAPI.GetSingletonBuffer<SpawnRequest>();
            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

            var player = SystemAPI.GetSingletonEntity<PlayerTag>();
            float2 ppos = SystemAPI.GetComponent<Pos>(player).Value;
            var pdata = SystemAPI.GetComponent<PlayerData>(player);
            var php = SystemAPI.GetComponent<Health>(player);
            var copy = SystemAPI.GetComponent<CopyForm>(player);
            bool copied = copy.Time > 0f;
            float pr = copied ? copy.Size * 0.5f : clock.PlayerRadius;
            float armor = stats.Armor + (copied ? copy.Armor : 0f);
            float heal = 0f;
            int numBudget = 28;
            bool hurtThisFrame = false;
            // Per-frame VFX budgets keep hordes readable and cheap.
            int sparkBudget = 30, corpseBudget = 14, splatBudget = 10, fxBudget = 16;

            // ---------------- Gather enemies & build grid ----------------
            var eEnt = enemyQ.ToEntityArray(Allocator.Temp);
            var eEnemy = enemyQ.ToComponentDataArray<Enemy>(Allocator.Temp);
            var ePos = enemyQ.ToComponentDataArray<Pos>(Allocator.Temp);
            var eHp = enemyQ.ToComponentDataArray<Health>(Allocator.Temp);
            var eVis = enemyQ.ToComponentDataArray<Visual>(Allocator.Temp);
            var eRad = enemyQ.ToComponentDataArray<Radius>(Allocator.Temp);
            int n = eEnt.Length;
            var hpCur = new NativeArray<float>(n, Allocator.Temp);
            var dmgAcc = new NativeArray<float>(n, Allocator.Temp);
            var knock = new NativeArray<float2>(n, Allocator.Temp);
            var freeze = new NativeArray<float>(n, Allocator.Temp);
            var crit = new NativeArray<byte>(n, Allocator.Temp);
            var consumed = new NativeArray<byte>(n, Allocator.Temp);
            var grid = new NativeParallelMultiHashMap<int, int>(math.max(16, n), Allocator.Temp);
            float maxER = 0.5f;
            for (int i = 0; i < n; i++)
            {
                hpCur[i] = eHp[i].Value;
                if (eHp[i].Value <= 0f) continue;
                int2 c = CellOf(ePos[i].Value);
                grid.Add(Key(c.x, c.y), i);
                maxER = math.max(maxER, eRad[i].Value);
            }

            // ---------------- Separation ----------------
            var disp = new NativeArray<float2>(n, Allocator.Temp);
            for (int i = 0; i < n; i++)
            {
                if (hpCur[i] <= 0f || eEnemy[i].Boss != 0) continue;
                int2 c = CellOf(ePos[i].Value);
                int checks = 0;
                for (int dy = -1; dy <= 1 && checks < 10; dy++)
                for (int dx = -1; dx <= 1 && checks < 10; dx++)
                {
                    if (!grid.TryGetFirstValue(Key(c.x + dx, c.y + dy), out int j, out var it)) continue;
                    do
                    {
                        if (j == i) continue;
                        float2 d = ePos[i].Value - ePos[j].Value;
                        float minD = eRad[i].Value + eRad[j].Value;
                        float d2 = math.lengthsq(d);
                        if (d2 < minD * minD)
                        {
                            float dist = math.sqrt(d2);
                            float2 dir = dist > 1e-4f ? d / dist : SpawnUtil.Dir(eEnemy[i].Seed * 57f);
                            float w = eEnemy[j].Boss != 0 ? 1f : 0.5f;
                            disp[i] += dir * (minD - dist) * w;
                        }
                        checks++;
                    } while (checks < 10 && grid.TryGetNextValue(out j, ref it));
                }
            }
            float lim = clock.ArenaHalf - 0.5f;
            for (int i = 0; i < n; i++)
                if (math.lengthsq(disp[i]) > 0f)
                    ePos[i] = new Pos { Value = math.clamp(ePos[i].Value + disp[i] * 0.6f, -lim, lim) };

            int reach = (int)math.ceil((0.6f + maxER) / Cell);

            // ---------------- Player projectiles ----------------
            var sEnt = shotQ.ToEntityArray(Allocator.Temp);
            var sProj = shotQ.ToComponentDataArray<Projectile>(Allocator.Temp);
            var sPos = shotQ.ToComponentDataArray<Pos>(Allocator.Temp);
            var sRad = shotQ.ToComponentDataArray<Radius>(Allocator.Temp);
            var sVel = shotQ.ToComponentDataArray<Vel>(Allocator.Temp);
            bool anyHit = false, anyCrit = false;
            for (int s = 0; s < sEnt.Length; s++)
            {
                var p = sProj[s];
                if (p.Life <= 0f) continue;
                float2 sp = sPos[s].Value;
                int2 c = CellOf(sp);
                int hit = -1;
                int sreach = math.max(reach, (int)math.ceil((sRad[s].Value + maxER) / Cell));
                for (int dy = -sreach; dy <= sreach && hit < 0; dy++)
                for (int dx = -sreach; dx <= sreach && hit < 0; dx++)
                {
                    if (!grid.TryGetFirstValue(Key(c.x + dx, c.y + dy), out int j, out var it)) continue;
                    do
                    {
                        if (hpCur[j] <= 0f) continue;
                        if (p.Kind == ProjKind.Copy && eEnt[j] != p.Target) continue;
                        if (eEnt[j] == p.LastHit && p.HitCooldown > 0f) continue;
                        float rr = sRad[s].Value + eRad[j].Value;
                        if (math.distancesq(sp, ePos[j].Value) < rr * rr) { hit = j; break; }
                    } while (grid.TryGetNextValue(out j, ref it));
                }
                if (hit < 0) continue;
                anyHit = true;
                float2 away = SpawnUtil.SafeNormalize(ePos[hit].Value - ppos, new float2(1f, 0f));
                float2 bdir = SpawnUtil.SafeNormalize(sVel[s].Value, away);
                float2 hitPoint = ePos[hit].Value - bdir * eRad[hit].Value * 0.8f;

                if (p.Kind == ProjKind.Copy)
                {
                    consumed[hit] = 1;
                    hpCur[hit] = 0f;
                    p.Life = 0f;
                    ecb.DestroyEntity(sEnt[s]);
                    sProj[s] = p;
                    continue;
                }
                if (p.Kind == ProjKind.Explode)
                {
                    MotionSystem.SpawnExplosion(ecb, pf.Zone, sp, p.Damage, p.Area * stats.Area, p.Knockback);
                    p.Life = 0f;
                    ecb.DestroyEntity(sEnt[s]);
                    sProj[s] = p;
                    continue;
                }

                float dmg = p.Damage;
                if (rng.NextFloat() < stats.Luck) { dmg *= clock.CritMult; crit[hit] = 1; anyCrit = true; }
                dmgAcc[hit] += dmg;
                hpCur[hit] -= dmg;
                // Bullets get a slight shove (they should cut through, not bounce enemies like tennis balls).
                float2 shove = p.Kind == ProjKind.Orbit ? away : bdir;
                knock[hit] += shove * p.Knockback * 4f;
                if (sparkBudget > 0)
                {
                    sparkBudget -= 2;
                    Fx.Impact(ecb, pf.Particle, hitPoint, shove, math.lerp(eEnemy[hit].BaseColor, new float4(1f), 0.5f), 2, ref rng);
                }

                if (p.Kind == ProjKind.Orbit)
                {
                    p.LastHit = eEnt[hit];
                    p.HitCooldown = 0.3f;
                }
                else
                {
                    p.Pierce--;
                    p.LastHit = eEnt[hit];
                    p.HitCooldown = 99f;
                    if (p.Pierce < 0)
                    {
                        p.Life = 0f;
                        ecb.DestroyEntity(sEnt[s]);
                    }
                }
                sProj[s] = p;
            }
            shotQ.CopyFromComponentDataArray(sProj);
            if (anyHit) sfx.Add(new SfxEvent { Id = Sfx.Hit });
            if (anyCrit) sfx.Add(new SfxEvent { Id = Sfx.Crit });

            // ---------------- Damage zones ----------------
            var zEnt = zoneQ.ToEntityArray(Allocator.Temp);
            var zZone = zoneQ.ToComponentDataArray<DamageZone>(Allocator.Temp);
            var zPos = zoneQ.ToComponentDataArray<Pos>(Allocator.Temp);
            var zVis = zoneQ.ToComponentDataArray<Visual>(Allocator.Temp);
            var eaters = new NativeList<float3>(4, Allocator.Temp);
            for (int z = 0; z < zEnt.Length; z++)
            {
                var zo = zZone[z];
                var vi = zVis[z];
                if (zo.Follow != 0) zPos[z] = new Pos { Value = ppos };
                float2 zp = zPos[z].Value;

                if (zo.Delay > 0f)
                {
                    if (zo.Vfx == ZoneVfx.Meteor && zo.VfxState == 0)
                    {
                        Fx.FallingRock(ecb, pf.Particle, zp, zo.Delay, 1.1f, new float4(1f, 0.45f, 0.15f, 0.8f));
                        zo.VfxState = 1;
                    }
                    zo.Delay -= dt;
                    vi.Mesh = (byte)ShapeId.Ring;
                    vi.Scale = new float3(zo.Radius * 2f, 1f, zo.Radius * 2f);
                    vi.Color = new float4(zo.Color.xyz, 0.4f + 0.3f * math.sin(zo.Delay * 30f));
                    zZone[z] = zo; zVis[z] = vi;
                    continue;
                }

                // The moment a zone goes off: its effect.
                if (zo.Vfx != ZoneVfx.None && zo.VfxState < 2)
                {
                    zo.VfxState = 2;
                    switch (zo.Vfx)
                    {
                        case ZoneVfx.Explosion:
                            Fx.Explosion(ecb, pf.Particle, zp, zo.Radius, zo.Color, feel.SplatLife * 0.6f, false, ref rng);
                            ev.ShakeRequest = math.max(ev.ShakeRequest, 0.28f);
                            ev.HitStop = math.max(ev.HitStop, 0.03f);
                            ev.Rumble = math.max(ev.Rumble, 0.5f);
                            sfx.Add(new SfxEvent { Id = Sfx.Explode });
                            break;
                        case ZoneVfx.Meteor:
                            Fx.Explosion(ecb, pf.Particle, zp, zo.Radius, zo.Color, feel.SplatLife, true, ref rng);
                            Fx.Debris(ecb, pf.Particle, zp, 0.6f, 6, new float4(0.35f, 0.2f, 0.15f, 0.3f), 9f, 0.35f, 1.1f, ref rng);
                            ev.ShakeRequest = math.max(ev.ShakeRequest, 0.45f);
                            ev.HitStop = math.max(ev.HitStop, 0.05f);
                            ev.Rumble = math.max(ev.Rumble, 0.8f);
                            sfx.Add(new SfxEvent { Id = Sfx.Explode });
                            break;
                        case ZoneVfx.Frost:
                            Fx.FrostNova(ecb, pf.Particle, zp, zo.Radius, ref rng);
                            ev.ShakeRequest = math.max(ev.ShakeRequest, 0.3f);
                            ev.Punch = math.max(ev.Punch, 0.08f);
                            sfx.Add(new SfxEvent { Id = Sfx.Shatter });
                            break;
                        case ZoneVfx.Devour:
                            Fx.Glow(ecb, pf.Particle, zp, 0.6f, zo.MaxRadius * 1.6f, new float4(zo.Color.xyz, 0.9f), 0.25f);
                            Fx.Shockwave(ecb, pf.Particle, zp, 0.15f, 0.5f, zo.MaxRadius * 1.2f, new float4(zo.Color.xyz, 1f), 0.4f);
                            ev.Punch = math.max(ev.Punch, 0.1f);
                            break;
                        case ZoneVfx.Mend:
                            Fx.Shockwave(ecb, pf.Particle, zp, 0.12f, 0.4f, zo.Radius, new float4(zo.Color.xyz, 0.9f), 0.35f);
                            Fx.Sparkles(ecb, pf.Particle, zp, 12, 1.2f, new float4(zo.Color.xyz, 1f), ref rng);
                            break;
                        case ZoneVfx.Aegis:
                            Fx.Shockwave(ecb, pf.Particle, zp, 0.3f, 0.4f, zo.Radius * 1.4f, new float4(1f, 0.85f, 0.4f, 1f), 0.3f);
                            Fx.Sparkles(ecb, pf.Particle, zp, 10, 1f, new float4(1f, 0.85f, 0.4f, 1f), ref rng);
                            break;
                        case ZoneVfx.Slash:
                            Fx.Slash(ecb, pf.Particle, zp, zo.Dir, zo.Radius, new float4(zo.Color.xyz, 1f), zo.VfxFlip != 0, ref rng);
                            break;
                    }
                }
                if ((zo.Flags & ZoneFlags.Grow) != 0) zo.Radius = math.min(zo.MaxRadius, zo.Radius + zo.GrowRate * dt);

                zo.Timer -= dt;
                if (zo.Timer <= 0f)
                {
                    zo.Timer = zo.Interval;
                    if ((zo.Flags & ZoneFlags.Heal) != 0)
                    {
                        if (math.distance(ppos, zp) <= zo.Radius + pr)
                        {
                            heal += zo.Damage;
                            if (fxBudget-- > 0) Fx.Sparkles(ecb, pf.Particle, ppos, 3, 0.7f, new float4(zo.Color.xyz, 1f), ref rng);
                        }
                    }
                    else if (zo.Damage > 0f || (zo.Flags & ZoneFlags.Freeze) != 0)
                    {
                        if ((zo.Flags & ZoneFlags.ConsumeEnemies) != 0 && fxBudget-- > 0)
                            Fx.Converge(ecb, pf.Particle, zp, zo.Radius, 6, 0.25f, new float4(zo.Color.xyz, 0.9f), ref rng);
                        bool single = (zo.Flags & ZoneFlags.SingleTick) != 0;
                        bool cone = (zo.Flags & ZoneFlags.Cone) != 0;
                        int zoneHits = 0;
                        int2 c = CellOf(zp);
                        int zr = (int)math.ceil((zo.Radius + maxER) / Cell);
                        for (int dy = -zr; dy <= zr; dy++)
                        for (int dx = -zr; dx <= zr; dx++)
                        {
                            if (!grid.TryGetFirstValue(Key(c.x + dx, c.y + dy), out int j, out var it)) continue;
                            do
                            {
                                if (hpCur[j] <= 0f) continue;
                                float rr = zo.Radius + eRad[j].Value;
                                float2 d = ePos[j].Value - zp;
                                if (math.lengthsq(d) >= rr * rr) continue;
                                if (cone)
                                {
                                    // Melee arc: only what's in front (enemies hugging the hero always get hit).
                                    float dl = math.length(d);
                                    if (dl > eRad[j].Value + 0.3f && math.dot(d / dl, zo.Dir) < zo.ConeCos) continue;
                                }
                                if (zo.Damage > 0f)
                                {
                                    dmgAcc[j] += zo.Damage;
                                    hpCur[j] -= zo.Damage;
                                    heal += zo.Damage * zo.Lifesteal;
                                }
                                float2 nd = SpawnUtil.SafeNormalize(d, cone ? zo.Dir : new float2(1, 0));
                                if (zo.Knockback > 0f) knock[j] += nd * zo.Knockback;
                                if ((zo.Flags & ZoneFlags.Freeze) != 0) freeze[j] = math.max(freeze[j], zo.FreezeTime);
                                zoneHits++;
                                // One-shot zones (melee, explosions, meteors) spark on everything they hit.
                                if (single && zo.Damage > 0f && sparkBudget > 0)
                                {
                                    sparkBudget -= 2;
                                    Fx.Impact(ecb, pf.Particle, ePos[j].Value - nd * eRad[j].Value * 0.6f, nd,
                                        math.lerp(new float4(zo.Color.xyz, 1f), new float4(1f), 0.4f), 2, ref rng);
                                }
                            } while (grid.TryGetNextValue(out j, ref it));
                        }
                        if (cone && zoneHits > 0)
                        {
                            // A connecting swing: a few frozen frames, a jolt, a thud.
                            ev.HitStop = math.max(ev.HitStop, 0.045f);
                            ev.ShakeRequest = math.max(ev.ShakeRequest, 0.2f);
                            ev.Rumble = math.max(ev.Rumble, 0.4f);
                            sfx.Add(new SfxEvent { Id = Sfx.Hit });
                        }
                    }
                    if ((zo.Flags & ZoneFlags.SingleTick) != 0)
                    {
                        zo.Damage = 0f;
                        zo.Flags = (byte)(zo.Flags & ~ZoneFlags.Freeze);
                        zo.Interval = 1e6f;
                        zo.Timer = 1e6f;
                    }
                }
                if ((zo.Flags & ZoneFlags.ConsumeBullets) != 0) eaters.Add(new float3(zp, zo.Radius));

                float alpha = zo.Color.w;
                if (zo.Life >= 0f)
                {
                    zo.Life -= dt;
                    alpha *= math.saturate(zo.Life / 0.3f);
                    if (zo.Life <= 0f) ecb.DestroyEntity(zEnt[z]);
                }
                vi.Mesh = (zo.Flags & ZoneFlags.Aura) != 0 ? (byte)ShapeId.Ring : (byte)ShapeId.Disc;
                vi.Scale = new float3(zo.Radius * 2f, 1f, zo.Radius * 2f);
                vi.Color = new float4(zo.Color.xyz, alpha);
                zZone[z] = zo; zVis[z] = vi;
            }
            zoneQ.CopyFromComponentDataArray(zZone);
            zoneQ.CopyFromComponentDataArray(zPos);
            zoneQ.CopyFromComponentDataArray(zVis);

            // ---------------- Enemy bullets ----------------
            int eatenFx = 6;
            foreach (var (shot, sp, e) in SystemAPI.Query<RefRO<EnemyShot>, RefRO<Pos>>().WithEntityAccess())
            {
                if (shot.ValueRO.Life <= 0f) continue;
                float2 bp = sp.ValueRO.Value;
                bool eaten = false;
                for (int k = 0; k < eaters.Length; k++)
                    if (math.distancesq(bp, eaters[k].xy) < eaters[k].z * eaters[k].z) { eaten = true; break; }
                if (eaten)
                {
                    if (eatenFx-- > 0) Fx.Glow(ecb, pf.Particle, bp, 0.6f, 0.8f, new float4(1f, 0.4f, 0.9f, 0.8f), 0.08f);
                    ecb.DestroyEntity(e);
                    continue;
                }
                float rr = shot.ValueRO.Radius + pr;
                if (math.distancesq(bp, ppos) < rr * rr)
                {
                    if (TryHurt(ref pdata, ref php, buffs, armor, shot.ValueRO.Damage, clock.InvulnTime, feel, ref ev, nums, sfx, ppos, bp))
                        hurtThisFrame = true;
                    ecb.DestroyEntity(e);
                }
            }

            // ---------------- Contact damage (+ thorns, + copy body damage) ----------------
            {
                int2 c = CellOf(ppos);
                int cr = (int)math.ceil((pr + maxER) / Cell);
                for (int dy = -cr; dy <= cr; dy++)
                for (int dx = -cr; dx <= cr; dx++)
                {
                    if (!grid.TryGetFirstValue(Key(c.x + dx, c.y + dy), out int j, out var it)) continue;
                    do
                    {
                        if (hpCur[j] <= 0f) continue;
                        float rr = pr + eRad[j].Value;
                        if (math.distancesq(ppos, ePos[j].Value) >= rr * rr) continue;
                        bool enemyFrozen = eEnemy[j].FreezeTimer > 0f;
                        if (!enemyFrozen && TryHurt(ref pdata, ref php, buffs, armor, eEnemy[j].ContactDamage, clock.InvulnTime, feel, ref ev, nums, sfx, ppos, ePos[j].Value))
                        {
                            hurtThisFrame = true;
                            if (stats.Thorns > 0f) { dmgAcc[j] += stats.Thorns; hpCur[j] -= stats.Thorns; }
                        }
                        if (copied && copy.ContactDps > 0f)
                        {
                            float cd = copy.ContactDps * stats.Might * dt;
                            dmgAcc[j] += cd; hpCur[j] -= cd;
                            knock[j] += SpawnUtil.SafeNormalize(ePos[j].Value - ppos, new float2(1, 0)) * 2f;
                        }
                    } while (grid.TryGetNextValue(out j, ref it));
                }
            }

            // ---------------- Pickups ----------------
            bool picked = false, chest = false;
            foreach (var (pk, pp, e) in SystemAPI.Query<RefRO<Pickup>, RefRO<Pos>>().WithEntityAccess())
            {
                if (math.distancesq(pp.ValueRO.Value, ppos) > (pr + 0.7f) * (pr + 0.7f)) continue;
                switch (pk.ValueRO.Kind)
                {
                    case PickupKind.Xp: ev.Xp += pk.ValueRO.Value; break;
                    case PickupKind.Gold: ev.Gold += (int)math.round(pk.ValueRO.Value); break;
                    case PickupKind.Heal: heal += pk.ValueRO.Value; break;
                    case PickupKind.Chest:
                        ev.Chests++;
                        ev.Gold += (int)clock.ChestGold;
                        chest = true;
                        // Stars that pop + light.
                        Fx.Stars(ecb, pf.Particle, pp.ValueRO.Value, 9, ref rng);
                        Fx.Glow(ecb, pf.Particle, pp.ValueRO.Value, 0.8f, 4f, new float4(1f, 0.85f, 0.4f, 1f), 0.3f);
                        Fx.Shockwave(ecb, pf.Particle, pp.ValueRO.Value, 0.2f, 0.5f, 3.5f, new float4(1f, 0.85f, 0.4f, 1f), 0.35f);
                        ev.Punch = math.max(ev.Punch, 0.12f);
                        break;
                }
                picked = true;
                ecb.DestroyEntity(e);
            }
            if (picked) sfx.Add(new SfxEvent { Id = Sfx.Pickup });
            if (chest) sfx.Add(new SfxEvent { Id = Sfx.Chest });

            if (heal > 0f && php.Value > 0f)
            {
                float before = php.Value;
                php.Value = math.min(php.Max, php.Value + heal);
                float gained = php.Value - before;
                ev.Healed += gained;
                if (gained >= 1f) nums.Add(new DamageNumber { Pos = ppos + new float2(0, 1), Amount = gained, Kind = NumKind.Heal });
            }

            // ---------------- Apply enemy damage, deaths, drops ----------------
            bool killed = false, bigKill = false;
            for (int j = 0; j < n; j++)
            {
                var en = eEnemy[j];
                if (consumed[j] != 0)
                {
                    var hpc = eHp[j]; hpc.Value = 0f; eHp[j] = hpc;
                    ev.CopyHit = 1;
                    ev.CopyHitDef = en.Def;
                    ev.CopyHitElite = en.Elite;
                    ev.Kills++;
                    ev.Score += en.Score;
                    SpawnUtil.Pickup(ecb, pf.Pickup, ePos[j].Value, PickupKind.Xp, en.Xp, ref rng);
                    SpawnUtil.Burst(ecb, pf.Particle, ePos[j].Value, 16, en.BaseColor, 9f, 0.35f, 0.7f, ref rng);
                    Fx.Glow(ecb, pf.Particle, ePos[j].Value, 0.8f, 5f, new float4(1f, 0.35f, 0.5f, 1f), 0.25f);
                    Fx.Shockwave(ecb, pf.Particle, ePos[j].Value, 0.2f, 0.5f, 4f, new float4(1f, 0.35f, 0.5f, 1f), 0.35f);
                    ecb.DestroyEntity(eEnt[j]);
                    sfx.Add(new SfxEvent { Id = Sfx.Copy });
                    continue;
                }
                if (freeze[j] > 0f) en.FreezeTimer = math.max(en.FreezeTimer, freeze[j]);
                if (dmgAcc[j] > 0f)
                {
                    var h = eHp[j];
                    float before = h.Value;
                    h.Value -= dmgAcc[j];
                    eHp[j] = h;
                    bool dies = before > 0f && h.Value <= 0f;

                    // Flash white + squash. Bosses barely move and wobble less.
                    var vi = eVis[j];
                    vi.Flash = 1f;
                    float amp = math.clamp(0.35f + dmgAcc[j] / math.max(1f, h.Max) * 3f, 0.35f, 1f) * (en.Boss != 0 ? 0.35f : 1f);
                    if (vi.SquashT > 0.1f || amp > vi.SquashAmp * 1.2f) { vi.SquashAmp = amp; vi.SquashT = 0f; }
                    eVis[j] = vi;
                    en.Push += knock[j] * (en.Boss != 0 ? 0.05f : en.Elite != 0 ? 0.4f : 1f);

                    if (dies || (numBudget > 0 && dmgAcc[j] >= 1f))
                    {
                        numBudget--;
                        byte kind = dies ? (crit[j] != 0 ? NumKind.KillCrit : NumKind.Kill) : crit[j] != 0 ? NumKind.Crit : NumKind.Normal;
                        if (nums.Length < 60) nums.Add(new DamageNumber { Pos = ePos[j].Value, Amount = dmgAcc[j], Kind = kind });
                    }

                    if (dies)
                    {
                        killed = true;
                        ev.Kills++;
                        ev.Score += en.Score;
                        float2 dp = ePos[j].Value;
                        float kl = math.length(knock[j]);
                        float2 kd = kl > 1e-3f ? knock[j] / kl : SpawnUtil.SafeNormalize(dp - ppos, new float2(1f, 0f));
                        float4 col = en.BaseColor;
                        var splatCol = new float4(col.xyz * 0.42f, 0.2f);
                        if (en.Boss != 0)
                        {
                            ev.BossKills++;
                            ev.ShakeRequest = math.max(ev.ShakeRequest, 1.2f);
                            for (int k = 0; k < 12; k++) SpawnUtil.Pickup(ecb, pf.Pickup, dp + SpawnUtil.Dir(k * 30f) * 1.5f, PickupKind.Xp, math.max(1, en.Xp / 12), ref rng);
                            for (int k = 0; k < 6; k++) SpawnUtil.Pickup(ecb, pf.Pickup, dp, PickupKind.Gold, math.max(1, en.Gold / 6), ref rng);
                            SpawnUtil.Pickup(ecb, pf.Pickup, dp, PickupKind.Chest, 1, ref rng);
                            // Anime finish: long hang, white flash, shockwave, a huge mess.
                            Fx.Corpse(ecb, pf.Particle, dp, vi, kd, 5f, math.max(0.2f, feel.CorpseFreeze * 4f), ref rng);
                            Fx.Glow(ecb, pf.Particle, dp, 1f, 16f, new float4(1f, 1f, 1f, 1f), 0.35f);
                            Fx.Shockwave(ecb, pf.Particle, dp, 0.2f, 1f, 16f, new float4(col.xyz, 1f), 0.8f);
                            Fx.Sparks(ecb, pf.Particle, dp, 1f, kd, 360f, 30, 10f, 26f, 0.8f, new float4(col.xyz, 1f), 0.7f, ref rng);
                            SpawnUtil.Burst(ecb, pf.Particle, dp, 50, col, 16f, 0.7f, 1.4f, ref rng);
                            Fx.Splat(ecb, pf.Particle, dp, vi.Scale.x * 2.2f, splatCol, feel.SplatLife * 2f, ref rng);
                            Fx.Droplets(ecb, pf.Particle, dp, kd, 16, new float4(col.xyz * 0.6f, 0.25f), feel.SplatLife * 1.5f, ref rng);
                            ev.HitStop = math.max(ev.HitStop, feel.HitStopBoss);
                            ev.HitStopForce = 1;
                            ev.ScreenFlash = 1f;
                            ev.BurstLines = 1;
                            ev.Punch = math.max(ev.Punch, 0.35f);
                            ev.Rumble = 1f;
                            bigKill = true;
                        }
                        else
                        {
                            bool elite = en.Elite != 0;
                            SpawnUtil.Pickup(ecb, pf.Pickup, dp, PickupKind.Xp, en.Xp, ref rng);
                            if (buffs.GoldRush > 0f || rng.NextFloat() < en.GoldChance * (1f + stats.Greed))
                                SpawnUtil.Pickup(ecb, pf.Pickup, dp, PickupKind.Gold, math.max(1f, math.round(en.Gold * (1f + stats.Greed))), ref rng);
                            if (elite) SpawnUtil.Pickup(ecb, pf.Pickup, dp, PickupKind.Chest, 1, ref rng);
                            if (rng.NextFloat() < clock.HealPickupChance) SpawnUtil.Pickup(ecb, pf.Pickup, dp, PickupKind.Heal, clock.HealPickupAmount, ref rng);

                            // The kill: freeze, then throw the body. The last hit hits hardest.
                            if (corpseBudget-- > 0)
                            {
                                float throwSpeed = feel.CorpseThrow * (1f + math.min(1f, kl * 0.05f)) * (elite ? 0.75f : 1f);
                                Fx.Corpse(ecb, pf.Particle, dp, vi, kd, throwSpeed, feel.CorpseFreeze * (elite ? 1.7f : 1f), ref rng);
                            }
                            else SpawnUtil.Burst(ecb, pf.Particle, dp, elite ? 18 : 6, col, 7f, 0.22f * vi.Scale.x, 0.45f, ref rng);
                            if (splatBudget-- > 0)
                            {
                                Fx.Splat(ecb, pf.Particle, dp + kd * 0.6f, vi.Scale.x * rng.NextFloat(1.1f, 1.7f), splatCol, feel.SplatLife, ref rng);
                                Fx.Droplets(ecb, pf.Particle, dp, kd, elite ? 7 : 3, new float4(col.xyz * 0.6f, 0.25f), feel.SplatLife * 0.7f, ref rng);
                            }
                            if (sparkBudget > 0)
                            {
                                sparkBudget -= 4;
                                Fx.Sparks(ecb, pf.Particle, dp, 0.6f, kd, 140f, 4, 7f, 15f, 0.4f, new float4(math.lerp(col.xyz, new float3(1f), 0.4f), 1f), 0.22f, ref rng);
                            }
                            ev.HitStop = math.max(ev.HitStop, elite ? feel.HitStopElite : feel.HitStopKill);
                            if (elite)
                            {
                                Fx.Shockwave(ecb, pf.Particle, dp, 0.2f, 0.5f, 5f, new float4(col.xyz, 1f), 0.4f);
                                ev.ShakeRequest = math.max(ev.ShakeRequest, 0.3f);
                                ev.Punch = math.max(ev.Punch, 0.1f);
                                bigKill = true;
                            }
                        }
                        if (en.SplitDef >= 0 && en.SplitCount > 0)
                            requests.Add(new SpawnRequest { Def = en.SplitDef, Pos = dp, Count = en.SplitCount });
                        ecb.DestroyEntity(eEnt[j]);
                    }
                }
                eEnemy[j] = en;
            }
            if (killed) sfx.Add(new SfxEvent { Id = Sfx.Kill });
            if (bigKill) sfx.Add(new SfxEvent { Id = Sfx.BigKill });

            enemyQ.CopyFromComponentDataArray(eEnemy);
            enemyQ.CopyFromComponentDataArray(ePos);
            enemyQ.CopyFromComponentDataArray(eHp);
            enemyQ.CopyFromComponentDataArray(eVis);

            SystemAPI.SetComponent(player, pdata);
            SystemAPI.SetComponent(player, php);
            if (hurtThisFrame)
            {
                var pv = SystemAPI.GetComponent<Visual>(player);
                pv.Flash = 1f;
                pv.SquashAmp = 1f;
                pv.SquashT = 0f;
                SystemAPI.SetComponent(player, pv);
            }
            evRW.ValueRW = ev;
        }
    }
}
