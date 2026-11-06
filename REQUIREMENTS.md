# ChessFixer — Requirements

## 1. Purpose
ChessFixer manages rapid chess tournaments. It is a web application used by the
tournament leader to run a tournament on a club evening. The leader's laptop (or a
connected screen) shows the public dashboard so all players can see their pairings,
the running games and the standings.

## 2. Users
- **Tournament leader**: creates and configures tournaments, manages players, starts rounds, closes the tournament.
- **Tournament players**: view the dashboard. Results are entered on the dashboard (no login for now).

## 3. Core features (must have)
- Create and configure tournaments.
- Maintain a player list that is shared across tournaments; pick players from it for a tournament.
- Add and remove players during a tournament (between rounds).
- Automatically pair players for the next round (Swiss or round robin).
- Enter and correct game results.
- Track standings with configurable scoring and tie-breaks.
- Live-updating public dashboard.

## 4. Real-life usage
On a rapid chess club evening, the leader opens the application, creates a new
tournament, picks the players, configures the settings and starts the first round.
Players see on the screen who they play and with which color. After each game the
result is entered on the dashboard. When all results are in, the next round is paired
automatically and shown on the dashboard; the leader starts it. This repeats until the
leader closes the tournament after a round.

## 5. Tournament

### 5.1 Settings
| Setting | Notes |
|---|---|
| Title | Can always be changed. |
| Pairing system | Swiss or round robin. |
| Game time | Minutes per player. Informational only (no clock in the app). |
| Increment | Seconds per move. Informational only. |
| Points for a win | Default 1 |
| Points for a draw | Default 0.5 |
| Points for a loss | Default 0 |

All settings except the title can only be changed while no round has started.
There is **no** configured number of rounds: the leader decides when to stop.
Tournaments have **no date**, only a state.

### 5.2 States
`New` → `Round active` → `Waiting for next round` → `Round active` → … → `Closed`

- **New**: being configured; players are added.
- **Round active**: games are being played; results are being entered.
- **Waiting for next round**: all results are in, the next round is already paired and visible on the dashboard, and the leader can start it or close the tournament.
- **Closed**: final; standings are frozen.

### 5.3 Pairing rules
- **Swiss**: players with similar scores play each other. Round 1 is seeded by rating (if present).
- **Round robin**: everyone plays everyone.
- No player meets the same opponent twice.
- White and black are alternated as much as possible.
- With an odd number of players, one player gets a bye (the same player never gets a second bye if avoidable).
- In the first version the leader cannot adjust pairings manually.

### 5.4 Results
- Possible results: White wins, Draw, Black wins, Forfeit (white or black wins by forfeit).
- Results can be corrected until the next round has started.

### 5.5 Standings
- Score according to the tournament's scoring settings.
- Tie-breaks: Buchholz, then Sonneborn-Berger.

## 6. Players
- Players are stored in a central list and reused across tournaments.
- Player data: name (required), rating (optional, used for seeding).
- Players can be added to and removed from a tournament between rounds.
- A player who joins late starts with 0 points.
- A player who is removed keeps their earlier games and results in the standings but is no longer paired.

## 7. Pages
UI language: **Dutch only** for now (other languages later, so keep texts in resource files).

### Tournament overview — `/tournament`
Table of all tournaments with their state. Create a new tournament. Clicking a tournament opens the management page.

### Tournament management — `/tournament/{id}`
Edit the settings (see 5.1). Start the next round, close the tournament.

### Public dashboard — `/tournament/{id}/dashboard`
Shown on the screen in the playing room. Shows the round info, current pairings and games in progress, and the standings.
Results are entered here. Updates live.
When all results of a round are in, the state becomes *Waiting for next round* and the new pairings are shown.

### Tournament players — `/tournament/{id}/players`
Manage the players in this tournament: pick from the central player list, create a new player, remove a player.

## 8. Platform & technology
- .NET 10. Blazor **WebAssembly** frontend using **MudBlazor**.
- ASP.NET Core **Web API** backend, hosted in the same process as the frontend.
- The frontend communicates with the backend only through the Web API. Keep the backend stateless.
- **SignalR** pushes live updates to the frontend (dashboard and other open pages).
- **SQLite** database.
- No user accounts or login for now. Design so authentication can be added later.
- During development: no migrations or data fixes. When the data model changes, drop the database and recreate it.

## 9. Out of scope (first version)
- User accounts / login
- Players registering themselves
- Manual pairing adjustments
- Built-in chess clock
- Languages other than Dutch
- Tournament dates / scheduling

## 10. Additional decisions
- **Round robin:** players can only be added or removed before round 1. The tournament ends automatically once everyone has played everyone.
- **Points for a bye:** extra tournament setting, default 1.
- **Points for a forfeit:** the same as a normal win or loss.
