using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WordApp.Auth;
using WordApp.Data;
using WordApp.Models;

namespace WordApp.Controllers;

[ApiController]
[Route("admin/api/idioms")]
[ServiceFilter(typeof(AdminAuthFilter))]
public class AdminIdiomsController : ControllerBase
{
    public record IdiomDto(string Title, string Category, string Example, string Explanation, string Note, string Status);

    private readonly AppDbContext _db;
    public AdminIdiomsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? status)
    {
        var q = _db.Idioms.AsNoTracking();
        if (!string.IsNullOrEmpty(status))
            q = q.Where(i => i.Status == status);

        return Ok(await q.ToListAsync());
    }

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] IdiomDto dto)
    {
        var idiom = new Idiom
        {
            Id = Guid.NewGuid().ToString(),
            Title = dto.Title,
            Category = dto.Category,
            Example = dto.Example,
            Explanation = dto.Explanation,
            Note = dto.Note,
            Status = dto.Status
        };
        _db.Idioms.Add(idiom);
        await _db.SaveChangesAsync();
        return Ok(idiom);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Put(string id, [FromBody] IdiomDto dto)
    {
        var idiom = await _db.Idioms.FindAsync(id);
        if (idiom == null) return NotFound();

        idiom.Title = dto.Title;
        idiom.Category = dto.Category;
        idiom.Example = dto.Example;
        idiom.Explanation = dto.Explanation;
        idiom.Note = dto.Note;
        idiom.Status = dto.Status;
        await _db.SaveChangesAsync();
        return Ok(idiom);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var idiom = await _db.Idioms.FindAsync(id);
        if (idiom == null) return NotFound();

        _db.Idioms.Remove(idiom);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
