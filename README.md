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

Then open http://localhost:5016 and log in. A new database only contains the system administrator
(`admin` / `HelloWorld1!`; change this password after the first login). The administrator creates the
tournament leaders under *Instellingen*.

## Conventions

- **Live updates:** the SignalR hub (`/hubs/tournament`) only sends notifications (`TournamentChanged`, `TournamentListChanged`). Clients then re-fetch data through the API. The server keeps no in-memory state.
- **Database:** EF Core migrations (`src/ChessFixer.Server/Data/Migrations`); existing data is kept. On startup the database is migrated automatically, after making a backup (`chessfixer.db.<timestamp>.bak`) when there are pending migrations. After changing the model, add a migration:
  ```
  cd src/ChessFixer.Server
  dotnet ef migrations add <Name> --output-dir Data/Migrations
  ```
  Never edit a migration that has been pushed (it has run on the server); add a new one. Enums are stored by name, so never rename an enum value without a migration that updates the data. The tournament state is not stored: it follows from `ClosedAt`, the rounds and the results (`TournamentService.StateOf`).
- **Authentication:** cookie login (`AuthController`). All controllers require a login by default (global `AuthorizeFilter`); mark exceptions with `[AllowAnonymous]`. Admin-only endpoints use `[Authorize(Roles = nameof(UserRole.Admin))]`. On the client every page requires a login via `@attribute [Authorize]` in `_Imports.razor`.
- **Texts:** all UI text is in `src/ChessFixer.Client/Resources/Strings.resx` (Dutch). Use `IStringLocalizer<Strings>`; never hard-code text in components.
