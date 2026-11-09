using System;
using System.Collections.Generic;
using FSO.Common.Rendering.Framework.IO;

namespace Simitone.Client.UI.Model
{
    public enum PadKeyAction
    {
        Char,
        Shift,
        Space,
        Backspace,
        Done
    }

    public class PadKey
    {
        public string Label;
        public char Char;
        public char ShiftChar;
        public PadKeyAction Action;
        public int Column; //in key widths from the left of the row
        public int Width = 1;

        /// <summary>The text this key types, or null for action keys.</summary>
        public string Type(bool shift)
        {
            switch (Action)
            {
                case PadKeyAction.Char: return (shift ? ShiftChar : Char).ToString();
                case PadKeyAction.Space: return " ";
                default: return null;
            }
        }
    }

    /// <summary>
    /// The on-screen keyboard's keys, and how the pad moves between them: left/right along a row (wrapping), up/down to
    /// the key in the row above/below that covers the same column (wrapping between top and bottom). Up/down remember
    /// the column, so going down through the wide Space key and back up returns to the same letter.
    /// </summary>
    public class PadKeyboardLayout
    {
        public const int Columns = 10;

        public readonly List<List<PadKey>> Rows = new List<List<PadKey>>();
        public int Row { get; private set; }
        public int Index { get; private set; }
        private float Preferred; //column up/down aim for

        public PadKeyboardLayout()
        {
            AddCharRow("1234567890", "!@#$%&*()?");
            AddCharRow("qwertyuiop", "QWERTYUIOP");
            AddCharRow("asdfghjkl'", "ASDFGHJKL\"");
            AddCharRow("zxcvbnm,.-", "ZXCVBNM;:_");
            Rows.Add(new List<PadKey>
            {
                new PadKey { Label = "Shift", Action = PadKeyAction.Shift, Column = 0, Width = 2 },
                new PadKey { Label = "Space", Action = PadKeyAction.Space, Column = 2, Width = 4 },
                new PadKey { Label = "Delete", Action = PadKeyAction.Backspace, Column = 6, Width = 2 },
                new PadKey { Label = "Done", Action = PadKeyAction.Done, Column = 8, Width = 2 },
            });
            Reset();
        }

        private void AddCharRow(string chars, string shifted)
        {
            var row = new List<PadKey>();
            for (int i = 0; i < chars.Length; i++)
            {
                row.Add(new PadKey { Label = chars[i].ToString(), Char = chars[i], ShiftChar = shifted[i], Column = i });
            }
            Rows.Add(row);
        }

        public PadKey Current => Rows[Row][Index];

        /// <summary>Back to the first letter key ("q").</summary>
        public void Reset()
        {
            Row = 1;
            Index = 0;
            Preferred = 0.5f;
        }

        public void Move(PadDirection direction)
        {
            var row = Rows[Row];
            switch (direction)
            {
                case PadDirection.Left:
                    Index = (Index - 1 + row.Count) % row.Count;
                    Preferred = Current.Column + Current.Width / 2f;
                    break;
                case PadDirection.Right:
                    Index = (Index + 1) % row.Count;
                    Preferred = Current.Column + Current.Width / 2f;
                    break;
                case PadDirection.Up:
                case PadDirection.Down:
                    var key = Current;
                    var column = Preferred;
                    if (column < key.Column || column >= key.Column + key.Width) column = key.Column + key.Width / 2f;
                    //a wide key's middle can sit exactly between two keys: take the left one
                    var aim = (key.Width > 1 && column == key.Column + key.Width / 2f) ? column - 0.01f : column;
                    var step = (direction == PadDirection.Up) ? -1 : 1;
                    Row = (Row + step + Rows.Count) % Rows.Count;
                    Index = KeyAt(Rows[Row], aim);
                    Preferred = column;
                    break;
            }
        }

        /// <summary>The key in a row covering a column position (keys tile the row, so one always does).</summary>
        private static int KeyAt(List<PadKey> row, float column)
        {
            var best = 0;
            var bestDistance = float.MaxValue;
            for (int i = 0; i < row.Count; i++)
            {
                var key = row[i];
                if (column >= key.Column && column < key.Column + key.Width) return i;
                var distance = Math.Abs(key.Column + key.Width / 2f - column);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }
            return best;
        }
    }
}
