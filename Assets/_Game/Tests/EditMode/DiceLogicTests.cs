using System.Collections.Generic;
using DiceMania.Data;
using DiceMania.Game;
using NUnit.Framework;
using UnityEngine;

namespace DiceMania.Tests
{
    public class DiceLogicTests
    {
        [OneTimeSetUp]
        public void Load() => GameDatabase.Load();

        static DiceLogic.ComboConfig Cfg => new DiceLogic.ComboConfig
        {
            DoublesMult = 1.75f, TriplesMult = 3f, TriplesBonusRolls = 1, StraightBonusRolls = 1, SnakeEyesLevelBonus = 2,
        };

        [Test]
        public void SheetsLoadWithoutWarnings()
        {
            Assert.IsTrue(GameDatabase.Loaded);
            Assert.Greater(GameDatabase.Faces.Count, 5);
            Assert.Greater(GameDatabase.Enemies.Count, 5);
            Assert.AreEqual(0, CsvTable.Warnings.Count, string.Join("\n", CsvTable.Warnings));
        }

        [Test]
        public void WeightedRollRespectsWeights()
        {
            var die = Die.FromIds(new[] { "bolt", "copy", "mend", "twin", "devour", "reroll" });
            for (int i = 0; i < 6; i++) die.Slots[i].Weight = 0;
            die.Slots[3].Weight = 1;
            var rng = new System.Random(1);
            for (int i = 0; i < 100; i++) Assert.AreEqual(3, DiceLogic.RollSlot(die, rng));
        }

        [Test]
        public void DoublesAndTriplesAreDetected()
        {
            var dice = new List<Die>
            {
                Die.FromIds(new[] { "bolt" }), Die.FromIds(new[] { "bolt" }), Die.FromIds(new[] { "mend" }),
            };
            var o = new RollOutcome();
            o.Rolls.Add(new DieRoll { DieIndex = 0, SlotIndex = 2 });
            o.Rolls.Add(new DieRoll { DieIndex = 1, SlotIndex = 4 });
            o.Rolls.Add(new DieRoll { DieIndex = 2, SlotIndex = 0 });
            DiceLogic.Evaluate(dice, o, new List<int>(), Cfg);
            Assert.IsTrue(o.Doubles);
            Assert.IsFalse(o.Triples);
            Assert.AreEqual(2, o.Activations.Count);
            Assert.AreEqual(1.75f, o.Activations[0].Power, 1e-4f);

            dice[2] = Die.FromIds(new[] { "bolt" });
            var t = new RollOutcome();
            t.Rolls.AddRange(o.Rolls);
            DiceLogic.Evaluate(dice, t, new List<int>(), Cfg);
            Assert.IsTrue(t.Triples);
            Assert.AreEqual(1, t.BonusRolls);
        }

        [Test]
        public void SnakeEyesAddsLevels()
        {
            var dice = new List<Die> { Die.FromIds(new[] { "bolt" }), Die.FromIds(new[] { "mend" }) };
            var o = new RollOutcome();
            o.Rolls.Add(new DieRoll { DieIndex = 0, SlotIndex = 0 });
            o.Rolls.Add(new DieRoll { DieIndex = 1, SlotIndex = 0 });
            DiceLogic.Evaluate(dice, o, new List<int>(), Cfg);
            Assert.IsTrue(o.SnakeEyes);
            Assert.AreEqual(3, o.Activations[0].Level);
        }

        [Test]
        public void StraightGivesBonusRoll()
        {
            var dice = new List<Die> { Die.FromIds(new[] { "bolt" }) };
            var history = new List<int>();
            int bonus = 0;
            foreach (int slot in new[] { 1, 2, 3 })
            {
                var o = new RollOutcome();
                o.Rolls.Add(new DieRoll { DieIndex = 0, SlotIndex = slot });
                DiceLogic.Evaluate(dice, o, history, Cfg);
                bonus += o.BonusRolls;
            }
            Assert.AreEqual(1, bonus);
        }

        [Test]
        public void CsvHandlesQuotesAndSemicolons()
        {
            var t = CsvTable.Parse("T", "﻿Id;Name;Value\r\na;\"Hello; world\";1,5\r\n#comment;x;y\r\n\r\nb;\"Say \"\"hi\"\"\";2\n");
            Assert.AreEqual(2, t.RowCount);
            Assert.AreEqual("Hello; world", t.Str(0, "Name"));
            Assert.AreEqual(1.5f, t.Float(0, "Value"), 1e-5f);
            Assert.AreEqual("Say \"hi\"", t.Str(1, "Name"));
        }

        [Test]
        public void FeelColumnsAndKeysAreParsed()
        {
            var pip = GameDatabase.Weapon("pipshot");
            var cannon = GameDatabase.Weapon("cannon");
            Assert.IsNotNull(pip);
            Assert.IsNotNull(cannon);
            Assert.Greater(pip.Recoil, 0f, "Pip Shot should push the hero back a little");
            Assert.Greater(cannon.Recoil, pip.Recoil, "the Cannon should kick harder than the Pip Shot");
            Assert.Greater(cannon.Kick, pip.Kick);
            Assert.AreEqual(1, pip.Shell);
            Assert.AreEqual(2, cannon.Shell);
            Assert.Less(pip.Knockback, cannon.Knockback, "bullets shove less than explosions");
            foreach (var key in new[] { "HitStopHurt", "HitStopKill", "HitStopElite", "HitStopBoss", "HitStopCooldown", "CorpseFreeze",
                         "CorpseThrow", "SplatLife", "ShellLife", "ScreenShake", "CameraKick", "Recoil", "Rumble", "DamageNumbers" })
                Assert.IsTrue(GameDatabase.Config.ContainsKey(key), "Config.csv is missing feel key " + key);
        }

        [Test]
        public void XpCurveIncreases()
        {
            Assert.Less(RunState.XpForLevel(1), RunState.XpForLevel(2));
            Assert.Less(RunState.XpForLevel(5), RunState.XpForLevel(10));
        }
    }
}
