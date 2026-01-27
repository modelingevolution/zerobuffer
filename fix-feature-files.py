#!/usr/bin/env python3
import os
import re

features_dir = "/mnt/d/source/modelingevolution/streamer/src/zerobuffer/modules/harmony/ModelingEvolution.Harmony/Features"

def fix_feature_file(filepath):
    with open(filepath, 'r') as f:
        content = f.read()
    
    original_content = content
    
    # Fix platform-specific reader/writer steps - these should be removed entirely
    # as they're redundant with the platform selection in the test configuration
    patterns_to_remove = [
        r"^\s*(Given|When|Then|And|But)\s+the\s+(reader|writer)\s+is\s+'(csharp|python|cpp)'\s*\n",
    ]
    
    for pattern in patterns_to_remove:
        content = re.sub(pattern, '', content, flags=re.MULTILINE)
    
    # Fix steps that need process context added
    replacements = [
        # Steps that create buffers without process context
        (r"^(\s*)(Given|And)\s+create\s+buffer\s+'", r"\1\2 the 'reader' process creates buffer '"),
        (r"^(\s*)(When|And)\s+connect\s+to\s+buffer\s+'", r"\1\2 the 'writer' process connects to buffer '"),
        
        # Fix any remaining isolated process steps
        (r"^(\s*)(When|Then|And)\s+the\s+'(reader|writer)'\s+process\s*$", r""),
    ]
    
    for pattern, replacement in replacements:
        content = re.sub(pattern, replacement, content, flags=re.MULTILINE)
    
    # Clean up any double blank lines created by removals
    content = re.sub(r'\n\n\n+', '\n\n', content)
    
    # Remove any trailing whitespace
    content = '\n'.join(line.rstrip() for line in content.split('\n'))
    
    # Ensure file ends with newline
    if not content.endswith('\n'):
        content += '\n'
    
    if content != original_content:
        with open(filepath, 'w') as f:
            f.write(content)
        return True
    return False

# Process all feature files
for filename in os.listdir(features_dir):
    if filename.endswith('.feature'):
        filepath = os.path.join(features_dir, filename)
        print(f"Processing {filename}...")
        if fix_feature_file(filepath):
            print(f"  ✓ Fixed {filename}")
        else:
            print(f"  - No changes needed for {filename}")

print("Done!")