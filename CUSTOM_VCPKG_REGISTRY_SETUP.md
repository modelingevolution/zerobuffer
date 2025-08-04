# Custom vcpkg Git Registry Setup Guide

This guide shows how to create and maintain your own vcpkg git registry for distributing ZeroBuffer.

## Overview

A custom vcpkg git registry allows you to:
- Distribute your library privately or publicly
- Control versioning and release cycles
- Avoid the official vcpkg submission process
- Integrate with your CI/CD pipeline

## Repository Structure

Your registry repository needs this structure:

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

## Step-by-Step Setup

### 1. Create Registry Repository

Create a new GitHub repository: `zerobuffer-vcpkg-registry`

### 2. Initial Port Setup

The port files are already created in `vcpkg-port/zerobuffer/`. These will be copied to the registry.

### 3. Version Database

The version database tracks all versions and their git-tree hashes.

### 4. Baseline File

The baseline maps port names to their latest versions.

## Automation

The `.github/workflows/publish-vcpkg-registry.yml` workflow automatically:
1. Creates/updates the registry on new releases
2. Calculates git-tree hashes
3. Updates version database
4. Publishes with proper baseline

## User Installation

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

Then install:
```bash
vcpkg install zerobuffer
```

## Advantages

1. **Full Control**: You manage releases and versions
2. **Private Distribution**: Can use private repositories
3. **Fast Updates**: No review process
4. **Custom Features**: Add your own port features
5. **CI Integration**: Automate with GitHub Actions

## Concerns & Solutions

### 1. Maintenance Burden
**Concern**: You must maintain the registry
**Solution**: Automated workflows handle updates

### 2. User Trust
**Concern**: Users must trust your registry
**Solution**: Sign commits, use secure practices

### 3. Baseline Management
**Concern**: Users need correct baseline SHA
**Solution**: Document in README, provide latest SHA

### 4. Version Conflicts
**Concern**: Conflicts with official vcpkg
**Solution**: Use unique package names if needed

### 5. Binary Caching
**Concern**: No official binary cache
**Solution**: Set up your own or use GitHub Actions cache

## Testing Strategy

### Local Testing
```bash
# Clone your registry
git clone https://github.com/modelingevolution/zerobuffer-vcpkg-registry
cd zerobuffer-vcpkg-registry

# Test with overlay
vcpkg install zerobuffer --overlay-ports=./ports

# Test as registry
cd /your/test/project
# Create vcpkg-configuration.json with your registry
vcpkg install zerobuffer
```

### CI Testing
The `test-vcpkg-port.yml` workflow tests on all platforms.

### Integration Testing
Create a test project that uses your library via vcpkg.

## Best Practices

1. **Semantic Versioning**: Use proper version tags
2. **Commit Signing**: Sign registry commits
3. **Documentation**: Keep README updated with baseline
4. **Automation**: Use CI/CD for all updates
5. **Testing**: Test before pushing to registry
6. **Backup**: Keep registry backed up

## Comparison with Official vcpkg

| Feature | Official vcpkg | Custom Registry |
|---------|---------------|-----------------|
| Review Process | 1-2 weeks | Immediate |
| Control | Microsoft | You |
| Trust | High | Depends on you |
| Maintenance | Microsoft | You |
| Binary Cache | Provided | DIY |
| Discovery | vcpkg.io | Your docs |

## Security Considerations

1. **Access Control**: Use GitHub's security features
2. **Signed Commits**: GPG sign your commits
3. **Dependency Scanning**: Use GitHub security scanning
4. **Private Registry**: Use GitHub private repos if needed
5. **Token Management**: Secure your CI tokens

## Troubleshooting

### "Could not find registry"
- Check repository URL and access
- Verify baseline commit exists

### "Port not found"
- Ensure port exists at baseline commit
- Check package name in vcpkg.json

### "Version mismatch"
- Update baseline to latest commit
- Verify version database is correct