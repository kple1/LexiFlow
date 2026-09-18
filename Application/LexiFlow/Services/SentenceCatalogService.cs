using System.Text.Json;
using System.Text.RegularExpressions;
using LexiFlow.Models;

namespace LexiFlow.Services;

/// <summary>
/// Builds personalized sentence lessons from server-managed words and examples.
/// The curated catalog below is only used when the server cannot supply enough
/// complete examples.
/// </summary>
public sealed class SentenceCatalogService
{
    private readonly SessionService _session;

    public SentenceCatalogService(SessionService session)
    {
        _session = session;
    }

    private static readonly IReadOnlyList<SentenceExercise> Catalog =
    [
        Exercise(
            "evidence", SentenceExerciseKind.ChooseMeaning,
            "The evidence was clear enough to support the decision.",
            "그 증거는 그 결정을 뒷받침할 만큼 명확했다.",
            "evidence", "증거",
            ["그 증거는 그 결정을 뒷받침할 만큼 명확했다.", "그 회의는 결정을 내리기 전에 취소되었다.", "그 보고서는 증거 없이 작성되었다."],
            ("evidence", "증거"), ("clear", "명확한"), ("support", "뒷받침하다"), ("decision", "결정")),

        Exercise(
            "feasible", SentenceExerciseKind.FillBlank,
            "We need a feasible plan before Friday.",
            "우리는 금요일 전까지 실행 가능한 계획이 필요하다.",
            "feasible", "실행 가능한",
            [],
            ("feasible", "실행 가능한"), ("plan", "계획"), ("before", "~전에")),

        Exercise(
            "alternative", SentenceExerciseKind.FillBlank,
            "Please choose an alternative route during construction.",
            "공사 중에는 대체 경로를 선택해 주세요.",
            "alternative", "대안의, 대체 가능한",
            [],
            ("alternative", "대안의, 대체 가능한"), ("route", "경로"), ("during", "~동안"), ("construction", "공사")),

        Exercise(
            "precise", SentenceExerciseKind.ChooseMeaning,
            "Every detail must be precise in the final report.",
            "최종 보고서의 모든 세부 사항은 정확해야 한다.",
            "precise", "정확한, 정밀한",
            ["최종 보고서의 모든 세부 사항은 정확해야 한다.", "최종 보고서는 내일까지 제출할 필요가 없다.", "그는 보고서의 모든 항목을 삭제했다."],
            ("detail", "세부 사항"), ("precise", "정확한, 정밀한"), ("final", "최종의"), ("report", "보고서")),

        Exercise(
            "alongside", SentenceExerciseKind.FillBlank,
            "She worked alongside the design team.",
            "그녀는 디자인 팀과 함께 일했다.",
            "alongside", "~와 함께, 나란히",
            [],
            ("worked", "일했다"), ("alongside", "~와 함께, 나란히"), ("design", "디자인, 설계")),

        Exercise(
            "occupied", SentenceExerciseKind.ChooseMeaning,
            "The meeting room is currently occupied.",
            "회의실은 현재 사용 중이다.",
            "occupied", "사용 중인, 점유된",
            ["회의실은 현재 사용 중이다.", "회의실은 오늘 하루 종일 비어 있다.", "회의 장소가 다른 건물로 바뀌었다."],
            ("meeting", "회의"), ("currently", "현재"), ("occupied", "사용 중인, 점유된")),

        Exercise(
            "mechanism", SentenceExerciseKind.FillBlank,
            "The safety mechanism stopped the machine.",
            "안전 장치가 기계를 멈췄다.",
            "mechanism", "장치, 기구",
            [],
            ("safety", "안전"), ("mechanism", "장치, 기구"), ("stopped", "멈췄다"), ("machine", "기계")),

        Exercise(
            "during", SentenceExerciseKind.ChooseMeaning,
            "He remained calm during the interview.",
            "그는 면접 동안 침착함을 유지했다.",
            "during", "~동안",
            ["그는 면접 동안 침착함을 유지했다.", "그는 면접 직전에 자리를 떠났다.", "그는 면접 질문을 모두 잊어버렸다."],
            ("remained", "계속 ~한 상태였다"), ("calm", "침착한"), ("during", "~동안"), ("interview", "면접")),

        Exercise(
            "audit", SentenceExerciseKind.FillBlank,
            "The team completed an audit last month.",
            "그 팀은 지난달에 감사를 완료했다.",
            "audit", "감사, 심사",
            [],
            ("completed", "완료했다"), ("audit", "감사, 심사"), ("last", "지난"), ("month", "달")),

        Exercise(
            "recognizes", SentenceExerciseKind.ChooseMeaning,
            "The new system recognizes your voice.",
            "새 시스템은 당신의 목소리를 인식한다.",
            "recognizes", "인식한다",
            ["새 시스템은 당신의 목소리를 인식한다.", "새 시스템은 음성을 자동으로 삭제한다.", "새 시스템은 수동으로만 작동한다."],
            ("system", "시스템"), ("recognizes", "인식한다"), ("voice", "목소리")),

        Exercise(
            "entirely", SentenceExerciseKind.FillBlank,
            "The result was entirely different from what we expected.",
            "결과는 우리가 예상한 것과 완전히 달랐다.",
            "entirely", "완전히, 전적으로",
            [],
            ("result", "결과"), ("entirely", "완전히, 전적으로"), ("different", "다른"), ("expected", "예상했다")),

        Exercise(
            "adhered", SentenceExerciseKind.ChooseMeaning,
            "The team adhered to the style guide throughout the project.",
            "그 팀은 프로젝트 내내 스타일 가이드를 준수했다.",
            "adhered", "준수했다, 고수했다",
            ["그 팀은 프로젝트 내내 스타일 가이드를 준수했다.", "그 팀은 프로젝트가 끝난 뒤 가이드를 만들었다.", "그 팀은 모든 디자인 파일을 폐기했다."],
            ("adhered", "준수했다, 고수했다"), ("style", "스타일"), ("guide", "지침서"), ("throughout", "~내내"), ("project", "프로젝트")),

        Exercise(
            "deadline", SentenceExerciseKind.FillBlank,
            "We moved the deadline to give everyone more time.",
            "모두에게 시간을 더 주기 위해 마감일을 옮겼다.",
            "deadline", "마감일",
            [],
            ("moved", "옮겼다"), ("deadline", "마감일"), ("everyone", "모두"), ("time", "시간")),

        Exercise(
            "reliable", SentenceExerciseKind.ChooseMeaning,
            "A reliable backup can prevent serious data loss.",
            "신뢰할 수 있는 백업은 심각한 데이터 손실을 막을 수 있다.",
            "reliable", "신뢰할 수 있는",
            ["신뢰할 수 있는 백업은 심각한 데이터 손실을 막을 수 있다.", "백업 파일은 항상 더 많은 오류를 만든다.", "데이터 손실은 백업과 아무 관련이 없다."],
            ("reliable", "신뢰할 수 있는"), ("backup", "백업"), ("prevent", "예방하다"), ("serious", "심각한"), ("loss", "손실")),

        Exercise(
            "available", SentenceExerciseKind.FillBlank,
            "The updated document is now available online.",
            "업데이트된 문서를 이제 온라인에서 이용할 수 있다.",
            "available", "이용 가능한",
            [],
            ("updated", "업데이트된"), ("document", "문서"), ("available", "이용 가능한"), ("online", "온라인에서")),

        Exercise(
            "improve", SentenceExerciseKind.ChooseMeaning,
            "Regular feedback helps us improve the product.",
            "정기적인 피드백은 제품을 개선하는 데 도움이 된다.",
            "improve", "개선하다",
            ["정기적인 피드백은 제품을 개선하는 데 도움이 된다.", "피드백이 많으면 제품 개발을 중단해야 한다.", "제품은 정기적으로 가격만 변경한다."],
            ("regular", "정기적인"), ("feedback", "피드백"), ("improve", "개선하다"), ("product", "제품"))
    ];

    public IReadOnlyList<SentenceExercise> CreateLesson(
        IEnumerable<Word> words,
        IEnumerable<WordProgress> progress,
        IEnumerable<ArchivedWord> archivedWords,
        int count = 10)
    {
        var serverExercises = BuildServerExercises(words).ToList();
        var candidates = serverExercises.ToList();

        if (candidates.Count < count)
        {
            var existingTargets = candidates
                .Select(item => Normalize(item.TargetWord))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            candidates.AddRange(Catalog.Where(item => !existingTargets.Contains(Normalize(item.TargetWord))));
        }

        return MixKinds(SelectWithoutRepeats(candidates, progress, archivedWords, count));
    }

    public IReadOnlyList<SentenceExercise> CreateLesson(int count = 10)
        => MixKinds(SelectWithoutRepeats(GetCoursePool([]), [], [], count));

    public IReadOnlyList<SentenceExercise> GetCoursePool(IEnumerable<Word> words)
    {
        var pool = BuildServerExercises(words).ToList();
        if (pool.Count == 0) pool = Catalog.ToList();
        var translations = pool.Select(item => item.Korean).Distinct().ToList();
        return pool.Select(item => new SentenceExercise
        {
            Id = item.Id, Sentence = item.Sentence, Korean = item.Korean,
            TargetWord = item.TargetWord, TargetMeaning = item.TargetMeaning, Kind = item.Kind,
            Vocabulary = item.Vocabulary,
            Choices = translations.Where(value => value != item.Korean).Take(2).Prepend(item.Korean).ToList()
        }).ToList();
    }

    public static SentenceExercise WithKind(SentenceExercise item, SentenceExerciseKind kind)
    {
        if (kind == SentenceExerciseKind.ChooseMeaning && item.Choices.Count < 3)
            kind = SentenceExerciseKind.ArrangeWords;
        if (kind == SentenceExerciseKind.FillBlank && !Regex.IsMatch(item.TargetWord, @"^[\p{L}']+$"))
            kind = SentenceExerciseKind.ArrangeWords;
        return new SentenceExercise
        {
            Id = item.Id, Sentence = item.Sentence, Korean = item.Korean,
            TargetWord = item.TargetWord, TargetMeaning = item.TargetMeaning, Kind = kind,
            Choices = item.Choices, Vocabulary = item.Vocabulary
        };
    }

    private static IReadOnlyList<SentenceExercise> MixKinds(IReadOnlyList<SentenceExercise> items)
        => items.Select((item, index) => WithKind(item, (SentenceExerciseKind)(index % 4))).ToList();

    private static IEnumerable<SentenceExercise> BuildServerExercises(IEnumerable<Word> words)
    {
        var parsed = words
            .Select(ParseServerSentence)
            .Where(item => item is not null)
            .Cast<ServerSentence>()
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        var translations = parsed
            .Select(item => item.Korean)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var item in parsed)
        {
            var canFillBlank = Regex.IsMatch(item.TargetWord, @"^[\p{L}']+$");
            var kind = SentenceExerciseKind.ChooseMeaning;

            var choices = translations
                    .Where(value => !value.Equals(item.Korean, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(_ => Random.Shared.Next())
                    .Take(2)
                    .Prepend(item.Korean)
                    .ToList();

            if (kind == SentenceExerciseKind.ChooseMeaning && choices.Count < 3)
            {
                kind = canFillBlank ? SentenceExerciseKind.FillBlank : SentenceExerciseKind.ArrangeWords;
            }

            yield return new SentenceExercise
            {
                Id = item.Id,
                Kind = kind,
                Sentence = item.Sentence,
                Korean = item.Korean,
                TargetWord = item.TargetWord,
                TargetMeaning = item.TargetMeaning,
                Choices = choices,
                Vocabulary =
                [
                    new VocabularyHint
                    {
                        Word = item.TargetWord,
                        Meaning = item.TargetMeaning
                    }
                ]
            };
        }
    }

    private IReadOnlyList<SentenceExercise> SelectWithoutRepeats(
        IEnumerable<SentenceExercise> source,
        IEnumerable<WordProgress> progress,
        IEnumerable<ArchivedWord> archivedWords,
        int count)
    {
        var candidates = source
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        if (candidates.Count == 0)
            return [];

        count = Math.Clamp(count, 1, candidates.Count);
        var candidateIds = candidates
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var history = LoadHistory()
            .Where(candidateIds.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var historySet = history.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var progressByWordId = progress
            .Where(item => !string.IsNullOrWhiteSpace(item.WordId))
            .GroupBy(item => item.WordId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        var archived = archivedWords
            .Select(item => Normalize(item.Word))
            .Where(item => item.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var selected = Rank(candidates.Where(item => !historySet.Contains(item.Id)), progressByWordId, archived)
            .Take(count)
            .ToList();

        if (selected.Count < count)
        {
            var selectedIds = selected
                .Select(item => item.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            selected.AddRange(Rank(candidates.Where(item => !selectedIds.Contains(item.Id)), progressByWordId, archived)
                .Take(count - selected.Count));

            // A complete pass through the catalog finished. The current lesson
            // becomes the beginning of the next no-repeat cycle.
            history = selected.Select(item => item.Id).ToList();
        }
        else
        {
            history.AddRange(selected.Select(item => item.Id));
        }

        SaveHistory(history);
        return selected;
    }

    private static IEnumerable<SentenceExercise> Rank(
        IEnumerable<SentenceExercise> source,
        IReadOnlyDictionary<string, WordProgress> progress,
        IReadOnlySet<string> archived)
        => source
            .Select(item => new
            {
                Exercise = item,
                Score = Priority(item, progress, archived),
                Shuffle = Random.Shared.Next()
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Shuffle)
            .Select(item => item.Exercise);

    private static int Priority(
        SentenceExercise exercise,
        IReadOnlyDictionary<string, WordProgress> progress,
        IReadOnlySet<string> archived)
    {
        var score = 0;
        if (archived.Contains(Normalize(exercise.TargetWord)))
            score += 10_000;
        else if (Regex.Matches(exercise.Sentence, @"[\p{L}']+")
            .Cast<Match>()
            .Select(match => Normalize(match.Value))
            .Any(archived.Contains))
            score += 8_000;

        if (!progress.TryGetValue(exercise.Id, out var state))
            return score + 1_000;

        score += state.WrongCount * 700;
        score -= state.CorrectCount * 40;
        if (state.Status.Equals("Learning", StringComparison.OrdinalIgnoreCase))
            score += 4_000;
        else if (state.Status.Equals("Mastered", StringComparison.OrdinalIgnoreCase))
            score -= 1_500;

        if (state.LastReviewed is DateTime reviewed)
            score += Math.Min((DateTime.UtcNow - reviewed.ToUniversalTime()).Days, 365);
        return score;
    }

    private static ServerSentence? ParseServerSentence(Word word)
    {
        if (string.IsNullOrWhiteSpace(word.English)
            || string.IsNullOrWhiteSpace(word.Meaning)
            || string.IsNullOrWhiteSpace(word.Example))
            return null;

        var target = word.English.Trim();
        var translation = Regex.Matches(word.Example, @"\((?<value>[^()]*)\)")
            .Cast<Match>()
            .FirstOrDefault(match => Regex.IsMatch(match.Groups["value"].Value, "[가-힣]"));
        if (translation is null)
            return null;

        var sentence = word.Example[..translation.Index].Trim();
        sentence = Regex.Replace(sentence, @"\s*[—–-]\s*$", "").Trim();
        var korean = translation.Groups["value"].Value.Trim();
        if (sentence.Length < 5 || korean.Length < 2 || !ContainsTarget(sentence, target))
            return null;

        return new ServerSentence(
            string.IsNullOrWhiteSpace(word.Id) ? $"server:{Normalize(target)}" : word.Id,
            sentence,
            korean,
            target,
            word.Meaning.Trim());
    }

    private static bool ContainsTarget(string sentence, string target)
        => Regex.IsMatch(
            sentence,
            $@"(?<![\p{{L}}']){Regex.Escape(target)}(?![\p{{L}}'])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private List<string> LoadHistory()
    {
        try
        {
            var json = Preferences.Get(HistoryKey(), "[]");
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void SaveHistory(IEnumerable<string> history)
    {
        try
        {
            Preferences.Set(HistoryKey(), JsonSerializer.Serialize(history.Distinct(StringComparer.OrdinalIgnoreCase)));
        }
        catch
        {
            // Lesson generation should still work if device preferences are unavailable.
        }
    }

    private string HistoryKey()
    {
        return LocalAccountData.Key(_session, "history");
    }

    private static string Normalize(string value)
        => Regex.Replace(value.Trim().ToLowerInvariant(), @"[^\p{L}\p{N}']", "");

    private sealed record ServerSentence(
        string Id,
        string Sentence,
        string Korean,
        string TargetWord,
        string TargetMeaning);

    private static SentenceExercise Exercise(
        string id,
        SentenceExerciseKind kind,
        string sentence,
        string korean,
        string targetWord,
        string targetMeaning,
        IReadOnlyList<string> choices,
        params (string Word, string Meaning)[] vocabulary)
        => new()
        {
            Id = id,
            Kind = kind,
            Sentence = sentence,
            Korean = korean,
            TargetWord = targetWord,
            TargetMeaning = targetMeaning,
            Choices = choices,
            Vocabulary = vocabulary
                .Select(item => new VocabularyHint { Word = item.Word, Meaning = item.Meaning })
                .ToList()
        };
}
