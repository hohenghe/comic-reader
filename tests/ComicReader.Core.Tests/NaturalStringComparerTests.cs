using ComicReader.Core;

namespace ComicReader.Core.Tests;

public class NaturalStringComparerTests
{
    [Theory]
    [InlineData("page1.jpg", "page2.jpg")]
    [InlineData("page2.jpg", "page10.jpg")]
    [InlineData("01.png", "02.png")]
    [InlineData("1.jpg", "2.jpg")]
    [InlineData("ch1/page9.jpg", "ch1/page10.jpg")]
    public void SortsNumerically(string first, string second)
    {
        Assert.True(NaturalStringComparer.Instance.Compare(first, second) < 0);
        Assert.True(NaturalStringComparer.Instance.Compare(second, first) > 0);
    }

    [Fact]
    public void SortsMixedTextCaseInsensitively()
    {
        Assert.True(NaturalStringComparer.Instance.Compare("PAGE1.jpg", "page2.jpg") < 0);
        Assert.Equal(0, NaturalStringComparer.Instance.Compare("Page.jpg", "page.jpg"));
    }

    [Fact]
    public void FullOrderingMatchesExpectation()
    {
        var names = new[] { "page10.jpg", "page2.jpg", "page1.jpg", "page20.jpg", "page3.jpg" };
        Array.Sort(names, NaturalStringComparer.Instance);
        Assert.Equal(new[] { "page1.jpg", "page2.jpg", "page3.jpg", "page10.jpg", "page20.jpg" }, names);
    }
}
