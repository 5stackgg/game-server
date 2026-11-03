using System.Text;
using FiveStack.Utilities;
using Xunit;

public class PlayerNameUtilityTests
{
    [Fact]
    public void AShortNameIsLeftAlone()
    {
        Assert.Equal("s1mple", PlayerNameUtility.Fit("s1mple"));
    }

    [Fact]
    public void ANameOfExactlyTheLimitIsLeftAlone()
    {
        string name = new string('a', PlayerNameUtility.MaxBytes);

        Assert.Equal(name, PlayerNameUtility.Fit(name));
    }

    [Fact]
    public void ThirtyTwoEmojiAreCutToWhatFits()
    {
        string name = string.Concat(Enumerable.Repeat("😀", 32));

        string fitted = PlayerNameUtility.Fit(name);

        Assert.Equal(string.Concat(Enumerable.Repeat("😀", 31)), fitted);
    }

    [Fact]
    public void ACharacterIsNeverCutInHalf()
    {
        string name = new string('a', PlayerNameUtility.MaxBytes - 1) + "é";

        string fitted = PlayerNameUtility.Fit(name);

        Assert.Equal(new string('a', PlayerNameUtility.MaxBytes - 1), fitted);
        Assert.Equal(fitted, Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(fitted)));
    }
}
