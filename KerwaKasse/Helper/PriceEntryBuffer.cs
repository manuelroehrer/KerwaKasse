using System;
using System.Globalization;

namespace KerwaKasse.Helper
{
    /// <summary>
    /// The typing logic of the price pad, kept apart from the control so it can be tested. Digits as on
    /// a calculator ("4" is 4,00 €), one decimal comma with up to two places, at most three digits before
    /// it (999,99 €). A suggestion is shown like selected text: the first typed key replaces it.
    /// </summary>
    public class PriceEntryBuffer
    {
        private const int MaxWholeDigits = 3;

        private string _text = string.Empty;

        /// <summary>True while the shown value is the untouched suggestion.</summary>
        public bool IsSuggestion { get; private set; }

        /// <summary>The value typed so far (0 when nothing is typed).</summary>
        public decimal Value
        {
            get
            {
                string[] parts = _text.Split(',');
                int whole = parts[0].Length > 0 ? int.Parse(parts[0], CultureInfo.InvariantCulture) : 0;
                int cents = parts.Length > 1 && parts[1].Length > 0
                    ? int.Parse(parts[1].PadRight(2, '0'), CultureInfo.InvariantCulture)
                    : 0;
                return whole + cents / 100m;
            }
        }

        public void SetSuggestion(decimal price)
        {
            _text = price % 1 == 0
                ? ((int)price).ToString(CultureInfo.InvariantCulture)
                : price.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');
            IsSuggestion = true;
        }

        public void Clear()
        {
            _text = string.Empty;
            IsSuggestion = false;
        }

        /// <summary>Takes a digit or a decimal separator (',' or '.'); anything else is ignored. Returns
        /// whether the key was one of those.</summary>
        public bool Type(char key)
        {
            bool isSeparator = key is ',' or '.';
            if (!isSeparator && !char.IsAsciiDigit(key)) return false;

            if (IsSuggestion)
                Clear();

            int comma = _text.IndexOf(',');
            if (isSeparator)
            {
                if (comma < 0)
                    _text = (_text.Length == 0 ? "0" : _text) + ",";
            }
            else if (comma >= 0)
            {
                if (_text.Length - comma - 1 < 2)
                    _text += key;
            }
            else
            {
                if (_text == "0") _text = string.Empty;
                if (_text.Length < MaxWholeDigits) _text += key;
            }
            return true;
        }

        /// <summary>Removes the last typed character; a suggestion is removed as a whole.</summary>
        public void Backspace()
        {
            if (IsSuggestion)
                Clear();
            else if (_text.Length > 0)
                _text = _text[..^1];
        }
    }
}
