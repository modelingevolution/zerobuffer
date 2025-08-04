#!/bin/bash
# Script to set up and test a custom vcpkg registry for ZeroBuffer

set -e

echo "=== Custom vcpkg Registry Setup ==="
echo

# Configuration
REGISTRY_NAME="zerobuffer-vcpkg-registry"
REGISTRY_PATH="./test-registry"
TEST_PROJECT_PATH="./test-vcpkg-integration"

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

# Function to print colored output
print_status() {
    echo -e "${GREEN}✓${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}⚠${NC} $1"
}

print_error() {
    echo -e "${RED}✗${NC} $1"
}

# Step 1: Create local registry structure
echo "Step 1: Creating registry structure..."
rm -rf "$REGISTRY_PATH"
mkdir -p "$REGISTRY_PATH/ports/zerobuffer"
mkdir -p "$REGISTRY_PATH/versions/z-"

# Copy port files
cp -r vcpkg-port/zerobuffer/* "$REGISTRY_PATH/ports/zerobuffer/"
print_status "Port files copied"

# Step 2: Initialize git repository
echo
echo "Step 2: Initializing git repository..."
cd "$REGISTRY_PATH"
git init
git add ports/zerobuffer
git -c user.name="Test" -c user.email="test@example.com" commit -m "Add zerobuffer port"

# Get git-tree hash
GIT_TREE=$(git rev-parse HEAD:ports/zerobuffer)
print_status "Git tree hash: $GIT_TREE"

# Step 3: Create version database
echo
echo "Step 3: Creating version database..."

# Get version from vcpkg.json
VERSION=$(grep '"version"' ports/zerobuffer/vcpkg.json | head -1 | sed 's/.*"version": "\(.*\)".*/\1/')
print_status "Detected version: $VERSION"

# Create baseline.json
cat > versions/baseline.json << EOF
{
  "default": {
    "zerobuffer": {
      "baseline": "$VERSION",
      "port-version": 0
    }
  }
}
EOF

# Create version entry
cat > versions/z-/zerobuffer.json << EOF
{
  "versions": [
    {
      "version": "$VERSION",
      "port-version": 0,
      "git-tree": "$GIT_TREE"
    }
  ]
}
EOF

git add versions/
git -c user.name="Test" -c user.email="test@example.com" commit -m "Add version database"

# Get baseline commit
BASELINE_COMMIT=$(git rev-parse HEAD)
print_status "Baseline commit: $BASELINE_COMMIT"

cd ..

# Step 4: Create test project
echo
echo "Step 4: Creating test project..."
rm -rf "$TEST_PROJECT_PATH"
mkdir -p "$TEST_PROJECT_PATH"
cd "$TEST_PROJECT_PATH"

# Create vcpkg-configuration.json
cat > vcpkg-configuration.json << EOF
{
  "registries": [
    {
      "kind": "git",
      "repository": "file://$PWD/../$REGISTRY_PATH",
      "baseline": "$BASELINE_COMMIT",
      "packages": ["zerobuffer"]
    }
  ]
}
EOF

# Create vcpkg.json
cat > vcpkg.json << EOF
{
  "dependencies": [
    "zerobuffer"
  ]
}
EOF

# Create test CMakeLists.txt
cat > CMakeLists.txt << 'EOF'
cmake_minimum_required(VERSION 3.20)
project(test_zerobuffer_registry)

find_package(zerobuffer CONFIG REQUIRED)

add_executable(test_app main.cpp)
target_link_libraries(test_app PRIVATE zerobuffer::zerobuffer)
EOF

# Create test main.cpp
cat > main.cpp << 'EOF'
#include <zerobuffer/zerobuffer.h>
#include <iostream>

int main() {
    try {
        zerobuffer::BufferConfig config(1024, 1024*1024);
        std::cout << "✓ ZeroBuffer loaded from custom registry!" << std::endl;
        std::cout << "  Metadata size: " << config.metadata_size << std::endl;
        std::cout << "  Payload size: " << config.payload_size << std::endl;
        return 0;
    } catch (const std::exception& e) {
        std::cerr << "✗ Error: " << e.what() << std::endl;
        return 1;
    }
}
EOF

cd ..

# Step 5: Test installation
echo
echo "Step 5: Testing vcpkg installation..."
echo "Run these commands to test:"
echo
echo "  cd $TEST_PROJECT_PATH"
echo "  vcpkg install"
echo "  mkdir build && cd build"
echo "  cmake .. -DCMAKE_TOOLCHAIN_FILE=[vcpkg-root]/scripts/buildsystems/vcpkg.cmake"
echo "  cmake --build ."
echo "  ./test_app"
echo

# Step 6: Create deployment instructions
cat > "$REGISTRY_PATH/DEPLOY.md" << EOF
# Deploying the Registry

## GitHub Deployment

1. Create repository: https://github.com/$USER/$REGISTRY_NAME

2. Push the registry:
   \`\`\`bash
   cd $REGISTRY_PATH
   git remote add origin https://github.com/$USER/$REGISTRY_NAME.git
   git push -u origin main
   \`\`\`

3. Users configure with:
   \`\`\`json
   {
     "registries": [{
       "kind": "git",
       "repository": "https://github.com/$USER/$REGISTRY_NAME",
       "baseline": "$BASELINE_COMMIT",
       "packages": ["zerobuffer"]
     }]
   }
   \`\`\`

## Private Registry

For private distribution, create a private GitHub repository and add users as collaborators.

## GitLab/Bitbucket

The same structure works with any git hosting service.
EOF

# Summary
echo "=== Setup Complete ==="
echo
print_status "Registry created at: $REGISTRY_PATH"
print_status "Test project at: $TEST_PROJECT_PATH"
print_status "Baseline commit: $BASELINE_COMMIT"
echo
echo "Next steps:"
echo "1. Test locally using the commands above"
echo "2. Push to GitHub/GitLab/Bitbucket"
echo "3. Share configuration with users"
echo
print_warning "Remember to update the baseline commit when you update the registry!"