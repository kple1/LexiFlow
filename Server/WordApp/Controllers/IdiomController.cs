using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WordApp.Data;

namespace WordApp.Controllers
{
    [ApiController]
    [Route("idioms")]
    public class IdiomController : ControllerBase
    {
        private readonly AppDbContext _db;
        public IdiomController(AppDbContext db) => _db = db;

        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] string? status)
        {
            var q = _db.Idioms.AsNoTracking();
            if (!string.IsNullOrEmpty(status))
                q = q.Where(i => i.Status == status);

            var list = await q.ToListAsync();
            return Ok(list);
        }
    }
}
