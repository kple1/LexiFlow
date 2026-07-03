using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WordApp.Data;
using WordApp.Models;

namespace WordApp.Controllers
{
    [ApiController]
    [Route("users")]
    public class UserController : ControllerBase
    {
        public record ChangePwDto(string Pw);
        private readonly AppDbContext _db;
        public UserController(AppDbContext db) => _db = db;

        [HttpGet("{id}")]
        public async Task<IActionResult> Get(int id)
        {
            var q = await _db.Users.FindAsync(id);
            if (q == null)
                return NotFound();
            return Ok(q);
        }

        [HttpPost]
        public async Task<IActionResult> Post([FromBody] User user)
        {
            user.Pw = BCrypt.Net.BCrypt.HashPassword(user.Pw);
            _db.Users.Add(user);
            await _db.SaveChangesAsync();
            return CreatedAtAction(nameof(Get), new { id = user.Id }, user);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var user = await _db.Users.FindAsync(id);
            if (user == null)
                return NotFound();
            _db.Users.Remove(user);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        [HttpPatch("{id}")]
        public async Task<IActionResult> Patch(int id, [FromBody] ChangePwDto dto)
        {
            var q = await _db.Users.FindAsync(id);
            if (q == null)
                return NotFound();
            q.Pw = BCrypt.Net.BCrypt.HashPassword(dto.Pw);
            await _db.SaveChangesAsync();
            return Ok(q);
        }
    }
}
