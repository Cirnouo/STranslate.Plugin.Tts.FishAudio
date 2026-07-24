# Completion Workflow

- After code changes, update all affected localization resources and documentation in the same requirement batch.
- Treat source comments as documentation: keep them accurate, useful, and synchronized with behavior.
- Update `CHANGELOG.md`, `docs/DESIGN_DECISIONS.md`, locale files, and `CLAUDE.md`/`AGENTS.md` whenever behavior or user-facing text changes.
- After code and documentation updates, run the documented Debug build command with regression checks (`.\build.ps1 -Clean -Test`) and perform full regression checks relevant to the change.
- Only after verification passes, write a conventional, specific commit message and commit the tracked changes.
