using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WordApp.Data;

namespace WordApp.Controllers
{
    [ApiController]
    [Route("[grammars]")]
    public class GrammarController : ControllerBase
    {
        private readonly AppDbContext _db;
        public GrammarController(AppDbContext db) => _db = db;

        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] string? status)
        {
            var q = _db.Grammars.AsNoTracking();
            if (!string.IsNullOrEmpty(status))
                q = q.Where(g => g.Status == status);

            var list = await q.ToListAsync();
            return Ok(list);
        }
    }
}
