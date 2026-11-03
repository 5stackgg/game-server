using System.Text;

namespace FiveStack.Utilities;

public static class PlayerNameUtility
{
    // m_iszPlayerName is char[128] with a null terminator, and both runtimes throw on a longer write
    public const int MaxBytes = 127;

    public static string Fit(string name)
    {
        if (Encoding.UTF8.GetByteCount(name) <= MaxBytes)
        {
            return name;
        }

        StringBuilder fitted = new StringBuilder();
        int bytes = 0;

        foreach (Rune rune in name.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > MaxBytes)
            {
                break;
            }

            fitted.Append(rune.ToString());
            bytes += rune.Utf8SequenceLength;
        }

        return fitted.ToString();
    }
}
