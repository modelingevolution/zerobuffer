# Understanding vcpkg Baselines

This document explains how vcpkg baselines work and why they're important for registry management.

## What is a Baseline?

A baseline in vcpkg is a **git commit SHA** that represents a specific point in time of a registry. It ensures reproducible builds by locking versions to a specific state.

## Key Concepts

### 1. Baseline != Version

The baseline is NOT a version number or branch name. It's a specific git commit SHA:

```json
{
  "baseline": "95f8b9c3e3b8c0c8c85211c3b96c6e1a75c8e5c9"  // ✓ Correct
  "baseline": "main"                                          // ✗ Wrong
  "baseline": "1.0.0"                                         // ✗ Wrong
}
```

### 2. Registry Baseline Structure

In the registry repository, `versions/baseline.json` contains:

```json
{
  "default": {
    "zerobuffer": {
      "baseline": "1.0.0",      // The version string
      "port-version": 0         // Port revision number
    }
  }
}
```

### 3. User Configuration

Users specify the baseline commit SHA in their `vcpkg-configuration.json`:

```json
{
  "registries": [
    {
      "kind": "git",
      "repository": "https://github.com/modelingevolution/zerobuffer-vcpkg-registry",
      "baseline": "a1b2c3d4e5f6...",  // Commit SHA from the registry repo
      "packages": ["zerobuffer"]
    }
  ]
}
```

## How It Works

1. **Registry Updates**: When you update the registry, you commit changes creating a new SHA
2. **User References**: Users reference that specific commit SHA as their baseline
3. **Version Resolution**: vcpkg reads `versions/baseline.json` at that commit to determine versions
4. **Reproducibility**: Same baseline = same versions, always

## Example Workflow

### Registry Maintainer:
```bash
# Update port files
git add ports/zerobuffer
git commit -m "Update zerobuffer to 1.0.1"

# Update baseline
vim versions/baseline.json  # Update version to 1.0.1
git add versions/baseline.json
git commit -m "Update baseline for zerobuffer 1.0.1"

# Get the commit SHA
git rev-parse HEAD
# Output: 7f8e9d0c1b2a3456789abcdef0123456789abcde
```

### User:
```json
// vcpkg-configuration.json
{
  "registries": [
    {
      "kind": "git",
      "repository": "https://github.com/modelingevolution/zerobuffer-vcpkg-registry",
      "baseline": "7f8e9d0c1b2a3456789abcdef0123456789abcde",
      "packages": ["zerobuffer"]
    }
  ]
}
```

## Finding the Current Baseline

### Method 1: Command Line
```bash
git ls-remote https://github.com/modelingevolution/zerobuffer-vcpkg-registry HEAD
```

### Method 2: GitHub UI
Visit: https://github.com/modelingevolution/zerobuffer-vcpkg-registry/commits/main

### Method 3: Local Clone
```bash
git clone https://github.com/modelingevolution/zerobuffer-vcpkg-registry
cd zerobuffer-vcpkg-registry
git rev-parse HEAD
```

## Best Practices

1. **Document Baselines**: Always document recommended baseline SHAs in your README
2. **Tag Releases**: Create git tags for important baselines
3. **Update Regularly**: Keep baseline updated but test thoroughly
4. **Version Carefully**: Increment port-version for port changes, version for upstream changes

## Common Mistakes

1. ❌ Using branch names as baseline
2. ❌ Using version numbers as baseline
3. ❌ Not updating baseline.json when adding new versions
4. ❌ Forgetting to commit after updating baseline.json

## Troubleshooting

### "Could not find baseline"
- Ensure the commit SHA exists in the registry repository
- Check you're using the full SHA (at least 7 characters)

### "Version not found"
- The baseline commit might not contain the version you need
- Update to a newer baseline commit

### "Registry not found"
- Verify the repository URL is correct
- Ensure the repository is publicly accessible