#!/bin/bash
# Script to publish ZeroBuffer to the custom vcpkg registry
# Repository: https://github.com/modelingevolution/zerobuffer-vcpkg-registry

set -e

echo "=== Publishing ZeroBuffer to Custom vcpkg Registry ==="
echo

# Check if we're in the right directory
if [ ! -f "cpp/CMakeLists.txt" ]; then
    echo "Error: Run this script from the zerobuffer root directory"
    exit 1
fi

# Get version from CMakeLists.txt
VERSION=$(grep "project(zerobuffer VERSION" cpp/CMakeLists.txt | sed -n 's/.*VERSION \([0-9.]*\).*/\1/p')
echo "Detected version: $VERSION"

# Check if tag exists
if ! git rev-parse "v$VERSION" >/dev/null 2>&1; then
    echo "Error: Git tag v$VERSION not found"
    echo "Create and push the tag first:"
    echo "  git tag -a v$VERSION -m 'Release v$VERSION'"
    echo "  git push origin v$VERSION"
    exit 1
fi

# Calculate SHA512 for the release
echo "Calculating SHA512 for release tarball..."
TARBALL_URL="https://github.com/modelingevolution/zerobuffer/archive/refs/tags/v$VERSION.tar.gz"
TEMP_FILE=$(mktemp)

echo "Downloading $TARBALL_URL..."
if curl -L -o "$TEMP_FILE" "$TARBALL_URL" 2>/dev/null; then
    SHA512=$(sha512sum "$TEMP_FILE" | cut -d' ' -f1)
    echo "SHA512: $SHA512"
    rm "$TEMP_FILE"
else
    echo "Error: Could not download release tarball"
    echo "Make sure the release v$VERSION exists on GitHub"
    rm -f "$TEMP_FILE"
    exit 1
fi

# Clone or update the registry repository
REGISTRY_DIR="/tmp/zerobuffer-vcpkg-registry"
if [ -d "$REGISTRY_DIR" ]; then
    echo "Updating existing registry clone..."
    cd "$REGISTRY_DIR"
    git fetch origin
    git checkout main
    git pull origin main
else
    echo "Cloning registry repository..."
    git clone https://github.com/modelingevolution/zerobuffer-vcpkg-registry.git "$REGISTRY_DIR"
    cd "$REGISTRY_DIR"
fi

# Update the port files
echo "Copying port files..."
PORT_DIR="ports/zerobuffer"
mkdir -p "$PORT_DIR"

# Copy port files from source
cp "$OLDPWD/vcpkg-port/zerobuffer/vcpkg.json" "$PORT_DIR/"
cp "$OLDPWD/vcpkg-port/zerobuffer/portfile.cmake" "$PORT_DIR/"

# Update portfile.cmake with correct SHA512
cat > "$PORT_DIR/portfile.cmake" << EOF
vcpkg_from_github(
    OUT_SOURCE_PATH SOURCE_PATH
    REPO modelingevolution/zerobuffer
    REF v${VERSION}
    SHA512 ${SHA512}
    HEAD_REF main
)

vcpkg_cmake_configure(
    SOURCE_PATH "\${SOURCE_PATH}/cpp"
    OPTIONS
        -DBUILD_TESTS=OFF
        -DBUILD_EXAMPLES=OFF
        -DBUILD_BENCHMARKS=OFF
)

vcpkg_cmake_install()
vcpkg_cmake_config_fixup(CONFIG_PATH lib/cmake/zerobuffer)

file(REMOVE_RECURSE "\${CURRENT_PACKAGES_DIR}/debug/include")

vcpkg_install_copyright(FILE_LIST "\${SOURCE_PATH}/LICENSE")
EOF

# Update version in vcpkg.json
sed -i "s/\"version\": \"[0-9.]*\"/\"version\": \"$VERSION\"/" "$PORT_DIR/vcpkg.json"

# Generate git-tree hash for the port
echo "Generating git-tree hash..."
GIT_TREE=$(git -C "$PORT_DIR" write-tree --prefix=ports/zerobuffer/)
if [ -z "$GIT_TREE" ]; then
    # Fallback: manually calculate
    cd "$PORT_DIR"
    GIT_TREE=$(git hash-object -t tree --stdin <<< $(git ls-files -s | git mktree))
    cd "$REGISTRY_DIR"
fi
echo "Git tree hash: $GIT_TREE"

# Update versions database
VERSIONS_DIR="versions/z-/zerobuffer"
mkdir -p "$VERSIONS_DIR"

# Create or update version file
VERSION_FILE="$VERSIONS_DIR/versions.json"
if [ -f "$VERSION_FILE" ]; then
    # Add new version entry using jq or manual JSON manipulation
    echo "Updating existing versions.json..."
    
    # Create new version entry
    NEW_VERSION_ENTRY=$(cat <<EOF
{
  "version": "$VERSION",
  "git-tree": "$GIT_TREE"
}
EOF
)
    
    # Read existing content and append new version
    if command -v jq >/dev/null 2>&1; then
        jq ".versions += [$NEW_VERSION_ENTRY]" "$VERSION_FILE" > "$VERSION_FILE.tmp"
        mv "$VERSION_FILE.tmp" "$VERSION_FILE"
    else
        # Manual JSON manipulation (less reliable but works without jq)
        sed -i '/"versions": \[/,/\]/{ 
            /\]/ i\
  ,\
  {\
    "version": "'"$VERSION"'",\
    "git-tree": "'"$GIT_TREE"'"\
  }
        }' "$VERSION_FILE"
    fi
else
    # Create new versions.json
    echo "Creating new versions.json..."
    cat > "$VERSION_FILE" << EOF
{
  "versions": [
    {
      "version": "$VERSION",
      "git-tree": "$GIT_TREE"
    }
  ]
}
EOF
fi

# Update baseline
BASELINE_FILE="versions/baseline.json"
if [ -f "$BASELINE_FILE" ]; then
    echo "Updating baseline.json..."
    if command -v jq >/dev/null 2>&1; then
        jq ".default.zerobuffer.baseline = \"$VERSION\"" "$BASELINE_FILE" > "$BASELINE_FILE.tmp"
        mv "$BASELINE_FILE.tmp" "$BASELINE_FILE"
    else
        sed -i "s/\"zerobuffer\": {[^}]*}/\"zerobuffer\": { \"baseline\": \"$VERSION\" }/" "$BASELINE_FILE"
    fi
else
    # Create baseline.json
    echo "Creating baseline.json..."
    cat > "$BASELINE_FILE" << EOF
{
  "default": {
    "zerobuffer": {
      "baseline": "$VERSION"
    }
  }
}
EOF
fi

# Commit changes
echo
echo "Committing changes..."
git add ports/zerobuffer/
git add versions/
git commit -m "[zerobuffer] Update to version $VERSION" || echo "No changes to commit"

# Show what would be pushed
echo
echo "=== Changes to be pushed ==="
git log --oneline -n 5
echo
echo "=== Modified files ==="
git diff --name-only HEAD~1

echo
echo "=== Next Steps ==="
echo
echo "1. Review the changes in: $REGISTRY_DIR"
echo
echo "2. If everything looks good, push to the registry:"
echo "   cd $REGISTRY_DIR"
echo "   git push origin main"
echo
echo "3. Or create a pull request:"
echo "   cd $REGISTRY_DIR"
echo "   git checkout -b update-zerobuffer-$VERSION"
echo "   git push origin update-zerobuffer-$VERSION"
echo "   # Then create PR on GitHub"
echo
echo "4. Test the registry in a project by creating vcpkg-configuration.json:"
cat << 'EOF'
{
  "default-registry": {
    "kind": "git",
    "repository": "https://github.com/Microsoft/vcpkg",
    "baseline": "latest"
  },
  "registries": [
    {
      "kind": "git",
      "repository": "https://github.com/modelingevolution/zerobuffer-vcpkg-registry",
      "baseline": "main",
      "packages": ["zerobuffer"]
    }
  ]
}
EOF
echo
echo "Then run: vcpkg install zerobuffer"