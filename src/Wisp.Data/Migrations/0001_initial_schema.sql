CREATE TABLE Games (
    GameId                         INTEGER PRIMARY KEY AUTOINCREMENT,
    AppId                          INTEGER NOT NULL UNIQUE,
    Name                           TEXT    NOT NULL,
    Installed                      INTEGER NOT NULL DEFAULT 0,
    InstallDir                     TEXT,
    IsFreeToPlay                   INTEGER NOT NULL DEFAULT 0,
    IsToolOrUtility                INTEGER NOT NULL DEFAULT 0,   -- excludes editors/dedicated servers/soundtracks/benchmarks
    IsDemo                         INTEGER NOT NULL DEFAULT 0,
    IsVrOnly                       INTEGER NOT NULL DEFAULT 0,
    SupportsController             INTEGER NOT NULL DEFAULT 0,
    IsSinglePlayer                 INTEGER NOT NULL DEFAULT 0,
    IsMultiplayer                  INTEGER NOT NULL DEFAULT 0,
    IsStoryFocused                 INTEGER NOT NULL DEFAULT 0,
    HeaderImagePath                TEXT,                          -- local cache path, null until lazily fetched
    HeaderImageFetchedUtc          TEXT,
    MetadataFetchedUtc             TEXT,
    MetadataStale                  INTEGER NOT NULL DEFAULT 1,
    SteamCumulativePlaytimeMinutes INTEGER NOT NULL DEFAULT 0,   -- bootstrapped from localconfig.vdf
    SteamLastPlayedUtc             TEXT,
    CreatedUtc                     TEXT    NOT NULL,
    UpdatedUtc                     TEXT    NOT NULL
);
CREATE UNIQUE INDEX IX_Games_AppId ON Games(AppId);
CREATE INDEX IX_Games_Installed   ON Games(Installed);

CREATE TABLE Sessions (
    SessionId               INTEGER PRIMARY KEY AUTOINCREMENT,
    GameId                  INTEGER NOT NULL,
    ProfileId               INTEGER NOT NULL,
    StartUtc                TEXT    NOT NULL,
    EndUtc                  TEXT,                                 -- null while session is open
    RuntimeSeconds          INTEGER,                               -- null while open
    ActiveForegroundSeconds INTEGER NOT NULL DEFAULT 0,
    RestartCount            INTEGER NOT NULL DEFAULT 0,
    LaunchSource            TEXT    NOT NULL CHECK (LaunchSource IN ('Recommendation','Manual')),
    FOREIGN KEY (GameId)    REFERENCES Games(GameId) ON DELETE CASCADE,
    FOREIGN KEY (ProfileId) REFERENCES SteamProfiles(ProfileId) ON DELETE CASCADE
);
CREATE INDEX IX_Sessions_Game_Profile ON Sessions(GameId, ProfileId);
CREATE INDEX IX_Sessions_StartUtc     ON Sessions(StartUtc);
CREATE INDEX IX_Sessions_Open         ON Sessions(EndUtc) WHERE EndUtc IS NULL;

CREATE TABLE SessionRestartEvents (
    RestartEventId  INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId       INTEGER NOT NULL,
    ClosedUtc       TEXT    NOT NULL,
    RelaunchedUtc   TEXT    NOT NULL,
    FOREIGN KEY (SessionId) REFERENCES Sessions(SessionId) ON DELETE CASCADE
);

CREATE TABLE Feedback (
    FeedbackId    INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId     INTEGER NOT NULL UNIQUE,
    GameId        INTEGER NOT NULL,
    ProfileId     INTEGER NOT NULL,
    FeedbackType  TEXT    NOT NULL CHECK (FeedbackType IN
                    ('KeepGoing','NotFeelingIt','TechnicalIssue','Interrupted',
                     'MaybeLater','Finished','Dropped','Pending')),
    RecordedUtc   TEXT,                                            -- null while IsPending = 1
    IsPending     INTEGER NOT NULL DEFAULT 0,
    Edited        INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (SessionId) REFERENCES Sessions(SessionId) ON DELETE CASCADE,
    FOREIGN KEY (GameId)    REFERENCES Games(GameId) ON DELETE CASCADE,
    FOREIGN KEY (ProfileId) REFERENCES SteamProfiles(ProfileId) ON DELETE CASCADE
);
CREATE INDEX IX_Feedback_Game_Profile ON Feedback(GameId, ProfileId);
CREATE INDEX IX_Feedback_Pending      ON Feedback(IsPending) WHERE IsPending = 1;

CREATE TABLE SteamProfiles (
    ProfileId    INTEGER PRIMARY KEY AUTOINCREMENT,
    SteamId64    TEXT    NOT NULL UNIQUE,
    AccountName  TEXT    NOT NULL,
    PersonaName  TEXT,
    LastSeenUtc  TEXT    NOT NULL
);

CREATE TABLE ProfileSettings (
    ProfileId                   INTEGER PRIMARY KEY,
    MaybeLaterCooldownDays      INTEGER NOT NULL DEFAULT 7,
    IncludeFinishedGames        INTEGER NOT NULL DEFAULT 0,
    IncludeDemos                INTEGER NOT NULL DEFAULT 0,
    IncludeVrOnly                INTEGER NOT NULL DEFAULT 0,
    StartWithWindows            INTEGER NOT NULL DEFAULT 1,
    ShowPostSessionFeedback     INTEGER NOT NULL DEFAULT 1,
    AdvancedFiltersExpanded     INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (ProfileId) REFERENCES SteamProfiles(ProfileId) ON DELETE CASCADE
);

CREATE TABLE GameStates (
    GameId             INTEGER NOT NULL,
    ProfileId          INTEGER NOT NULL,
    State              TEXT    NOT NULL CHECK (State IN
                         ('NoData','Active','MaybeLater','Finished','Dropped')),
    ActiveRankScore    REAL    NOT NULL DEFAULT 0,
    MaybeLaterUntilUtc TEXT,
    StateChangedUtc    TEXT    NOT NULL,
    PRIMARY KEY (GameId, ProfileId),
    FOREIGN KEY (GameId)    REFERENCES Games(GameId) ON DELETE CASCADE,
    FOREIGN KEY (ProfileId) REFERENCES SteamProfiles(ProfileId) ON DELETE CASCADE
);
CREATE INDEX IX_GameStates_Profile_State ON GameStates(ProfileId, State);

CREATE TABLE TagDictionary (       -- bundled static map, refreshed only via app releases
    TagId   INTEGER PRIMARY KEY,
    TagName TEXT NOT NULL
);

CREATE TABLE GameTags (
    GameId INTEGER NOT NULL,
    TagId  INTEGER NOT NULL,
    PRIMARY KEY (GameId, TagId),
    FOREIGN KEY (GameId) REFERENCES Games(GameId) ON DELETE CASCADE,
    FOREIGN KEY (TagId)  REFERENCES TagDictionary(TagId)
);
CREATE INDEX IX_GameTags_TagId ON GameTags(TagId);

CREATE TABLE SchemaMigrations (
    Version     INTEGER PRIMARY KEY,
    AppliedUtc  TEXT NOT NULL
);
