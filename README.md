# VPM Repository

This folder is the generated VRChat Creator Companion repository payload.

Run this from the workspace root whenever the package changes:

```powershell
.\tools\build-vpm-repository.ps1
```

Publish these generated files somewhere public:

- `vpm-repository/index.json`
- `vpm-repository/packages/com.omistaja.error-whisperer-0.1.0.zip`

For GitHub Pages, the final Creator Companion repository URL would look like:

```text
https://OMISTAJA.github.io/vrchat-error-whisperer/index.json
```

For GitHub Releases instead of GitHub Pages, update the script's `BaseUrl` so `index.json` and the package zip point at the actual release asset URLs.
