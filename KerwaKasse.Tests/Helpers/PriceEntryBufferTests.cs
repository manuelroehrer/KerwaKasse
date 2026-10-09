using KerwaKasse.Helper;

namespace KerwaKasse.Tests.Helpers;

public class PriceEntryBufferTests
{
    private static PriceEntryBuffer Typed(string keys)
    {
        var buffer = new PriceEntryBuffer();
        foreach (char key in keys)
            buffer.Type(key);
        return buffer;
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("4", 4)]
    [InlineData("45", 45)]
    [InlineData("4,5", 4.5)]
    [InlineData("4,50", 4.5)]
    [InlineData("4.5", 4.5)]
    [InlineData(",5", 0.5)]
    [InlineData("0,05", 0.05)]
    public void Typing_WorksLikeACalculator(string keys, decimal expected)
    {
        Assert.Equal(expected, Typed(keys).Value);
    }

    [Fact]
    public void Typing_IsLimitedTo999Comma99()
    {
        Assert.Equal(999m, Typed("9999").Value);
        Assert.Equal(1.23m, Typed("1,234").Value);
        Assert.Equal(1.2m, Typed("1,2,").Value); // a second comma is ignored
    }

    [Fact]
    public void LeadingZero_IsReplacedByTheNextDigit()
    {
        Assert.Equal(5m, Typed("05").Value);
        Assert.Equal(0.5m, Typed("0,5").Value);
    }

    [Fact]
    public void OtherKeys_AreIgnored()
    {
        var buffer = new PriceEntryBuffer();

        Assert.False(buffer.Type('x'));
        Assert.False(buffer.Type(' '));
        Assert.Equal(0m, buffer.Value);
    }

    [Fact]
    public void Suggestion_IsReplacedByTheFirstKey()
    {
        var buffer = new PriceEntryBuffer();
        buffer.SetSuggestion(7.50m);
        Assert.True(buffer.IsSuggestion);
        Assert.Equal(7.50m, buffer.Value);

        buffer.Type('8');

        Assert.False(buffer.IsSuggestion);
        Assert.Equal(8m, buffer.Value);
    }

    [Fact]
    public void Backspace_RemovesTheLastCharacter_OrTheWholeSuggestion()
    {
        var typed = Typed("12,5");
        typed.Backspace();
        Assert.Equal(12m, typed.Value);

        var suggested = new PriceEntryBuffer();
        suggested.SetSuggestion(10m);
        suggested.Backspace();
        Assert.Equal(0m, suggested.Value);
        Assert.False(suggested.IsSuggestion);
    }

    [Fact]
    public void Suggestion_KeepsItsCentsWhenEditedFurther()
    {
        // After the suggestion is replaced, typing continues normally, e.g. "9,5" -> 9,50.
        var buffer = new PriceEntryBuffer();
        buffer.SetSuggestion(7m);

        foreach (char key in "9,5")
            buffer.Type(key);

        Assert.Equal(9.5m, buffer.Value);
    }
}
