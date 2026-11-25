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
    /// pickups, deaths and drops. Runs single-threaded under Burst (Web builds are single-threaded anyway).
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
            enemyQ = SystemAPI.QueryBuilder().WithAllRW<Enemy, Pos>().WithAllRW<Health, Visual>().WithAll<Radius>().Build();
            shotQ = SystemAPI.QueryBuilder().WithAllRW<Projectile>().WithAll<Pos, Radius>().Build();
            zoneQ = SystemAPI.QueryBuilder().WithAllRW<DamageZone, Pos>().WithAllRW<Visual>().Build();
            rng = Random.CreateFromIndex(9001);
        }

        static int Key(int x, int y) => (x * 73856093) ^ (y * 19349663);
        static int2 CellOf(float2 p) => (int2)math.floor(p / Cell);

        static bool TryHurt(ref PlayerData pd, ref Health hp, in Buffs buffs, float armor, float raw, float invulnTime,
            ref GameEvents ev, DynamicBuffer<DamageNumber> nums, DynamicBuffer<SfxEvent> sfx, float2 ppos)
        {
            if (pd.InvulnTimer > 0f || buffs.Invuln > 0f || hp.Value <= 0f || raw <= 0f) return false;
            float d = math.max(1f, raw - armor);
            hp.Value -= d;
            pd.InvulnTimer = invulnTime;
            ev.DamageTaken += d;
            ev.ShakeRequest = math.max(ev.ShakeRequest, 0.35f);
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
            bool anyHit = false;
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
                    sfx.Add(new SfxEvent { Id = Sfx.Explode });
                    p.Life = 0f;
                    ecb.DestroyEntity(sEnt[s]);
                    sProj[s] = p;
                    continue;
                }

                float dmg = p.Damage;
                if (rng.NextFloat() < stats.Luck) { dmg *= clock.CritMult; crit[hit] = 1; }
                dmgAcc[hit] += dmg;
                hpCur[hit] -= dmg;
                knock[hit] += SpawnUtil.SafeNormalize(ePos[hit].Value - sp, new float2(1, 0)) * p.Knockback * 4f;

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
                    zo.Delay -= dt;
                    vi.Mesh = (byte)ShapeId.Ring;
                    vi.Scale = new float3(zo.Radius * 2f, 1f, zo.Radius * 2f);
                    vi.Color = new float4(zo.Color.xyz, 0.4f + 0.3f * math.sin(zo.Delay * 30f));
                    zZone[z] = zo; zVis[z] = vi;
                    continue;
                }
                if ((zo.Flags & ZoneFlags.Grow) != 0) zo.Radius = math.min(zo.MaxRadius, zo.Radius + zo.GrowRate * dt);

                zo.Timer -= dt;
                if (zo.Timer <= 0f)
                {
                    zo.Timer = zo.Interval;
                    if ((zo.Flags & ZoneFlags.Heal) != 0)
                    {
                        if (math.distance(ppos, zp) <= zo.Radius + pr) heal += zo.Damage;
                    }
                    else if (zo.Damage > 0f || (zo.Flags & ZoneFlags.Freeze) != 0)
                    {
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
                                if (zo.Damage > 0f)
                                {
                                    dmgAcc[j] += zo.Damage;
                                    hpCur[j] -= zo.Damage;
                                    heal += zo.Damage * zo.Lifesteal;
                                }
                                if (zo.Knockback > 0f) knock[j] += SpawnUtil.SafeNormalize(d, new float2(1, 0)) * zo.Knockback;
                                if ((zo.Flags & ZoneFlags.Freeze) != 0) freeze[j] = math.max(freeze[j], zo.FreezeTime);
                            } while (grid.TryGetNextValue(out j, ref it));
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
            foreach (var (shot, sp, e) in SystemAPI.Query<RefRO<EnemyShot>, RefRO<Pos>>().WithEntityAccess())
            {
                if (shot.ValueRO.Life <= 0f) continue;
                float2 bp = sp.ValueRO.Value;
                bool eaten = false;
                for (int k = 0; k < eaters.Length; k++)
                    if (math.distancesq(bp, eaters[k].xy) < eaters[k].z * eaters[k].z) { eaten = true; break; }
                if (eaten) { ecb.DestroyEntity(e); continue; }
                float rr = shot.ValueRO.Radius + pr;
                if (math.distancesq(bp, ppos) < rr * rr)
                {
                    TryHurt(ref pdata, ref php, buffs, armor, shot.ValueRO.Damage, clock.InvulnTime, ref ev, nums, sfx, ppos);
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
                        if (!enemyFrozen && TryHurt(ref pdata, ref php, buffs, armor, eEnemy[j].ContactDamage, clock.InvulnTime, ref ev, nums, sfx, ppos))
                        {
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
            bool picked = false;
            foreach (var (pk, pp, e) in SystemAPI.Query<RefRO<Pickup>, RefRO<Pos>>().WithEntityAccess())
            {
                if (math.distancesq(pp.ValueRO.Value, ppos) > (pr + 0.7f) * (pr + 0.7f)) continue;
                switch (pk.ValueRO.Kind)
                {
                    case PickupKind.Xp: ev.Xp += pk.ValueRO.Value; break;
                    case PickupKind.Gold: ev.Gold += (int)math.round(pk.ValueRO.Value); break;
                    case PickupKind.Heal: heal += pk.ValueRO.Value; break;
                    case PickupKind.Chest: ev.Chests++; ev.Gold += (int)clock.ChestGold; break;
                }
                picked = true;
                ecb.DestroyEntity(e);
            }
            if (picked) sfx.Add(new SfxEvent { Id = Sfx.Pickup });

            if (heal > 0f && php.Value > 0f)
            {
                float before = php.Value;
                php.Value = math.min(php.Max, php.Value + heal);
                float gained = php.Value - before;
                ev.Healed += gained;
                if (gained >= 1f) nums.Add(new DamageNumber { Pos = ppos + new float2(0, 1), Amount = gained, Kind = NumKind.Heal });
            }

            // ---------------- Apply enemy damage, deaths, drops ----------------
            bool killed = false;
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
                    var vi = eVis[j]; vi.Flash = 1f; eVis[j] = vi;
                    en.Push += knock[j] * (en.Boss != 0 ? 0.05f : en.Elite != 0 ? 0.4f : 1f);
                    if (numBudget > 0 && dmgAcc[j] >= 1f)
                    {
                        numBudget--;
                        nums.Add(new DamageNumber { Pos = ePos[j].Value, Amount = dmgAcc[j], Kind = crit[j] != 0 ? NumKind.Crit : NumKind.Normal });
                    }
                    if (before > 0f && h.Value <= 0f)
                    {
                        killed = true;
                        ev.Kills++;
                        ev.Score += en.Score;
                        float2 dp = ePos[j].Value;
                        if (en.Boss != 0)
                        {
                            ev.BossKills++;
                            ev.ShakeRequest = math.max(ev.ShakeRequest, 1.2f);
                            for (int k = 0; k < 12; k++) SpawnUtil.Pickup(ecb, pf.Pickup, dp + SpawnUtil.Dir(k * 30f) * 1.5f, PickupKind.Xp, math.max(1, en.Xp / 12), ref rng);
                            for (int k = 0; k < 6; k++) SpawnUtil.Pickup(ecb, pf.Pickup, dp, PickupKind.Gold, math.max(1, en.Gold / 6), ref rng);
                            SpawnUtil.Pickup(ecb, pf.Pickup, dp, PickupKind.Chest, 1, ref rng);
                            SpawnUtil.Burst(ecb, pf.Particle, dp, 60, en.BaseColor, 16f, 0.7f, 1.4f, ref rng);
                        }
                        else
                        {
                            SpawnUtil.Pickup(ecb, pf.Pickup, dp, PickupKind.Xp, en.Xp, ref rng);
                            if (buffs.GoldRush > 0f || rng.NextFloat() < en.GoldChance * (1f + stats.Greed))
                                SpawnUtil.Pickup(ecb, pf.Pickup, dp, PickupKind.Gold, math.max(1f, math.round(en.Gold * (1f + stats.Greed))), ref rng);
                            if (en.Elite != 0) SpawnUtil.Pickup(ecb, pf.Pickup, dp, PickupKind.Chest, 1, ref rng);
                            if (rng.NextFloat() < clock.HealPickupChance) SpawnUtil.Pickup(ecb, pf.Pickup, dp, PickupKind.Heal, clock.HealPickupAmount, ref rng);
                            SpawnUtil.Burst(ecb, pf.Particle, dp, en.Elite != 0 ? 18 : 6, en.BaseColor, 7f, 0.22f * eVis[j].Scale.x, 0.45f, ref rng);
                        }
                        if (en.SplitDef >= 0 && en.SplitCount > 0)
                            requests.Add(new SpawnRequest { Def = en.SplitDef, Pos = dp, Count = en.SplitCount });
                        ecb.DestroyEntity(eEnt[j]);
                    }
                }
                eEnemy[j] = en;
            }
            if (killed) sfx.Add(new SfxEvent { Id = Sfx.Kill });

            enemyQ.CopyFromComponentDataArray(eEnemy);
            enemyQ.CopyFromComponentDataArray(ePos);
            enemyQ.CopyFromComponentDataArray(eHp);
            enemyQ.CopyFromComponentDataArray(eVis);

            SystemAPI.SetComponent(player, pdata);
            SystemAPI.SetComponent(player, php);
            evRW.ValueRW = ev;
        }
    }
}
