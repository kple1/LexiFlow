namespace LexiFlow.Models;

public sealed class CourseStage
{
    public string Id { get; set; } = "";
    public int Number { get; set; }
    public List<SentenceExercise> Exercises { get; set; } = [];
    public int Unit => Math.Min((Number - 1) / 3, 3);
    public string UnitTitle => Unit switch
    {
        0 => "문장에 익숙해지기",
        1 => "어순과 표현 만들기",
        2 => "직접 문장 쓰기",
        _ => "긴 문장에 도전하기"
    };
    public string Tip => Unit switch
    {
        0 => "짧은 문장의 뜻을 읽고, 빈칸과 단어 순서를 연습해요.",
        1 => "주어 → 동사 → 나머지 표현 순으로 문장을 조립해 보세요.",
        2 => "해석과 핵심 표현을 보고 예문 전체를 써 보세요. 막히면 예문 힌트를 열 수 있어요.",
        _ => "수식어와 연결 표현까지 살려 네 가지 방식으로 복습해요."
    };
}
