using ChessFixer.Server.Services.Development;
using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChessFixer.Server.Controllers;

/// <summary>Beheer, Development: tools for trying the application out. Only the system administrator.</summary>
[ApiController]
[Route("api/development")]
[Authorize(Roles = nameof(UserRole.Admin))]
public class DevelopmentController(DevelopmentService service) : ControllerBase
{
    /// <summary>Deletes all tournaments, players, clubs and the rating log (after a backup) and seeds clubs and
    /// players. The accounts, settings and activity log are kept.</summary>
    [HttpPost("reset")]
    public Task<DevelopmentResetDto> Reset() => service.ResetAsync();
}
