namespace ChessFixer.Shared.Dtos;

public record PlayerDto(int Id, string Name, int? Rating);

public record CreatePlayerRequest(string Name, int? Rating);
