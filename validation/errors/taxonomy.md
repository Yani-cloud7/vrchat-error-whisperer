# Diagnosis Taxonomy

## Priority Lanes

- `fixFirst`: blockers that make other errors meaningless until resolved.
- `then`: important issues after the build or main behavior path works.
- `later`: real issues that should not distract from the root cause.

## Categories

- `build-export`: Unity/VRChat bundle creation fails before upload.
- `upload-readiness`: upload should wait for local Build & Test or blueprint checks.
- `udon-compile`: C# or UdonSharp compile blocks.
- `udon-import`: Unity imports scripts but UdonSharp program assets/components are broken.
- `network-sync`: state divergence, late-join failure, ownership/sync mistakes.
- `audio-networking`: audio feedback fails due to local/network event timing or AudioSource setup.
- `interaction-wiring`: UI/interact events appear to work visually but do not invoke the real logic path.
- `optimization`: size, texture, material, or platform-readiness problems.
- `visual-polish`: world UX/readability issues, especially screenshots or in-world UI.
- `creator-workflow`: publishing, changelog, documentation, or live-world workflow issues.
- `project-safety`: backups, versioning, rollback, cache/recovery.

## Product Rule

When a case matches multiple categories, show the category that best answers:

> What should the creator do first to get unstuck?

That is more important than detecting every possible warning.
