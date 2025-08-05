#!/bin/bash

# Fix platform-specific steps in all feature files
FEATURES_DIR="/mnt/d/source/modelingevolution/streamer/src/zerobuffer/modules/harmony/ModelingEvolution.Harmony/Features"

echo "Fixing platform-specific steps in feature files..."

# Process each feature file
for file in "$FEATURES_DIR"/*.feature; do
    if [ -f "$file" ]; then
        filename=$(basename "$file")
        echo "Processing $filename..."
        
        # Create a temporary file
        temp_file="${file}.tmp"
        
        # Replace platform-specific patterns
        sed -E \
            -e "s/Given the reader is '(csharp|python|cpp)'/Given the 'reader' process/g" \
            -e "s/When the reader is '(csharp|python|cpp)'/When the 'reader' process/g" \
            -e "s/Then the reader is '(csharp|python|cpp)'/Then the 'reader' process/g" \
            -e "s/And the reader is '(csharp|python|cpp)'/And the 'reader' process/g" \
            -e "s/Given the writer is '(csharp|python|cpp)'/Given the 'writer' process/g" \
            -e "s/When the writer is '(csharp|python|cpp)'/When the 'writer' process/g" \
            -e "s/Then the writer is '(csharp|python|cpp)'/Then the 'writer' process/g" \
            -e "s/And the writer is '(csharp|python|cpp)'/And the 'writer' process/g" \
            -e "s/And create buffer/And the 'reader' process creates buffer/g" \
            -e "s/When connect to buffer/When the 'writer' process connects to buffer/g" \
            -e "s/And connect to buffer/And the 'writer' process connects to buffer/g" \
            "$file" > "$temp_file"
        
        # Move the temporary file back
        mv "$temp_file" "$file"
    fi
done

echo "Done fixing platform-specific steps!"