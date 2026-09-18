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

if (args.Contains("--live"))
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    var liveWords = await http.GetFromJsonAsync<List<Word>>("http://lexiflow.duckdns.org:5276/words") ?? [];
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
