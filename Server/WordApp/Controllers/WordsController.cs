using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WordApp.Data;

namespace WordApp.Controllers;

[ApiController]
[Route("words")]
public class WordsController : ControllerBase
{
    private readonly AppDbContext _db;
    public WordsController(AppDbContext db) => _db = db;

    // GET /words  또는  GET /words?status=모름
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? status)
    {
        var q = _db.Words.AsNoTracking();
        if (!string.IsNullOrEmpty(status))
            q = q.Where(w => w.Status == status);

        var list = await q.OrderByDescending(w => w.NotionCreated).ToListAsync();
        return Ok(list);
    }
}