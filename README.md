# Dice Mania: Survivors (prototype)
A fusion of Vampire Survivors, dice rolls and bullet hell — the sequel to the PP Jam #8 game *Dice Mania*.

**Unity 6.6 (6000.6.2f1) · Entities (ECS) + Burst · Web (WebGL2)** · basic-shape prototype (visual pass comes later).

## Open & play
1. Open this folder in Unity Hub with **6000.6.2f1**. First import creates `Assets/_Game/Scenes/Main.unity` (or run `Dice Mania → Setup Project`).
2. Press **Play** in any scene — the game boots itself (`Bootstrap.cs`), no scene setup needed.
3. Web build: `Dice Mania → Build Web` → `Builds/Web` (or CLI: `-executeMethod DiceMania.EditorTools.WebBuild.BuildWeb`).

## Controls
WASD move · weapons auto-fire · **SPACE** roll dice · **hold/release LMB** to aim & fire a Copy shot · 1/2/3 pick level-up card · ESC pause · F5 (Editor) hot-reload data.

## Edit the game without code
Every ability, upgrade, enemy, wave and stat is in **`Assets/_Game/Resources/Data/*.csv`** → see [docs/03_DATA_SHEETS.md](docs/03_DATA_SHEETS.md).

## Docs
- [01 Original game analysis](docs/01_ORIGINAL_GAME_ANALYSIS.md)
- [02 Design & technical plan](docs/02_DESIGN_AND_TECH_PLAN.md)
- [03 Data sheets](docs/03_DATA_SHEETS.md)

## Code map (`Assets/_Game/Runtime`)
| Folder | |
|---|---|
| `Data/` | CSV reader + typed definitions (`GameDatabase`) |
| `ECS/` | Components and Burst systems: Player, Weapon, Enemy, Motion, Combat (spatial hash) |
| `Presentation/` | Procedural meshes, `InstancedRenderSystem` (RenderMeshInstanced, WebGL2-safe), camera, procedural SFX |
| `Game/` | `GameDirector` (state machine, waves, dice, Copy, level-ups, Dice Forge), run state, meta save, ECS bridge |
| `UI/` | IMGUI prototype HUD/menus |
