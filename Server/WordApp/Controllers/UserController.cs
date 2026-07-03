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
        public record RegisterDto(string UserId, string Pw);
        public record LoginDto(string UserId, string Pw);
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

        // Sign up. Uses a DTO because User.Pw is [JsonIgnore], which would otherwise
        // drop the incoming password on deserialization and store an empty hash.
        [HttpPost]
        public async Task<IActionResult> Post([FromBody] RegisterDto dto)
        {
            if (await _db.Users.AnyAsync(u => u.UserId == dto.UserId))
                return Conflict("User ID already exists.");

            var user = new User
            {
                UserId = dto.UserId,
                Pw = BCrypt.Net.BCrypt.HashPassword(dto.Pw)
            };
            _db.Users.Add(user);
            await _db.SaveChangesAsync();
            return CreatedAtAction(nameof(Get), new { id = user.Id }, user);
        }

        // Sign in. Verifies the password server-side; the hash is never sent to clients.
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == dto.UserId);
            if (user == null || !BCrypt.Net.BCrypt.Verify(dto.Pw, user.Pw))
                return Unauthorized();

            return Ok(new { user.Id, user.UserId });
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
