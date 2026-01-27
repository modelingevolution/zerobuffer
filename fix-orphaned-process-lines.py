#!/usr/bin/env python3
import os
import re

features_dir = "/mnt/d/source/modelingevolution/streamer/src/zerobuffer/modules/harmony/ModelingEvolution.Harmony/Features"

def fix_feature_file(filepath):
    with open(filepath, 'r') as f:
        lines = f.readlines()
    
    fixed_lines = []
    i = 0
    
    while i < len(lines):
        line = lines[i]
        
        # Check if this is an orphaned process line (just "Given/When/Then/And the 'reader/writer' process")
        orphaned_pattern = r"^\s*(Given|When|Then|And|But)\s+the\s+'(reader|writer)'\s+process\s*$"
        match = re.match(orphaned_pattern, line)
        
        if match:
            # Skip this orphaned line
            i += 1
            continue
        
        # Fix steps that need process context
        line = re.sub(r"^(\s*)(And|When)\s+write\s+frame", r"\1\2 the 'writer' process writes frame", line)
        line = re.sub(r"^(\s*)(And|When)\s+read\s+frame", r"\1\2 the 'reader' process reads frame", line)
        line = re.sub(r"^(\s*)(And|When)\s+signal\s+space", r"\1\2 the 'reader' process signals space", line)
        line = re.sub(r"^(\s*)(And|When)\s+attempt\s+to\s+write", r"\1\2 the 'writer' process attempts to write", line)
        line = re.sub(r"^(\s*)(And|When)\s+write\s+should", r"\1\2 the 'writer' process write should", line)
        line = re.sub(r"^(\s*)(And|When)\s+fill\s+buffer", r"\1\2 the 'writer' process fills buffer", line)
        line = re.sub(r"^(\s*)(And|When)\s+continue\s+filling", r"\1\2 the 'writer' process continues filling", line)
        line = re.sub(r"^(\s*)(And|Then)\s+attempt\s+to\s+connect", r"\1\2 another writer attempts to connect", line)
        line = re.sub(r"^(\s*)(And|Then)\s+connection\s+should", r"\1\2 the connection should", line)
        line = re.sub(r"^(\s*)(And|When)\s+read\s+all\s+frames", r"\1\2 the 'reader' process reads all frames", line)
        
        fixed_lines.append(line)
        i += 1
    
    # Join lines and clean up
    content = ''.join(fixed_lines)
    
    # Clean up any double blank lines
    content = re.sub(r'\n\n\n+', '\n\n', content)
    
    # Ensure file ends with newline
    if not content.endswith('\n'):
        content += '\n'
    
    with open(filepath, 'w') as f:
        f.write(content)
    
    return True

# Process all feature files
for filename in os.listdir(features_dir):
    if filename.endswith('.feature'):
        filepath = os.path.join(features_dir, filename)
        print(f"Processing {filename}...")
        fix_feature_file(filepath)

print("Done!")