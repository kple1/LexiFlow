using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WordApp.Auth;
using WordApp.Data;
using WordApp.Models;

namespace WordApp.Controllers;

[ApiController]
[Route("admin/api/grammars")]
[ServiceFilter(typeof(AdminAuthFilter))]
public class AdminGrammarsController : ControllerBase
{
    public record GrammarDto(string Title, string Category, string Example, string Explanation, string Note, string Status);

    private readonly AppDbContext _db;
    public AdminGrammarsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? status)
    {
        var q = _db.Grammars.AsNoTracking();
        if (!string.IsNullOrEmpty(status))
            q = q.Where(g => g.Status == status);

        return Ok(await q.ToListAsync());
    }

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] GrammarDto dto)
    {
        var grammar = new Grammar
        {
            Id = Guid.NewGuid().ToString(),
            Title = dto.Title,
            Category = dto.Category,
            Example = dto.Example,
            Explanation = dto.Explanation,
            Note = dto.Note,
            Status = dto.Status
        };
        _db.Grammars.Add(grammar);
        await _db.SaveChangesAsync();
        return Ok(grammar);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Put(string id, [FromBody] GrammarDto dto)
    {
        var grammar = await _db.Grammars.FindAsync(id);
        if (grammar == null) return NotFound();

        grammar.Title = dto.Title;
        grammar.Category = dto.Category;
        grammar.Example = dto.Example;
        grammar.Explanation = dto.Explanation;
        grammar.Note = dto.Note;
        grammar.Status = dto.Status;
        await _db.SaveChangesAsync();
        return Ok(grammar);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var grammar = await _db.Grammars.FindAsync(id);
        if (grammar == null) return NotFound();

        _db.Grammars.Remove(grammar);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
