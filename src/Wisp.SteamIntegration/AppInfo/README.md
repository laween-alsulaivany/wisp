Phase 4 implements the existing `IAppInfoParser` and `ITagDictionaryProvider`
contracts. It reads local files only and adds no project or package references.

The parser supports appinfo v40 (`0x07564428`) and v41 (`0x07564429`), including
v41's indexed keys and shared string table. The outer layout follows the
[SteamAppInfo format reference](https://github.com/ValveResourceFormat/SteamAppInfo/blob/master/README.md).
Entry sizes count the 60 bytes of fixed fields plus the binary KV payload,
excluding the AppID and size fields. Hash fields are read but not verified.
ValveKeyValue deserializes each bounded KV blob; tags and genres remain numeric
IDs in `AppInfoRecord`. Full and partial controller support both set the flag.

Malformed payloads and short fixed headers are skipped using the declared entry
boundary. A truncated file or unusable entry size ends enumeration normally,
retaining earlier records. Recovery beyond a lost boundary is not reliable, so
the parser does not scan arbitrary payload bytes for possible app IDs. An
unknown magic, unreadable file, or damaged shared string table yields no records.
Cancellation propagates normally. Diagnostics use local `Trace.TraceWarning`.

Corrupt input is limited to 16 MiB per entry, 64 MiB for the shared string table,
one million table keys, 16 KiB per table key, and 64 nested KV objects. The iterative structure check
prevents the installed recursive ValveKeyValue reader from overflowing its stack;
metadata extraction still uses ValveKeyValue's binary deserializer.

The repository's `assets/tags.json` contains 76 English starter mappings covering genres, moods, themes,
and play styles, verified against [Steam's public tag list](https://store.steampowered.com/tagdata/populartags/english)
on 2026-09-19. Both Data and SteamIntegration link this single physical file into
their build and publish output. The provider reads that linked file once, while
Data upserts it into the database on every startup. Maintain this file
by hand for future app releases; unknown IDs resolve to null. There is no runtime
download or database dependency.

`AppInfoFixture` in the test project writes synthetic binary fixture files with
`BinaryWriter`, independently of ValveKeyValue's serializer. Tests enumerate the
complete file, including valid apps after corrupt ones, and use no Steam install.
