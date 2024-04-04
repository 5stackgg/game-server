using System.Text;

namespace FiveStack.Utilities
{
    public static class StringUtility
    {
        public static string ConvertCamelToHumanReadable(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            StringBuilder result = new StringBuilder(input.Length + 10);
            result.Append(char.ToUpper(input[0]));

            for (int i = 1; i < input.Length; i++)
            {
                if (char.IsUpper(input[i]) && input[i - 1] != ' ')
                {
                    result.Append(' ');
                }
                result.Append(input[i]);
            }

            return result.ToString();
        }
    }
}
