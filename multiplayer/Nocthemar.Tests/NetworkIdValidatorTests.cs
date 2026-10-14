using Server.Nocthemar.Persistence;
using Xunit;

namespace NocthemarTests;

/// <summary>T5: network ids become file names, so traversal and platform-hostile ids are refused.</summary>
public class NetworkIdValidatorTests {
    [Theory]
    [InlineData("../escape")]
    [InlineData("..\\escape")]
    [InlineData("..")]
    [InlineData("x..y")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\temp\\x")]
    [InlineData("a\0b")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("CON")]
    [InlineData("con.txt")]
    [InlineData("NUL")]
    [InlineData("LPT1")]
    [InlineData("a:b")]
    [InlineData("a|b")]
    [InlineData("a*b")]
    [InlineData("a?b")]
    [InlineData("a<b")]
    [InlineData("a\"b")]
    [InlineData("name.")]
    [InlineData(".hidden")]
    [InlineData(" lead")]
    [InlineData("trail ")]
    [InlineData("tab\tchar")]
    [InlineData("line\nbreak")]
    public void Invalid_ids_are_rejected(string id) => Assert.False(NetworkIdValidator.IsValid(id));

    [Fact]
    public void Null_is_rejected() => Assert.False(NetworkIdValidator.IsValid(null));

    [Theory]
    [InlineData("player1")]
    [InlineData("Abc_def-12")]
    [InlineData("user.name")]
    [InlineData("a b")]
    [InlineData("üñí")]
    [InlineData("x")]
    [InlineData("console")]
    public void Plain_ids_are_accepted(string id) => Assert.True(NetworkIdValidator.IsValid(id));

    [Fact]
    public void Length_limit_is_enforced() {
        Assert.True(NetworkIdValidator.IsValid(new string('a', NetworkIdValidator.MaxLength)));
        Assert.False(NetworkIdValidator.IsValid(new string('a', NetworkIdValidator.MaxLength + 1)));
    }

    [Fact]
    public void Resolved_path_is_directly_inside_the_directory() {
        var dir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "noc-validator")));

        Assert.True(NetworkIdValidator.TryResolveSavePath(dir, "hero", out var path));
        Assert.Equal(Path.Combine(dir, "hero.json"), path);
        Assert.False(NetworkIdValidator.TryResolveSavePath(dir, "../hero", out _));
    }
}
