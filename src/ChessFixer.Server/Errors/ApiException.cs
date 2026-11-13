using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;
using Microsoft.AspNetCore.Diagnostics;

namespace ChessFixer.Server.Errors;

/// <summary>A business rule was violated. Returned to the client as <see cref="ApiError"/>.</summary>
public sealed class ApiException(ApiErrorCode code, string? detail = null) : Exception(detail ?? code.ToString())
{
    public ApiErrorCode Code { get; } = code;

    /// <summary>Extra explanation for the user (Dutch); sent along in <see cref="ApiError.Detail"/>.</summary>
    public string? Detail { get; } = detail;

    public int StatusCode => Code switch
    {
        ApiErrorCode.TournamentNotFound or ApiErrorCode.PlayerNotFound or ApiErrorCode.GameNotFound
            or ApiErrorCode.UserNotFound or ApiErrorCode.WritingStyleNotFound or ApiErrorCode.ClubNotFound
            => StatusCodes.Status404NotFound,
        ApiErrorCode.TitleRequired or ApiErrorCode.FirstNameRequired or ApiErrorCode.InvalidSettings or ApiErrorCode.InvalidResult
            or ApiErrorCode.InvalidPhoto or ApiErrorCode.InvalidRating
            or ApiErrorCode.UserNameRequired or ApiErrorCode.DisplayNameRequired or ApiErrorCode.PasswordTooShort
            or ApiErrorCode.CurrentPasswordIncorrect or ApiErrorCode.WritingStyleNameRequired
            or ApiErrorCode.ClubNameRequired or ApiErrorCode.InvalidPgn or ApiErrorCode.InvalidScoresheetImage
            => StatusCodes.Status400BadRequest,
        ApiErrorCode.InvalidCredentials => StatusCodes.Status401Unauthorized,
        ApiErrorCode.TooManyLoginAttempts => StatusCodes.Status429TooManyRequests,
        _ => StatusCodes.Status409Conflict
    };
}

public sealed class ApiExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not ApiException apiException) return false;

        context.Response.StatusCode = apiException.StatusCode;
        await context.Response.WriteAsJsonAsync(new ApiError(apiException.Code, apiException.Detail), cancellationToken);
        return true;
    }
}
