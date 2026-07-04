using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WordApp.Data;
using WordApp.Models;

namespace WordApp.Controllers;

[ApiController]
[Route("users/{userId}/progress")]
public class ProgressController : ControllerBase
{
    // Client sends the outcome of reviewing one word. Status is optional:
    // the client owns New/Learning/Mastered transitions; if omitted we keep the existing status.
    public record UpsertProgressDto(string WordId, bool Correct, string? Status);

    private readonly AppDbContext _db;
    public ProgressController(AppDbContext db) => _db = db;

    // GET /users/{userId}/progress
    [HttpGet]
    public async Task<IActionResult> Get(string userId)
    {
        var list = await _db.WordProgresses
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .ToListAsync();
        return Ok(list);
    }

    // POST /users/{userId}/progress  — upsert one word's progress after a review.
    [HttpPost]
    public async Task<IActionResult> Upsert(string userId, [FromBody] UpsertProgressDto dto)
    {
        if (string.IsNullOrEmpty(dto.WordId))
            return BadRequest("WordId is required.");

        var row = await _db.WordProgresses
            .FirstOrDefaultAsync(p => p.UserId == userId && p.WordId == dto.WordId);

        if (row == null)
        {
            row = new WordProgress
            {
                UserId = userId,
                WordId = dto.WordId,
                Status = dto.Status ?? "Learning"
            };
            _db.WordProgresses.Add(row);
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
