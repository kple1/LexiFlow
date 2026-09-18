using System.Text.RegularExpressions;

namespace LexiFlow.Services;

public static class SentenceAnswer
{
    public static string[] Tokens(string value)
        => Regex.Matches(value.Replace('’', '\''), @"[\p{L}\p{N}]+(?:'[\p{L}\p{N}]+)*")
            .Select(match => match.Value).ToArray();

    // Ignore punctuation/case, but retain word boundaries ("a part" != "apart").
    public static bool Matches(string input, string expected)
        => Tokens(input).Select(token => token.ToLowerInvariant())
            .SequenceEqual(Tokens(expected).Select(token => token.ToLowerInvariant()));
}
