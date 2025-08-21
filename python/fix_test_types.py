#!/usr/bin/env python3
"""
Script to automatically add type annotations to test methods
"""

import re
import sys
from pathlib import Path

def fix_test_file(filepath: Path) -> None:
    """Fix type annotations in a test file"""
    
    with open(filepath, 'r') as f:
        content = f.read()
    
    # Pattern for test methods without return type
    test_method_pattern = r'(\s+def test_[a-zA-Z_0-9]+\(self)([^)]*)\):'
    content = re.sub(test_method_pattern, r'\1\2) -> None:', content)
    
    # Pattern for setup/teardown methods
    setup_pattern = r'(\s+def (setup|teardown)_[a-zA-Z_0-9]+\(self)([^)]*)\):'
    content = re.sub(setup_pattern, r'\1\3) -> None:', content)
    
    # Pattern for fixture methods
    fixture_pattern = r'(\s+def [a-zA-Z_0-9]+\(self)\):(?!\s*->)'
    
    # Check for @pytest.fixture decorated methods
    lines = content.split('\n')
    new_lines = []
    for i, line in enumerate(lines):
        if '@pytest.fixture' in line or '@fixture' in line:
            # Next non-empty line should be the def
            j = i + 1
            while j < len(lines) and not lines[j].strip():
                j += 1
            if j < len(lines) and 'def ' in lines[j] and '):' in lines[j] and '-> ' not in lines[j]:
                lines[j] = lines[j].replace('):', ') -> str:') if 'buffer_name' in lines[j] else lines[j].replace('):', ') -> None:')
        new_lines.append(line)
    
    content = '\n'.join(new_lines)
    
    # Fix inner functions
    inner_func_pattern = r'(\s{8,}def [a-zA-Z_0-9]+\()([^)]*)\):(?!\s*->)'
    
    # Process content line by line for inner functions
    lines = content.split('\n')
    new_lines = []
    for line in lines:
        if re.match(r'\s{8,}def [a-zA-Z_0-9]+\([^)]*\):$', line) and '-> ' not in line:
            line = line.replace('):', ') -> None:')
        new_lines.append(line)
    
    content = '\n'.join(new_lines)
    
    # Fix async methods
    async_pattern = r'(\s+async def test_[a-zA-Z_0-9]+\(self)([^)]*)\):(?!\s*->)'
    content = re.sub(async_pattern, r'\1\2) -> None:', content)
    
    # Write back
    with open(filepath, 'w') as f:
        f.write(content)
    
    print(f"Fixed: {filepath}")

# Fix all test files
test_dir = Path("tests")
for test_file in test_dir.glob("test_*.py"):
    fix_test_file(test_file)

print("Done!")