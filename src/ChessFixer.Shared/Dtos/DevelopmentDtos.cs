namespace ChessFixer.Shared.Dtos;

/// <summary>The result of resetting the database (Beheer, Development).</summary>
/// <param name="Backup">File name of the backup made before the reset; null when there was nothing to copy.</param>
public record DevelopmentResetDto(int Players, int Clubs, string? Backup);
