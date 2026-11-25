using System.Collections.Generic;
using DiceMania.Data;

namespace DiceMania.Game
{
    public struct DieRoll
    {
        public int DieIndex;
        public int SlotIndex;
        public int Pip => SlotIndex + 1;
    }

    public class FaceActivation
    {
        public FaceDef Face;
        public int Level;
        public int Matches;     // how many dice showed this face
        public float Power;     // combo multiplier (1 = single)
        public bool Jackpot;    // triples or more
    }

    public class RollOutcome
    {
        public readonly List<DieRoll> Rolls = new List<DieRoll>();
        public readonly List<FaceActivation> Activations = new List<FaceActivation>();
        public bool Doubles, Triples, SnakeEyes, Straight;
        public int BonusRolls;
        public string ComboText = "";
    }

    /// <summary>Pure dice logic (no Unity dependencies) so it can be unit tested.</summary>
    public static class DiceLogic
    {
        public static int RollSlot(Die die, System.Random rng)
        {
            int total = die.TotalWeight;
            if (total <= 0) return rng.Next(6);
            int r = rng.Next(total);
            for (int i = 0; i < 6; i++)
            {
                r -= System.Math.Max(0, die.Slots[i].Weight);
                if (r < 0) return i;
            }
            return 5;
        }

        public struct ComboConfig
        {
            public float DoublesMult, TriplesMult;
            public int TriplesBonusRolls, StraightBonusRolls, SnakeEyesLevelBonus;
        }

        public static RollOutcome Roll(IList<Die> dice, System.Random rng, List<int> pipHistory, ComboConfig cfg)
        {
            var o = new RollOutcome();
            for (int d = 0; d < dice.Count; d++) o.Rolls.Add(new DieRoll { DieIndex = d, SlotIndex = RollSlot(dice[d], rng) });
            Evaluate(dice, o, pipHistory, cfg);
            return o;
        }

        public static void Evaluate(IList<Die> dice, RollOutcome o, List<int> pipHistory, ComboConfig cfg)
        {
            // Snake eyes: two or more dice show pip 1.
            int ones = 0;
            foreach (var r in o.Rolls) if (r.Pip == 1) ones++;
            o.SnakeEyes = ones >= 2;

            // Group identical faces.
            var order = new List<string>();
            var groups = new Dictionary<string, FaceActivation>();
            foreach (var r in o.Rolls)
            {
                var slot = dice[r.DieIndex].Slots[r.SlotIndex];
                if (!groups.TryGetValue(slot.Face.Id, out var act))
                {
                    act = new FaceActivation { Face = slot.Face, Level = slot.Level, Matches = 0 };
                    groups[slot.Face.Id] = act;
                    order.Add(slot.Face.Id);
                }
                act.Matches++;
                if (slot.Level > act.Level) act.Level = slot.Level;
            }
            foreach (var id in order)
            {
                var a = groups[id];
                a.Power = a.Matches >= 3 ? cfg.TriplesMult : a.Matches == 2 ? cfg.DoublesMult : 1f;
                a.Jackpot = a.Matches >= 3;
                if (a.Matches == 2) o.Doubles = true;
                if (a.Matches >= 3) { o.Triples = true; o.BonusRolls += cfg.TriplesBonusRolls; }
                if (o.SnakeEyes) a.Level += cfg.SnakeEyesLevelBonus;
                o.Activations.Add(a);
            }

            // Straight: the first die's pip over the last three rolls goes up by one each time.
            if (o.Rolls.Count > 0 && pipHistory != null)
            {
                pipHistory.Add(o.Rolls[0].Pip);
                if (pipHistory.Count > 3) pipHistory.RemoveAt(0);
                if (pipHistory.Count == 3 && pipHistory[1] == pipHistory[0] + 1 && pipHistory[2] == pipHistory[1] + 1)
                {
                    o.Straight = true;
                    o.BonusRolls += cfg.StraightBonusRolls;
                    pipHistory.Clear();
                }
            }

            var parts = new List<string>();
            if (o.Triples) parts.Add("JACKPOT!");
            else if (o.Doubles) parts.Add("DOUBLES!");
            if (o.SnakeEyes) parts.Add("SNAKE EYES!");
            if (o.Straight) parts.Add("STRAIGHT!");
            o.ComboText = string.Join("  ", parts);
        }
    }
}
