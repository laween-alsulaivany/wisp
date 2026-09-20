# Wisp.Data

Create a `WispDatabase` with the writable user database path and the existing,
bundled `assets/completion-estimates.db` path. The user database's parent directory must
already exist. Await `IMigrationRunner.ApplyAsync` before using repositories:

```csharp
var database = new WispDatabase(userDatabasePath, completionEstimatesPath);
IMigrationRunner migrations = new MigrationRunner(database);
await migrations.ApplyAsync(cancellationToken);
await new TagDictionarySeeder(database).SeedAsync(cancellationToken);
IGameRepository games = new GameRepository(database);
```

The types above use `Wisp.Data`, `Wisp.Data.Repositories`, and
`Wisp.Core.Interfaces`. App startup/DI registration is a later phase.

All repository writes and migrations enter one process-wide channel with one
consumer, including when separate `WispDatabase` instances point to the same file.
Await write calls before shutting down. Failed or canceled work does not stop the
consumer. Reads open independent, short-lived, pooled read-only connections.
Connections enable WAL, foreign keys, normal synchronization, and a five-second
busy timeout. `Default Timeout=5` is the Microsoft.Data.Sqlite connection-string
setting; `PRAGMA busy_timeout=5000` also sets the native timeout.

`Migrations/NNNN_description.sql` resources migrate only the user database. The
runner validates both version records and commits DDL, migration history, and
`user_version` together. It refuses a newer schema or inconsistent history.
`Migrations/Bundled/0001_completion_estimates.sql` defines the separate dataset
schema and is deliberately excluded from user migrations. Supply the dataset
as a release asset; runtime connections attach it with `mode=ro`. The canonical
`assets/completion-estimates.db` currently contains the schema and zero rows.
Game Duration estimates are deliberately unavailable until a real dataset ships.
Tests build their own small dataset from this resource, with no production metadata invented.

SQL parameters pass through `SqliteValues` so booleans become integer 0/1,
enums use their names, and timestamps become UTC text with millisecond precision.
Reading timestamps uses the same format.

Game upserts preserve the database-generated identity and creation time. Tags
are a set returned in name order. Their names must exist in the bundled
`TagDictionary`; unknown names roll back the upsert rather than inventing Steam
tag IDs. After migrations, startup calls `TagDictionarySeeder.SeedAsync` on every
launch. It upserts the linked `assets/tags.json` in a transaction through the same
write queue, preserving old IDs referenced by existing games. Data and
SteamIntegration link the same physical file at the repository root. Seeding
does not add a schema-migration entry, so later releases refresh existing databases.
Upserts persist installation paths, classification flags, metadata freshness,
and artwork cache fields. New `Game` records default to stale metadata and null
artwork paths/timestamps. Read an existing game and use `with` when changing only
some fields; an upsert replaces all fields in the Core `Game` contract.
`GetAllAsync` returns every stored game, including uninstalled and otherwise
ineligible games, with tags in name order and games in `GameId` order. It requires
no profile and applies no recommendation filters.

Session completion persists the caller-supplied runtime and foreground seconds.
Only completed sessions contribute raw samples to `PlaytimeDistribution`.
`RecommendationSnapshotProvider` computes personal medians once there are at
least three completed sessions and a profile median across all completed sessions,
including games outside the eligible pool. Both use active foreground seconds
converted to minutes, with no rounding. The engine owns scoring and filtering.

Construct the snapshot provider with the database, `IGameRepository`,
`ISettingsRepository`, and `IClock`. It captures time once, obtains the base eligible
pool using the profile's inclusion settings (defaults when no settings row exists),
then reads session history, states, and bundled estimates in one read transaction.
The pool/settings calls precede that transaction; this is not an atomic snapshot
across concurrent library or settings updates. Returned records are detached from
subsequent database changes. `LastSessionUtc` is the latest completed session's
start time; Steam bootstrap playtime affects `NeverPlayed` without creating local
session history. Missing states and estimates remain default/null values.

Migration 2 adds `GameStates.ConsecutiveKeepGoingCount`, defaulting existing rows
to zero. Game-state upserts round-trip this value. Updating it from feedback is
the Phase 9 service's responsibility.

Run the phase gate from the repository root:

```powershell
dotnet build Wisp.sln
dotnet test Wisp.sln
```

Data tests create unique temporary SQLite files and remove them after each test.
They reference only Wisp.Data and its Core dependency.
