# Publishing ZeroBuffer to vcpkg Registry

This document explains how to publish ZeroBuffer to the custom vcpkg registry.

## Automated Publishing (GitHub Actions)

The repository includes a GitHub Actions workflow that automatically publishes to the registry when you push a version tag.

### Prerequisites

1. **Create a Personal Access Token (PAT)**:
   - Go to GitHub Settings → Developer settings → Personal access tokens
   - Create a new token with `repo` scope
   - Name it something like "VCPKG_REGISTRY_PAT"

2. **Add the PAT as a Repository Secret**:
   - Go to the zerobuffer repository settings
   - Navigate to Secrets and variables → Actions
   - Add a new repository secret named `VCPKG_REGISTRY_PAT`
   - Paste your PAT as the value

### Publishing Process

1. **Tag a Release**:
   ```bash
   git tag -a v1.0.0 -m "Release v1.0.0"
   git push origin v1.0.0
   ```

2. The workflow will automatically:
   - Create the vcpkg port structure
   - Calculate SHA512 for the release
   - Update the registry repository
   - Push changes to https://github.com/modelingevolution/zerobuffer-vcpkg-registry

### Manual Publishing

If you need to publish manually, use the included script:

```bash
./publish-to-registry.sh
```

This script will:
1. Detect the version from CMakeLists.txt
2. Download the release tarball and calculate SHA512
3. Update the registry repository locally
4. Provide instructions for pushing changes

### Registry Structure

The registry repository contains:
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
└── README.md
```

### Using the Registry

Projects can use zerobuffer by adding this to their `vcpkg-configuration.json`:

```json
{
  "registries": [
    {
      "kind": "git",
      "repository": "https://github.com/modelingevolution/zerobuffer-vcpkg-registry",
      "baseline": "main",
      "packages": ["zerobuffer"]
    }
  ]
}
```

Then install:
```bash
vcpkg install zerobuffer
```

## Troubleshooting

### Authentication Errors

If you see "Permission denied" errors:
1. Ensure the PAT is correctly set as `VCPKG_REGISTRY_PAT` secret
2. Verify the PAT has `repo` scope
3. Check that the PAT hasn't expired

### Version Conflicts

If the version already exists in the registry:
1. Increment the version in `cpp/CMakeLists.txt`
2. Create a new tag with the updated version
3. Push the tag to trigger the workflow

### Manual Registry Updates

If you need to update the registry manually:
1. Clone https://github.com/modelingevolution/zerobuffer-vcpkg-registry
2. Update the port files and version database
3. Commit and push the changes