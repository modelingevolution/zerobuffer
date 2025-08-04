# Submitting ZeroBuffer to vcpkg Official Repository

This guide explains how to submit ZeroBuffer to the official vcpkg repository for easy installation.

## Prerequisites

1. Fork the vcpkg repository: https://github.com/Microsoft/vcpkg
2. Clone your fork locally
3. Set up vcpkg on your system

## Steps to Submit

### 1. Prepare the Port

Our port files are already created in `vcpkg-port/zerobuffer/`. We need to:

1. Copy them to the vcpkg ports directory
2. Test the port locally
3. Update version information

### 2. Local Testing

```bash
# From your vcpkg root directory
cp -R /path/to/zerobuffer/vcpkg-port/zerobuffer ./ports/

# Test the port installation
vcpkg install zerobuffer
vcpkg install zerobuffer[tests,benchmarks]
```

### 3. Create Version Files

After testing successfully:

```bash
# From vcpkg root
git add ports/zerobuffer/.
git commit -m "Add zerobuffer port"

# Generate version files
vcpkg x-add-version zerobuffer --overwrite-version

# Amend commit to include version files
git add versions/.
git commit --amend -m "[zerobuffer] Add new port"
```

### 4. Update Port Files for Submission

Before submitting, update `vcpkg-port/zerobuffer/portfile.cmake`:

```cmake
vcpkg_from_github(
    OUT_SOURCE_PATH SOURCE_PATH
    REPO modelingevolution/zerobuffer
    REF v1.0.0  # Use actual release tag
    SHA512 <actual-sha512>  # Calculate from release
    HEAD_REF main
)
```

To calculate SHA512:
```bash
# Download the release tarball
wget https://github.com/modelingevolution/zerobuffer/archive/refs/tags/v1.0.0.tar.gz

# Calculate SHA512
sha512sum v1.0.0.tar.gz
```

### 5. Submit Pull Request

1. Push to your fork:
   ```bash
   git push origin add-zerobuffer-port
   ```

2. Create PR at https://github.com/Microsoft/vcpkg/pulls

3. PR title: `[zerobuffer] Add new port`

4. PR description template:
   ```markdown
   **Describe the pull request**
   
   Add new port for zerobuffer - a high-performance zero-copy IPC library.
   
   - [x] Changes comply with the [maintainer guide](https://github.com/microsoft/vcpkg-docs/blob/main/vcpkg/contributing/maintainer-guide.md).
   - [x] SHA512s are updated for each updated download.
   - [x] The "supports" clause reflects platforms that may be fixed by this new version.
   - [x] Any fixed [CI baseline](https://github.com/microsoft/vcpkg/blob/master/scripts/ci.baseline.txt) entries are removed from that file.
   - [x] Any patches that are no longer applied are deleted from the port's directory.
   - [x] The version database is fixed by rerunning `vcpkg x-add-version --all` and committing the result.
   - [x] Only one version is added to each modified port's versions file.
   
   **Additional details**
   
   ZeroBuffer provides zero-copy inter-process communication for video streaming applications.
   Tested on Windows, Linux, and macOS.
   ```

## Alternative: Custom Registry

If you need immediate availability before official acceptance, create a custom registry:

### 1. Create Registry Repository

Create a new GitHub repository (e.g., `zerobuffer-vcpkg-registry`) with:

```
zerobuffer-vcpkg-registry/
├── ports/
│   └── zerobuffer/
│       ├── portfile.cmake
│       └── vcpkg.json
├── versions/
│   ├── baseline.json
│   └── z-/
│       └── zerobuffer.json
└── vcpkg-configuration.json
```

### 2. baseline.json

```json
{
  "default": {
    "zerobuffer": {
      "baseline": "1.0.0",
      "port-version": 0
    }
  }
}
```

### 3. versions/z-/zerobuffer.json

```json
{
  "versions": [
    {
      "version": "1.0.0",
      "port-version": 0,
      "git-tree": "<git-tree-sha>"
    }
  ]
}
```

### 4. Users Configure Registry

Users add to their `vcpkg-configuration.json`:

```json
{
  "registries": [
    {
      "kind": "git",
      "repository": "https://github.com/modelingevolution/zerobuffer-vcpkg-registry",
      "baseline": "<commit-sha>",
      "packages": ["zerobuffer"]
    }
  ]
}
```

Then install normally:
```bash
vcpkg install zerobuffer
```

## Timeline

- **Custom Registry**: Available immediately
- **Official Repository**: 1-2 weeks after PR submission (requires review)

## Benefits of Official Repository

1. **No configuration needed** - Works out of the box
2. **Automatic CI/CD** - Microsoft tests on 13 platforms
3. **Version management** - Handled by vcpkg team
4. **Discovery** - Listed on vcpkg.io
5. **Trust** - Official Microsoft validation

## Next Steps

1. Create a GitHub release with tag `v1.0.0`
2. Calculate SHA512 of release tarball
3. Update portfile.cmake with correct SHA512
4. Test locally with vcpkg
5. Submit PR to Microsoft/vcpkg

Once accepted, users can simply run:
```bash
vcpkg install zerobuffer
```

No configuration needed!