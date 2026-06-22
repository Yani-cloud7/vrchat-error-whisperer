# Error Corpus

This folder stores real-world creator problems that can drive both prototypes:

- Web: paste an error/log and get a ranked answer.
- Unity/VCC: analyze the current Console and show the same ranked answer in-editor.

The seed file is:

- `vrchat-error-corpus.json`

Each case records:

- raw signals to match in logs or user descriptions
- likely root cause
- what to fix first
- what to ignore until later
- VRChat-specific notes
- recommendations
- validation metadata

This is the product moat. UI and packaging should consume this corpus rather than inventing separate rules.
