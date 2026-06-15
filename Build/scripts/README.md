# Build Scripts

PowerShell scripts for building, packaging, and deploying KleiKodesh.

## Scripts

- `build-installer.ps1` — Main build script that orchestrates the full build process
- `build-variants.ps1` — Builds all three architecture variants (x64, x86, AnyCPU)
- `create-release.ps1` — Creates GitHub release with built installers
- `sign-exe.ps1` — Code-signs executable files
- `version-check.ps1` — Validates version consistency across projects

## Build Process

The build process:
1. Validates prerequisites (Visual Studio, .NET SDK, NSIS)
2. Cleans previous builds
3. Builds the solution for each architecture variant
4. Creates installer packages via NSIS
5. Optionally signs and publishes to GitHub Releases

See `Build/README.md` for full build instructions.
