#!/bin/bash

# Script to copy feature files from source of truth and fix "And" steps
# Python's pytest-bdd doesn't support @and decorator, so we need to replace
# "And" with the appropriate Given/When/Then based on the previous step

set -e

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$SCRIPT_DIR"

# Source of truth for feature files
SOURCE_DIR="../ZeroBuffer.Harmony.Tests/Features"
TARGET_DIR="features"

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

# Check if source directory exists
if [ ! -d "$SOURCE_DIR" ]; then
    echo -e "${RED}Error: Source feature directory not found at $SOURCE_DIR${NC}"
    exit 1
fi

# Create target directory if it doesn't exist
mkdir -p "$TARGET_DIR"

echo -e "${YELLOW}Copying and fixing feature files from source of truth...${NC}"

# Function to fix And steps in a feature file
fix_and_steps() {
    local input_file="$1"
    local output_file="$2"
    
    # Use awk to track the previous step type and replace And accordingly
    awk '
    BEGIN {
        last_step_type = ""
    }
    {
        # Remove leading whitespace for checking, but preserve it for output
        line = $0
        gsub(/^[ \t]+/, "", line)
        
        # Check if this is a Given/When/Then step
        if (line ~ /^Given /) {
            last_step_type = "Given"
            print $0
        }
        else if (line ~ /^When /) {
            last_step_type = "When"
            print $0
        }
        else if (line ~ /^Then /) {
            last_step_type = "Then"
            print $0
        }
        else if (line ~ /^And /) {
            # Replace "And" with the last step type
            if (last_step_type != "") {
                # Preserve original indentation
                match($0, /^[ \t]+/)
                indent = substr($0, RSTART, RLENGTH)
                # Get the text after "And "
                text = $0
                sub(/^[ \t]+And /, "", text)
                print indent last_step_type " " text
            } else {
                # If we haven'\''t seen a step type yet, keep as is
                print $0
            }
        }
        else {
            # Not a step line, print as is
            print $0
            # Reset if we hit a new scenario/example
            if (line ~ /^Scenario:/ || line ~ /^Scenario Outline:/ || line ~ /^Examples:/) {
                last_step_type = ""
            }
        }
    }
    ' "$input_file" > "$output_file"
}

# Copy and fix all feature files
for feature in "$SOURCE_DIR"/*.feature; do
    if [ -f "$feature" ]; then
        filename=$(basename "$feature")
        echo "Processing $filename..."
        
        # Copy and fix "And" steps
        fix_and_steps "$feature" "$TARGET_DIR/$filename"
        
        echo "  ✓ Fixed and copied $filename"
    fi
done

# Count the copied files
count=$(ls -1 "$TARGET_DIR"/*.feature 2>/dev/null | wc -l)

if [ $count -gt 0 ]; then
    echo -e "${GREEN}✓ Successfully processed $count feature files${NC}"
    echo -e "${GREEN}✓ 'And' steps have been converted to their appropriate types${NC}"
    echo -e "${GREEN}✓ Feature files are ready for pytest-bdd${NC}"
else
    echo -e "${RED}✗ No feature files were processed${NC}"
    exit 1
fi