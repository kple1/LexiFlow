using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using WordApp.Auth;
using WordApp.Data;
using WordApp.Models;

namespace WordApp.Controllers;

[ApiController]
[Authorize]
[Route("users/{userId}/grammar-progress")]
public class GrammarProgressController : ControllerBase
{
    public record UpsertProgressDto(string GrammarId, bool Correct, string? Status);

    private readonly AppDbContext _db;
    public GrammarProgressController(AppDbContext db) => _db = db;

    // GET /users/{userId}/grammar-progress
    [HttpGet]
    public async Task<IActionResult> Get(string userId)
    {
        if (!AccountSecurity.IsOwnProgress(User, userId)) return Forbid();
        var list = await _db.GrammarProgresses
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .ToListAsync();
        return Ok(list);
    }

    // POST /users/{userId}/grammar-progress  — upsert one grammar point's progress.
    [HttpPost]
    public async Task<IActionResult> Upsert(string userId, [FromBody] UpsertProgressDto dto)
    {
        if (!AccountSecurity.IsOwnProgress(User, userId)) return Forbid();
        if (dto.Status is not null && dto.Status is not ("New" or "Learning" or "Mastered"))
            return BadRequest("Invalid progress status.");
        if (!await _db.Grammars.AnyAsync(item => item.Id == dto.GrammarId))
            return BadRequest("Unknown learning item.");
        if (string.IsNullOrEmpty(dto.GrammarId))
            return BadRequest("GrammarId is required.");

        var row = await _db.GrammarProgresses
            .FirstOrDefaultAsync(p => p.UserId == userId && p.GrammarId == dto.GrammarId);

        if (row == null)
        {
            row = new GrammarProgress
            {
                UserId = userId,
                GrammarId = dto.GrammarId,
                Status = dto.Status ?? "Learning"
            };
            _db.GrammarProgresses.Add(row);
        }
        else if (dto.Status != null)
        {
            row.Status = dto.Status;
        }

        if (dto.Correct)
            row.CorrectCount++;
        else
            row.WrongCount++;

        row.LastReviewed = DateTime.UtcNow;
        row.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(row);
    }
}
