using ComicReader.Core;

namespace ComicReader.Core.Tests;

public class ComicInfoTests
{
    private const string Sample = """
        <?xml version="1.0" encoding="utf-8"?>
        <ComicInfo xmlns:xsd="http://www.w3.org/2001/XMLSchema">
          <Title>Test Manga</Title>
          <Series>Test Series</Series>
          <Number>3</Number>
          <Count>12</Count>
          <Summary>A summary.</Summary>
          <Writer>Someone</Writer>
          <Genre>Action</Genre>
          <Year>2024</Year>
          <Month>6</Month>
          <LanguageISO>zh</LanguageISO>
          <PageCount>42</PageCount>
          <Manga>YesAndRightToLeft</Manga>
        </ComicInfo>
        """;

    [Fact]
    public void ParsesAllFields()
    {
        var info = ComicInfo.Parse(Sample);
        Assert.Equal("Test Manga", info.Title);
        Assert.Equal("Test Series", info.Series);
        Assert.Equal("3", info.Number);
        Assert.Equal("A summary.", info.Summary);
        Assert.Equal(2024, info.Year);
        Assert.Equal(6, info.Month);
        Assert.Equal(42, info.PageCount);
        Assert.True(info.IsRightToLeft);
    }

    [Fact]
    public void InvalidXmlReturnsNull()
    {
        Assert.Null(ComicInfo.TryParse("not xml at all"));
        Assert.Null(ComicInfo.TryParse(""));
    }

    [Fact]
    public void MissingOptionalFieldsAreNull()
    {
        var info = ComicInfo.Parse("<ComicInfo><Title>T</Title></ComicInfo>");
        Assert.Equal("T", info.Title);
        Assert.Null(info.Series);
        Assert.Null(info.Year);
        Assert.False(info.IsRightToLeft);
    }
}
