using System.Net.Http.Json;
using LexiFlow.Models;
using LexiFlow.Services;

var checks = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
    checks++;
    Console.WriteLine($"PASS {description}");
}

const string storageTestKey = "large-json-check";
Preferences.Set(storageTestKey, "legacy contents");
Check(LargePreferenceStore.Get(storageTestKey, "") == "legacy contents", "large storage reads existing unchunked records without deleting them");
var largeText = string.Concat(Enumerable.Repeat("한글🙂 learning content ", 1500));
LargePreferenceStore.Set(storageTestKey, largeText);
Check(LargePreferenceStore.Get(storageTestKey, "") == largeText, "large content round-trips under the Windows 8 KiB per-value limit");
Preferences.RemainingWritesBeforeFailure = 2;
try { LargePreferenceStore.Set(storageTestKey, largeText + largeText); throw new InvalidOperationException("Interrupted write succeeded."); }
catch (IOException) { Check(true, "interrupted chunk write is reported to the caller"); }
finally { Preferences.RemainingWritesBeforeFailure = -1; }
Check(LargePreferenceStore.Get(storageTestKey, "") == largeText, "interrupted save preserves the previous complete generation");
LargePreferenceStore.Remove(storageTestKey);
Check(!LargePreferenceStore.Contains(storageTestKey) && !Preferences.ContainsKey(storageTestKey), "large storage removes its active generation and legacy value");

var words = Enumerable.Range(1, 40).Select(index => new Word
{
    Id = $"word-{index}", English = "plan", Meaning = "계획",
    Example = $"We have a plan for project {index}. (우리는 프로젝트 {index}의 계획이 있다.) — 해설"
}).ToList();
var session = new SessionService { CurrentUserId = "test-a" };
var catalog = new SentenceCatalogService(session);
var course = new CourseService(session, catalog);
var stages = course.GetStages(words);
Check(stages.Count == 5 && stages.All(stage => stage.Exercises.Count == 8), "40 examples form five distinct stages");
Check(stages.SelectMany(stage => stage.Exercises).Select(item => item.Id).Distinct().Count() == 40, "stage contents do not overlap");
Check(course.StartStage(stages[1].Id).Count == 0, "locked stages cannot be opened through navigation");
Check(course.Complete(stages[0].Id, 4, 4) == 0 && course.Stars(stages[0].Id) == 0, "partial completion does not unlock next stage");
Check(course.Complete(stages[0].Id, 8, 8) == 3, "perfect completion gives three stars");
Check(course.StartStage(stages[1].Id).Count == 8, "completion unlocks next stage");
course.Complete(stages[0].Id, 0, 8);
Check(course.Stars(stages[0].Id) == 3, "replay never reduces best stars");
course = new CourseService(session, catalog);
Check(course.Stars(stages[0].Id) == 3, "progress persists across service recreation");
var initialIds = stages[0].Exercises.Select(item => item.Id).ToArray();
Check(course.GetStages(words.AsEnumerable().Reverse())[0].Exercises.Select(item => item.Id).SequenceEqual(initialIds), "server reorder cannot move existing stage contents");
Check(course.StartStage(stages[0].Id).Select(item => item.Kind).Distinct().Count() == 3, "first unit includes meaning, arrangement and blanks");
for (var i = 1; i < 3; i++) course.Complete(stages[i].Id, 7, 8);
Check(course.StartStage(stages[3].Id).Select(item => item.Kind).Distinct().Count() == 4, "later units include all four exercise modes");
session.CurrentUserId = "test-b";
Check(course.Stars(stages[0].Id) == 0 && course.StartStage(stages[1].Id).Count == 0, "course progress is isolated per account");

session.CurrentUserId = "review";
var first = catalog.CreateLesson(words, [], [], 10);
var second = new SentenceCatalogService(session).CreateLesson(words, [], [], 10);
Check(!first.Select(item => item.Id).Intersect(second.Select(item => item.Id)).Any(), "review avoids prior lesson across recreation");
Check(first.Select(item => item.Kind).Distinct().Count() == 4, "review contains all four exercise modes");
Check(first.Where(item => item.Kind == SentenceExerciseKind.ChooseMeaning).All(item => item.Choices.Count == 3 && item.Choices.Contains(item.Korean)), "choice exercises contain correct translation and distractors");
Check(SentenceAnswer.Matches("  WE have a plan! ", "We have a plan."), "grading ignores case, punctuation and repeated whitespace");
Check(!SentenceAnswer.Matches("Wehave a plan", "We have a plan"), "grading preserves word boundaries");
Check(!SentenceAnswer.Matches("We a have plan", "We have a plan"), "grading rejects wrong word order");
Check(SentenceAnswer.Matches("I don’t know.", "I don't know"), "grading accepts curly apostrophes");
Check(SentenceAnswer.Tokens("The plan is the plan.").Count(token => token == "plan") == 2, "word tiles preserve duplicate words");
Check(SentenceAnswer.Tokens("It costs 25 dollars.").Contains("25"), "word tiles preserve numbers");
var phrase = new SentenceExercise { Id = "phrase", TargetWord = "in order to", Sentence = "We learn in order to improve." };
Check(SentenceCatalogService.WithKind(phrase, SentenceExerciseKind.FillBlank).Kind == SentenceExerciseKind.ArrangeWords, "multiword targets cannot create impossible single-token blanks");

var core = BuiltInVocabulary.GetWords();
Check(core.Count == 360 && core.Select(word => word.Id).Distinct().Count() == 360, "360 bundled words have stable unique identities");
Check(core.GroupBy(word => word.Topic).Count() == 12 && core.GroupBy(word => word.Topic).All(group => group.Count() == 30), "12 vocabulary topics contain 30 words each");
Check(core.All(word => !string.IsNullOrWhiteSpace(word.PartOfSpeech) && word.Level is "기초" or "일상" or "확장"), "all bundled entries include difficulty and part of speech");
session.CurrentUserId = "core-course";
var coreStages = course.GetStages(core);
Check(coreStages.Count == 45 && coreStages.Select(stage => stage.Unit).Distinct().Count() == 15, "bundled course extends to 45 stages across 15 units");
Check(coreStages.SelectMany(stage => stage.Exercises).Count() == 360, "every bundled example becomes a usable course exercise");
Check(coreStages.SelectMany(stage => stage.Exercises).All(item => item.Choices.Distinct().Count() == 3 && item.Choices.Contains(item.Korean)), "bundled meaning choices are distinct and contain the answer");
Check(coreStages.SelectMany(stage => stage.Exercises).All(item => item.Vocabulary.Any(hint => hint.Word.Equals(item.TargetWord, StringComparison.OrdinalIgnoreCase))), "every target has a clickable vocabulary hint");
var savedStageIds = coreStages[0].Exercises.Select(item => item.Id).ToArray();
course.Complete(coreStages[0].Id, 8, 8);
var extended = course.GetStages(core.Concat(words));
Check(extended.Count == 50 && extended[0].Exercises.Select(item => item.Id).SequenceEqual(savedStageIds) && course.Stars(extended[0].Id) == 3, "new content appends without moving completed stages or stars");
var chosen = core[0];
var merged = BuiltInVocabulary.Merge([new Word { Id = "existing-server-id", English = chosen.English.ToUpperInvariant(), Meaning = "기존의 뜻", Example = "기존 예문" }]);
Check(merged.Count == 360 && merged[0].Id == "existing-server-id" && merged[0].Meaning == "기존의 뜻" && merged[0].Example == "기존 예문", "merge preserves existing server identity and meaning without duplicate headwords");
core[0].UserStatus = "Mastered";
Check(BuiltInVocabulary.GetWords()[0].UserStatus is null, "bundled word instances do not leak another account's badges");
var queryWords = new[]
{
    new Word { Id = "a", English = "Apple", Meaning = "사과", Example = "A red apple.", Topic = "음식", Level = "기초", UserStatus = "Mastered" },
    new Word { Id = "b", English = "bread", Meaning = "빵", Example = "Fresh bread.", Topic = "음식", Level = "기초", UserStatus = "Learning" },
    new Word { Id = "c", English = "Cloud", Meaning = "구름", Topic = "날씨", Level = "일상", UserStatus = "Unknown" }
};
Check(WordLibraryQuery.Filter(queryWords, "APPLE 사과").Single().Id == "a", "library search is case-insensitive and matches all search terms across fields");
Check(WordLibraryQuery.Filter(queryWords, "fresh", "기초", "음식", "학습 중").Single().Id == "b", "library combines text, difficulty, topic and progress filters");
Check(WordLibraryQuery.Filter(queryWords, status: "미확인").Single().Id == "c", "unavailable progress is not mislabeled as a new word");
Check(WordLibraryQuery.Filter(queryWords, sort: WordLibraryQuery.ReverseAlphabetical).Select(word => word.Id).SequenceEqual(["c", "b", "a"]), "library supports deterministic reverse-alphabetical ordering");
Check(WordLibraryQuery.Filter(queryWords, "missing").Count == 0 && WordLibraryQuery.Filter(queryWords).Count == 3, "resetting filters restores words after an empty search");
Check(WordLibraryQuery.TopicOptions(queryWords).Count == 3, "topic picker deduplicates topics and includes an all-topics choice");
Check(WordLibraryQuery.SplitExample("I eat bread. (나는 빵을 먹는다.)") == ("I eat bread.", "나는 빵을 먹는다."), "detail separates inline Korean example translations");
Check(WordLibraryQuery.SplitExample("I eat bread.\n나는 빵을 먹는다.") == ("I eat bread.", "나는 빵을 먹는다."), "detail separates multiline example translations");
Check(WordLibraryQuery.SplitExample("Use a pen (or pencil).") == ("Use a pen (or pencil).", ""), "detail preserves ordinary English parentheses");
session.CurrentUserId = "core-no-repeat";
var seenCore = new HashSet<string>();
for (var batch = 0; batch < 36; batch++)
    foreach (var item in catalog.CreateLesson([], [], [], 10))
        if (!seenCore.Add(item.Id)) throw new InvalidOperationException("Bundled lessons repeated before completing their first pass");
Check(seenCore.Count == 360, "36 consecutive bundled lessons cover all 360 words without repetition");
session.CurrentUserId = "review-memory";
var memory = new SentenceReviewService(session);
var now = DateTime.UtcNow;
memory.Record(chosen.Id, false, now);
Check(memory.Read()[chosen.Id].NeedsPractice && memory.Read()[chosen.Id].DueAt == now.AddMinutes(10), "wrong answer is remembered with a ten-minute review interval");
Check(catalog.CreateMistakeLesson(core).Single().Id == chosen.Id, "mistake-only lesson selects recorded weak vocabulary");
memory.Record(chosen.Id, true, now);
Check(memory.Read()[chosen.Id].NeedsPractice, "one later correct answer does not immediately erase a mistake");
memory.Record(chosen.Id, true, now);
Check(!memory.Read()[chosen.Id].NeedsPractice && memory.Read()[chosen.Id].DueAt == now.AddDays(3), "two consecutive correct answers graduate a mistake and extend review spacing");
Check(catalog.CreateMistakeLesson(core).Count == 0, "mastered mistakes leave the focused review queue");
session.CurrentUserId = "different-review-account";
Check(memory.Read().Count == 0, "review memory is isolated by account");
session.CurrentUserId = "review-memory";
LocalAccountData.DeleteCurrent(session);
Check(memory.Read().Count == 0, "account deletion also removes device-local review memory");
session.CurrentUserId = "due-reviews";
var dueTargets = core.Take(10).Select(word => word.Id).ToHashSet();
foreach (var id in dueTargets) memory.Record(id, false, now.AddDays(-1));
var dueLesson = catalog.CreateLesson(core, [], [], 10);
Check(dueLesson.Count == 10 && dueLesson.Take(3).All(item => dueTargets.Contains(item.Id)) && dueLesson.Select(item => item.Id).Distinct().Count() == 10,
    "due reviews reserve three of ten slots without repeating a sentence within a lesson");
session.CurrentUserId = "future-reviews";
memory.Record(core[0].Id, true, now);
Check(memory.Read()[core[0].Id].DueAt == now.AddDays(1), "first unassisted correct answer schedules next-day review");

if (args.Contains("--live"))
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    var liveWords = await http.GetFromJsonAsync<List<Word>>("https://lexiflow.duckdns.org/words") ?? [];
    session.CurrentUserId = "live-check";
    var pool = catalog.GetCoursePool(liveWords);
    var liveStages = course.GetStages(liveWords);
    Check(pool.Count > 16, "live server has more than the fallback 16 examples");
    Check(liveStages.SelectMany(stage => stage.Exercises).Count() == pool.Count, "every valid server example reaches the course");
    var seen = new HashSet<string>();
    for (var i = 0; i < pool.Count / 10; i++)
    {
        var lesson = catalog.CreateLesson(liveWords, [], [], 10);
        Check(lesson.All(item => seen.Add(item.Id)), $"live review batch {i + 1} has no repeats");
    }
    Console.WriteLine($"Live data: {liveWords.Count} words, {pool.Count} sentences, {liveStages.Count} stages.");
}
Console.WriteLine($"{checks} checks passed.");
