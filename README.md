# FireSquad --- Backend & TCP Lobby

Backend services for **FireSquad**. This repository contains the REST API, MongoDB persistence and TCP Lobby Server. The separate Game Server owns real-time gameplay over LiteNetLib/UDP.

> **Current integration status:** When the Host starts a full room, the Lobby persists the match through the REST API and sends `GAME_STARTING` to all Unity clients. The message includes the Game Server endpoint. The Unity integration stores the endpoint, loads the configured Gameplay Scene and connects with the existing LiteNetLib client. This is an endpoint handoff and connection integration only; it is not a complete Game Server admission/readiness handshake.

## 1. Features

### REST API (`FireSquad.Api`)

- Register and log in player accounts.
- Store user and match data in MongoDB.
- Hash passwords and issue JWT access tokens.
- Validate JWTs on protected endpoints.
- Provide Swagger/OpenAPI during development.
- Provide authenticated match history.
- Expose internal match-persistence endpoints protected by `X-Service-Key`.

### TCP Lobby (`FireSquad.Lobby`)

- TCP communication using UTF-8, newline-delimited JSON.
- Authenticate TCP sessions using the API-issued JWT.
- Create, join and leave rooms using a six-character room code.
- Support **3 players per room**.
- Assign a Host and migrate Host when the current Host leaves or disconnects.
- Support three unique roles: `Scout`, `Foamer`, `Breaker`.
- Support Ready/Unready and authoritative room-state broadcasts.
- Compute `canStart` on the server.
- Allow only the Host to send `START_GAME`.
- Generate a shared `matchId` and persist the match through the API before notifying Unity.
- Send `GAME_STARTING` with the Game Server host, port and transport.
- Roll back a match if persistence fails, a player disconnects during startup, or the startup timeout expires.

### Unity handoff

The Unity integration is responsible for:

1.  Receiving `GAME_STARTING`.
2.  Reading `matchId` and `gameServer.host` / `gameServer.port`.
3.  Loading the configured Gameplay Scene.
4.  Connecting the LiteNetLib client to the supplied UDP endpoint.

The Unity project is separate from this backend solution.

## 2. Technology Stack

Component Technology

---

Runtime .NET 10
REST API ASP.NET Core
Database MongoDB
Local database runtime Docker Compose
Authentication JWT / HS256
Lobby transport TCP
Gameplay transport LiteNetLib / UDP (separate Game Server)
API documentation Swagger / OpenAPI
Game client Unity

## 3. Solution Structure

```text
FireSquad-Backend/
├── FireSquad.Api/
│   ├── Controllers/
│   ├── DTOs/
│   ├── Models/
│   ├── Repositories/
│   └── Services/
├── FireSquad.Lobby/
│   ├── Managers/
│   ├── Models/
│   ├── Networking/
│   ├── Security/
│   └── Services/
├── FireSquad.Shared/
├── docker-compose.yml
├── .env                  # Local MongoDB credentials; do not commit
├── .jwt-key.local        # Local JWT signing key; do not commit
├── .service-key.local    # Shared API/Lobby internal key; do not commit
└── README.md
```

The Unity client and Game Server are separate projects and are not built by this solution.

## 4. Prerequisites

- .NET SDK 10
- Docker Desktop with Docker Compose
- PowerShell
- Unity Editor for end-to-end client testing
- The separate Game Server executable/project for gameplay connection tests

Check installed tools:

```powershell
dotnet --version
docker --version
docker compose version
```

## 5. Local Configuration

Run the commands below from the backend repository root:

```powershell
cd D:\UnityLearning\FireSquad-Backend
```

### 5.1 MongoDB environment file

The Compose service is named `mongodb`, uses the `mongo:latest` image and maps the host port `27018` to the container port `27017`.

Create or update the root `.env` file with your own local credentials:

```dotenv
MONGO_ROOT_USERNAME=your_local_mongo_username
MONGO_ROOT_PASSWORD=your_local_mongo_password
```

Do not commit `.env` or publish its values.

Start MongoDB:

```powershell
docker compose up -d mongodb
docker compose ps
```

The API database name in the current development configuration is `firesquad_db`. The current Docker host mapping is `localhost:27018`.

The API's MongoDB connection string can be overridden with the `MongoDb__ConnectionString` environment variable. It must use the same username and password configured in `.env`. For example, replace the placeholders below with your local values:

```powershell
$env:MongoDb__ConnectionString = "mongodb://your_local_mongo_username:your_local_mongo_password@localhost:27018/?authSource=admin"
$env:MongoDb__DatabaseName = "firesquad_db"
```

### 5.2 JWT signing key

The API and Lobby must use the **same** JWT signing key. The key is read through the `Jwt__Key` environment variable.

Create `.jwt-key.local` once, if it does not already exist:

```powershell
if (-not (Test-Path ".jwt-key.local")) {
    $bytes = New-Object byte[] 32
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()

    try {
        $rng.GetBytes($bytes)
        $key = [Convert]::ToBase64String($bytes)
        [System.IO.File]::WriteAllText(
            (Join-Path (Get-Location) ".jwt-key.local"),
            $key,
            [System.Text.Encoding]::ASCII
        )
    }
    finally {
        $rng.Dispose()
    }
}
```

### 5.3 Internal API service key

The Lobby calls internal API endpoints to create and update match records. These requests include the `X-Service-Key` HTTP header.

The API reads the expected value from `InternalApi:ServiceKey`; the Lobby reads it from `InternalApi__ServiceKey`. Set the **same value** in both processes.

Create `.service-key.local` once, if it does not already exist:

```powershell
if (-not (Test-Path ".service-key.local")) {
    $bytes = New-Object byte[] 32
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()

    try {
        $rng.GetBytes($bytes)
        $key = [Convert]::ToBase64String($bytes)
        [System.IO.File]::WriteAllText(
            (Join-Path (Get-Location) ".service-key.local"),
            $key,
            [System.Text.Encoding]::ASCII
        )
    }
    finally {
        $rng.Dispose()
    }
}
```

**Important:** Do not generate a different service key for the API and Lobby. Both terminals must read the same `.service-key.local` file.

### 5.4 Git ignore / secret handling

Ensure the root `.gitignore` contains:

```gitignore
.env
.jwt-key.local
.service-key.local
```

Never commit local database credentials, JWT signing keys, service keys or access tokens. If a secret has already been committed, rotate it; adding it to `.gitignore` does not remove it from Git history.

### 5.5 JWT settings

Current development JWT settings:

Setting Value

---

Issuer `FireSquad.Api`
Audience `FireSquad.Unity`
Algorithm HS256
Access-token lifetime 144 minutes

The API and Lobby must agree on the signing key, issuer and audience. Generating a new JWT key invalidates tokens issued with the previous key.

## 6. Build and Run

Start MongoDB first. Run the API and Lobby in **separate PowerShell terminals** because environment variables are scoped to the terminal process.

### Terminal A --- REST API

```powershell
cd D:\UnityLearning\FireSquad-Backend

$env:Jwt__Key = (Get-Content ".jwt-key.local" -Raw).Trim()
$env:InternalApi__ServiceKey = (Get-Content ".service-key.local" -Raw).Trim()

# Set these two values to match your local .env file:
$env:MongoDb__ConnectionString = "mongodb://your_local_mongo_username:your_local_mongo_password@localhost:27018/?authSource=admin"
$env:MongoDb__DatabaseName = "firesquad_db"

dotnet run --project FireSquad.Api --urls http://localhost:5000
```

Swagger:

- `http://localhost:5000/swagger`

Keep this terminal running.

### Terminal B --- TCP Lobby

```powershell
cd D:\UnityLearning\FireSquad-Backend

$env:Jwt__Key = (Get-Content ".jwt-key.local" -Raw).Trim()
$env:InternalApi__ServiceKey = (Get-Content ".service-key.local" -Raw).Trim()
$env:Api__BaseUrl = "http://localhost:5000/"

# Local test: the Game Server runs on this same computer.
$env:GameServer__Host = "127.0.0.1"
$env:GameServer__Port = "9050"

dotnet run --project FireSquad.Lobby
```

The Lobby listens on:

- TCP `127.0.0.1:7777`

`GameServer__Host` and `GameServer__Port` are included in `GAME_STARTING`. The defaults are `127.0.0.1` and `9050`.

Keep this terminal running.

### Start the separate Game Server

Start the teammate-owned Game Server separately. It must listen for LiteNetLib/UDP connections on the configured port (default `9050`).

This backend README does not define or modify the Game Server's internal gameplay behavior.

### Build the backend solution

From the backend repository root:

```powershell
dotnet build
```

## 7. Local Ports

---

Service Protocol Local address Responsibility

---

FireSquad.Api HTTP `http://localhost:5000` Accounts, JWT and match data

FireSquad.Lobby TCP `127.0.0.1:7777` Login session, rooms and match start

MongoDB TCP `localhost:27018` Persistent data

Game Server UDP `127.0.0.1:9050` by default Real-time gameplay; separate project

---

These are local development addresses, not production deployment settings.

## 8. REST API Reference

### Public / player endpoints

---

Method Endpoint Authentication Description

---

`POST` `/api/auth/register` Public Register an account

`POST` `/api/auth/login` Public Log in and receive a JWT

`GET` `/api/auth/me` Bearer JWT Get the current user's identity

`GET` `/api/matches/history` Bearer JWT Get the current user's match history

---

Verified authentication responses during development:

- Successful registration: `201 Created`
- Duplicate username/email: `409 Conflict`
- Successful login: `200 OK`
- Invalid login credentials: `401 Unauthorized`

Use Swagger as the authoritative source for the exact request and response DTO fields.

### Internal match endpoints

These endpoints are for service-to-service use by the Lobby. They are hidden from Swagger and require the `X-Service-Key` header.

---

Method Endpoint Purpose

---

`POST` `/internal/matches` Persist a newly created match

`PATCH` `/internal/matches/{matchId}/status` Update a match status

`POST` `/internal/matches/{matchId}/complete` Save match completion/result data

---

Do not call these internal endpoints directly from Unity. Unity should use the public API endpoints and the TCP Lobby protocol.

## 9. TCP Lobby Protocol

The Lobby uses UTF-8, newline-delimited JSON: each message is one JSON object followed by `\n`.

### Message envelope

```json
{
  "type": "MESSAGE_TYPE",
  "requestId": "client-generated-request-id",
  "payload": {}
}
```

- `type`: request or server event name.
- `requestId`: request correlation ID; server broadcasts may not have one.
- `payload`: message-specific data.
- JSON property names use camelCase.

### 9.1 Authenticate

First call `POST /api/auth/login` and obtain the JWT. Then send it to the Lobby:

```json
{
  "type": "AUTHENTICATE",
  "requestId": "auth-001",
  "payload": {
    "token": "<accessToken-from-login>"
  }
}
```

A valid token receives `AUTH_SUCCESS`. Room operations require an authenticated TCP session.

### 9.2 Room operations

Create room:

```json
{ "type": "CREATE_ROOM", "requestId": "create-001", "payload": {} }
```

Join room:

```json
{
  "type": "JOIN_ROOM",
  "requestId": "join-001",
  "payload": { "roomCode": "AB12CD" }
}
```

Leave room:

```json
{ "type": "LEAVE_ROOM", "requestId": "leave-001", "payload": {} }
```

Select role:

```json
{
  "type": "SELECT_ROLE",
  "requestId": "role-001",
  "payload": { "role": "Scout" }
}
```

Ready:

```json
{ "type": "SET_READY", "requestId": "ready-001", "payload": { "ready": true } }
```

Unready:

```json
{ "type": "SET_READY", "requestId": "ready-002", "payload": { "ready": false } }
```

The request property is `ready`; the player state in a room snapshot is `isReady`.

Start match (Host only):

```json
{ "type": "START_GAME", "requestId": "start-001", "payload": {} }
```

### 9.3 Important server messages

---

Message Meaning

---

`WELCOME` TCP connection accepted

`AUTH_SUCCESS` JWT accepted for this TCP session

`ROOM_CREATED` Room created

`ROOM_JOINED` Player joined a room

`ROOM_UPDATED` Authoritative room snapshot/broadcast

`ROLE_SELECTED` Role selection acknowledged

`READY_UPDATED` Ready-state change acknowledged

`ROOM_LEFT` Player left a room

`GAME_STARTING` Match record saved and gameplay endpoint sent to clients

`START_FAILED` Start flow failed or was cancelled

`ERROR` Request rejected or failed

---

### 9.4 Example `GAME_STARTING` payload

The following is an illustrative payload. The actual response also contains the normal message envelope:

```json
{
  "roomCode": "AB12CD",
  "matchId": "generated-match-id",
  "levelId": 1,
  "state": "Starting",
  "startedAt": "2026-11-11T10:00:00Z",
  "gameServer": {
    "host": "127.0.0.1",
    "port": 9050,
    "transport": "UDP"
  },
  "players": [
    {
      "playerId": "lobby-player-id",
      "username": "firefighter01",
      "role": "Scout",
      "isHost": true
    }
  ]
}
```

### 9.5 Room rules

A room can start only when all conditions are true:

1.  Exactly **3 players** have joined.
2.  Every player has selected a role.
3.  Roles are unique: `Scout`, `Foamer`, `Breaker`.
4.  All 3 players are Ready.
5.  The room state is `Waiting`.
6.  The requester is the Host.

The server is authoritative. Unity should update its UI from `ROOM_UPDATED` rather than assuming that a request succeeded.

## 10. Match Persistence and Start Lifecycle

When the Host sends `START_GAME`:

1.  The Lobby validates room state, player count, roles, Ready states and Host identity.
2.  The Lobby creates a `matchId` and changes the room to `Starting`.
3.  The Lobby sends `POST /internal/matches` to the API with the room, Host and three players.
4.  The API validates and stores the match in MongoDB with status `Starting`.
5.  After successful persistence, the Lobby broadcasts `ROOM_UPDATED` and `GAME_STARTING`.
6.  Unity reads the Game Server endpoint, loads the configured Gameplay Scene and connects over LiteNetLib/UDP.

If match persistence fails, the Lobby rolls the room back and sends `START_FAILED`.

### Current integration limitation

The current Lobby-to-Unity patch provides the Game Server endpoint and triggers the Unity scene/connect flow. However:

- The existing Game Server protocol is unchanged.
- The Game Server still uses its existing shared test `ConnectionKey`.
- The Game Server does not yet receive or validate `matchId`, Lobby user identity or Lobby role through this patch.
- There is no Game Server → Lobby readiness/admission confirmation in this patch.
- The Lobby has a **20-second Starting timeout**. If no compatible startup confirmation/transition is implemented before the timeout, the Lobby can mark the match `Failed`, roll the room back to `Waiting` and send `START_FAILED`, even if a Unity client has already loaded the Gameplay Scene or connected to the Game Server.

Therefore, treat this as a working endpoint handoff/connection step, not a complete production match-start protocol. A future handshake and admission-ticket contract must be coordinated with the teammate who owns the Game Server.

## 11. Unity Setup for Lobby → Gameplay

After applying the Unity connection patch:

1.  Keep `FireSquadApp` persistent across Scene changes.
2.  In the `FireSquadApp` Inspector, set **Gameplay Scene Name** to the exact name of the Gameplay Scene.
3.  Add that Scene to **File → Build Profiles → Scene List** (or Build Settings in older Unity versions).
4.  Keep `allowDirectConnectForTesting` disabled for the normal Lobby flow.
5.  Ensure the Gameplay Scene contains the required LiteNetLib client object, or that the network client persists correctly according to the Unity project setup.
6.  Start the Game Server before testing the Lobby Start flow.

Expected Unity Console messages from the connection patch include:

```text
[App] Match <match-id>; Game Server <host>:<port> (UDP).
[Connecting] <host>:<port>
[Client connected]
[Welcome received] PlayerId=<id>
```

The exact messages depend on the current Unity scripts.

## 12. Testing Checklist

### API and database

- [ ] MongoDB container starts and remains healthy.
- [ ] Swagger opens at `http://localhost:5000/swagger`.
- [ ] Register a new user; expect `201`.
- [ ] Register a duplicate user; expect `409`.
- [ ] Log in with correct credentials; expect `200` and a JWT.
- [ ] Log in with incorrect credentials; expect `401`.
- [ ] Call `/api/auth/me` using the Bearer token.
- [ ] Verify the same `.service-key.local` value is set in API and Lobby terminals.

### TCP Lobby

- [ ] Client connects and receives `WELCOME`.
- [ ] Valid JWT receives `AUTH_SUCCESS`.
- [ ] Invalid/expired JWT is rejected.
- [ ] Host creates a room.
- [ ] Two other clients join the same room.
- [ ] All clients receive `ROOM_UPDATED`.
- [ ] Duplicate role selection is rejected.
- [ ] Ready/Unready updates correctly.
- [ ] `canStart` becomes true only after all 3 players select unique roles and Ready.
- [ ] Non-Host cannot start.
- [ ] Host Start persists a match record and all clients receive the same `matchId`.
- [ ] Leaving/disconnecting during `Starting` causes a safe rollback.

### Unity and Game Server connection

- [ ] Game Server is running on the configured UDP endpoint.
- [ ] All 3 Unity clients receive `GAME_STARTING`.
- [ ] All clients load the configured Gameplay Scene.
- [ ] Each client attempts LiteNetLib connection to the endpoint in `GAME_STARTING`.
- [ ] Unity Console shows the expected connection and welcome messages.
- [ ] Check the 20-second Lobby timeout limitation described above.

For local multi-client testing, run multiple Unity clients on the same development computer and use separate accounts. For a real multi-PC test, configure reachable LAN addresses and firewall rules.

## 13. Troubleshooting

---

Symptom What to check

---

`.service-key.local` not found Create it once from the command in section 5.3, from the backend repository root

`InternalApi__ServiceKey environment variable is missing` Set the variable in the Lobby terminal before starting Lobby

Internal match endpoint returns `401` API and Lobby must read the exact same `.service-key.local` value

API starts but match persistence fails Check API availability, `Api__BaseUrl`, service key, MongoDB connection and API logs

`JWT signing key is missing` Set `Jwt__Key` in both API and Lobby terminals

Lobby rejects a valid API JWT Compare JWT key, issuer, audience, expiry and Base64 handling

Swagger does not open Confirm API is running on port `5000` and Development environment is enabled

MongoDB connection fails Run `docker compose ps`; verify `.env`, port `27018` and `MongoDb__ConnectionString`

TCP Lobby connection fails Confirm Lobby is running on `127.0.0.1:7777`

`SET_READY` is ignored Request payload must use `ready`, not `isReady`

`canStart` remains false Check that exactly 3 players are present, all roles are unique and all players are Ready

Host cannot start Confirm the sender is the current Host and room state is `Waiting`

Unity does not load Gameplay Scene Check `Gameplay Scene Name` and Build Profiles / Scene List

Unity loads the Scene but cannot connect Check Game Server process, host, UDP port `9050`, firewall and LiteNetLib protocol compatibility

Lobby returns to Waiting after about 20 seconds The current Lobby startup timeout expired; see the integration limitation in section 10

---

Useful local checks:

```powershell
docker compose ps
Test-NetConnection 127.0.0.1 -Port 5000
Test-NetConnection 127.0.0.1 -Port 7777
```

`Test-NetConnection` checks TCP connectivity. It does **not** verify the Game Server's UDP port. Use the Game Server console and a real LiteNetLib client connection to verify UDP.

## 14. Network Notes for Multiple Computers

The current Lobby server binds to `IPAddress.Loopback` (`127.0.0.1`). This is suitable for local development but prevents clients on other computers from connecting to the Lobby.

For multi-PC testing, the Lobby bind address and Unity Lobby host configuration must also be changed to a reachable LAN address or an appropriate network interface. Do not use `127.0.0.1` on a remote client to reach another computer.

For the Game Server:

- Set `GameServer__Host` to the Game Server machine's reachable LAN IP or DNS name.
- Set `GameServer__Port` to the actual UDP port (default `9050`).
- Allow inbound UDP traffic on that port in the host firewall/router as required.

## 15. Scope and Ownership

### Backend/Lobby responsibility

- REST API and account authentication.
- JWT issuance and validation.
- MongoDB persistence for users and match records.
- TCP sessions and room lifecycle.
- Host migration, role selection, Ready and server-side start validation.
- Internal API authentication using the shared service key.
- Match persistence and Lobby-to-Unity Game Server endpoint handoff.
- Unity API/TCP Lobby integration needed to test the flow.

### Separate Game Server / gameplay responsibility

- LiteNetLib UDP server internals.
- Player admission and match/role validation inside the Game Server.
- Real-time movement, collision, abilities and world simulation.
- Gameplay state synchronization.

This README documents the Backend/Lobby side and the current connection contract. Changes to Game Server internals must be coordinated with the teammate responsible for that component.
