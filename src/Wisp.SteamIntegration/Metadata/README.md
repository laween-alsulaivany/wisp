# Metadata sync and artwork

`MetadataSyncService` implements the Phase 5 startup and delta entry points using Core interfaces. Register one instance so calls share its manifest cache and serialization gate. Hosting and file-change wiring belong to Phase 10.

Startup scans every configured library, compares against `IGameRepository.GetAllAsync`, and upserts installs/removals. Removed games keep their rows, metadata, and artwork. Missing libraries or incomplete manifest parses defer the sync to avoid false removals.

Delta calls enumerate manifest filenames, lengths, and modification timestamps, then reparse only changed libraries. Unchanged games are not rewritten. A delta call also works before startup. File changes that preserve both length and modification time require an explicit startup sync to force a rescan.

Metadata comes only from local `appcache/appinfo.vdf`. The existing parser streams the file; sync applies records only to new or stale manifest games. Missing entries stay stale. Repeated delta calls do not retry the same missing entries until appinfo changes or another pending AppID appears. Startup forces a retry. Successful metadata writes resolve bundled tag names and record the injected clock's UTC time.

`ArtworkFetcher` is separate from sync and is called only when artwork is explicitly needed. Inject an unauthenticated `HttpClient`, `IGameRepository`, and `IClock`. It downloads the specified Steam CDN header into `%LOCALAPPDATA%\Wisp\artwork\{appId}.jpg` using a temporary file, then records the path and fetch time. Existing files are reused across instances. HTTP, network, timeout, and filesystem failures return null; caller-requested cancellation propagates. It does not own the injected HTTP client.

Both services implement `IDisposable` for their serialization gates. All Phase 5 HTTP tests use an injected fake handler; no test contacts Steam.
