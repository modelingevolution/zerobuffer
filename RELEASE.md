# Release Process

This document describes how to release new versions of ZeroBuffer packages.

## Prerequisites

### Secrets Required in GitHub Repository

1. **NUGET_API_KEY** - API key for publishing to NuGet.org
2. **PYPI_API_TOKEN** - API token for publishing to PyPI (use __token__ as username)

To add these secrets:
1. Go to the repository Settings → Secrets and variables → Actions
2. Add the secrets with the appropriate values

## Release Process

### Automated Release (Recommended)

1. **Tag the release:**
   ```bash
   # For all packages
   git tag -a v1.0.0 -m "Release version 1.0.0"
   
   # For specific package only
   git tag -a csharp-v1.0.0 -m "Release C# version 1.0.0"
   git tag -a python-v1.0.0 -m "Release Python version 1.0.0"
   
   # Push tags
   git push origin --tags
   ```

2. The GitHub Actions workflows will automatically:
   - Build the packages
   - Run all tests
   - Publish to NuGet/PyPI if tests pass

### Manual Release

You can also trigger releases manually from GitHub Actions:

1. Go to Actions tab in the repository
2. Select the appropriate workflow:
   - "Publish to NuGet" for C#
   - "Publish to PyPI" for Python
3. Click "Run workflow"
4. Enter the version number (e.g., 1.0.0)
5. Click "Run workflow"

## Version Numbering

We follow Semantic Versioning (SemVer):
- MAJOR.MINOR.PATCH (e.g., 1.0.0)
- MAJOR: Breaking API changes
- MINOR: New features (backwards compatible)
- PATCH: Bug fixes

## Pre-release Checklist

Before creating a release:

1. **Update version numbers:**
   - C#: Update version in workflow (it will override the project version)
   - Python: Version is set by the workflow

2. **Update documentation:**
   - Ensure README.md is up to date
   - Update any API documentation
   - Add release notes

3. **Test locally:**
   ```bash
   # C#
   cd csharp
   dotnet test
   dotnet pack
   
   # Python
   cd python
   pytest
   python -m build
   ```

4. **Check package contents:**
   ```bash
   # C#
   dotnet pack --configuration Release
   # Check the .nupkg file contents
   
   # Python
   python -m build
   twine check dist/*
   ```

## Package-specific Notes

### C# (NuGet)

- Package ID: `ZeroBuffer`
- Supports: .NET 6.0+
- Platform: Cross-platform (Windows, Linux, macOS)

### Python (PyPI)

- Package name: `zerobuffer`
- Supports: Python 3.8+
- Platform-specific dependencies are handled via extras

## Troubleshooting

### Build Failures

1. Check the GitHub Actions logs
2. Ensure all tests pass locally
3. Verify version numbers are correct

### Publishing Failures

1. Verify API keys/tokens are correct
2. Check if the version already exists (use --skip-duplicate)
3. Ensure package metadata is valid

## Post-release

After a successful release:

1. Create a GitHub Release with release notes
2. Update any dependent projects
3. Announce the release if appropriate