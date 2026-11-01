using System;

namespace Simitone.Client.UI.Model
{
    /// <summary>
    /// The shoulder buttons and triggers (LB, RB, LT, RT: the indices below), which can be used alone, held, or all
    /// together. Each acts when let go (a <see cref="Tap"/>) unless another of the four was down at the same time, so
    /// pressing all four for the cheat combo (<see cref="Combo"/>, as the console games' L1+R1+L2+R2) doesn't also
    /// switch Sims or open panels on the way. A button allowed to (<see cref="Holdable"/>) is a <see cref="Hold"/>
    /// instead of a tap once held for <see cref="HoldTime"/>. Pure logic, so it can be tested on its own.
    /// </summary>
    public class PadChords
    {
        public const int LB = 0, RB = 1, LT = 2, RT = 3;
        public const double HoldTime = 0.6;
        public const double ComboTime = 1.5;

        /// <summary>Which buttons can be held for a second action (the others tap however long they're held).</summary>
        public readonly bool[] Holdable = new bool[4];

        /// <summary>Let go this frame, alone and (if holdable) quickly.</summary>
        public readonly bool[] Tap = new bool[4];
        /// <summary>Held alone for <see cref="HoldTime"/>: true on the frame it got there.</summary>
        public readonly bool[] Hold = new bool[4];
        /// <summary>All four held for <see cref="ComboTime"/>: true on the frame it got there.</summary>
        public bool Combo { get; private set; }

        private const double Ignored = -2; //held through a Reset: let go without acting
        private readonly double[] DownAt = { -1, -1, -1, -1 };
        private readonly bool[] Held = new bool[4];
        private bool Together; //two or more were down at once since all were last up
        private double AllDownAt = -1;
        private bool ComboDone;

        /// <param name="down">LB, RB, LT, RT down this frame.</param>
        public void Update(bool[] down, double now)
        {
            Array.Clear(Tap, 0, 4);
            Array.Clear(Hold, 0, 4);
            Combo = false;

            var count = 0;
            foreach (var d in down) if (d) count++;
            if (count >= 2) Together = true;

            for (int i = 0; i < 4; i++)
            {
                if (down[i])
                {
                    if (DownAt[i] == -1)
                    {
                        DownAt[i] = now;
                        Held[i] = false;
                    }
                    if (Holdable[i] && DownAt[i] >= 0 && !Together && !Held[i] && now - DownAt[i] >= HoldTime)
                    {
                        Hold[i] = true;
                        Held[i] = true;
                    }
                }
                else if (DownAt[i] != -1)
                {
                    if (DownAt[i] >= 0 && !Together && !Held[i]) Tap[i] = true;
                    DownAt[i] = -1;
                }
            }

            if (count == 4)
            {
                if (AllDownAt < 0) AllDownAt = now;
                if (!ComboDone && now - AllDownAt >= ComboTime)
                {
                    Combo = true;
                    ComboDone = true;
                }
            }
            else AllDownAt = -1;

            if (count == 0)
            {
                Together = false;
                ComboDone = false;
            }
        }

        /// <summary>
        /// Forgets presses in progress (something else had the pad, e.g. a dialog): buttons still down act on nothing
        /// when let go.
        /// </summary>
        public void Reset()
        {
            for (int i = 0; i < 4; i++)
            {
                if (DownAt[i] != -1) DownAt[i] = Ignored;
            }
            Array.Clear(Tap, 0, 4);
            Array.Clear(Hold, 0, 4);
            Combo = false;
            AllDownAt = -1;
        }
    }
}
