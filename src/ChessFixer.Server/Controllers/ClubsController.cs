using ChessFixer.Server.Services.Clubs;
using ChessFixer.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace ChessFixer.Server.Controllers;

/// <summary>The chess clubs.</summary>
[ApiController]
[Route("api/clubs")]
public class ClubsController(ClubService clubs) : ControllerBase
{
    [HttpGet]
    public Task<List<ClubDto>> GetAll() => clubs.GetAllAsync();

    [HttpPost]
    public Task<ClubDto> Create(SaveClubRequest request) => clubs.CreateAsync(request);

    [HttpPut("{id:int}")]
    public Task<ClubDto> Update(int id, SaveClubRequest request) => clubs.UpdateAsync(id, request);

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await clubs.DeleteAsync(id);
        return NoContent();
    }
}
