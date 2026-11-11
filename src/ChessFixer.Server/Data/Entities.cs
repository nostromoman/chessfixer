using ChessFixer.Core.Rating;
using ChessFixer.Shared.Enums;

namespace ChessFixer.Server.Data;

/// <summary>A player in the central player list, reused across tournaments.</summary>
public class Player
{
    public int Id { get; set; }
    public required string FirstName { get; set; }
    public string LastName { get; set; } = "";

    /// <summary>Full name for display and sorting; computed by the database from first and last name.</summary>
    public string Name { get; private set; } = "";

    /// <summary>Current Elo rating. Changes after every game in a rated tournament; can also be set by hand.</summary>
    public int Rating { get; set; } = EloCalculator.DefaultRating;

    public DateOnly? BirthDate { get; set; }
    public string? City { get; set; }

    /// <summary>A guest player is not a member of the own club. Games with a guest player are not rated.</summary>
    public bool IsGuest { get; set; }

    /// <summary>The club of a guest player; null when unknown.</summary>
    public int? ClubId { get; set; }
    public Club? Club { get; set; }

    /// <summary>Profile photo, in its own table so loading players never loads the image data. Null without a photo.</summary>
    public PlayerPhoto? Photo { get; set; }

    /// <summary>
    /// Incremented on every photo change (also on removal), so clients can bust their image cache.
    /// Kept on the player, not on the photo, so it keeps counting when a photo is removed and a new one added.
    /// </summary>
    public int PhotoVersion { get; set; }

    /// <summary>
    /// Soft delete: the player is gone from the player list and cannot join tournaments anymore,
    /// but stays in the tournaments they played (standings, games, statistics).
    /// </summary>
    public bool IsDeleted { get; set; }
}

/// <summary>The profile photo of a player (small, resized by the client).</summary>
public class PlayerPhoto
{
    public int PlayerId { get; set; }
    public required byte[] Data { get; set; }
    public required string ContentType { get; set; }
}

public class Tournament
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public string? Location { get; set; }
    public string? Description { get; set; }

    public PairingSystem PairingSystem { get; set; } = PairingSystem.RoundRobin;
    public int GameTimeMinutes { get; set; } = 15;
    public int IncrementSeconds { get; set; } = 10;
    public double PointsForWin { get; set; } = 1;
    public double PointsForDraw { get; set; } = 0.5;
    public double PointsForLoss { get; set; } = 0;
    public double PointsForBye { get; set; } = 1;

    /// <summary>Whether the games change the players' ratings. Locked once round 1 has started.</summary>
    public bool IsRated { get; set; } = true;

    /// <summary>Number of boards (playing places). Games that do not fit wait in a queue for a free board.</summary>
    public int Boards { get; set; } = 8;

    /// <summary>
    /// When the leader closed the tournament; null while it is open. The other states (new, round active,
    /// waiting for the next round) are not stored: they follow from the rounds and results.
    /// </summary>
    public DateTime? ClosedAt { get; set; }

    public List<TournamentPlayer> Players { get; set; } = [];
    public List<Round> Rounds { get; set; } = [];

    /// <summary>The AI-written report; null when none was requested.</summary>
    public TournamentReport? Report { get; set; }
}

/// <summary>The report of a closed tournament, written by an AI.</summary>
public class TournamentReport
{
    public int TournamentId { get; set; }

    /// <summary>Generating, Ready or Failed (no row means <see cref="ReportStatus.None"/>).</summary>
    public ReportStatus Status { get; set; } = ReportStatus.Generating;

    /// <summary>The current report; an earlier report stays while a new one is written or when writing fails.</summary>
    public string? Text { get; set; }
    public DateTime? GeneratedAt { get; set; }

    /// <summary>Why the last attempt failed.</summary>
    public string? Error { get; set; }

    /// <summary>The AI (provider and model) that wrote the current report.</summary>
    public string? Model { get; set; }

    /// <summary>The prompt that produced the current report.</summary>
    public string? SystemPrompt { get; set; }
    public string? UserPrompt { get; set; }

    // ----- What the leader gave in the report wizard for the last requested report -----

    /// <summary>Noteworthy events that are not in the results (stored as a JSON array).</summary>
    public List<string> Events { get; set; } = [];

    /// <summary>The chosen writing style. Not a foreign key: the style may be changed or deleted later,
    /// so its name and description are copied (the report is written in the background).</summary>
    public int? WritingStyleId { get; set; }
    public string? WritingStyleName { get; set; }
    public string? WritingStyleDescription { get; set; }
}

/// <summary>A writing style for the AI report (Settings, AI-verslag), chosen in the report wizard.</summary>
public class WritingStyle
{
    public int Id { get; set; }
    public required string Name { get; set; }

    /// <summary>Instructions for the AI on how to write.</summary>
    public required string Description { get; set; }
}

/// <summary>A player's participation in one tournament.</summary>
public class TournamentPlayer
{
    public int Id { get; set; }
    public int TournamentId { get; set; }
    public Tournament Tournament { get; set; } = null!;
    public int PlayerId { get; set; }
    public Player Player { get; set; } = null!;

    /// <summary>False once the player is removed; their earlier games stay in the standings.</summary>
    public bool IsActive { get; set; } = true;
}

public class Round
{
    public int Id { get; set; }
    public int TournamentId { get; set; }
    public Tournament Tournament { get; set; } = null!;
    public int Number { get; set; }

    /// <summary>Null while the round is paired but not yet started (the pending round; at most one per tournament).</summary>
    public DateTime? StartedAt { get; set; }

    public List<Game> Games { get; set; } = [];
}

public class Game
{
    public int Id { get; set; }
    public int RoundId { get; set; }
    public Round Round { get; set; } = null!;
    /// <summary>Order of the game in the pairing; queued games get a board in this order.</summary>
    public int Number { get; set; }

    /// <summary>The board the game is (or was) played on; null while waiting in the queue, and for a bye.</summary>
    public int? Board { get; set; }

    public int WhiteId { get; set; }
    public TournamentPlayer White { get; set; } = null!;

    /// <summary>Null when White has a bye (and then the result is always <see cref="GameResult.Bye"/>).</summary>
    public int? BlackId { get; set; }
    public TournamentPlayer? Black { get; set; }

    public GameResult Result { get; set; } = GameResult.NotPlayed;

    // ----- Rating: filled in when a rated tournament is closed, for the games that count for the rating -----

    /// <summary>The ratings the game was calculated with: the ratings of the players before the tournament.
    /// Null when the game did not change the ratings.</summary>
    public int? WhiteRatingBefore { get; set; }
    public int? BlackRatingBefore { get; set; }

    /// <summary>The exact (unrounded) rating change of this game; the changes of all games of the tournament are
    /// added up and rounded once. Games rated before ratings were processed at closing hold whole, applied changes.</summary>
    public decimal? WhiteRatingChange { get; set; }
    public decimal? BlackRatingChange { get; set; }

    /// <summary>The K-factors used; null for games rated before they were stored.</summary>
    public int? WhiteK { get; set; }
    public int? BlackK { get; set; }

    public DateTime? RatedAt { get; set; }

    public bool IsRated => RatedAt is not null;
}

/// <summary>Application-wide settings; there is exactly one row (Id 1).</summary>
public class AppSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public AiProvider AiProvider { get; set; } = AiProvider.Ollama;

    // Ollama (needs no authentication).
    public string OllamaUrl { get; set; } = "";
    public string OllamaModel { get; set; } = "";

    // Claude (Anthropic API).
    public string ClaudeApiKey { get; set; } = "";
    public string ClaudeModel { get; set; } = "claude-opus-5-5";

    // The report prompt. Null means the built-in default (TournamentReportPrompt), so improvements to the
    // default reach everyone who did not change it.
    public string? ReportSystemPrompt { get; set; }
    public int? ReportUpsetRatingDifference { get; set; }

    /// <summary>The writing style that is preselected in the report wizard; null preselects the first style.
    /// Becomes null when that style is deleted.</summary>
    public int? DefaultWritingStyleId { get; set; }
}

/// <summary>
/// Someone who can log in: a tournament leader or the system administrator. Players never log in.
/// (Not called "User" to avoid confusion with ControllerBase.User.)
/// </summary>
public class AppUser
{
    public int Id { get; set; }

    /// <summary>Login name; unique, case-insensitive.</summary>
    public required string UserName { get; set; }
    public required string DisplayName { get; set; }
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; } = UserRole.TournamentLeader;

    /// <summary>
    /// Stored in the login cookie and checked on every request. Changing it (new password, role change,
    /// deleting the account) ends all existing sessions of the user.
    /// </summary>
    public string SecurityStamp { get; set; } = NewSecurityStamp();

    /// <summary>The last request of the user, to the minute (updated at most once a minute).</summary>
    public DateTime? LastSeenAt { get; set; }

    public static string NewSecurityStamp() => Guid.NewGuid().ToString("N");
}

/// <summary>A chess club. Guest players can belong to one; at most one club is the own club.</summary>
public class Club
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? City { get; set; }
    public string? Description { get; set; }
    public bool IsOwnClub { get; set; }
}

/// <summary>
/// One rating change, written when it happens and never changed afterwards (the rating log). Game details are
/// copied, so the entry stays readable when the round is cancelled or the tournament renamed.
/// </summary>
public class RatingLogEntry
{
    public int Id { get; set; }
    public DateTime At { get; set; }
    public int PlayerId { get; set; }
    public Player Player { get; set; } = null!;
    public int RatingBefore { get; set; }
    public int RatingAfter { get; set; }
    public RatingChangeReason Reason { get; set; }

    /// <summary>Null when the tournament is gone; the title is kept.</summary>
    public int? TournamentId { get; set; }
    public string? TournamentTitle { get; set; }
    public int? Round { get; set; }

    /// <summary>For the change of a closed tournament: the number of games it is made of.</summary>
    public int? Games { get; set; }
    public int? OpponentId { get; set; }
    public Player? Opponent { get; set; }
    public int? OpponentRating { get; set; }
    public GameResult? Result { get; set; }
    public bool? IsWhite { get; set; }

    /// <summary>Display name of the user who made the change.</summary>
    public string? ChangedBy { get; set; }
}

/// <summary>
/// One entry of the activity log: a login, logout, password change or a change made through the API. Written when it
/// happens and never changed. The user name is copied, so the entry stays readable when the account is deleted.
/// </summary>
public class ActivityLogEntry
{
    public long Id { get; set; }
    public DateTime At { get; set; }

    /// <summary>Not a foreign key: the log is kept when the account is deleted. Null for an unknown user name.</summary>
    public int? UserId { get; set; }
    public required string UserName { get; set; }
    public string? DisplayName { get; set; }

    public ActivityKind Kind { get; set; }

    /// <summary>For an action: "Controller.Action", the HTTP method, path and status code of the answer.</summary>
    public string? Action { get; set; }
    public string? Method { get; set; }
    public string? Path { get; set; }
    public int? StatusCode { get; set; }

    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}
