using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;
using Microsoft.AspNetCore.Diagnostics;

namespace ChessFixer.Server.Errors;

/// <summary>A business rule was violated. Returned to the client as <see cref="ApiError"/>.</summary>
public sealed class ApiException(ApiErrorCode code) : Exception(code.ToString())
{
    public ApiErrorCode Code { get; } = code;

    public int StatusCode => Code switch
    {
        ApiErrorCode.TournamentNotFound or ApiErrorCode.PlayerNotFound or ApiErrorCode.GameNotFound
            => StatusCodes.Status404NotFound,
        ApiErrorCode.TitleRequired or ApiErrorCode.NameRequired or ApiErrorCode.InvalidSettings or ApiErrorCode.InvalidResult
            => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status409Conflict
    };
}

public sealed class ApiExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not ApiException apiException) return false;

        context.Response.StatusCode = apiException.StatusCode;
        await context.Response.WriteAsJsonAsync(new ApiError(apiException.Code), cancellationToken);
        return true;
    }
}
