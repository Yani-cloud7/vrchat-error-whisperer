# Changelog

## 0.1.16

- Added generic C# compiler error analysis for fresh scripts not covered by the corpus.
- Detects file, line, column, compiler code, and likely next action for errors such as `CS1002`, `CS1022`, and `CS1513`.
- Added color cues for error, warning, readiness, upload chance, and finding lanes.

## 0.1.15

- Renamed the packaged corpus to `vrchat-error-corpus.json`.
- Removed project-specific corpus metadata and private seed-project wording from packaged files.
- Updated README install guidance and advisory language.
- Removed placeholder author email from the package manifest.

## 0.1.14

- Added current open scene awareness to the suspect asset scanner.
- Boosted suspects referenced by currently open scenes.
- Added the `Likely active in current open scene` label.
- Improved next-action wording for suspects that are probably active in the current world.
