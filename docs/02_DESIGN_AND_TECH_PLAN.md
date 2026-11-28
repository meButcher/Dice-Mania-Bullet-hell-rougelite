# Dice Mania: Survivors (working title): Design & Technical Plan

A bullet-hell roguelite in the vein of Vampire Survivors, built around the dice of the original Dice Mania.
Target: **Unity 6 (LTS)**, **Entities (ECS) + Burst + Jobs**, **Web** (WebGL2, WebGPU optional).

> Status: **Prototype M0–M8 implemented with basic shapes** (Unity 6000.6.2f1), now compiled, tested and built on the owner's PC:
> EditMode + PlayMode tests (a bot plays waves, a boss and every dice face), Windows + Web builds, and a full **game-feel pass**
> (see [04_GAME_FEEL.md](04_GAME_FEEL.md)): hit flash/squash/knockback, gun kick + recoil + camera kick, shells and splats, kill
> freeze-then-throw, hit-stop, ability VFX, procedural music. Owner decisions (Nov 2026):
> - Face 2 **Melee → Copy**: hold LMB to target an enemy, release to fire a slow-motion shot that consumes it; you become that enemy for 15 s (its attack, speed, body). Upgradable (duration, power, copy elites at Lv3, bosses at Lv5).
> - All abilities / upgrades / enemies / stats live in editable CSV sheets the game reads (docs/03_DATA_SHEETS.md).
> - Basic shapes only for now; visual pass later. Title stays "Dice Mania".

---

## Part A: Game design

### A1. Pillars
1. **Every roll matters.** Dice decide your power moment to moment, but you can shape the odds (loaded dice, rerolls, face forging).
2. **Horde pressure + readable bullet hell.** Hundreds of enemies and bullet patterns you can read, on a neon-on-dark field.
3. **Builds through between-wave upgrades.** Each run ends up with a different die.
4. **Instant restart, short runs.** About 15 minutes per full run, playable in a browser tab.

### A2. Core loop
```
Run start (pick character + starting die)
  └► Wave 1..N (≈60–90 s each)
        • auto-weapons fire on their own (Survivors-style)
        • you move, dodge bullets, collect Pips (XP)
        • SPACE = roll your Dice → face triggers a powerful "Fate" effect
        • level up mid-wave → pick 1 of 3 upgrades (the offer itself is a dice roll)
  └► Between waves: THE DICE FORGE (shop / upgrade screen)
        • spend Gold on face upgrades, face swaps, new dice, relics, loaded weights
  └► Every 5th wave: BOSS
  └► Win after wave 15 (or endless mode), or die → Meta-currency → unlocks
```

### A3. The dice system (heart of the game)
- You own **1–3 dice** (start with 1 d6). Each die has **6 face slots** and each slot holds a **Face card**.
- **Rolling:** you get **roll charges** per wave (start 5, as in the original), plus they recharge slowly over time (upgradable). SPACE rolls **all owned dice**.
- **Face activation:** each rolled face fires its effect immediately or for a short duration (5–8 s), scaled by the face's **level (I–V)**.
- **Combos** (from multiple dice / consecutive rolls):
  - *Doubles:* two dice show the same face → effect ×2 and a bonus burst.
  - *Triples:* → "Jackpot" screen-clear version of the face.
  - *Straight (1-2-3 across rolls):* bonus roll charge.
  - *Snake Eyes (two 1s):* risky curse/blessing event.
- **Starting faces** (the original six, remade):

| # | Face | Effect (level I) | Scaling |
|---|---|---|---|
| 1 | **Bolt** | Burst of homing missiles at nearest enemies | +missiles, +damage |
| 2 | **Copy** | Hold LMB to target, release: slow-mo shot consumes the enemy and you **become it for 15 s** (its bullets, speed, body slam). Same-type enemies stop shooting you | duration, power, elites (Lv3), bosses (Lv5) |
| 3 | **Mend** | Heal-over-time ring, regen while standing in it | heal amount, ring size |
| 4 | **Twin** | Spawn a clone that mirrors your auto-weapons for 8 s | duration, clone count |
| 5 | **Devour** | Expanding dome that consumes enemies **and enemy bullets**, costs HP | radius, lower HP cost, lifesteal |
| 6 | **Reroll** | Refund +2 roll charges and briefly slow time | +charges, longer slow |

- **Unlockable faces** (forge / meta), e.g. *Meteor, Frost Nova, Magnet (pull all pips), Bomb Die, Shield, Chain Lightning, Gold Rush, Curse (enemies weaker, you take more dmg)*.
- **Face manipulation in the Forge:** upgrade a face, **replace** a face (e.g. two Bolts on one die), **load** the die (weight a face), **add a pip** to give a face a stronger tier, **buy a new die** (d4: fewer faces, more consistent; d8/d12: wider but swingier).

### A4. Auto-weapons (Survivors layer)
Always-on weapons with cooldowns, stacking up to 6 weapon slots and 6 passive slots:
- *Pip Shot* (starter): fires dice-pip bullets at the nearest enemy.
- *Orbiting Dice*: small dice orbit and hit enemies.
- *Dice Cannon*: slow heavy shots whose damage = a roll of d6×level.
- *Card Fan*, *Chip Ring*, *Lucky Aura*, ... (casino/tabletop theme).
- **Evolutions:** max weapon + matching passive + matching face → evolved weapon (e.g. Pip Shot + Bolt face V = "Loaded Barrage").

Passives: Max HP, Move speed, Pickup radius, Cooldown, Area, Projectile count, Luck (better roll odds and upgrade rarity), Armor, Regen, Roll recharge.

### A5. Level-ups mid-wave
Pips dropped by enemies → XP bar → on level up, the game pauses and shows **3 upgrade cards** (weapons/passives/new faces), rarity coloured. You can spend a roll charge to **reroll the offer**, so rolls compete between combat and build.

### A6. The Dice Forge (between waves)
Shown during the break (replacing the original's 15 s countdown, untimed or with an optional timer):
- Gold earned in the wave plus interest (like a casino chip stack).
- Shop: 4–5 random offers (face upgrades, new faces, relics, dice), lock and reroll buttons.
- **Die editor:** drag faces between slots, see odds per face.
- Heal option (costs gold).
- Preview of the next wave's enemy types / modifiers.

### A7. Enemies & bullet hell
Keeps the original's look (glowing red spheres) and adds variety through shape and colour:
| Enemy | Behaviour | Bullets |
|---|---|---|
| Grunt (orig. Type1) | Walks toward player | Single aimed pellet |
| Rusher (orig. Type2) | Fast, contact damage | none |
| Gunner (orig. Type3) | Keeps distance | 3-way spread |
| Swarmer | Tiny, comes in packs of 20+ | none |
| Spinner | Stationary turret | Spiral pattern |
| Splitter | Splits into 2–3 on death | none |
| Elite (any + crown) | 3× HP, drops a chest | ring burst |
| **Bosses** (waves 5/10/15) | e.g. *The Croupier*, *Snake Eyes* (two-body), *The House* | multi-phase patterns |

Bullet pattern library (data-driven emitters): aimed, spread-N, ring-N, spiral, wave, random spray, laser telegraph. Spawn director scales enemy count and pattern density per wave, targeting **500+ enemies and 2,000+ bullets on screen** (the main reason to use ECS).

### A8. Meta progression
- **Chips** (meta-currency) kept after every run.
- Unlock: characters (each with a unique starting die / passive), new faces into the pool, new dice types, starting relics, permanent small stat upgrades.
- Saved in browser storage (PlayerPrefs → IndexedDB on Web).

### A9. Art & audio direction
Recreates the original's mood with more readability:
- Dark navy ground with low-poly stone and grass, **coloured neon campfires** as arena landmarks and light pools.
- Primitive, emissive shapes: blue player sphere, red enemy spheres, glowing cube particles, dice-pip bullets.
- URP with **Bloom** (cheap on web), additive unlit materials for bullets. No real-time shadows (web budget).
- Juice: hit-flash, screen shake, damage numbers, shatter-into-hexes death, dice tumble animation on roll.
- UI: same clean sans-serif, glassy circles for dice / roll count. UI Toolkit for HUD and menus.
- All art generated procedurally in code (meshes + materials) at first, so nothing depends on store assets you've lost. Swappable later for your art.

---

## Part B: Technical plan

### B1. Versions
| Item | Choice |
|---|---|
| Editor | **Unity 6000.3 LTS** (or 6000.0 LTS if that's what you have installed) |
| `com.unity.entities` | 1.5.x (supports 6000.0+) |
| `com.unity.burst` | 1.8.31 |
| `com.unity.collections`, `com.unity.mathematics` | as pulled by Entities |
| `com.unity.render-pipelines.universal` | built-in version for the editor |
| `com.unity.inputsystem` | 1.20.x |
| `com.unity.entities.graphics` | **not used on WebGL2** (see B2) |
| Physics | **no Unity Physics**: custom spatial-hash collision (cheaper, Burst-friendly, web-safe) |

### B2. The big web risk & how we avoid it
- **Entities Graphics** relies on BatchRendererGroup, which needs compute/SSBO features that **WebGL2 doesn't reliably support**. Unity lists it as working only on the experimental **WebGPU** backend (URP only).
- **Plan:** keep the whole simulation in ECS (entities, components, Burst jobs) and render with a small **custom instanced renderer**. Each frame a system gathers `LocalTransform` plus colour per "render archetype" into NativeArrays and draws with `Graphics.RenderMeshInstanced` (GPU instancing works on WebGL2 and WebGPU). One draw call per 1,023 instances per mesh/material.
- **No SubScenes / baking dependency.** Web subscene loading has been fragile. Entities are created at runtime from code-defined archetypes (`EntityManager` + `EntityCommandBuffer`), and tuning data lives in ScriptableObjects that a bootstrap converts to blob assets / singletons.
- **Burst on web:** Burst compiles to WebAssembly. Jobs run single-threaded on WebGL by default (web multithreading for Burst jobs officially arrives in Unity 6.4), so systems are written to be fast single-threaded first (`IJobEntity` with `.Run()` / `ScheduleParallel` that degrades gracefully).
- Optional later: a WebGPU build variant that switches to Entities Graphics if it's worth it.

### B3. ECS architecture

**Components (data only)**
```
Player, PlayerInput, MoveSpeed, Health, MaxHealth, Armor, PickupRadius
Enemy { type }, EnemyAI { state, timer }, ContactDamage, XPDrop, GoldDrop
Projectile { damage, pierce, lifetime, owner(Team) }, Velocity, Homing
BulletEmitter { patternId, cooldown, timer, angle }  // bullet-hell patterns
Weapon { id, level, cooldown, timer }  (buffer on player)
DiceBuffer: DiceFace { faceId, level, weight }  ×6 per die
RollState { charges, rechargeTimer }, ActiveFaceEffect { faceId, timeLeft, level }
Pickup { kind, value }, Magnetized
CircleCollider { radius }, Team { Player|Enemy }
RenderKind { meshId, materialId }, TintColor, HitFlash
DestroyTag, DeathEvent
Singletons: GameState { phase, wave, timer }, RunStats, WaveDirector, RNG (Unity.Mathematics.Random), GameConfig (blob)
```

**System groups (execution order)**
```
InitializationSystemGroup
  └ BootstrapSystem (create archetypes, config blobs, spawn player)
SimulationSystemGroup
  ├ InputGatherSystem (managed, reads Input System → PlayerInput)
  ├ GameFlowSystem (phase machine: Menu, Wave, LevelUp, Forge, Boss, GameOver)
  ├ WaveDirectorSystem (spawn budget, enemy mix per wave)
  ├ DiceRollSystem → FaceEffectSystems (Bolt, Slash, Mend, Twin, Devour, Reroll, ...)
  ├ WeaponFireSystem (auto-weapons)
  ├ EnemyAISystem / BulletEmitterSystem (bullet patterns)
  ├ MovementSystem (Burst, velocity integration, separation)
  ├ SpatialHashBuildSystem → CollisionSystem (projectile vs target, contact, pickups)
  ├ DamageSystem → DeathSystem (drops, score, particles)
  ├ PickupMagnetSystem / XPSystem (level-up trigger)
  └ CleanupSystem (ECB playback, lifetime)
PresentationSystemGroup
  ├ InstancedRenderSystem (RenderMeshInstanced batches)
  ├ CameraFollowSystem + ScreenShake
  ├ VFX bridge (pooled ParticleSystems for bursts, campfires)
  └ UIBridgeSystem (pushes snapshot to UI Toolkit HUD / Forge / Level-up screens)
```

- Separation/steering instead of NavMesh: open arena with scattered obstacles; enemies use seek + separation over the spatial hash (what Survivors-likes do; NavMesh doesn't scale to hundreds of agents).
- All gameplay numbers are data-driven: `FaceDefinition`, `WeaponDefinition`, `EnemyDefinition`, `WaveDefinition`, `BulletPattern`, `RelicDefinition` ScriptableObjects.
- UI/menus and save/meta stay in MonoBehaviours (UI Toolkit); they read ECS state through a snapshot singleton.

### B4. Project layout
```
DiceManiaSurvivors/            (Unity project root inside this repo)
  Assets/_Game/
    Scripts/ECS/Components | Systems | Authoring(none) | Jobs
    Scripts/Data/          (ScriptableObject definitions)
    Scripts/Presentation/  (renderer, camera, VFX, audio)
    Scripts/UI/            (HUD, LevelUp, Forge, Menus: UI Toolkit)
    Scripts/Meta/          (save, unlocks)
    Data/  Materials/  Shaders/  UI/  Audio/  Scenes/Main.unity
  Packages/manifest.json
  ProjectSettings/ (Web: WebGL2, Brotli/gzip, IL2CPP, URP asset)
docs/
```

### B5. Milestones
| # | Milestone | Done when |
|---|---|---|
| M0 | Project skeleton | Unity 6 project, packages, URP, empty web build loads in browser |
| M1 | ECS core | Player moves, camera follows, enemies spawn & chase, instanced rendering of 1,000+ entities at 60 fps on web |
| M2 | Combat | Auto-weapon, projectiles, spatial-hash collisions, damage, death, pips/XP |
| M3 | Dice | Roll charges, the 6 original faces, HUD die with tumble, combos |
| M4 | Waves & bullet hell | Wave director, 6 enemy types, bullet pattern emitters, wave timer/break |
| M5 | Roguelite layer | Level-up cards, Dice Forge shop between waves, gold, relics |
| M6 | Bosses & meta | 3 bosses, meta unlocks, save/load |
| M7 | Juice & polish | Bloom, VFX, screen shake, audio, menus, tutorial |
| M8 | Web optimisation & ship | Size/perf pass, itch.io-ready build |

### B6. Verification
- Batch mode on the owner's PC (see README → Tests): compile, EditMode tests, PlayMode smoke tests (bot plays several waves, a boss
  wave, every dice face; camera captures for visual review), then the Windows playtest build and the Web build.
- Pure-logic parts (dice odds, wave budgets, upgrade math) are kept in plain C# with EditMode tests.
- Lesson learned: materials are created in code, so Graphics Settings must keep GPU-instancing variants (`ProjectSetup` forces
  "Keep All"); with the default "Strip Unused" the player builds draw an empty world while the Editor looks fine.

### B7. Risks
| Risk | Mitigation |
|---|---|
| Entities Graphics on WebGL2 | Custom instanced renderer (B2) |
| Single-threaded jobs on web | Budgeted entity caps, Burst everywhere, profiling at M1 |
| Build size (Entities adds ~MBs) | Strip engine code, Brotli, no unused packages |
| Can't compile from cloud | Remote Control to owner's PC, or milestone check-ins |
| Scope creep | Milestones are playable on their own; M5 is the "real game" checkpoint |

---

## Open questions for the owner
1. Which Unity 6 version is installed on your PC (6000.0 LTS, 6000.3 LTS, newer)?
2. Can this session use your PC via Remote Control (to compile/build Unity there)?
3. Art: recreate the original's neon-primitive look procedurally, or do you have / want asset packs?
4. Name: keep "Dice Mania" or new title?
5. Scope for first delivery: up to M5 (complete roguelite loop) first, then bosses/meta?
