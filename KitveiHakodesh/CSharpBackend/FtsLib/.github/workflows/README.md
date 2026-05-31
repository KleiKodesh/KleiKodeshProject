# workflows/

GitHub Actions CI/CD workflows.

## Overview

This directory contains GitHub Actions workflow definitions for automated testing, building, and deployment.

## Current Status

This directory is currently empty — workflows need to be defined.

## Planned Workflows

### ci.yml
- Build C# projects on Windows
- Run test suite
- Report results

### build-dart.yml
- Build Dart library
- Run Dart tests
- Compile AOT binaries

### release.yml
- Create release builds
- Package binaries
- Generate release notes

## Future Workflows

| Workflow | Purpose |
|---|---|
| `ci.yml` | Continuous integration |
| `build-dart.yml` | Dart/AOT builds |
| `build-csharp.yml` | .NET builds |
| `test.yml` | Run test suites |
| `release.yml` | Release automation |
| `docs.yml` | Documentation deployment |

## Workflow Examples

### CI Workflow (ci.yml)
```yaml
name: CI
on: [push, pull_request]
jobs:
  build-csharp:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v3
      - name: Setup MSBuild
        uses: microsoft/setup-msbuild@v1
      - name: Build
        run: msbuild Ftslib-Csharp/FtsLib.slnx /p:Configuration=Release
  build-dart:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - uses: dart-lang/setup-dart@v1
      - run: cd FtsLib-Dart/FtsDartLib && dart pub get && dart test
```
