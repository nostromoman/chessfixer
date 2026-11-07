# ChessFixer — Requirements

## 1. Purpose
ChessFixer manages rapid chess tournaments. It is a web application used by the
tournament leader to run a tournament on a club evening. The leader's laptop (or a
connected screen) shows the tournament page so all players can see their pairings,
the running games and the standings.

## 2. Users
- **Tournament leader**: creates and configures tournaments, manages players, starts rounds, closes the tournament.
- **Tournament players**: view the tournament page. Results are entered in the Matches tab (no login for now).

## 3. Core features (must have)
- Create and configure tournaments.
- Maintain a player list that is shared across tournaments; pick players from it for a tournament.
- Add and remove players during a tournament, also while a round is being played.
- Automatically pair players for the next round (Swiss or round robin).
- Enter and correct game results.
- Track standings with configurable scoring and tie-breaks.
- Live-updating tournament page.

## 4. Real-life usage
On a rapid chess club evening, the leader opens the application, creates a new
tournament, picks the players, configures the settings and starts the first round.
Players see on the screen who they play and with which color. After each game the
result is entered in the Matches tab. When all results are in, the next round is paired
automatically and shown in the Matches tab; the leader starts it. This repeats until the
leader closes the tournament after a round.

## 5. Tournament

### 5.1 Settings
| Setting | Notes |
|---|---|
| Title | |
| Pairing system | Swiss or round robin. |
| Game time | Minutes per player. Informational only (no clock in the app). |
| Increment | Seconds per move. Informational only. |
| Boards | Number of playing places available for the next round. Default 10. See 5.6. |
| Points for a win | Default 1 |
| Points for a draw | Default 0.5 |
| Points for a loss | Default 0 |

Settings can be changed before and between rounds, never during a round or after closing. Once round 1 has started only the title, game time, increment and boards can still change; the pairing system and the points are fixed.
Starting round 1 asks for confirmation. During a round the settings are shown as read-only text.
There is **no** configured number of rounds: the leader decides when to stop.
Tournaments have **no date**, only a state.

### 5.2 States
`New` → `Round active` → `Waiting for next round` → `Round active` → … → `Closed`

- **New**: being configured; players are added.
- **Round active**: games are being played; results are being entered. When the last result is entered, the round ends automatically and the tournament goes to *Waiting for next round*.
- **Waiting for next round**: all results are in, the next round is already paired and visible in the Matches tab, and the leader can start it or close the tournament. Results of the round just finished can still be corrected.
- **Closed**: only the leader can close a tournament (from *New* or *Waiting for next round*). Final; standings are frozen.

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

### 5.6 Boards and queue
- A tournament has a limited number of boards (playing places), set in the settings.
- When a round is paired, the first games (in pairing order) get boards 1, 2, …; the games that do not fit wait in a queue. A bye never takes a board.
- When a result is entered, the board is free and the first game in the queue moves to it. A queued game cannot get a result before it has a board.
- A game whose result is cleared goes back to its board; if that board has been taken by the next game, it goes back into the queue (at the front).
- The round is finished when all games, including the queued ones, have a result.

## 6. Players
- Players are stored in a central list and reused across tournaments.
- Player data: first name (required); last name, rating (used for seeding), birth date, city, club and profile photo (all optional).
- A player can be deleted (after confirmation), unless they are in a tournament.
- Players can be added to and removed from (withdraw from) a tournament at any time, also during a round. A player added during a round waits for the next round. A player who withdraws during a round keeps their current game, which still needs a result.
- A withdrawn player who has played games stays in the tournament player list and can rejoin; a player without games is removed completely.
- A player who joins late starts with 0 points.
- A player who is removed keeps their earlier games and results in the standings but is no longer paired.

## 7. Pages
UI language: **Dutch only** for now (other languages later, so keep texts in resource files).

A main menu on the left links to **Toernooien** (`/tournament`) and **Spelers** (`/players`).

### Tournament overview — `/tournament`
Table of all tournaments with their title and state. A + button at the top right opens a drawer from the right to create a new tournament (only the title); after creating, the tournament detail page opens. Clicking a tournament opens its detail page.

### Tournament detail — `/tournament/{id}`
- **Header**: title, state, round, time control and boards. A gear icon at the top right opens a drawer from the right with all settings (see 5.1), stacked one below the other in two groups (general; pairing and points); saving closes the drawer, closing without saving discards changes.
- **Progress** (full width): state info, start the next round, close the tournament. The start button is disabled (with the reason below it) when there are not enough players or boards to pair the next round.
- Below that, three **tabs**:
  1. **Spelers**: table of the players in this tournament with photo, name, rating and status: *Wacht op volgende ronde*, *Wacht op een bord*, *Bezig in een match*, *Bye game* or *Uit toernooi gestapt*. At the top right of the table a search field (autocomplete on the central player list) with a + button adds players: pick an existing player, or type a name that does not exist yet and press + or Enter to create a new player with that first name. At the end of each row a pencil opens the same player drawer as on the Players page, and an icon lets the player withdraw; for a withdrawn player it becomes an icon to rejoin.
  2. **Matches**: the current round as player cards (large board number, color badge, photo, bold name, big "VS" or the result in the middle), grouped as *Bezig* (with board number and result entry), *Wachtrij* (in queue order) and *Gespeeld* (results can be corrected). While waiting for the next round: the next pairing (boards and queue) and the still correctable results of the round just finished. Byes are not shown here. When a result is entered the card fades out and pops up under *Gespeeld*. Result entry offers 1-0, ½-½ and 0-1; a forfeit (reglementair) can be chosen via the edit menu of a played game. Updates live.
  3. **Toernooistand**: the standings table, centered (not full width). Shown only once there are results; before that a short message.

  The tab that matters most opens first: Spelers for a new tournament, Matches while playing, standings when closed.

### Players — `/players`
The central player list shared by all tournaments, shown as a table with a search field at the top left and a + button at the top right. Clicking a player name opens a drawer from the right (half the screen) with the player data; saving closes the drawer. The + button opens the same drawer to create a new player (only the first name is required). A trash icon at the end of each row deletes the player after confirmation.

## 8. Platform & technology
- .NET 10. Blazor **WebAssembly** frontend using **MudBlazor**.
- ASP.NET Core **Web API** backend, hosted in the same process as the frontend.
- The frontend communicates with the backend only through the Web API. Keep the backend stateless.
- **SignalR** pushes live updates to the frontend (all open pages).
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
- **Round robin:** players can only be added or removed before round 1. Once everyone has played everyone, no next round can be started; the leader closes the tournament (results of the last round can still be corrected until then).
- **Closing:** a tournament is only ever closed by the leader, never automatically.
- **Points for a bye:** extra tournament setting, default 1.
- **Points for a forfeit:** the same as a normal win or loss.
