using MediaRelay.Uploads;

namespace MediaRelay.Tests.Uploads;

public sealed class ObjectIdGeneratorTests
{
    [Fact]
    public void CreateReturnsCryptographicallyRandomUrlSafeIdsWithOnlyTheExtension()
    {
        var generator = new ObjectIdGenerator();
        var first = generator.Create(".png");
        var second = generator.Create(".png");

        Assert.EndsWith(".png", first);
        Assert.NotEqual(first, second);
        Assert.Matches("^[A-Za-z0-9_-]{24}\\.png$", first);
        Assert.DoesNotContain("original-name", first, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("png")]
    [InlineData(".png;original-name")]
    [InlineData(".EXE")]
    public void CreateRejectsUnsafeExtensions(string extension) =>
        Assert.Throws<ArgumentException>(() => new ObjectIdGenerator().Create(extension));
}
