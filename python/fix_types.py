#!/usr/bin/env python3
"""
Script to fix common type annotation issues in the codebase.
"""

import os
import re
from pathlib import Path

def fix_file(filepath: Path) -> int:
    """Fix type annotations in a single file."""
    with open(filepath, 'r') as f:
        content = f.read()
    
    original = content
    fixes = 0
    
    # Fix missing return type for __init__ and other void functions
    patterns = [
        # Fix __init__ methods
        (r'(\s+def __init__\(self[^)]*\)):', r'\1 -> None:'),
        
        # Fix methods that clearly don't return anything (have no return statement)
        (r'(\s+def (cleanup|reset|clear_cache|set_data|add_resource|initialize|signal_space_available)\(self[^)]*\)):', r'\1 -> None:'),
        
        # Fix async methods that don't return anything
        (r'(\s+async def \w+\(self[^)]*\)):(?!\s*->)', r'\1 -> None:'),
        
        # Fix regular methods that are missing return types
        (r'(\s+def \w+\(self[^)]*\)):(?!\s*->)(?=\s*""")', r'\1 -> None:'),
    ]
    
    for pattern, replacement in patterns:
        new_content = re.sub(pattern, replacement, content)
        if new_content != content:
            fixes += len(re.findall(pattern, content))
            content = new_content
    
    # Write back if changed
    if content != original:
        with open(filepath, 'w') as f:
            f.write(content)
        print(f"Fixed {fixes} type annotations in {filepath}")
        return fixes
    
    return 0

def main():
    """Fix type annotations in all Python files."""
    base_dir = Path('/mnt/d/source/modelingevolution/streamer/src/zerobuffer/python/zerobuffer_serve')
    
    total_fixes = 0
    files_fixed = 0
    
    for py_file in base_dir.rglob('*.py'):
        if 'refactored' in str(py_file):
            continue  # Skip our refactored file which is already properly typed
            
        fixes = fix_file(py_file)
        if fixes > 0:
            total_fixes += fixes
            files_fixed += 1
    
    print(f"\nTotal: Fixed {total_fixes} type annotations in {files_fixed} files")

if __name__ == '__main__':
    main()