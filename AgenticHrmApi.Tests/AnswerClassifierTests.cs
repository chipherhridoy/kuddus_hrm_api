using AgenticHrmApi.Services;
using Xunit;

namespace AgenticHrmApi.Tests;

public class AnswerClassifierTests
{
    [Theory]
    [InlineData("yes")] [InlineData("yeah")] [InlineData("go ahead")]
    [InlineData("do it")] [InlineData("ha")] [InlineData("haan")]
    [InlineData("ji")] [InlineData("thik ache")] [InlineData("accha")]
    public void Affirmatives(string s) => Assert.Equal(AnswerKind.Affirmative, AnswerClassifier.Classify(s));

    [Theory]
    [InlineData("no")] [InlineData("nope")] [InlineData("na")] [InlineData("lagbe na")]
    public void Negatives(string s) => Assert.Equal(AnswerKind.Negative, AnswerClassifier.Classify(s));

    [Theory]
    [InlineData("cancel")] [InlineData("never mind")] [InlineData("forget it")]
    [InlineData("thak")] [InlineData("baad dao")]
    public void Cancellings(string s) => Assert.Equal(AnswerKind.Cancelling, AnswerClassifier.Classify(s));

    [Theory]
    [InlineData("no, Saturday not Friday")]
    [InlineData("na, reason ta family wedding")]
    public void Negative_with_content_is_a_correction(string s) =>
        Assert.Equal(AnswerKind.Correction, AnswerClassifier.Classify(s));

    [Theory]
    [InlineData("what is the leave policy")] [InlineData("")]
    public void Everything_else(string s) => Assert.Equal(AnswerKind.Other, AnswerClassifier.Classify(s));

    [Fact]
    public void Is_case_and_punctuation_insensitive() =>
        Assert.Equal(AnswerKind.Affirmative, AnswerClassifier.Classify("  YES!  "));
}
