using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using WordApp.Auth;
using WordApp.Data;
using WordApp.Models;

namespace WordApp.Controllers;

[ApiController]
[Authorize]
[Route("users/{userId}/idiom-progress")]
public class IdiomProgressController : ControllerBase
{
    public record UpsertProgressDto(string IdiomId, bool Correct, string? Status);

    private readonly AppDbContext _db;
    public IdiomProgressController(AppDbContext db) => _db = db;

    // GET /users/{userId}/idiom-progress
    [HttpGet]
    public async Task<IActionResult> Get(string userId)
    {
        if (!AccountSecurity.IsOwnProgress(User, userId)) return Forbid();
        var list = await _db.IdiomProgresses
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .ToListAsync();
        return Ok(list);
    }

    // POST /users/{userId}/idiom-progress — upsert one idiom's progress.
    [HttpPost]
    public async Task<IActionResult> Upsert(string userId, [FromBody] UpsertProgressDto dto)
    {
        if (!AccountSecurity.IsOwnProgress(User, userId)) return Forbid();
        if (dto.Status is not null && dto.Status is not ("New" or "Learning" or "Mastered"))
            return BadRequest("Invalid progress status.");
        if (!await _db.Idioms.AnyAsync(item => item.Id == dto.IdiomId))
            return BadRequest("Unknown learning item.");
        if (string.IsNullOrEmpty(dto.IdiomId))
            return BadRequest("IdiomId is required.");

        var row = await _db.IdiomProgresses
            .FirstOrDefaultAsync(p => p.UserId == userId && p.IdiomId == dto.IdiomId);

        if (row == null)
        {
            row = new IdiomProgress
            {
                UserId = userId,
                IdiomId = dto.IdiomId,
                Status = dto.Status ?? "Learning"
            };
            _db.IdiomProgresses.Add(row);
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
