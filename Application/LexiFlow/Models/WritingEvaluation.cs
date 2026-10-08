using System.Text.Json;
using System.Text.Json.Serialization;

namespace LexiFlow.Models;

public sealed class WritingEvaluation
{
    [JsonRequired]
    public string Verdict { get; init; } = "";

    [JsonRequired]
    public string Feedback { get; init; } = "";

    [JsonRequired]
    public string CorrectedAnswer { get; init; } = "";

    [JsonRequired]
    public string SuggestedAnswer { get; init; } = "";

    [JsonRequired]
    public IReadOnlyList<WritingCorrection> Corrections { get; init; } = [];

    // These are derived from a validated verdict, never trusted response flags.
    [JsonIgnore]
    public bool Accepted => Verdict == "accepted";

    [JsonIgnore]
    public bool IsDecidable => Verdict is "accepted" or "revise" or "incorrect";

    public void Validate(string? submittedAnswer = null)
    {
        if (Verdict is not ("accepted" or "revise" or "incorrect" or "uncertain"))
            throw new JsonException("The writing evaluation has an invalid verdict.");
        ValidateText(Feedback, 600, required: true);
        ValidateText(CorrectedAnswer, 1000, required: false);
        ValidateText(SuggestedAnswer, 1000, required: true);
        if (Corrections is null || Corrections.Count > 3)
            throw new JsonException("The writing evaluation has invalid corrections.");
        if (!ContainsKorean(Feedback) || !ContainsEnglish(SuggestedAnswer))
            throw new JsonException("The writing evaluation uses an invalid feedback language.");
        if (CorrectedAnswer.Length > 0 && !ContainsEnglish(CorrectedAnswer))
            throw new JsonException("The corrected answer must be English.");
        if (Verdict == "uncertain" && (Corrections.Count != 0 || CorrectedAnswer.Length != 0))
            throw new JsonException("An uncertain evaluation cannot claim a correction.");
        if (Verdict == "revise" && (string.IsNullOrWhiteSpace(CorrectedAnswer) || Corrections.Count == 0))
            throw new JsonException("A revision must explain its correction.");

        foreach (var correction in Corrections)
        {
            if (correction is null)
                throw new JsonException("The writing evaluation contains an invalid correction.");
            ValidateText(correction.Original, 1000, required: false);
            ValidateText(correction.Revised, 1000, required: false);
            ValidateText(correction.Reason, 300, required: true);
            // An insertion or deletion can have one empty side, but not both.
            if (string.IsNullOrWhiteSpace(correction.Original) && string.IsNullOrWhiteSpace(correction.Revised))
                throw new JsonException("The writing evaluation contains an empty correction.");
            if (!ContainsKorean(correction.Reason))
                throw new JsonException("Correction reasons must be Korean.");
            if (submittedAnswer is not null && correction.Original.Length > 0
                && !submittedAnswer.Contains(correction.Original, StringComparison.Ordinal))
                throw new JsonException("A correction does not match the submitted answer.");
            if (correction.Revised.Length > 0 && !CorrectedAnswer.Contains(correction.Revised, StringComparison.Ordinal))
                throw new JsonException("A correction does not match the corrected answer.");
        }
    }

    private static void ValidateText(string? value, int maximumLength, bool required)
    {
        if (value is null || value.Length > maximumLength || (required && string.IsNullOrWhiteSpace(value)))
            throw new JsonException("The writing evaluation contains missing or oversized text.");
    }

    private static bool ContainsKorean(string value) => value.Any(character => character is >= '가' and <= '힣');
    private static bool ContainsEnglish(string value)
        => value.Any(char.IsAsciiLetter) && !ContainsKorean(value);
}

public sealed class WritingCorrection
{
    [JsonRequired]
    public string Original { get; init; } = "";

    [JsonRequired]
    public string Revised { get; init; } = "";

    [JsonRequired]
    public string Reason { get; init; } = "";
}
