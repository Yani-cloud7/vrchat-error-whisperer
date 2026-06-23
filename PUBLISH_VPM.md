# Publish To VRChat Creator Companion

This project now has a VPM-compatible package and repository listing.

## Files

- Package source: `Packages/com.yanicloud7.error-whisperer`
- Repository listing: `vpm-repository/index.json`
- Package zip: `vpm-repository/packages/com.yanicloud7.error-whisperer-0.1.15.zip`
- Rebuild script: `tools/build-vpm-repository.ps1`

## Before Publishing

1. Confirm `Packages/com.yanicloud7.error-whisperer/package.json` has the intended version.
2. Run the matcher/build script:

   ```powershell
   .\tools\build-vpm-repository.ps1
   ```

3. Confirm the build reports `Matcher fixtures passed`.
4. Decide the public repo name. The current generated URLs assume:

   `https://yani-cloud7.github.io/vrchat-error-whisperer`

5. If the GitHub repo or username differs, rebuild with:

   ```powershell
   .\tools\build-vpm-repository.ps1 -BaseUrl "https://YOUR_USER.github.io/YOUR_REPO"
   ```

## GitHub Pages Flow

1. Create a public GitHub repo, for example `vrchat-error-whisperer`.
2. Commit this workspace.
3. Configure GitHub Pages to serve the `vpm-repository` folder as the site root, or copy the generated `index.json` and `packages` folder to your Pages branch/root.
4. Confirm this URL opens in a browser:

   `https://yani-cloud7.github.io/vrchat-error-whisperer/index.json`

5. In VRChat Creator Companion, add that URL as a community repository.
6. The package should appear as `VRChat Error Whisperer`.
7. Install it into a clean test project.
8. Open `Tools > VRChat Utility > Error Whisperer` and confirm the window opens.
9. Run `Analyze Current Console` and `Find Udon Assets` in a VRChat project with a known failed build.

## Release Update Flow

1. Change `version` in `Packages/com.yanicloud7.error-whisperer/package.json`.
2. Run:

   ```powershell
   .\tools\build-vpm-repository.ps1
   ```

3. Commit the changed package, `vpm-repository/index.json`, and new zip.
4. Publish the updated Pages content.
5. Re-add or refresh the repository in VCC and confirm the new version appears.

## Alpha Publishing Checklist

- [ ] README includes install/use steps and advisory warning.
- [ ] CHANGELOG has the release version.
- [ ] `tools/test-matcher.ps1` passes.
- [ ] `tools/build-vpm-repository.ps1` passes.
- [ ] `vpm-repository/index.json` references the correct package zip.
- [ ] Package zip contains `Editor/vrchat-error-corpus.json`.
- [ ] VCC can install the package from the GitHub Pages URL.
- [ ] Unity opens `Tools > VRChat Utility > Error Whisperer`.
