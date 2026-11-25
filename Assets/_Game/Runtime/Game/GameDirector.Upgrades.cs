using System.Collections.Generic;
using DiceMania.Data;
using DiceMania.Presentation;
using UnityEngine;

namespace DiceMania.Game
{
    public enum OfferType { NewWeapon, WeaponLevel, Evolution, NewPassive, PassiveLevel, Gold, Heal }

    public class Offer
    {
        public OfferType Type;
        public WeaponDef Weapon;
        public PassiveDef Passive;
        public string Title, Body, Tag;
        public Color Color;
    }

    public class ShopOffer
    {
        public ShopDef Def;
        public int Cost;
        public bool Locked, Sold;
        public FaceDef Face;
        public RelicDef Relic;
        public OwnedWeapon Weapon;
        public int DieIndex = -1, SlotIndex = -1;
        public string Title, Body;
        public Color Color;
    }

    public enum PlaceMode { None, NewFace, LoadFace, Swap }

    public partial class GameDirector
    {
        public readonly List<Offer> LevelOffers = new List<Offer>();
        public bool LevelUpIsChest { get; private set; }
        public readonly List<ShopOffer> ShopOffers = new List<ShopOffer>();
        public PlaceMode Placing { get; private set; }
        public ShopOffer PlacingOffer { get; private set; }
        public int SwapDie { get; private set; } = -1;
        public int SwapSlot { get; private set; } = -1;
        public int ShopRerolls { get; private set; }
        public int LastInterest { get; private set; }
        public int LastWaveGold { get; private set; }

        int MaxWeapons => Mathf.RoundToInt(GameDatabase.Cfg("MaxWeapons", 6));
        int MaxPassives => Mathf.RoundToInt(GameDatabase.Cfg("MaxPassives", 6));
        int MaxDice => Mathf.RoundToInt(GameDatabase.Cfg("MaxDice", 3));

        // ================================================================== level up

        GameState levelUpReturnState = GameState.Playing;

        void OpenLevelUp()
        {
            CancelCopyAim();
            levelUpReturnState = State == GameState.WaveClear ? GameState.WaveClear : GameState.Playing;
            LevelUpIsChest = pendingLevelUps <= 0 && pendingChests > 0;
            BuildLevelOffers();
            Audio.Play(SfxPlayer.Id.LevelUp);
            SetState(GameState.LevelUp);
        }

        void UpdateLevelUpInput()
        {
            if (realTimeInState < 0.25f) return;
            if (GameInput.NumberDown >= 1 && GameInput.NumberDown <= LevelOffers.Count) PickOffer(GameInput.NumberDown - 1);
        }

        public bool CanRerollLevelUp => Run != null && Run.RollsLeft >= Mathf.RoundToInt(GameDatabase.Cfg("LevelUpRerollRollCost", 1));

        public void RerollLevelUp()
        {
            int cost = Mathf.RoundToInt(GameDatabase.Cfg("LevelUpRerollRollCost", 1));
            if (Run.RollsLeft < cost) { Audio.Play(SfxPlayer.Id.Deny); return; }
            Run.RollsLeft -= cost;
            BuildLevelOffers();
            Audio.Play(SfxPlayer.Id.Roll);
        }

        void BuildLevelOffers()
        {
            LevelOffers.Clear();
            var pool = new List<(Offer o, float w)>();

            // Evolutions are always offered when available.
            foreach (var ow in Run.Weapons)
            {
                if (ow.Def.IsEvolution || ow.Level < ow.Def.MaxLevel) continue;
                foreach (var evo in GameDatabase.Weapons)
                {
                    if (!evo.IsEvolution || evo.EvolvesFrom != ow.Def.Id) continue;
                    if (Run.FindPassive(evo.EvolveRequires) == null) continue;
                    LevelOffers.Add(new Offer
                    {
                        Type = OfferType.Evolution, Weapon = evo, Title = evo.Name, Body = evo.Description, Tag = "EVOLUTION",
                        Color = new Color(1f, 0.85f, 0.2f),
                    });
                }
            }

            foreach (var w in GameDatabase.Weapons)
            {
                if (w.IsEvolution) continue;
                var owned = Run.FindWeapon(w.Id);
                bool evolvedOwned = Run.Weapons.Exists(x => x.Def.IsEvolution && x.Def.EvolvesFrom == w.Id);
                if (evolvedOwned) continue;
                if (owned == null)
                {
                    if (Run.Weapons.Count >= MaxWeapons) continue;
                    pool.Add((new Offer { Type = OfferType.NewWeapon, Weapon = w, Title = w.Name, Body = w.Description, Tag = "NEW WEAPON", Color = w.Color }, w.Weight));
                }
                else if (owned.Level < w.MaxLevel)
                {
                    pool.Add((new Offer
                    {
                        Type = OfferType.WeaponLevel, Weapon = w, Title = $"{w.Name} Lv {owned.Level + 1}", Body = WeaponLevelText(w, owned.Level + 1),
                        Tag = "WEAPON", Color = w.Color,
                    }, w.Weight * 1.3f));
                }
            }
            foreach (var p in GameDatabase.Passives)
            {
                var owned = Run.FindPassive(p.Id);
                if (owned == null)
                {
                    if (Run.Passives.Count >= MaxPassives) continue;
                    pool.Add((new Offer { Type = OfferType.NewPassive, Passive = p, Title = p.Name, Body = p.Description, Tag = "NEW PASSIVE", Color = new Color(0.6f, 0.8f, 1f) }, p.Weight));
                }
                else if (owned.Level < p.MaxLevel)
                {
                    pool.Add((new Offer { Type = OfferType.PassiveLevel, Passive = p, Title = $"{p.Name} Lv {owned.Level + 1}", Body = p.Description, Tag = "PASSIVE", Color = new Color(0.6f, 0.8f, 1f) }, p.Weight));
                }
            }

            int want = Mathf.RoundToInt(GameDatabase.Cfg("LevelUpChoices", 3)) + (LevelUpIsChest ? 1 : 0);
            while (LevelOffers.Count < want && pool.Count > 0)
            {
                float total = 0f;
                foreach (var e in pool) total += Mathf.Max(0.01f, e.w);
                float r = (float)rng.NextDouble() * total;
                int pick = pool.Count - 1;
                for (int i = 0; i < pool.Count; i++) { r -= Mathf.Max(0.01f, pool[i].w); if (r <= 0f) { pick = i; break; } }
                LevelOffers.Add(pool[pick].o);
                pool.RemoveAt(pick);
            }
            if (LevelOffers.Count < want)
                LevelOffers.Add(new Offer { Type = OfferType.Gold, Title = "Chip Stack", Body = "+25 gold", Tag = "GOLD", Color = new Color(1f, 0.82f, 0.2f) });
            if (LevelOffers.Count < want)
                LevelOffers.Add(new Offer { Type = OfferType.Heal, Title = "Comp Drink", Body = "Heal 30% HP", Tag = "HEAL", Color = new Color(0.4f, 1f, 0.5f) });
        }

        static string WeaponLevelText(WeaponDef w, int l)
        {
            var parts = new List<string>();
            float dmgNow = Lvl.At(w.Damage, w.DamagePerLevel, l - 1), dmgNext = Lvl.At(w.Damage, w.DamagePerLevel, l);
            if (dmgNext > dmgNow) parts.Add($"Damage {dmgNow:0.#} > {dmgNext:0.#}");
            int cNow = Lvl.AtInt(w.Count, w.CountPerLevel, l - 1), cNext = Lvl.AtInt(w.Count, w.CountPerLevel, l);
            if (cNext > cNow) parts.Add($"+{cNext - cNow} {(w.Kind == WeaponKind.Orbit ? "dice" : "projectile")}");
            int pNow = Lvl.AtInt(w.Pierce, w.PiercePerLevel, l - 1), pNext = Lvl.AtInt(w.Pierce, w.PiercePerLevel, l);
            if (pNext > pNow && w.Pierce < 900) parts.Add("+1 pierce");
            if (w.AreaPerLevel > 0) parts.Add("+area");
            if (w.CooldownReductionPerLevel > 0) parts.Add("faster");
            return parts.Count > 0 ? string.Join(", ", parts) : w.Description;
        }

        public void PickOffer(int index)
        {
            if (State != GameState.LevelUp || index < 0 || index >= LevelOffers.Count) return;
            var o = LevelOffers[index];
            switch (o.Type)
            {
                case OfferType.NewWeapon: Run.Weapons.Add(new OwnedWeapon { Def = o.Weapon, Level = 1 }); break;
                case OfferType.WeaponLevel: { var w = Run.FindWeapon(o.Weapon.Id); if (w != null) w.Level++; break; }
                case OfferType.Evolution:
                {
                    var w = Run.FindWeapon(o.Weapon.EvolvesFrom);
                    if (w != null) { w.Def = o.Weapon; w.Level = 1; }
                    ShowBanner("EVOLVED: " + o.Weapon.Name, new Color(1f, 0.85f, 0.2f), 2.5f);
                    break;
                }
                case OfferType.NewPassive: Run.Passives.Add(new OwnedPassive { Def = o.Passive, Level = 1 }); break;
                case OfferType.PassiveLevel: { var p = Run.FindPassive(o.Passive.Id); if (p != null) p.Level++; break; }
                case OfferType.Gold: Run.Gold += 25; break;
                case OfferType.Heal: { var h = Ecs.PlayerHealth; h.Value = Mathf.Min(h.Max, h.Value + h.Max * 0.3f); Ecs.SetPlayerHealth(h); break; }
            }
            RefreshBuild();
            Audio.Play(SfxPlayer.Id.Buy);

            if (LevelUpIsChest) pendingChests = Mathf.Max(0, pendingChests - 1);
            else pendingLevelUps = Mathf.Max(0, pendingLevelUps - 1);

            if (pendingLevelUps > 0 || pendingChests > 0)
            {
                LevelUpIsChest = pendingLevelUps <= 0;
                BuildLevelOffers();
                realTimeInState = 0f;
                return;
            }
            // Return to where we came from.
            SetState(levelUpReturnState);
        }

        // ================================================================== forge

        void OpenForge()
        {
            CancelCopyAim();
            int baseGold = Mathf.RoundToInt(GameDatabase.Cfg("WaveClearGold", 12) + GameDatabase.Cfg("WaveClearGoldPerWave", 4) * Run.Wave);
            int interest = Mathf.Min(Mathf.RoundToInt(GameDatabase.Cfg("InterestCap", 10)), Mathf.FloorToInt(Run.Gold * Run.Interest));
            LastWaveGold = baseGold;
            LastInterest = interest;
            Run.Gold += baseGold + interest;
            ShopRerolls = 0;
            Placing = PlaceMode.None;
            PlacingOffer = null;
            SwapDie = SwapSlot = -1;
            BuildShop(true);
            SetState(GameState.Forge);
        }

        public int ShopRerollCost => Mathf.RoundToInt(GameDatabase.Cfg("ShopRerollBaseCost", 4) + GameDatabase.Cfg("ShopRerollCostGrowth", 2) * ShopRerolls);

        public void RerollShop()
        {
            if (Run.Gold < ShopRerollCost) { Audio.Play(SfxPlayer.Id.Deny); return; }
            Run.Gold -= ShopRerollCost;
            ShopRerolls++;
            BuildShop(false);
            Audio.Play(SfxPlayer.Id.Roll);
        }

        void BuildShop(bool fresh)
        {
            var keep = new List<ShopOffer>();
            foreach (var o in ShopOffers) if (o.Locked && !o.Sold) keep.Add(o);
            ShopOffers.Clear();
            ShopOffers.AddRange(keep);
            int slots = Mathf.RoundToInt(GameDatabase.Cfg("ShopSlots", 5));
            int guard = 0;
            while (ShopOffers.Count < slots && guard++ < 60)
            {
                var o = RollShopOffer();
                if (o == null) continue;
                // Avoid exact duplicates.
                bool dup = ShopOffers.Exists(x => x.Title == o.Title);
                if (!dup) ShopOffers.Add(o);
            }
        }

        ShopOffer RollShopOffer()
        {
            var candidates = new List<ShopDef>();
            float total = 0f;
            foreach (var s in GameDatabase.Shop) if (s.MinWave <= Run.Wave && s.Weight > 0) { candidates.Add(s); total += s.Weight; }
            if (candidates.Count == 0) return null;
            float r = (float)rng.NextDouble() * total;
            ShopDef def = candidates[candidates.Count - 1];
            foreach (var c in candidates) { r -= c.Weight; if (r <= 0f) { def = c; break; } }

            var o = new ShopOffer { Def = def, Cost = def.BaseCost + def.CostPerWave * Run.Wave, Color = Color.white };
            switch (def.Type)
            {
                case ShopType.FaceUpgrade:
                {
                    var options = new List<(int d, int s)>();
                    for (int d = 0; d < Run.Dice.Count; d++)
                        for (int s = 0; s < 6; s++)
                            if (Run.Dice[d].Slots[s].Level < Run.Dice[d].Slots[s].Face.MaxLevel) options.Add((d, s));
                    if (options.Count == 0) return null;
                    var pick = options[rng.Next(options.Count)];
                    var slot = Run.Dice[pick.d].Slots[pick.s];
                    o.DieIndex = pick.d; o.SlotIndex = pick.s; o.Face = slot.Face;
                    o.Title = $"{slot.Face.Name} Lv {slot.Level + 1}";
                    o.Body = $"Die {pick.d + 1}, pip {pick.s + 1}: {slot.Face.Describe(slot.Level + 1)}";
                    o.Color = slot.Face.Color;
                    break;
                }
                case ShopType.NewFace:
                {
                    var faces = new List<FaceDef>();
                    float fw = 0f;
                    foreach (var f in GameDatabase.Faces) if (MetaProgress.IsFaceUnlocked(f) && f.ShopWeight > 0) { faces.Add(f); fw += f.ShopWeight; }
                    if (faces.Count == 0) return null;
                    float fr = (float)rng.NextDouble() * fw;
                    FaceDef chosen = faces[faces.Count - 1];
                    foreach (var f in faces) { fr -= f.ShopWeight; if (fr <= 0f) { chosen = f; break; } }
                    o.Face = chosen;
                    o.Title = "Face: " + chosen.Name;
                    o.Body = chosen.Describe(1) + "  (place on any slot)";
                    o.Color = chosen.Color;
                    break;
                }
                case ShopType.LoadFace:
                    o.Title = "Loaded Weight";
                    o.Body = "+1 weight on a slot of your choice - it rolls more often";
                    o.Color = new Color(0.8f, 0.7f, 1f);
                    break;
                case ShopType.WeaponUpgrade:
                {
                    var ups = Run.Weapons.FindAll(w => w.Level < w.Def.MaxLevel);
                    if (ups.Count == 0) return null;
                    o.Weapon = ups[rng.Next(ups.Count)];
                    o.Title = $"{o.Weapon.Def.Name} Lv {o.Weapon.Level + 1}";
                    o.Body = WeaponLevelText(o.Weapon.Def, o.Weapon.Level + 1);
                    o.Color = o.Weapon.Def.Color;
                    break;
                }
                case ShopType.Relic:
                {
                    var relics = GameDatabase.Relics.FindAll(x => !Run.Relics.Contains(x));
                    if (relics.Count == 0) return null;
                    o.Relic = relics[rng.Next(relics.Count)];
                    o.Cost = o.Relic.Cost + def.CostPerWave * Run.Wave;
                    o.Title = "Relic: " + o.Relic.Name;
                    o.Body = o.Relic.Description;
                    o.Color = new Color(1f, 0.75f, 0.35f);
                    break;
                }
                case ShopType.Heal:
                    o.Title = "Patch Up";
                    o.Body = "Restore 35% of max HP";
                    o.Color = new Color(0.4f, 1f, 0.5f);
                    break;
                case ShopType.RollCharge:
                    o.Title = "Extra Roll";
                    o.Body = "+1 roll charge every wave";
                    o.Color = new Color(0.7f, 0.5f, 1f);
                    break;
                case ShopType.ExtraDie:
                    if (Run.Dice.Count >= MaxDice) return null;
                    o.Title = "New Die";
                    o.Body = "Add a copy of your first die (Lv 1). All dice roll together - combos!";
                    o.Color = new Color(1f, 1f, 1f);
                    break;
            }
            return o;
        }

        public void BuyShopOffer(ShopOffer o)
        {
            if (o == null || o.Sold || Placing != PlaceMode.None) return;
            if (Run.Gold < o.Cost) { Audio.Play(SfxPlayer.Id.Deny); return; }

            switch (o.Def.Type)
            {
                case ShopType.NewFace:
                case ShopType.LoadFace:
                    // Gold is taken when the face is actually placed.
                    Placing = o.Def.Type == ShopType.NewFace ? PlaceMode.NewFace : PlaceMode.LoadFace;
                    PlacingOffer = o;
                    Audio.Play(SfxPlayer.Id.Click);
                    return;
                case ShopType.FaceUpgrade:
                {
                    var slot = Run.Dice[o.DieIndex].Slots[o.SlotIndex];
                    if (slot.Face != o.Face) { Audio.Play(SfxPlayer.Id.Deny); return; }
                    slot.Level = Mathf.Min(slot.Face.MaxLevel, slot.Level + 1);
                    if (slot.Face.Id == "copy") Run.CopyLevel = Mathf.Max(Run.CopyLevel, slot.Level);
                    break;
                }
                case ShopType.WeaponUpgrade: o.Weapon.Level = Mathf.Min(o.Weapon.Def.MaxLevel, o.Weapon.Level + 1); break;
                case ShopType.Relic: Run.Relics.Add(o.Relic); if (o.Relic.Stat == StatType.Revival) Run.RevivalsLeft += Mathf.RoundToInt(o.Relic.Value); break;
                case ShopType.Heal: { var h = Ecs.PlayerHealth; h.Value = Mathf.Min(h.Max, h.Value + h.Max * 0.35f); Ecs.SetPlayerHealth(h); break; }
                case ShopType.RollCharge: Run.BonusRolls++; break;
                case ShopType.ExtraDie:
                {
                    var d = new Die();
                    for (int i = 0; i < 6; i++) d.Slots[i] = new FaceSlot { Face = Run.Dice[0].Slots[i].Face, Level = 1, Weight = 1 };
                    Run.Dice.Add(d);
                    break;
                }
            }
            Run.Gold -= o.Cost;
            o.Sold = true;
            RefreshBuild();
            Audio.Play(SfxPlayer.Id.Buy);
        }

        public void CancelPlacing()
        {
            Placing = PlaceMode.None;
            PlacingOffer = null;
            SwapDie = SwapSlot = -1;
        }

        /// <summary>Called when the player clicks a face slot in the Forge's die editor.</summary>
        public void ClickDieSlot(int die, int slot)
        {
            if (State != GameState.Forge) return;
            var s = Run.Dice[die].Slots[slot];
            switch (Placing)
            {
                case PlaceMode.NewFace:
                    if (Run.Gold < PlacingOffer.Cost) { CancelPlacing(); return; }
                    s.Face = PlacingOffer.Face;
                    s.Level = 1;
                    Run.Gold -= PlacingOffer.Cost;
                    PlacingOffer.Sold = true;
                    if (s.Face.Id == "copy") Run.CopyLevel = Mathf.Max(1, Run.MaxFaceLevel("copy"));
                    CancelPlacing();
                    Audio.Play(SfxPlayer.Id.Buy);
                    break;
                case PlaceMode.LoadFace:
                    if (Run.Gold < PlacingOffer.Cost) { CancelPlacing(); return; }
                    s.Weight++;
                    Run.Gold -= PlacingOffer.Cost;
                    PlacingOffer.Sold = true;
                    CancelPlacing();
                    Audio.Play(SfxPlayer.Id.Buy);
                    break;
                default:
                    // Free swapping of faces between any two slots.
                    if (SwapDie < 0) { SwapDie = die; SwapSlot = slot; Placing = PlaceMode.None; Audio.Play(SfxPlayer.Id.Click); }
                    else
                    {
                        var a = Run.Dice[SwapDie].Slots[SwapSlot];
                        Run.Dice[SwapDie].Slots[SwapSlot] = s;
                        Run.Dice[die].Slots[slot] = a;
                        SwapDie = SwapSlot = -1;
                        Audio.Play(SfxPlayer.Id.Click);
                    }
                    break;
            }
        }

        public void ToggleLock(ShopOffer o) { if (o != null && !o.Sold) o.Locked = !o.Locked; }

        public void LeaveForge()
        {
            if (State != GameState.Forge) return;
            CancelPlacing();
            RefreshBuild();
            StartWave(Run.Wave + 1);
        }
    }
}
