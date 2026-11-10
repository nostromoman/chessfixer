# ChessFixer

Web application for running rapid chess tournaments. See [REQUIREMENTS.md](REQUIREMENTS.md).

## Solution structure

| Project | Purpose |
|---|---|
| `src/ChessFixer.Server` | ASP.NET Core host: Web API controllers, SignalR hub, EF Core + SQLite. Also serves the WASM client. |
| `src/ChessFixer.Client` | Blazor WebAssembly frontend (MudBlazor). Talks to the server only via the Web API; listens to SignalR for live updates. |
| `src/ChessFixer.Shared` | DTOs, enums and the SignalR contract shared by server and client. |
| `src/ChessFixer.Core` | Pure tournament logic (pairing, standings). No database or web dependencies, so it is easy to unit test. |
| `tests/ChessFixer.Core.Tests` | xUnit tests for `ChessFixer.Core`. |

## Running

```
dotnet run --project src/ChessFixer.Server
```

Then open http://localhost:5016 and log in. A new database contains these development accounts:

| User name | Password | Role |
|---|---|---|
| `admin` | `HelloWorld1!` | System administrator (also manages tournament leaders) |
| `roel` | `Appelsap1!` | Tournament leader (Roel Gielen) |

## Conventions

- **Live updates:** the SignalR hub (`/hubs/tournament`) only sends notifications (`TournamentChanged`, `TournamentListChanged`). Clients then re-fetch data through the API. The server keeps no in-memory state.
- **Database (development):** no migrations. When the EF model changes, the database (`chessfixer.db`) is dropped and recreated automatically on startup. See `DatabaseInitializer`.
- **Authentication:** cookie login (`AuthController`). All controllers require a login by default (global `AuthorizeFilter`); mark exceptions with `[AllowAnonymous]`. Admin-only endpoints use `[Authorize(Roles = nameof(UserRole.Admin))]`. On the client every page requires a login via `@attribute [Authorize]` in `_Imports.razor`.
- **Texts:** all UI text is in `src/ChessFixer.Client/Resources/Strings.resx` (Dutch). Use `IStringLocalizer<Strings>`; never hard-code text in components.
