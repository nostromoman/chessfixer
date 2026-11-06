using ChessFixer.Shared.Enums;

namespace ChessFixer.Server.Data;

/// <summary>A player in the central player list, reused across tournaments.</summary>
public class Player
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int? Rating { get; set; }
}

public class Tournament
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public TournamentState State { get; set; } = TournamentState.New;

    public PairingSystem PairingSystem { get; set; } = PairingSystem.Swiss;
    public int GameTimeMinutes { get; set; } = 15;
    public int IncrementSeconds { get; set; } = 10;
    public double PointsForWin { get; set; } = 1;
    public double PointsForDraw { get; set; } = 0.5;
    public double PointsForLoss { get; set; } = 0;
    public double PointsForBye { get; set; } = 1;

    public List<TournamentPlayer> Players { get; set; } = [];
    public List<Round> Rounds { get; set; } = [];
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

    /// <summary>Null while the round is paired but not yet started.</summary>
    public DateTime? StartedAt { get; set; }

    public List<Game> Games { get; set; } = [];
}

public class Game
{
    public int Id { get; set; }
    public int RoundId { get; set; }
    public Round Round { get; set; } = null!;
    public int Board { get; set; }

    public int WhiteId { get; set; }
    public TournamentPlayer White { get; set; } = null!;

    /// <summary>Null when White has a bye.</summary>
    public int? BlackId { get; set; }
    public TournamentPlayer? Black { get; set; }

    public GameResult Result { get; set; } = GameResult.NotPlayed;
}

/// <summary>Stores a hash of the database schema so the database can be recreated when the model changes.</summary>
public class SchemaInfo
{
    public int Id { get; set; }
    public required string Hash { get; set; }
}
