# Wisp.Data

Create a `WispDatabase` with the writable user database path and the existing,
bundled `completion-estimates.db` path. The user database's parent directory must
already exist. Await `IMigrationRunner.ApplyAsync` before using repositories:

```csharp
var database = new WispDatabase(userDatabasePath, completionEstimatesPath);
IMigrationRunner migrations = new MigrationRunner(database);
await migrations.ApplyAsync(cancellationToken);
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
as a release asset; runtime connections attach it with `mode=ro`. Tests build
their own small dataset from this resource, with no production metadata invented.

SQL parameters pass through `SqliteValues` so booleans become integer 0/1,
enums use their names, and timestamps become UTC text with millisecond precision.
Reading timestamps uses the same format.

Game upserts preserve the database-generated identity and creation time. Tags
are a set returned in name order. Their names must exist in the bundled
`TagDictionary`; unknown names roll back the upsert rather than inventing Steam
tag IDs. The dictionary's release data belongs to the later metadata phase.
Columns outside the current Core `Game` contract retain their schema defaults
or existing values.

Session completion persists the caller-supplied runtime and foreground seconds.
Only completed sessions contribute raw samples to `PlaytimeDistribution`.
Median calculation and recommendation logic remain outside this project.

Run the phase gate from the repository root:

```powershell
dotnet build Wisp.sln
dotnet test Wisp.sln
```

Data tests create unique temporary SQLite files and remove them after each test.
They reference only Wisp.Data and its Core dependency.
