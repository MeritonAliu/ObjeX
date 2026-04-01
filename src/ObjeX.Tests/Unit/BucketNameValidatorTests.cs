using ObjeX.Core.Validation;

namespace ObjeX.Tests.Unit;

public class BucketNameValidatorTests
{
    [Theory]
    [InlineData("abc")]
    [InlineData("my-bucket")]
    [InlineData("a-b")]
    [InlineData("bucket-abc")]
    [InlineData("a23")]
    [InlineData("bucket-with-numbers-123")]
    [InlineData("9apps")]
    public void Valid_Names_Return_Null(string name)
    {
        Assert.Null(BucketNameValidator.GetValidationError(name));
    }

    [Fact]
    public void Min_Length_3_Is_Valid()
    {
        Assert.Null(BucketNameValidator.GetValidationError("abc"));
    }

    [Fact]
    public void Max_Length_63_Is_Valid()
    {
        var name = "a" + new string('b', 61) + "c";
        Assert.Equal(63, name.Length);
        Assert.Null(BucketNameValidator.GetValidationError(name));
    }

    [Fact]
    public void Too_Short_Returns_Error()
    {
        Assert.NotNull(BucketNameValidator.GetValidationError("ab"));
        Assert.NotNull(BucketNameValidator.GetValidationError("a"));
    }

    [Fact]
    public void Too_Long_Returns_Error()
    {
        var name = "a" + new string('b', 62) + "c"; // 64 chars
        Assert.NotNull(BucketNameValidator.GetValidationError(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_Or_Whitespace_Returns_Error(string? name)
    {
        Assert.NotNull(BucketNameValidator.GetValidationError(name!));
    }

    [Theory]
    [InlineData("Abc")]
    [InlineData("ABC")]
    [InlineData("aBc")]
    public void Uppercase_Returns_Error(string name)
    {
        Assert.NotNull(BucketNameValidator.GetValidationError(name));
    }

    [Theory]
    [InlineData("-abc")]
    [InlineData("-bucket")]
    public void Leading_Hyphen_Returns_Error(string name)
    {
        Assert.NotNull(BucketNameValidator.GetValidationError(name));
    }

    [Theory]
    [InlineData("abc-")]
    [InlineData("bucket-")]
    public void Trailing_Hyphen_Returns_Error(string name)
    {
        Assert.NotNull(BucketNameValidator.GetValidationError(name));
    }

    [Fact]
    public void Consecutive_Periods_Returns_Error()
    {
        Assert.NotNull(BucketNameValidator.GetValidationError("a..b"));
    }

    [Theory]
    [InlineData("a@b")]
    [InlineData("a b")]
    [InlineData("a/b")]
    [InlineData("a_b")]
    public void Special_Characters_Returns_Error(string name)
    {
        Assert.NotNull(BucketNameValidator.GetValidationError(name));
    }

    [Fact]
    public void Hyphens_In_Middle_Are_Valid()
    {
        Assert.Null(BucketNameValidator.GetValidationError("a--b"));
    }
}
