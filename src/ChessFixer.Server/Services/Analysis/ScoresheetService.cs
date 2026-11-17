using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ChessFixer.Core.Chess;
using ChessFixer.Server.Data;
using ChessFixer.Server.Errors;
using ChessFixer.Server.Services.Ai;
using ChessFixer.Server.Services.Auth;
using ChessFixer.Shared.Dtos;
using ChessFixer.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace ChessFixer.Server.Services.Analysis;

/// <summary>
/// Photos of handwritten scoresheets. Every photo is kept for the records (also when reading it fails), and can be
/// turned into moves in a wizard: Claude reads them, the board checks them, and the user checks every move against
/// the photo (with the position on a board) before the moves are saved as PGN. The wizard is kept with the photo, so
/// the user can continue later.
/// </summary>
public class ScoresheetService(
    ChessFixerDbContext db,
    AiSettingsService aiSettings,
    IScoresheetReader reader,
    ILogger<ScoresheetService> logger)
{
    /// <summary>The client scales photos down to about 2000 px; this leaves room for a PNG.</summary>
    public const int MaxImageBytes = 5 * 1024 * 1024;

    private static readonly string[] MediaTypes = ["image/jpeg", "image/png", "image/gif", "image/webp"];

    // ----- The photos, kept for the records -----

    /// <summary>The photos of a game's scoresheet, oldest first (without the image data).</summary>
    public async Task<List<ScoresheetPhotoDto>> GetPhotosAsync(int gameId)
    {
        if (!await db.Games.AnyAsync(g => g.Id == gameId)) throw new ApiException(ApiErrorCode.GameNotFound);
        return await db.GameScoresheets
            .Where(s => s.GameId == gameId)
            .OrderBy(s => s.Id)
            .Select(s => new ScoresheetPhotoDto(s.Id, s.GameId, s.UploadedAt, s.UploadedBy, s.Data.Length))
            .ToListAsync();
    }

    /// <summary>The image of a photo; null when it does not exist (or belongs to another game).</summary>
    public async Task<(byte[] Data, string ContentType)?> GetImageAsync(int gameId, int photoId)
    {
        var photo = await db.GameScoresheets.AsNoTracking()
            .Where(s => s.Id == photoId && s.GameId == gameId)
            .Select(s => new { s.Data, s.ContentType })
            .SingleOrDefaultAsync();
        return photo is null ? null : (photo.Data, photo.ContentType);
    }

    /// <summary>Keeps a photo of the scoresheet without reading it.</summary>
    public async Task<ScoresheetPhotoDto> UploadAsync(int gameId, byte[] image, string? contentType, ClaimsPrincipal user)
    {
        var mediaType = ValidImageType(image, contentType);
        var game = await LoadPlayedGameAsync(g => g.Id == gameId, CancellationToken.None);
        var photo = await StoreAsync(game, image, mediaType, user);
        return new ScoresheetPhotoDto(photo.Id, photo.GameId, photo.UploadedAt, photo.UploadedBy, photo.Data.Length);
    }

    public async Task DeleteAsync(int gameId, int photoId)
    {
        var photo = await db.GameScoresheets.SingleOrDefaultAsync(s => s.Id == photoId && s.GameId == gameId)
                    ?? throw new ApiException(ApiErrorCode.ScoresheetNotFound);
        db.GameScoresheets.Remove(photo);
        await db.SaveChangesAsync();
    }

    private static string ValidImageType(byte[] image, string? contentType)
    {
        var mediaType = contentType?.Split(';')[0].Trim().ToLowerInvariant();
        if (mediaType is null || !MediaTypes.Contains(mediaType) || image.Length == 0 || image.Length > MaxImageBytes)
            throw new ApiException(ApiErrorCode.InvalidScoresheetImage);
        return mediaType;
    }

    /// <summary>A game played over the board (won, lost or drawn): only those have a scoresheet worth keeping.</summary>
    private async Task<Game> LoadPlayedGameAsync(System.Linq.Expressions.Expression<Func<Game, bool>> which,
        CancellationToken cancellationToken)
    {
        var game = await db.Games
                       .Include(g => g.Round).ThenInclude(r => r.Tournament)
                       .Include(g => g.White).ThenInclude(p => p.Player)
                       .Include(g => g.Black!).ThenInclude(p => p.Player)
                       .SingleOrDefaultAsync(which, cancellationToken)
                   ?? throw new ApiException(ApiErrorCode.GameNotFound);
        if (game.Black is null || game.Result is not (GameResult.WhiteWins or GameResult.Draw or GameResult.BlackWins))
            throw new ApiException(ApiErrorCode.PgnNotAllowed);
        return game;
    }

    private async Task<GameScoresheet> StoreAsync(Game game, byte[] image, string mediaType, ClaimsPrincipal user)
    {
        var userId = UserSession.GetUserId(user);
        var photo = new GameScoresheet
        {
            GameId = game.Id,
            Data = image,
            ContentType = mediaType,
            UploadedAt = DateTime.UtcNow,
            UploadedBy = userId is { } id ? await db.Users.Where(u => u.Id == id).Select(u => u.DisplayName).SingleOrDefaultAsync() : null
        };
        db.GameScoresheets.Add(photo);
        await db.SaveChangesAsync();
        return photo;
    }

    // ----- A QR code: the phone does the rest (photo, reading, checking, saving), no login there -----

    /// <summary>How long a QR code can be scanned.</summary>
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromMinutes(20);

    /// <summary>Once opened on a phone, how long the phone can go on with it (reading and checking the moves).</summary>
    public static readonly TimeSpan PhoneSessionLifetime = TimeSpan.FromHours(3);

    /// <summary>A new QR code for the game; an earlier one that was not opened yet is cancelled.</summary>
    public async Task<ScoresheetLinkDto> CreateLinkAsync(int gameId, ClaimsPrincipal user)
    {
        var game = await LoadPlayedGameAsync(g => g.Id == gameId, CancellationToken.None);
        if (game.Pgn is not null) throw new ApiException(ApiErrorCode.GameHasMoves);
        var now = DateTime.UtcNow;
        await db.ScoresheetUploadLinks
            .Where(l => l.GameId == gameId && l.OpenedAt == null && l.CancelledAt == null && l.ExpiresAt > now)
            .ExecuteUpdateAsync(u => u.SetProperty(l => l.CancelledAt, now));

        var userId = UserSession.GetUserId(user);
        var link = new ScoresheetUploadLink
        {
            GameId = gameId,
            // 192 random bits: the token is the only thing that protects the page.
            Token = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24))
                .Replace('+', '-').Replace('/', '_'),
            CreatedAt = now,
            ExpiresAt = now + LinkLifetime,
            CreatedBy = userId is { } id ? await db.Users.Where(u => u.Id == id).Select(u => u.DisplayName).SingleOrDefaultAsync() : null
        };
        db.ScoresheetUploadLinks.Add(link);
        await db.SaveChangesAsync();
        return LinkDto(link, now);
    }

    public async Task<ScoresheetLinkDto> GetLinkAsync(int gameId, int linkId)
    {
        var link = await db.ScoresheetUploadLinks.AsNoTracking().SingleOrDefaultAsync(l => l.Id == linkId && l.GameId == gameId)
                   ?? throw new ApiException(ApiErrorCode.UploadLinkInvalid);
        return LinkDto(link, DateTime.UtcNow);
    }

    /// <summary>Cancels a QR code that was not opened yet (once opened, it belongs to the phone).</summary>
    public async Task<ScoresheetLinkDto> CancelLinkAsync(int gameId, int linkId)
    {
        var link = await db.ScoresheetUploadLinks.SingleOrDefaultAsync(l => l.Id == linkId && l.GameId == gameId)
                   ?? throw new ApiException(ApiErrorCode.UploadLinkInvalid);
        if (link.OpenedAt is null && link.CancelledAt is null)
        {
            link.CancelledAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        return LinkDto(link, DateTime.UtcNow);
    }

    private static ScoresheetLinkDto LinkDto(ScoresheetUploadLink link, DateTime now) =>
        new(link.Id, link.Token, link.ExpiresAt, Status(link, now), link.PhotoId);

    private static ScoresheetLinkStatus Status(ScoresheetUploadLink link, DateTime now) =>
        link.CancelledAt is not null ? ScoresheetLinkStatus.Cancelled
        : link.UsedAt is not null ? ScoresheetLinkStatus.Uploaded
        : link.ExpiresAt <= now ? ScoresheetLinkStatus.Expired
        : link.OpenedAt is not null ? ScoresheetLinkStatus.Opened
        : ScoresheetLinkStatus.Waiting;

    /// <summary>A link that can still be used (not cancelled, expired or finished), with its game; otherwise UploadLinkInvalid.</summary>
    private async Task<ScoresheetUploadLink> UsableLinkAsync(string token)
    {
        var link = await db.ScoresheetUploadLinks
                       .Include(l => l.Game).ThenInclude(g => g.Round).ThenInclude(r => r.Tournament)
                       .Include(l => l.Game).ThenInclude(g => g.White).ThenInclude(p => p.Player)
                       .Include(l => l.Game).ThenInclude(g => g.Black!).ThenInclude(p => p.Player)
                       .SingleOrDefaultAsync(l => l.Token == token)
                   ?? throw new ApiException(ApiErrorCode.UploadLinkInvalid);
        if (link.CancelledAt is not null || link.ExpiresAt <= DateTime.UtcNow) throw new ApiException(ApiErrorCode.UploadLinkInvalid);
        if (link.Game.Pgn is not null) throw new ApiException(ApiErrorCode.GameHasMoves);
        return link;
    }

    /// <summary>
    /// The phone opened the page: from now on the phone does the rest (the computer closes its wizard). Also used to
    /// continue after a reload: returns the reading of the last photo the phone sent.
    /// </summary>
    public async Task<ScoresheetUploadInfoDto> OpenLinkAsync(string token)
    {
        var link = await UsableLinkAsync(token);
        if (link.OpenedAt is null)
        {
            link.OpenedAt = DateTime.UtcNow;
            link.ExpiresAt = link.OpenedAt.Value + PhoneSessionLifetime;
            await db.SaveChangesAsync();
        }
        var game = link.Game;
        return new ScoresheetUploadInfoDto(game.Id, game.White.Player.Name, game.Black?.Player.Name ?? "", game.Round.Tournament.Title,
            link.ExpiresAt, await LinkWizardAsync(link));
    }

    /// <summary>The wizard of the last photo the phone sent, while it is open.</summary>
    private async Task<ScoresheetWizardDto?> LinkWizardAsync(ScoresheetUploadLink link) =>
        link.PhotoId is { } photoId && await GetWizardAsync(link.GameId) is { } wizard && wizard.PhotoId == photoId ? wizard : null;

    /// <summary>A photo from the phone: kept with the game; the phone then has it read.</summary>
    public async Task<ScoresheetPhotoDto> UploadViaLinkAsync(string token, byte[] image, string? contentType)
    {
        var mediaType = ValidImageType(image, contentType);
        var link = await UsableLinkAsync(token);
        var game = await LoadPlayedGameAsync(g => g.Id == link.GameId, CancellationToken.None);
        var photo = new GameScoresheet
        {
            GameId = game.Id,
            Data = image,
            ContentType = mediaType,
            UploadedAt = DateTime.UtcNow,
            UploadedBy = Truncate(link.CreatedBy is null ? "telefoon (QR-code)" : $"{link.CreatedBy} (telefoon)", 100)
        };
        db.GameScoresheets.Add(photo);
        await db.SaveChangesAsync();

        link.UsedAt = DateTime.UtcNow;
        link.OpenedAt ??= link.UsedAt;
        link.PhotoId = photo.Id;
        await db.SaveChangesAsync();
        return new ScoresheetPhotoDto(photo.Id, photo.GameId, photo.UploadedAt, photo.UploadedBy, photo.Data.Length);
    }

    /// <summary>The last photo the phone sent: its game and id; otherwise ScoresheetNotFound.</summary>
    private async Task<(int GameId, int PhotoId)> LinkPhotoAsync(string token)
    {
        var link = await UsableLinkAsync(token);
        return link.PhotoId is { } photoId ? (link.GameId, photoId) : throw new ApiException(ApiErrorCode.ScoresheetNotFound);
    }

    /// <summary>The phone: Claude reads the last photo it sent.</summary>
    public async Task<ScoresheetWizardDto> ReadViaLinkAsync(string token)
    {
        var (gameId, photoId) = await LinkPhotoAsync(token);
        return await ReadAsync(gameId, photoId);
    }

    /// <summary>The phone: the reading of its last photo (polled while Claude reads); null when there is none.</summary>
    public async Task<ScoresheetWizardDto?> GetWizardViaLinkAsync(string token) => await LinkWizardAsync(await UsableLinkAsync(token));

    /// <summary>The phone: the image of the last photo it sent.</summary>
    public async Task<(byte[] Data, string ContentType)?> GetImageViaLinkAsync(string token)
    {
        var (gameId, photoId) = await LinkPhotoAsync(token);
        return await GetImageAsync(gameId, photoId);
    }

    /// <summary>The phone: saves the moves being checked.</summary>
    public async Task SaveReviewViaLinkAsync(string token, ScoresheetReviewDto review)
    {
        var (gameId, photoId) = await LinkPhotoAsync(token);
        await SaveReviewAsync(gameId, photoId, review);
    }

    /// <summary>
    /// The phone: the checked moves as PGN, with the tournament to save them in. The caller saves them and then calls
    /// <see cref="FinishLinkAsync"/>.
    /// </summary>
    public async Task<(int TournamentId, int GameId, string Pgn)> BuildPgnViaLinkAsync(string token, ScoresheetPgnRequest request)
    {
        var link = await UsableLinkAsync(token);
        var pgn = await BuildPgnAsync(link.GameId, request with { FromPhoto = true });
        return (link.Game.Round.TournamentId, link.GameId, pgn.Pgn);
    }

    /// <summary>The moves were saved: the link can no longer be used.</summary>
    public Task FinishLinkAsync(string token) =>
        db.ScoresheetUploadLinks.Where(l => l.Token == token)
            .ExecuteUpdateAsync(u => u.SetProperty(l => l.ExpiresAt, DateTime.UtcNow));

    // ----- The wizard: read the photo with Claude, the user checks the moves -----

    /// <summary>A reading that has not finished after this long was interrupted (e.g. the server restarted).</summary>
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromMinutes(10);

    private const int MaxReviewMoves = 400;
    private const int MaxMoveText = 20;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The wizard in progress for a game (the latest photo with an open wizard); null when there is none.</summary>
    public async Task<ScoresheetWizardDto?> GetWizardAsync(int gameId)
    {
        if (!await db.Games.AnyAsync(g => g.Id == gameId)) throw new ApiException(ApiErrorCode.GameNotFound);
        var photo = await db.GameScoresheets.AsNoTracking()
            .Where(s => s.GameId == gameId && s.ReviewOpen)
            .OrderByDescending(s => s.Id)
            .Select(s => new
            {
                s.Id, s.GameId, s.UploadedAt, s.ReadStatus, s.ReadStartedAt, s.ReadError, s.ReadModel, s.ReadRemarks, s.Review
            })
            .FirstOrDefaultAsync();
        return photo is null
            ? null
            : Wizard(photo.Id, photo.GameId, photo.UploadedAt, photo.ReadStatus, photo.ReadStartedAt, photo.ReadError,
                photo.ReadModel, photo.ReadRemarks, photo.Review);
    }

    private static ScoresheetWizardDto Wizard(GameScoresheet photo) =>
        Wizard(photo.Id, photo.GameId, photo.UploadedAt, photo.ReadStatus, photo.ReadStartedAt, photo.ReadError,
            photo.ReadModel, photo.ReadRemarks, photo.Review);

    private static ScoresheetWizardDto Wizard(int id, int gameId, DateTime uploadedAt, ScoresheetReadStatus status,
        DateTime? startedAt, string? error, string? model, string? remarks, string? review)
    {
        if (status == ScoresheetReadStatus.Reading && startedAt < DateTime.UtcNow - ReadTimeout)
            (status, error) = (ScoresheetReadStatus.Failed, "Het lezen is onderbroken; probeer het opnieuw.");
        return new ScoresheetWizardDto(id, gameId, uploadedAt, status, error, model, remarks ?? "",
            review is null ? null : JsonSerializer.Deserialize<ScoresheetReviewDto>(review, Json));
    }

    /// <summary>
    /// Opens the wizard for a kept photo (closing any other wizard of the game) and reads the moves with Claude. Reading
    /// goes on when the user closes the dialog: the result is kept with the photo and offered when the dialog is
    /// opened again. A failed reading is not an error here: the wizard shows it and offers to try again or to type
    /// the moves. The moves are checked on the board: the first legal reading wins, and when none is legal Claude is
    /// asked which legal move is written.
    /// </summary>
    public async Task<ScoresheetWizardDto> ReadAsync(int gameId, int photoId)
    {
        var photo = await db.GameScoresheets.SingleOrDefaultAsync(s => s.Id == photoId && s.GameId == gameId)
                    ?? throw new ApiException(ApiErrorCode.ScoresheetNotFound);
        var game = await LoadPlayedGameAsync(g => g.Id == gameId, CancellationToken.None);
        if (await aiSettings.GetGeneratorAsync() is not { Generator.Provider: AiProvider.Claude } ai)
            throw new ApiException(ApiErrorCode.ScoresheetNeedsClaude);
        var settings = ai.Settings;

        // Already being read (a second click, or a second device): that reading goes on.
        if (photo.ReviewOpen && Wizard(photo).ReadStatus == ScoresheetReadStatus.Reading) return Wizard(photo);

        await db.GameScoresheets.Where(s => s.GameId == gameId && s.ReviewOpen && s.Id != photoId)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.ReviewOpen, false));
        photo.ReviewOpen = true;
        photo.ReadStatus = ScoresheetReadStatus.Reading;
        photo.ReadStartedAt = DateTime.UtcNow;
        photo.ReadError = null;
        photo.ReadModel = settings.ClaudeModel.Trim();
        photo.ReadRemarks = null;
        photo.Review = null;
        await db.SaveChangesAsync();

        try
        {
            // Not cancelled when the user closes the dialog: the result is kept for when they come back.
            var cancellationToken = CancellationToken.None;
            var image = photo.Data;
            var reading = await reader.ReadAsync(settings, image, photo.ContentType,
                $"White: {game.White.Player.Name} - Black: {game.Black!.Player.Name}, result {ResultTag(game.Result)}",
                cancellationToken);
            if (reading.Moves.Count == 0) throw new InvalidOperationException("Er zijn geen zetten gevonden op de foto.");

            var ordered = reading.Moves.OrderBy(m => m.Number).ThenBy(m => m.White ? 0 : 1).ToList();
            var reconstructed = await ScoresheetReconstruction.ReconstructAsync(ordered,
                question => reader.RepairAsync(settings, image, photo.ContentType, question, cancellationToken));

            photo.Review = JsonSerializer.Serialize(InitialReview(ordered, reconstructed), Json);
            photo.ReadRemarks = Truncate(reading.Remarks.Trim(), 2000);
            photo.ReadStatus = ScoresheetReadStatus.Done;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Reading the scoresheet of game {GameId} failed.", gameId);
            photo.ReadStatus = ScoresheetReadStatus.Failed;
            photo.ReadError = Truncate(e switch
            {
                JsonException => "Het antwoord van Claude was onleesbaar.",
                InvalidOperationException => e.Message, // already a message for the user (see ClaudeTextGenerator)
                _ => "Het lezen is mislukt; probeer het opnieuw."
            }, 1000);
        }
        await db.SaveChangesAsync();
        return Wizard(photo);
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];

    /// <summary>
    /// The moves to check, one per half-move: the ones that could be replayed (in SAN), then the rest as read. A move
    /// is checked in advance only when Claude was sure and it is what was read; the user checks the others.
    /// </summary>
    public static ScoresheetReviewDto InitialReview(IReadOnlyList<ReadMove> ordered, ReconstructedGame reconstructed)
    {
        var moves = new List<ScoresheetReviewMoveDto>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var read = ordered[i];
            if (i < reconstructed.Moves.Count)
            {
                var confidence = reconstructed.Sources[i] switch
                {
                    MoveSource.AsRead => read.Confidence,
                    MoveSource.Alternative => read.Confidence == MoveConfidence.High ? MoveConfidence.Medium : read.Confidence,
                    _ => MoveConfidence.Low
                };
                moves.Add(new ScoresheetReviewMoveDto(reconstructed.Moves[i], read.Text, read.Alternatives, confidence,
                    confidence == MoveConfidence.High));
            }
            else
            {
                // Not replayed: the user corrects the first one, the board then checks the rest.
                moves.Add(new ScoresheetReviewMoveDto(read.Text, read.Text, read.Alternatives, read.Confidence,
                    read.Confidence == MoveConfidence.High));
            }
        }
        return new ScoresheetReviewDto(moves);
    }

    /// <summary>Saves the moves the user is checking (while they work; not in the activity log).</summary>
    public async Task SaveReviewAsync(int gameId, int photoId, ScoresheetReviewDto review)
    {
        if (review.Moves is null || review.Moves.Count > MaxReviewMoves
            || review.Moves.Any(m => m is null || (m.Text?.Length ?? 0) > MaxMoveText || (m.ReadAs?.Length ?? 0) > 100
                                     || (m.Alternatives?.Count ?? 0) > 10))
            throw new ApiException(ApiErrorCode.InvalidPgn);
        var photo = await db.GameScoresheets.SingleOrDefaultAsync(s => s.Id == photoId && s.GameId == gameId)
                    ?? throw new ApiException(ApiErrorCode.ScoresheetNotFound);
        if (!photo.ReviewOpen) return; // finished or started over meanwhile (e.g. in another tab)

        var clean = new ScoresheetReviewDto(
            review.Moves.Select(m => m with
            {
                Text = (m.Text ?? "").Trim(),
                ReadAs = m.ReadAs ?? "",
                Alternatives = m.Alternatives ?? []
            }).ToList(),
            Math.Clamp(review.Selected, 0, Math.Max(0, review.Moves.Count - 1)));
        photo.Review = JsonSerializer.Serialize(clean, Json);
        await db.SaveChangesAsync();
    }

    /// <summary>Starts over: the wizard of the game is closed (the photos are kept).</summary>
    public async Task CloseWizardAsync(int gameId)
    {
        if (!await db.Games.AnyAsync(g => g.Id == gameId)) throw new ApiException(ApiErrorCode.GameNotFound);
        await CloseWizardsAsync(db, gameId);
    }

    /// <summary>Closes the wizard of a game, e.g. because its moves were saved.</summary>
    public static Task CloseWizardsAsync(ChessFixerDbContext db, int gameId) =>
        db.GameScoresheets.Where(s => s.GameId == gameId && s.ReviewOpen)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.ReviewOpen, false));

    /// <summary>
    /// The checked moves as PGN with the game's tags, to save. Every move is checked on the board again; nothing is
    /// saved here.
    /// </summary>
    public async Task<GamePgnDto> BuildPgnAsync(int gameId, ScoresheetPgnRequest request)
    {
        var game = await LoadPlayedGameAsync(g => g.Id == gameId, CancellationToken.None);
        if (request.Moves is not { Count: > 0 } || request.Moves.Count > MaxReviewMoves)
            throw new ApiException(ApiErrorCode.InvalidPgn);

        var position = Position.FromFen(Position.StartFen);
        var sans = new List<string>();
        foreach (var text in request.Moves)
        {
            Move move;
            try
            {
                move = position.ParseSan(text ?? "");
            }
            catch (FormatException)
            {
                throw new ApiException(ApiErrorCode.InvalidPgn,
                    $"Zet {position.FullmoveNumber}{(position.WhiteToMove ? "." : "...")} {text} is niet mogelijk.");
            }
            sans.Add(position.ToSan(move));
            position = position.Play(move);
        }
        return new GamePgnDto(BuildPgn(game, sans, ResultTag(game.Result), request.FromPhoto));
    }

    private static string ResultTag(GameResult result) => result switch
    {
        GameResult.WhiteWins => "1-0",
        GameResult.BlackWins => "0-1",
        GameResult.Draw => "1/2-1/2",
        _ => "*"
    };

    /// <summary>
    /// The tags come from the app (not from the sheet: names there are often abbreviated), the moves in SAN, in lines
    /// of at most 80 characters as is usual in PGN.
    /// </summary>
    public static string BuildPgn(Game game, IReadOnlyList<string> moves, string result, bool fromPhoto = true)
    {
        var pgn = new StringBuilder();
        void Tag(string name, string value) =>
            pgn.Append('[').Append(name).Append(" \"").Append(value.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\"]\n");

        var date = game.IsCatchUp ? game.PlayedAt : game.Round.StartedAt;
        Tag("Event", game.Round.Tournament.Title);
        Tag("Date", date?.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture) ?? "????.??.??");
        Tag("Round", game.IsCatchUp ? "-" : game.Round.Number.ToString(CultureInfo.InvariantCulture));
        Tag("White", game.White.Player.Name);
        Tag("Black", game.Black!.Player.Name);
        Tag("Result", result);
        if (fromPhoto) Tag("Annotator", "Ingelezen van een foto van het notatieformulier");
        pgn.Append('\n');

        var tokens = new List<string>();
        for (var i = 0; i < moves.Count; i++)
        {
            if (i % 2 == 0) tokens.Add($"{i / 2 + 1}.");
            tokens.Add(moves[i]);
        }
        tokens.Add(result);

        var line = new StringBuilder();
        foreach (var token in tokens)
        {
            if (line.Length > 0 && line.Length + 1 + token.Length > 80)
            {
                pgn.Append(line).Append('\n');
                line.Clear();
            }
            if (line.Length > 0) line.Append(' ');
            line.Append(token);
        }
        return pgn.Append(line).Append('\n').ToString();
    }
}
