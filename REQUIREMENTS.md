# ChessFixer — Requirements

## 1. Purpose
ChessFixer manages rapid chess tournaments. It is a web application used by the
tournament leader to run a tournament on a club evening. The leader's laptop (or a
connected screen) shows the tournament page so all players can see their pairings,
the running games and the standings.

## 2. Users
- **System administrator** (*systeembeheerder*): may do everything, including creating, changing (also the password) and deleting tournament leader accounts, and watching the activity log.
- **Tournament leader**: creates and configures tournaments, manages players, starts rounds, closes the tournament, changes the application settings. Cannot create or change other tournament leaders.
- **Tournament players**: never log in. They look at the tournament page on the leader's (logged-in) screen; results are entered in the Matches tab on that screen.

### 2.1 Login
- The whole application requires a login (user name + password); only the login page is open. After logging in the user returns to the page they asked for.
- User names are case-insensitive. Passwords have at least 8 characters.
- The app bar shows the logged-in user with a menu to change the password and to log out. Changing a password (own, or by the administrator for a leader) logs that user out on all other devices; deleting a leader logs them out right away.
- At most 10 login attempts per minute per client address.
- A new database only contains the system administrator (`admin` / `HelloWorld1!`; change the password after the first login). There are no other seeded accounts, players or tournaments.

### 2.2 Activity log
- Every login (also failed attempts, with the user name that was typed), logout and password change is logged, and every change a logged-in user makes (create, change, delete, start a round, enter a result, ...), also when it is refused or fails. Just looking is not logged, but each account keeps when it was last active (to the minute).
- Each entry has the time, the user, what was done, the tournament it was about, the result, and the IP address and browser.
- Entries are kept when an account is deleted; entries older than one year are deleted when the server starts.

## 3. Core features (must have)
- Create and configure tournaments.
- Maintain a player list that is shared across tournaments; pick players from it for a tournament.
- Add and remove players during a tournament, also while a round is being played.
- Automatically pair players for the next round (Swiss or round robin).
- Enter and correct game results.
- Track standings with configurable scoring and tie-breaks.
- Live-updating tournament page.
- An AI writes a report of the tournament when it is closed.

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
| Location | Optional. Where the tournament is played; used for the report. |
| Description | Optional. Free text about the tournament; used for the report. |
| Pairing system | Swiss or round robin. Default: Swiss. |
| Game time | Minutes per player. Informational only (no clock in the app). |
| Increment | Seconds per move. Informational only. |
| Boards | Number of playing places available for the next round. Default 8. See 5.6. |
| Points for a win | Default 1 |
| Points for a draw | Default 0.5 |
| Points for a loss | Default 0 |
| Counts for the rating | Default yes. Whether the games change the players' ratings (see 6.1). |

Settings can be changed before and between rounds, never during a round or after closing. Once round 1 has started only the title, location, description, game time, increment and boards can still change; the pairing system, the points and whether the tournament counts for the rating are fixed.
Starting round 1 asks for confirmation. During a round the settings are shown as read-only text.
There is **no** configured number of rounds: the leader decides when to stop.
Tournaments have **no date**, only a state.

### 5.2 States
`New` → `Round active` → `Waiting for next round` → `Round active` → … → `Closed`

- **New**: being configured; players are added.
- **Round active**: games are being played; results are being entered. When the last result is entered, the round ends automatically and the tournament goes to *Waiting for next round*.
- **Waiting for next round**: all results are in and the next round is already paired, but its pairing is only shown once the leader starts it; the leader can start it or close the tournament. Results of the round just finished can still be corrected.
- **Closed**: only the leader can close a tournament (from *New* or *Waiting for next round*). Final; standings are frozen.

### 5.3 Pairing rules
- **Swiss** (default): players with similar scores play each other. Round 1 is seeded by rating (if present). For club evenings and competitions where the group changes, or when there are too few rounds for everyone to play everyone.
- **Round robin**: for a fixed group; everyone plays everyone in a fixed schedule (circle method, seeded by registration order). Once round 1 has started no new players can join, and nobody can be marked absent (a game that cannot be played is entered as a forfeit). A player who withdraws stays in the schedule so it does not shift for the others: their remaining games are lost by forfeit (the opponent wins, no board). They can rejoin and then play their remaining games again. In the schedule a forfeit counts as a game that has been played.
- No player meets the same opponent twice, as long as that is possible. Once it is not (e.g. everyone has played everyone), players meet again: first at most twice, then at most three times, and so on. The leader decides when to stop.
- White and black are alternated as much as possible.
- When two players meet again, the colors of their previous meeting are reversed, so every pair plays once with White and once with Black. This rule comes first, even if a player then gets the same color three times in a row.
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

### 5.7 Report
- When the leader closes a tournament and an AI is configured (see *Settings* in 7), the AI writes a report (*verslag*) of the tournament in Dutch, in the background. Closing does not wait for it; the page updates when the report is ready.
- The AI gets the tournament data: title, location, description, date (when round 1 started), pairing system, time control, scoring, the players (rating, age, club, city, withdrawn), the final standings, all games per round and notable results (a win against a player rated at least 100 points higher).
- The report is plain text: a title line followed by a few paragraphs. The AI may only use these facts (no invented moves or game course).
- If writing fails (or no AI was configured at closing), the report can be written (again) from the *Verslag* tab. An existing report stays visible until a new one is ready.
- The prompt that produced the report (instructions and tournament data) is stored with it; a *Prompt bekijken* button in the *Verslag* tab shows it. Below the report it says which AI (provider and model) wrote it.

## 6. Players
- Players are stored in a central list and reused across tournaments.
- Player data: first name (required); rating (default 1200, used for seeding, see 6.1); last name, birth date, city, club and profile photo (all optional). Without a photo a neutral silhouette is shown (no initials: two players with the same initial could be confused).
- A player can be deleted (after confirmation). This is a soft delete: the player leaves the player list but stays in the tournaments they played. Wherever the name still appears (tournament players, matches, standings, statistics, rating history) it is followed by *(verwijderd)*.
- Players can be added to and removed from (withdraw from) a tournament at any time, also during a round (round robin: no new players once round 1 has started, see 5.3). A player added during a round waits for the next round. A player who withdraws during a round keeps their current game, which still needs a result.
- A withdrawn player who has played games stays in the tournament player list and can rejoin; a player without games is removed completely.
- A player who joins late starts with 0 points.
- Per player a checkbox *Doet mee* (plays in the next round). Unchecked players are not paired and get a line *doet niet mee* for that round. During a round the box can only be switched on, for a latecomer.
- Swiss, latecomers during a round: when a player is switched on or added during a round, the players who are free in that round (the bye, latecomers, players who did not play but now do) are paired with each other where possible (not when they met before); those games join the queue of the round. The player with the bye goes first; with an odd number the one left over keeps what they had.
- A player who is removed keeps their earlier games and results in the standings but is no longer paired.

### 6.1 Rating
- Every player has an Elo rating. A player without a rating starts at **1200**; that is also the default when a player is created. The rating can still be set by hand.
- In a tournament that counts for the rating, the ratings of both players change right after each game (as soon as the result is entered), using the FIDE formula: change = K × (score − expected score), with the rating difference capped at 400. K = 40 for a player with fewer than 30 rated games, 10 from rating 2400, otherwise 20.
- Forfeits and byes do not change the rating. Correcting a result reverts the previous change and applies the new one; clearing a result reverts it.

## 7. Pages
UI language: **Dutch only** for now (other languages later, so keep texts in resource files).

A main menu on the left links to **Toernooien** (`/tournament`), **Spelers** (`/players`), **Clubs**, **Ratinglijst** and **Ratinglog**. For the system administrator only, a group **Beheer** follows with **Toernooileiders** (`/admin/leaders`) and **Activiteit** (`/admin/activity`); tournament leaders do not see it, and the server refuses them too. A gear icon at the bottom left of the menu opens the application **Instellingen** (`/settings`).

### Tournament overview — `/tournament`
Table of all tournaments with their title and state. Columns: title, type (round robin or Swiss), number of active players, boards, time control (minutes + increment) and state. A + button at the top right opens a wizard in a modal to create a new tournament: 1. title; 2. tournament type (Swiss or round robin, each with a short explanation; Swiss is preselected); 3. boards (default 8), game time and increment; 4. points for a win, draw and loss, and whether the tournament counts for the rating (default yes); 5. location and description (both optional, used for the report). All of this can be changed later in the settings (type, points and rating until round 1 starts). Clicking next to the modal does not close it; only Cancel or Escape does. After creating, the tournament detail page opens. Clicking a tournament opens its detail page.

### Tournament detail — `/tournament/{id}`
- **Header**: the title. A gear icon at the top right opens a drawer from the right with all settings (see 5.1), stacked one below the other in two groups (general; pairing, points and rating); saving closes the drawer, closing without saving discards changes.
- **Status** (full width): one clear status line (e.g. *3 van 5 wedstrijden van ronde 3 zijn afgerond* during a round, *Alle wedstrijden van ronde 3 zijn gespeeld* once it is done), with below it the buttons to start the next round and close the tournament. The start button is disabled (with the reason below it) when there are not enough players or boards to pair the next round.
- Below that, three **tabs**:
  1. **Spelers**: table of the players in this tournament with photo, name, rating and status: *Wacht op volgende ronde*, *Wacht op een bord*, *Bezig in een match*, *Bye game* or *Uit toernooi gestapt*. At the top right of the table a search field (autocomplete on the central player list) with a + button adds players: pick an existing player, or type a name that does not exist yet and press + or Enter to create a new player with that first name. Clicking a player name, or the pencil at the end of the row, opens the same player drawer as on the Players page, and an icon lets the player withdraw; for a withdrawn player it becomes an icon to rejoin.
  2. **Matches**: the current round as player cards (large board number, color badge, photo, bold name, big "VS" or the result in the middle), grouped as *Bezig* (with board number and result entry), *Wachtrij* (in queue order) and *Gespeeld* (results can be corrected). When the last result is entered the round stays on screen as it is (results can still be corrected); the pairing of the next round only appears once the leader starts it. Before round 1 the pairing of round 1 is shown. A player with a bye is shown as a separate card under *Vrij deze ronde (bye)*. When a result is entered the card fades out and pops up under *Gespeeld*. Result entry offers 1-0, ½-½ and 0-1; a forfeit (reglementair) can be chosen via the edit menu of a played game. Updates live.
  3. **Toernooistand**: the standings table, centered (not full width). Clicking a player name opens the player drawer. Shown only once there are results; before that a short message.

  4. **Verslag** (only for a closed tournament): the AI-written report (see 5.7), with a button to write it (again) and to copy it. While the AI is writing, a progress message is shown.

  The tab that matters most opens first: Spelers for a new tournament, Matches while playing, the report when closed (or the standings when there is no report).

### Players — `/players`
The central player list shared by all tournaments, shown as a table with a search field at the top left and a + button at the top right. Clicking a player name opens a drawer from the right (half the screen) with two tabs: **Algemeen** with the player data (saving closes the drawer) and **Statistieken** with totals (tournaments, games, won/drawn/lost, score percentage), the player's place in every tournament they played (linking to the tournament) and all their games with colour, opponent and result; and **Rating** with the current rating, the number of rated games, a line chart of the rating over time (hover shows each game) and a table with the change per game. The + button opens the same drawer to create a new player (only the first name is required; no tabs, since there are no statistics yet). A trash icon at the end of each row deletes the player after confirmation. Deleting is a soft delete: the player disappears from the player list and can no longer join a tournament, but stays in the tournaments they played (players, matches, standings, statistics), because those must remain intact.

### Login — `/login`
User name, password and an *Inloggen* button, centered on the screen, without the menu.

### Settings — `/settings`
Application settings, in tabs. Tab **AI-verslag**: the AI provider that writes the reports. Choice: *Geen* (no reports), *Ollama* or *Claude (Anthropic)*; each provider has its own fields and more can be added later. Ollama fields: URL (e.g. `http://localhost:11434`) and model (Ollama needs no login). Claude fields: API key (never sent back to the browser; leaving it empty keeps the stored one) and model (default `claude-opus-5-5`). For Claude models that support it, a request that Claude declines for policy reasons is automatically retried by Anthropic on another model (server-side fallback). A *Verbinding testen* button checks the settings without saving them and lists the available models, which can be clicked to choose one.

### Beheer — Toernooileiders — `/admin/leaders` (system administrator only)
Table of the tournament leaders (name, user name) with a + button to create one and per row a pencil to change and a trash icon to delete (after confirmation). The dialog asks for name, user name and password; when changing, an empty password keeps the current one.

### Beheer — Activiteit — `/admin/activity` (system administrator only)
At the top a table of all accounts: name, user name, last login, last active, number of logins, failed logins, changes and refused or failed changes. Clicking a user filters the log below. The log (newest first, a page at a time) shows when, who, what (login, failed login, logout, password change, or the change in words), details (a link to the tournament), the result (*Gelukt*, *Geweigerd* or *Mislukt* with the status code) and the IP address (the browser as tooltip). Filters: user, and *Alleen problemen* (failed logins and refused or failed changes). A *Vernieuwen* button reloads both.

## 8. Platform & technology
- .NET 10. Blazor **WebAssembly** frontend using **MudBlazor**.
- ASP.NET Core **Web API** backend, hosted in the same process as the frontend.
- The frontend communicates with the backend only through the Web API. Keep the backend stateless.
- **SignalR** pushes live updates to the frontend (all open pages).
- **SQLite** database.
- Authentication with an HttpOnly, SameSite=Strict cookie set by the Web API; every API endpoint and the SignalR hub require a login. Passwords are stored hashed (ASP.NET Core Identity password hasher).
- The data is kept across updates: schema changes are EF Core migrations, applied automatically on startup after a backup of the database file.

## 9. Out of scope (first version)
- Players registering themselves
- Manual pairing adjustments
- Built-in chess clock
- Languages other than Dutch
- Tournament dates / scheduling

## 10. Additional decisions
- **Round robin:** a fixed group (see 5.3); a varying group uses Swiss. Once everyone has played everyone, a next round can still be started: the schedule starts again from the beginning. The leader decides when to close the tournament.
- **Closing:** a tournament is only ever closed by the leader, never automatically.
- **Points for a bye:** extra tournament setting, default 1.
- **Points for a forfeit:** the same as a normal win or loss.
