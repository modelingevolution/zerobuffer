#!/usr/bin/env python3
import os
import re

features_dir = "/mnt/d/source/modelingevolution/streamer/src/zerobuffer/modules/harmony/ModelingEvolution.Harmony/Features"

def fix_feature_file(filepath):
    with open(filepath, 'r') as f:
        content = f.read()
    
    # Fix lines that have bad grammar like "reads frame should have"
    content = re.sub(r"reads frame should have", r"reads frame that should have", content)
    content = re.sub(r"write should succeed", r"should write successfully", content)
    content = re.sub(r"write should complete", r"should complete write", content)
    content = re.sub(r"write should fail", r"should fail to write", content)
    content = re.sub(r"write should wait", r"should wait to write", content)
    
    # Fix orphaned "And" at the beginning of scenarios
    content = re.sub(r'(\n\s*Scenario:[^\n]+\n\s*)And\s+', r'\1Given ', content)
    
    # Add missing process context to some steps
    content = re.sub(r"(\s+)And write '(\d+)' frames", r"\1And the 'writer' process writes '\2' frames", content)
    
    with open(filepath, 'w') as f:
        f.write(content)

# Process all feature files
for filename in os.listdir(features_dir):
    if filename.endswith('.feature'):
        filepath = os.path.join(features_dir, filename)
        print(f"Fixing {filename}...")
        fix_feature_file(filepath)

print("Done!")