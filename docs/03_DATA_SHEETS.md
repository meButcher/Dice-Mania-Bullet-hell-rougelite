# Design Spreadsheet (the game's single source of truth)

All abilities, upgrades, enemies, waves and stats are in **`Assets/_Game/Resources/Data/*.csv`**.
Each CSV is one "sheet". Open them in Excel, Google Sheets (File → Import), LibreOffice or any text editor.
The game reads them at startup, so nothing is hard-coded.

**Workflow**
1. Edit a sheet and save it as CSV (comma or semicolon separated, UTF-8 both work).
2. In the Unity Editor press **F5 during Play** to hot-reload, or just press Play again.
3. `Dice Mania → Data → Validate Sheets` lists typos (unknown ids, non-numbers, missing columns). The main menu also shows a warning count.
4. Rows starting with `#` are ignored (use them for notes). Column order doesn't matter; column **names** do.

| Sheet | What it controls |
|---|---|
| `Config.csv` | Global tuning: XP curve, rolls, combos, Copy rules, gold/interest, spawn limits, chips… |
| `Characters.csv` | Playable characters: HP, speed, start weapon, rolls, **starting dice** (`face|face|…;second die`) |
| `Faces.csv` | Dice faces (abilities). `Value/Count/Radius/Duration` + `…PerLevel`, unlock cost, shop weight |
| `Weapons.csv` | Auto weapons + evolutions (`EvolvesFrom` + `EvolveRequires` passive id) |
| `Passives.csv` | Level-up passive items: which stat and how much per level |
| `Relics.csv` | Forge relics (permanent run bonuses) |
| `Shop.csv` | Dice Forge offer types: weight, base cost, cost growth per wave, first wave |
| `Enemies.csv` | Enemy stats, behaviour, bullet pattern, drops, splitting, boss phases, **what you get when you Copy them** |
| `Patterns.csv` | Bullet patterns (Aimed / Spread / Ring / Spiral / Random / Summon) |
| `Waves.csv` | Each wave: duration, spawn rates, enemy mix (`id:weight|…`), elites, HP/damage scaling, boss, spawn events |
| `Meta.csv` | Permanent upgrades bought with chips between runs |

## Column reference

### Faces.csv
Level N value = `Value + ValuePerLevel × (N−1)` (same for Count / Radius / Duration). `Count` rounds down.
Description placeholders: `{value} {count} {radius} {duration} {hpcost}`.

| Face | Value | Count | Radius | Duration |
|---|---|---|---|---|
| bolt | missile damage | missiles | – | – |
| copy | damage multiplier for copied attacks | Copy charges gained | target snap radius around the cursor | seconds you stay transformed |
| mend | HP healed per second | – | ring radius | seconds |
| twin | clone shot damage | clones | – | seconds |
| devour | damage per 0.25s tick (+ eats bullets) | – | max dome radius | seconds (`HpCost` = HP paid) |
| reroll | – | roll charges gained | – | slow-mo seconds |
| meteor | damage | meteors | blast radius | – |
| frost | damage | – | radius | freeze seconds |
| aegis | aura damage | – | aura radius | invulnerable seconds |
| goldrush | – | – | – | seconds every kill drops gold |

Adding a **new face** needs one line of code in `GameDirector.Dice.cs → ActivateFace` (the switch on `Id`). Everything else (numbers, shop, unlocks, dice) is data.

### Weapons.csv
`Kind` = `PipShot | Orbit | Cannon | CardFan | ChipRing | Aura`. Per-level growth: Damage (+DamagePerLevel), Cooldown (×(1 − CooldownReductionPerLevel×(L−1))), Count, Pierce, Area. For **Orbit**, `Speed` = spin (rad/s) and `Area` = orbit distance. For **Aura**, `Area` = radius and `Cooldown` = tick time. Rows with `EvolvesFrom` are evolutions (offered when the base weapon is max level and you own the `EvolveRequires` passive).

**Feel columns** (how a volley *feels*, see [04_GAME_FEEL.md](04_GAME_FEEL.md)):

| Column | Meaning | Pip Shot | Cannon |
|---|---|---|---|
| `Knockback` | How far a hit shoves the enemy. Keep bullets small (they should cut through), explosions big | 0.6 | 2 |
| `Recoil` | Speed the hero is pushed back per volley (dies off in ~0.1 s) | 1.6 | 7 |
| `Kick` | Camera kick opposite the shot | 0.14 | 0.65 |
| `Shake` | Short screen vibration + gamepad rumble per volley | 0.12 | 0.45 |
| `Shell` | Casing ejected per volley: 0 none, 1 small, 2 big | 1 | 2 |

### Feel keys in Config.csv
| Key | Default | Meaning |
|---|---|---|
| `HitStopHurt` | 0.08 | Freeze frames when the player gets hit |
| `HitStopKill` | 0.02 | Tiny freeze on a kill (skipped during hordes, rate-limited by `HitStopCooldown`) |
| `HitStopElite` / `HitStopBoss` | 0.06 / 0.35 | Freeze when an elite / boss dies |
| `HitStopCooldown` | 0.3 | Minimum gap between small freezes |
| `CorpseFreeze` | 0.07 | Seconds a killed enemy hangs as a white silhouette before it's thrown |
| `CorpseThrow` | 13 | Speed the body is thrown along the killing hit |
| `SplatLife` / `ShellLife` | 14 / 8 | Seconds splats and shells stay on the floor |
| `ScreenShake`, `CameraKick`, `Recoil`, `Rumble` | 1 | Global multipliers (0 = off) |
| `DamageNumbers` | 1 | 0 hides damage numbers |

### Stats (used by Passives / Relics / Meta `Stat` columns)
`MaxHP, MoveSpeed, Might, Cooldown, Area, ProjectileCount, ProjectileSpeed, Duration, PickupRadius, Armor, Regen, Luck, Growth, Greed, RollCharges, RollRecharge, FacePower, Thorns, Revival, Interest, DoublesChance`.
Percent stats use fractions (`0.1` = +10%). `Luck` = crit chance. `DoublesChance` = chance a rolled face fires twice ("Echo").

### Enemies.csv
- `Behaviour`: `Chaser | Keeper | Turret | Rusher | Swarm | Boss`. `PreferredRange` is used by Keeper/Turret/Boss.
- `Shape`: `Sphere | Cube | Diamond`. `Size` = diameter.
- `Pattern` = a `Patterns.csv` id. `FireCooldown`, `BulletSpeed` (× pattern `SpeedMult`), `BulletDamage`, `Range`.
- `SplitInto` + `SplitCount`: spawn enemies on death.
- `BossPatterns`: `patternId:seconds|patternId:seconds|…`, cycled. Below 50% HP bosses enrage (faster).
- **Copy columns**: `CopyPattern` (the attack you get), `CopyDamageMult`, `CopyContactMult` (body-slam damage), `CopySpeedMult`, `CopyArmor`.

### Waves.csv
`Duration = 0` means "until the boss dies". `SpawnRateStart → SpawnRateEnd` (enemies/second, ramps over the wave), `MaxAlive`, `Mix` (`grunt:6|rusher:2`), `EliteChance`, `HpMult/DamageMult/SpeedMult`, `Boss`, and periodic events (`EventEvery` seconds, `EventEnemy` × `EventCount` in a `ring` around you or a `pack`). After `FinalWave` (Config) the run goes endless and reuses the last wave with growing HP.
