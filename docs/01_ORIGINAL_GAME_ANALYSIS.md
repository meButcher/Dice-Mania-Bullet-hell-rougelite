# Dice Mania (2024 jam version): Analysis

Source: https://mebutcher.itch.io/dice-mania (PP Jam #8, theme "Battle", mechanic "Dice").
Made with Unity 2022.3.16f1, URP-style look, published as a WebGL build.

How this was gathered:
1. **Playing it.** The live WebGL build ran in headless Chromium. A scripted session (start, roll, move, shoot) captured about 165 frames over roughly 5 minutes and got through wave 1 into wave 2. Reference frames are in `docs/reference/`.
2. **Reading the build data.** The shipped `.data` bundle and the IL2CPP `global-metadata.dat` were unpacked to recover class names, field names and serialized values. Type trees are stripped, so the values below were decoded by hand and are approximate where marked.

---

## 1. Core loop

```
Main menu ─► Wave N (120 s timer) ─► Break (15 s) ─► "Starting next wave…" ─► Wave N+1 ...
                │
                └─ HP hits 0 ─► "You Died!!" (ReStart / Exit)
```

- Survive as long as you can. Score goes up per kill (+10 per basic enemy, +50 for some types; 30 → 80 → 120 … 520 in our run).
- At the start of every wave you get **5 dice rolls** (`maxDiceRollCount = 5`). HP **does not** reset between waves.
- **Space** rolls the die. The rolled face becomes your *current ability* until you roll again.
- **LMB** uses the current ability (aimed at the mouse). **WASD** moves. The player turns toward the mouse (`rotateSpeed = 10`).

## 2. Dice faces (from the itch page plus what we saw)

| Face | Name | Behaviour seen | Colour |
|---|---|---|---|
| 1 | Shoot | Fires an orange missile with a sparkle trail toward the cursor (`bulletspeed = 100`) | Cyan / teal |
| 2 | Melee | Short-range swing (`MeeleAttack`, "swing" SFX) | Pink-red |
| 3 | Heal | Green ring under the player, heal over time (`HealOverTime` coroutine, `healAmount`) | Lime / yellow-green |
| 4 | Clone | Spawns a player clone that fights (`PlayerCloneBehaviour`, `maxCloneCount`) | (not captured) |
| 5 | Consume | A magenta dome **expands** around the player and eats enemies, costs 10 HP (`ability5`, `Expand` coroutine) | Magenta |
| 6 | Refresh | Refills dice rolls (`RefreshDiceRolls`) | Purple |

HUD die (bottom right) shows the current face number on a tile in that face's colour. A fan of six coloured rings next to it shows the face palette.

## 3. Enemies

- `EnemyBehaviour` with `EnemyType { Type1, Type2, Type3 }`, moved by a **NavMeshAgent**.
- Look: **red glowing spheres** with a small white cube "gun". They fire streams of small **red pellet bullets** at the player.
- Decoded prefab values (approximate): `moveSpeed ≈ 5`, `fireRate ≈ 0.5 s`, `bulletSpeed 25–50`, `maxEnemyHP 5–10`.
- Spawning (`GameBehaviour`): fixed spawn points around the map, `spawnInterval ≈ 1 s`, `spawnLimit ≈ 5` alive at once. Wave 2 visibly spawns more and in groups.
- Death: `deathParticle` burst. `PowerUp` pickups (`Pickup`, `pickupEffect`) exist in code.

## 4. Visual language (to carry forward)

- **Night scene:** near-black navy ground, low-poly **cobblestone path** and **grass tufts**, very dark ambient light.
- **Neon campfires** are the main light sources. Each burns a different saturated colour (yellow-green, cyan-blue, violet, hot pink) with **chunky cube particles**. They double as landmarks.
- **Characters are primitives:** player = bright **blue sphere** with a light glow and blue particle trail; enemies = **red spheres** with red glow. Bullets = tiny emissive squares.
- **Feedback:** camera shake on hit (`duration 0.5`, `magnitude 3`, Cinemachine Perlin). A **red vignette** flashes as HP drops. On death the player **shatters into blue hexagon chunks**.
- **UI:** clean white sans-serif (TextMeshPro). HP bar top left with a "+" icon, `Wave: 01` and countdown top centre, `Score:` top right, dice-roll count bottom left in a glassy circle, die bottom right in a glassy circle. Menu buttons are flat slate-blue rectangles.
- **Camera:** top-down, slightly tilted perspective following the player.

## 5. Audio (from `AudioScript`)

Music: `mainMenu`, `bkg`. SFX: `shoot`, `swing`, `heal`, `refill`, `spawn`, `diceRoll`, `explosion` (sourced from freesound.org).

## 6. Source architecture recovered (names only, code isn't recoverable)

`PlayerBehaviour`, `PlayerAttackBehaviour`, `PlayerCloneBehaviour`, `DiceBehaviour`, `DiceUI`, `MaterialForDice`, `EnemyBehaviour`, `GameBehaviour` (spawning, pause, tutorial, screens), `WaveManger` (`waveDuration = 120`, `breakDuration = 15`), `ScoreManager`, `PowerUp`, `CameraShake`, `AudioScript`, `Utilities`. Classic MonoBehaviour design with NavMesh, Cinemachine and Post-Processing.

## 7. What worked / what to improve

**Keep:** the dice-as-ability-selector idea, limited rolls per wave, the six-face identity and colour coding, the neon-on-dark look, the wave/break rhythm, the shatter death.

**Improve for a roguelite:**
- Progression stops after the first minute: there are no upgrades, no build variety and no reason to change how you play.
- Rolling is all-or-nothing. A bad roll (e.g. Heal at full HP) just wastes a charge, with no way to influence the odds.
- Enemy count is small (a 5-alive cap), so there's no horde or bullet-hell pressure.
- One enemy look and one bullet pattern.
- The 15 s break is empty. It's the obvious slot for a shop / upgrade screen.
