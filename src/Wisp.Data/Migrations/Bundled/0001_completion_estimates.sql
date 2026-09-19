CREATE TABLE CompletionEstimates (
    AppId                INTEGER PRIMARY KEY,
    MainStoryMinutes     INTEGER,
    MainPlusExtraMinutes INTEGER,
    CompletionistMinutes INTEGER,
    SourceVersion        TEXT NOT NULL
);
