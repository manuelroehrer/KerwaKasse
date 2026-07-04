using System.Linq;
using System.Windows.Controls;

namespace KerwaKasse.Helper
{
    /// <summary>Keystroke filter for the position input boxes: permits only digits and only while
    /// the resulting text stays a valid position (1..max). Pasted text is not filtered here; the
    /// commit path still clamps as a safety net.</summary>
    public static class NumericTextInput
    {
        public static bool IsValidPositionTyping(TextBox box, string typedText, int max)
        {
            if (typedText.Length == 0 || !typedText.All(char.IsDigit)) return false;

            string prospective = box.Text
                .Remove(box.SelectionStart, box.SelectionLength)
                .Insert(box.SelectionStart, typedText);

            return int.TryParse(prospective, out int value) && value >= 1 && value <= max;
        }
    }
}
