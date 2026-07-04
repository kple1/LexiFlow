using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WordApp.Auth;
using WordApp.Data;
using WordApp.Models;

namespace WordApp.Controllers;

[ApiController]
[Route("admin/api/words")]
[ServiceFilter(typeof(AdminAuthFilter))]
public class AdminWordsController : ControllerBase
{
    public record WordDto(string English, string Meaning, string Status, string? Note, string? Example);

    private readonly AppDbContext _db;
    public AdminWordsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? status)
    {
        var q = _db.Words.AsNoTracking();
        if (!string.IsNullOrEmpty(status))
            q = q.Where(w => w.Status == status);

        var list = await q.OrderByDescending(w => w.NotionCreated).ToListAsync();
        return Ok(list);
    }

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] WordDto dto)
    {
        var word = new Word
        {
            Id = Guid.NewGuid().ToString(),
            English = dto.English,
            Meaning = dto.Meaning,
            Status = dto.Status,
            Note = dto.Note,
            Example = dto.Example,
            NotionCreated = DateTime.UtcNow,
            Source = "Manual"
        };
        _db.Words.Add(word);
        await _db.SaveChangesAsync();
        return Ok(word);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Put(string id, [FromBody] WordDto dto)
    {
        var word = await _db.Words.FindAsync(id);
        if (word == null) return NotFound();
        if (word.Source != "Manual")
            return Conflict("Notion 출처 단어는 Notion에서 수정하세요. 여기서 수정해도 다음 동기화 때 되돌아갑니다.");

        word.English = dto.English;
        word.Meaning = dto.Meaning;
        word.Status = dto.Status;
        word.Note = dto.Note;
        word.Example = dto.Example;
        await _db.SaveChangesAsync();
        return Ok(word);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var word = await _db.Words.FindAsync(id);
        if (word == null) return NotFound();
        if (word.Source != "Manual")
            return Conflict("Notion 출처 단어는 Notion에서 삭제하세요. 여기서 삭제해도 다음 동기화 때 다시 생성됩니다.");

        _db.Words.Remove(word);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
