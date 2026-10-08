using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WordApp.Services;

namespace WordApp.Controllers;

[ApiController, Authorize, Route("ranking")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class RankingController(RankingService ranking) : ControllerBase
{
    private int AccountId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken token)
    {
        if (AccountId <= 0) return Unauthorized();
        try { return Ok(await ranking.ReadAsync(AccountId, token)); }
        catch (KeyNotFoundException) { return Unauthorized(); }
    }

    // Legacy clients cannot edit or withdraw the automatic server identity.
    [HttpPut("me")]
    public IActionResult LegacyParticipation() => StatusCode(StatusCodes.Status410Gone, "All accounts participate automatically. Update the app.");
}
