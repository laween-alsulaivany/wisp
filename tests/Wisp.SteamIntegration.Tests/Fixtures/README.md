All VDF and ACF samples here are small, hand-authored test data. Account IDs,
names, timestamps, game records, and library paths are synthetic. Tests read
these files from their copied output directory and never inspect a real Steam
installation, change registry values, or make network calls.

Library discovery returns library base directories. Manifest parsing accepts
those directories and appends `steamapps`; manifest installation paths are
absolute paths beneath `steamapps/common`.

`LocalConfig` includes missing, renamed, mixed-case, invalid, and out-of-range
fields. Unknown key names default to zero/null; they are not guessed from other
fields. The final valid entry checks that malformed app entries do not stop
parsing. `Truncated` and the truncated ACF simulate incomplete file writes.
