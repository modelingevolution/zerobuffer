#!/bin/bash
# Script to prepare ZeroBuffer for vcpkg submission

set -e

echo "=== ZeroBuffer vcpkg Submission Preparation ==="
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
if git rev-parse "v$VERSION" >/dev/null 2>&1; then
    echo "✓ Git tag v$VERSION exists"
else
    echo "✗ Git tag v$VERSION not found"
    echo "  Create it with: git tag -a v$VERSION -m 'Release v$VERSION'"
    echo "  Then push with: git push origin v$VERSION"
    exit 1
fi

# Calculate SHA512 for the release
echo
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

# Update portfile.cmake
echo
echo "Updating portfile.cmake..."
PORTFILE="vcpkg-port/zerobuffer/portfile.cmake"

# Create updated portfile
cat > "$PORTFILE.new" << EOF
vcpkg_from_github(
    OUT_SOURCE_PATH SOURCE_PATH
    REPO modelingevolution/zerobuffer
    REF v\${VERSION}
    SHA512 $SHA512
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

mv "$PORTFILE.new" "$PORTFILE"
echo "✓ Updated $PORTFILE"

# Update port vcpkg.json version
echo
echo "Updating port version..."
sed -i "s/\"version\": \"[0-9.]*\"/\"version\": \"$VERSION\"/" vcpkg-port/zerobuffer/vcpkg.json
echo "✓ Updated vcpkg-port/zerobuffer/vcpkg.json"

echo
echo "=== Next Steps ==="
echo
echo "1. Test the port locally:"
echo "   cp -R vcpkg-port/zerobuffer /path/to/vcpkg/ports/"
echo "   vcpkg install zerobuffer"
echo
echo "2. Fork https://github.com/Microsoft/vcpkg"
echo
echo "3. In your vcpkg fork:"
echo "   git checkout -b add-zerobuffer-port"
echo "   cp -R /path/to/zerobuffer/vcpkg-port/zerobuffer ./ports/"
echo "   git add ports/zerobuffer"
echo "   git commit -m '[zerobuffer] Add new port'"
echo "   vcpkg x-add-version zerobuffer"
echo "   git add versions/"
echo "   git commit --amend -m '[zerobuffer] Add new port'"
echo "   git push origin add-zerobuffer-port"
echo
echo "4. Create pull request at https://github.com/Microsoft/vcpkg/pulls"
echo
echo "Port files are ready in: vcpkg-port/zerobuffer/"