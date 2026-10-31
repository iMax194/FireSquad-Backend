# FireSquad — Backend & TCP Lobby Server

Backend services for **FireSquad**, a four-player cooperative firefighting game. This repository provides a REST API for player accounts and a TCP Lobby Server for multiplayer room coordination. Gameplay synchronization over UDP/LiteNetLib belongs to a separate Game Server component and is **not implemented by this backend/lobby module**.

> **Project status:** REST authentication and the TCP lobby flow are implemented and tested with multiple Unity clients. Integration between the Lobby Server and the separate Game Server is planned; `GAME_STARTING` does **not** currently mean that a gameplay server is ready.

## 1. Features

### REST API

- Player registration and login.
- MongoDB persistence of player accounts.
- Password hashing.
- JWT access-token issuance and validation.
- Protected endpoint to retrieve the current user's identity.
- Swagger/OpenAPI for API exploration.

### TCP Lobby Server

- Newline-delimited JSON (one JSON message per line).
- JWT authentication for TCP sessions via `AUTHENTICATE`.
- Create, join, and leave rooms using a room code.
- Up to **4 players** per room.
- Host assignment and host migration when the host leaves or disconnects.
- Select one of four unique roles: `Scout`, `Foamer`, `Breaker`, `Hoser`.
- Ready/Unready updates and room-wide state broadcasts.
- Server-controlled `canStart`: true only when four players occupy distinct roles and all four are Ready.
- Host-only `START_GAME`.
- `GAME_STARTING` notifications with a shared `matchId`.
- Rollback to `Waiting` after startup timeout or a disconnection during `Starting`.
- Versioned room snapshots (`roomVersion`) for Unity clients to ignore stale updates.

### Unity Lobby Integration

The Unity-side integration has been tested for login, TCP authentication, creating/joining/leaving rooms, role selection, Ready/Unready, Start, and handling room updates, timeout, and host migration. **Unity project assets are separate from this backend solution** unless your repository explicitly includes them.

## 2. Technology Stack

| Component | Technology |
|---|---|
| Runtime | .NET 10 |
| Web API | ASP.NET Core |
| Persistence | MongoDB (Docker) |
| Authentication | JWT (HS256) |
| TCP Lobby | .NET `TcpListener` / `TcpClient` |
| API Documentation | Swagger / OpenAPI |
| Game Client | Unity (separate project) |
| Gameplay networking | LiteNetLib / UDP — separate team-owned component, pending integration |

## 3. Solution Structure

```text
FireSquad-Backend/
├── FireSquad.Api/         # REST API: controllers, services, repositories
├── FireSquad.Lobby/       # TCP server, sessions, rooms, lobby management
├── FireSquad.Shared/      # Shared models / contracts
├── FireSquad.slnx
├── compose.yaml           # Or docker-compose.yml, depending on your setup
├── .jwt-key.local         # Local JWT secret; DO NOT COMMIT
└── README.md
```

The folders above illustrate the high-level organization; file-level paths may vary in the actual solution.

## 4. Prerequisites

- **.NET SDK 10**
- **Docker Desktop**, with Docker Compose available
- A terminal such as **PowerShell**
- A local Unity project for client testing (optional for backend-only tests)

Verify the tools:

```powershell
dotnet --version
docker --version
docker compose version
```

## 5. Local Configuration

### MongoDB

The development setup uses MongoDB through Docker, with a host port of **27018** (because port 27017 is already occupied on the development machine). Ensure the connection string used by the API matches your Docker Compose configuration.

Start MongoDB from the repository root:

```powershell
docker compose up -d
```

Check container status:

```powershell
docker compose ps
```

> The Compose filename, MongoDB credentials, database name and exact connection-string key must be checked against the configuration in your local repository. Do not publish MongoDB credentials in this README.

### JWT signing key

The API and Lobby **must use the same JWT signing key** and matching issuer/audience validation settings.

In local development, a Base64-encoded 32-byte secret is stored in `.jwt-key.local` at the repository root. This file must be excluded from Git.

To create it **only if it does not already exist**:

```powershell
if (-not (Test-Path ".jwt-key.local")) {
    $bytes = New-Object byte[] 32
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    [Convert]::ToBase64String($bytes) |
        Set-Content ".jwt-key.local" -NoNewline
}
```

Confirm `.gitignore` includes:

```gitignore
.jwt-key.local
```

**Important:** `Jwt__Key` in the examples below contains a **Base64 string**. API and Lobby code must interpret it consistently (for the existing project, using `Convert.FromBase64String` when constructing the signing key). Generating a new key invalidates previously issued tokens.

Known development JWT settings:

| Setting | Development value |
|---|---|
| Issuer | `FireSquad.Api` |
| Audience | `FireSquad.Unity` |
| Signing algorithm | HS256 |
| Access-token lifetime | 60 minutes |

For production deployment, use a proper secret store and TLS for sensitive traffic.

## 6. Build and Run

Use **separate terminals** for the API and Lobby. Environment variables set in one PowerShell process do not automatically apply to another.

### Terminal A — REST API

```powershell
cd D:\UnityLearning\FireSquad-Backend
$env:Jwt__Key = (Get-Content ".jwt-key.local" -Raw).Trim()
dotnet run --project FireSquad.Api --urls http://localhost:5000
```

Swagger: **http://localhost:5000/swagger**

### Terminal B — TCP Lobby

```powershell
cd D:\UnityLearning\FireSquad-Backend
$env:Jwt__Key = (Get-Content ".jwt-key.local" -Raw).Trim()
dotnet run --project FireSquad.Lobby
```

TCP endpoint: **127.0.0.1:7777**

### Build the entire solution

```powershell
dotnet build
```

### Local ports

| Service | Protocol | Address |
|---|---|---|
| FireSquad.Api | HTTP | `http://localhost:5000` |
| FireSquad.Lobby | TCP | `127.0.0.1:7777` |
| MongoDB (Docker host mapping) | TCP | `localhost:27018` |
| Game Server | UDP | Not part of this solution; integration pending |

These are **local development settings**, not production endpoints.

## 7. REST API Reference

| Method | Endpoint | Authentication | Description |
|---|---|---|---|
| `POST` | `/api/auth/register` | Public | Register an account |
| `POST` | `/api/auth/login` | Public | Login and receive a JWT |
| `GET` | `/api/auth/me` | Bearer JWT | Retrieve current user information |

### Login example

Request:

```http
POST /api/auth/login HTTP/1.1
Content-Type: application/json

{
  "username": "firefighter01",
  "password": "<your-password>"
}
```

A successful response includes an `accessToken`. The exact response fields are defined by the DTOs in `FireSquad.Api`; check Swagger for the authoritative schema.

Example statuses verified in development:

- Successful registration: **201 Created**
- Duplicate registration: **409 Conflict**
- Successful login: **200 OK**
- Invalid credentials: **401 Unauthorized**

Do not print passwords or full JWTs in application logs.

## 8. TCP Lobby Protocol

The Lobby Server uses **UTF-8, newline-delimited JSON**. Every message is one JSON object followed by `\n`.

### Message envelope

```json
{
  "type": "MESSAGE_TYPE",
  "requestId": "client-generated-request-id",
  "payload": {}
}
```

- `type`: operation or event name.
- `requestId`: correlate requests and responses. Server broadcasts may have no associated request ID.
- `payload`: message-specific JSON object.
- Client-side field names use **camelCase**.

### Authenticate a TCP session

First, obtain a JWT through `POST /api/auth/login`. Then send:

```json
{
  "type": "AUTHENTICATE",
  "requestId": "auth-001",
  "payload": {
    "token": "<accessToken-from-login>"
  }
}
```

The Lobby checks the token's signature, expiration, issuer and audience, and then replies with `AUTH_SUCCESS`. Room operations require an authenticated TCP session.

### Room operations

**Create room:**

```json
{"type":"CREATE_ROOM","requestId":"create-001","payload":{}}
```

**Join room:**

```json
{"type":"JOIN_ROOM","requestId":"join-001","payload":{"roomCode":"AB12CD"}}
```

**Leave room:**

```json
{"type":"LEAVE_ROOM","requestId":"leave-001","payload":{}}
```

**Select role:**

```json
{"type":"SELECT_ROLE","requestId":"role-001","payload":{"role":"Scout"}}
```

**Ready / Unready:**

```json
{"type":"SET_READY","requestId":"ready-001","payload":{"ready":true}}
```

```json
{"type":"SET_READY","requestId":"ready-002","payload":{"ready":false}}
```

**Important:** The **request** field is `ready`, not `isReady`. In room snapshots the **player state** field is `isReady`.

**Start match (Host only):**

```json
{"type":"START_GAME","requestId":"start-001","payload":{}}
```

### Key server messages

| Message | Meaning |
|---|---|
| `WELCOME` | TCP connection accepted |
| `AUTH_SUCCESS` | JWT validated for this TCP session |
| `ROOM_CREATED` | New room created |
| `ROOM_JOINED` | Joined an existing room |
| `ROOM_UPDATED` | Authoritative room snapshot / broadcast |
| `ROLE_SELECTED` | Role selection acknowledged |
| `READY_UPDATED` | Ready-state update acknowledged |
| `ROOM_LEFT` | Player left the room |
| `GAME_STARTING` | Lobby has entered startup phase and assigned `matchId` |
| `START_FAILED` | Match startup failed or was cancelled |
| `ERROR` | Request was rejected or failed |

The exact shape of each message should be checked against the server's current response builders/DTOs.

### Example `ROOM_UPDATED` (illustrative)

```json
{
  "type": "ROOM_UPDATED",
  "requestId": null,
  "payload": {
    "roomCode": "AB12CD",
    "roomVersion": 5,
    "hostPlayerId": "session-001",
    "state": "Waiting",
    "matchId": null,
    "startedAt": null,
    "maxPlayers": 4,
    "canStart": false,
    "players": [
      {
        "playerId": "session-001",
        "username": "firefighter01",
        "role": "Scout",
        "isReady": true,
        "isHost": true
      }
    ]
  }
}
```

Room state and `canStart` are **computed by the Backend**. Unity clients must not assume that a request succeeded until the authoritative response/snapshot arrives.

## 9. Lobby Rules and Lifecycle

### Roles

A room supports four unique roles:

- `Scout`
- `Foamer`
- `Breaker`
- `Hoser`

The Backend rejects duplicate role selection. A player's Ready state is reset when that player changes roles.

### Ready / Start

`canStart` becomes true only when all of the following are true:

1. The room has exactly **4 players**.
2. Each player has a role.
3. All four roles are different.
4. All four players have `isReady == true`.
5. The room is in `Waiting`.

Only the Host may send `START_GAME` successfully.

### Room lifecycle

```text
Waiting
   |
   | Host START_GAME (all conditions met)
   v
Starting -- startup timeout / disconnect / cancel --> Waiting
   |
   | Game Server integration (planned)
   v
InGame (not yet connected to a real Game Server)
```

When starting, the Lobby assigns a `matchId` and a start timestamp and notifies connected players. In the current implementation, a startup timeout of approximately **20 seconds** rolls the room back to `Waiting` if the Game Server has not completed the startup handshake.

Host migration is supported when the Host leaves or disconnects. The current desired Ready behavior is: preserve existing Ready states on normal join/leave **while Waiting**, but reset Ready on role changes and rollback from **Starting**. Check your actual Backend implementation if these rules have been configured differently.

## 10. Testing Checklist

### API tests

- [ ] Register a new user; expect HTTP 201.
- [ ] Register the same username again; expect HTTP 409.
- [ ] Log in using valid credentials; expect HTTP 200 and an access token.
- [ ] Log in with an invalid password; expect HTTP 401.
- [ ] Call `/api/auth/me` with the token in Swagger; expect success.

### TCP / Unity tests

- [ ] TCP connection produces `WELCOME`.
- [ ] Valid JWT produces `AUTH_SUCCESS`.
- [ ] Invalid/expired JWT is rejected.
- [ ] Authenticated client creates a room.
- [ ] Another client joins by room code.
- [ ] `ROOM_UPDATED` reaches all room members.
- [ ] Leave and disconnect update the room correctly.
- [ ] Host migration works when the Host leaves.
- [ ] Two players cannot occupy the same role.
- [ ] Ready and Unready both work (`ready: true` and `ready: false`).
- [ ] All four players with unique roles and Ready cause `canStart: true`.
- [ ] Non-Host cannot Start.
- [ ] Host Start notifies four clients with the same `matchId`.
- [ ] Startup timeout causes `START_FAILED` and rollback to `Waiting`.
- [ ] Disconnect during Starting causes a safe rollback.
- [ ] Repeated Start after rollback produces a new `matchId`.

For multi-client tests on one development machine, use the Unity Editor plus standalone Unity builds, each logged into a separate account.

## 11. Troubleshooting

| Symptom | Check |
|---|---|
| Swagger cannot be opened | Confirm API is running on `http://localhost:5000` |
| MongoDB connection fails | Run `docker compose ps`; verify port 27018 and MongoDB connection string |
| Port 27017 in use | The local development setup maps Docker MongoDB to port 27018 |
| `JWT signing key is missing` | Set `Jwt__Key` in **each** terminal before starting the process |
| Lobby rejects a valid API token | Compare JWT key, Base64 decoding, issuer, audience and token expiration |
| Unity cannot connect to TCP 7777 | Confirm Lobby is running and listening on `127.0.0.1:7777` |
| `SET_READY` does not update | Verify request payload is `{"ready":false}`, not `{"isReady":false}` |
| Unity sees old room state | Verify `roomVersion` increments and `ROOM_UPDATED` is broadcast |
| `START_GAME` does nothing | Verify Host privileges, room `Waiting`, 4 distinct roles and all Ready |
| `GAME_STARTING` returns to Waiting | This is expected without an integrated Game Server when startup times out |

Optional PowerShell port check:

```powershell
Test-NetConnection 127.0.0.1 -Port 7777
```

Note: This checks **TCP**, not any future UDP game server.

## 12. Game Server Integration — Pending

The Lobby and gameplay server are independently owned components. The proposed integration contract is:

1. Lobby receives a valid Host `START_GAME` request and assigns a `matchId`.
2. Lobby asks the Game Server to create a match using the authoritative roster and roles.
3. Game Server replies `MATCH_READY` or `MATCH_FAILED`.
4. For a ready match, the system provides per-player connection information and short-lived admission tickets.
5. Unity connects to the gameplay server and proves admission using its own ticket.
6. The Game Server confirms that all players are admitted before starting gameplay.
7. Any failure, disconnect or timeout cancels the match and cleans up both sides.

Possible new integration messages: `CREATE_MATCH`, `MATCH_READY`, `MATCH_FAILED`, `GAME_CONNECT_INFO`, `GAME_START_CONFIRMED`, and `CANCEL_MATCH`. These are **proposed contracts, not existing implemented endpoints**.

Backend/Lobby responsibilities end at account/session validation, room lifecycle, match orchestration and safe handoff. Real-time movement, collision, skills, fire simulation and gameplay synchronization are **out of scope** for this module.

## 13. Security Notes

- Never commit `.jwt-key.local`, API credentials or database secrets.
- Never print complete JWT access tokens or user passwords in logs.
- Use the same JWT validation configuration in API and Lobby.
- JWT authentication over plain TCP is suitable only for controlled localhost development; use TLS or another secure transport for remote deployment.
- A gameplay admission ticket must be unique, time-limited, match-bound and player-bound; a static UDP key is not sufficient authentication.
- Validate all room operations on the server even if Unity UI disables invalid buttons.
- Bind control-plane services to private/internal interfaces and authenticate service-to-service commands.

## 14. Scope and Ownership

**Backend/Lobby owner**

- REST API, account authentication and JWT.
- MongoDB integration for accounts.
- TCP sessions and room management.
- Host, roles, Ready and start orchestration.
- Server-side validation and room-state broadcasts.
- Lobby-to-Game-Server integration contract and timeout/rollback handling.
- Unity Lobby TCP/API integration as required for testing.

**Other team members**

- LiteNetLib UDP gameplay transport/server.
- Gameplay logic, world simulation and character abilities.
- Gameplay scene and real-time state synchronization.

---

**Maintainer note:** This README documents the development configuration and verified milestones discussed during implementation. Before publishing, reconcile any repository-specific names, DTO fields, Docker configuration and runtime ports against the current source code.
