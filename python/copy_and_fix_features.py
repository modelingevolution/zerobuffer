#!/usr/bin/env python3
"""
Copy feature files from ZeroBuffer.Harmony.Tests and fix syntax issues.

This script:
1. Copies all feature files to a local features/ directory
2. Fixes scenarios that incorrectly start with 'And' by replacing with the appropriate keyword
3. Tracks what keyword (Given/When/Then) was last used to determine proper replacement
"""

import re
import shutil
from pathlib import Path
from typing import Optional, List, Tuple


class FeatureFileFixer:
    """Fixes common syntax issues in Gherkin feature files"""
    
    def find_last_keyword(self, lines: List[str], current_index: int) -> str:
        """Look backwards through lines to find the last Given/When/Then"""
        for i in range(current_index - 1, -1, -1):
            stripped = lines[i].strip()
            if stripped.startswith('Given '):
                return 'Given'
            elif stripped.startswith('When '):
                return 'When'
            elif stripped.startswith('Then '):
                return 'Then'
        # If we can't find any, default to Given
        return 'Given'
    
    def fix_file(self, input_path: Path, output_path: Path) -> int:
        """Fix a feature file and return the number of fixes made"""
        fixes_made = 0
        
        with open(input_path, 'r', encoding='utf-8') as infile:
            lines = infile.readlines()
        
        fixed_lines = []
        for line_num, line in enumerate(lines):
            stripped = line.strip()
            
            # If line starts with 'And' at the beginning of a scenario/background
            # (i.e., no Given/When/Then has been seen yet in this block)
            if stripped.startswith('And '):
                # Look backwards to find the last Given/When/Then
                last_keyword = self.find_last_keyword(lines, line_num)
                
                # Check if we need to replace it (only if it's at the start of a block)
                # Look at previous non-empty line
                prev_line_idx = line_num - 1
                while prev_line_idx >= 0 and not lines[prev_line_idx].strip():
                    prev_line_idx -= 1
                
                if prev_line_idx >= 0:
                    prev_stripped = lines[prev_line_idx].strip()
                    # If previous line is a scenario/background declaration, fix this And
                    if (prev_stripped.startswith('Scenario:') or 
                        prev_stripped.startswith('Scenario Outline:') or
                        prev_stripped.startswith('Background:')):
                        # Replace And with the last keyword found
                        indent = line[:len(line) - len(line.lstrip())]
                        fixed_line = indent + last_keyword + line.strip()[3:]  # Remove 'And' and add new keyword
                        print(f"  Line {line_num + 1}: Replaced 'And' with '{last_keyword}'")
                        fixes_made += 1
                        fixed_lines.append(fixed_line)
                        continue
            
            fixed_lines.append(line)
        
        # Write the fixed content
        output_path.parent.mkdir(parents=True, exist_ok=True)
        with open(output_path, 'w', encoding='utf-8') as outfile:
            outfile.writelines(fixed_lines)
        
        return fixes_made


def copy_and_fix_features():
    """Main function to copy and fix all feature files"""
    # Determine source and destination directories
    script_dir = Path(__file__).parent
    
    # Try to find the source features directory
    possible_sources = [
        script_dir.parent / "ZeroBuffer.Harmony.Tests" / "Features",
        script_dir.parent / "csharp" / "ZeroBuffer.Tests" / "Features",
    ]
    
    source_dir = None
    for path in possible_sources:
        if path.exists():
            source_dir = path
            break
    
    if not source_dir:
        print(f"Error: Could not find feature files directory")
        print(f"Looked in: {possible_sources}")
        return False
    
    # Destination directory in Python project
    dest_dir = script_dir / "features"
    
    print(f"Source directory: {source_dir}")
    print(f"Destination directory: {dest_dir}")
    print()
    
    # Create destination directory if it doesn't exist
    dest_dir.mkdir(exist_ok=True)
    
    # Create .gitignore to exclude copied features from git
    gitignore_path = dest_dir / ".gitignore"
    if not gitignore_path.exists():
        with open(gitignore_path, 'w') as f:
            f.write("# Auto-generated feature files - do not edit directly\n")
            f.write("*.feature\n")
    
    # Copy and fix each feature file
    fixer = FeatureFileFixer()
    total_fixes = 0
    file_count = 0
    
    for feature_file in sorted(source_dir.glob("*.feature")):
        file_count += 1
        dest_file = dest_dir / feature_file.name
        
        print(f"Processing {feature_file.name}...")
        
        # Copy and fix the file
        fixes = fixer.fix_file(feature_file, dest_file)
        total_fixes += fixes
        
        if fixes > 0:
            print(f"  ✓ Fixed {fixes} issue(s)")
        else:
            print(f"  ✓ No issues found")
    
    print()
    print(f"Summary:")
    print(f"  - Copied {file_count} feature files")
    print(f"  - Fixed {total_fixes} syntax issue(s)")
    print(f"  - Features saved to: {dest_dir}")
    
    return True


if __name__ == "__main__":
    import sys
    success = copy_and_fix_features()
    sys.exit(0 if success else 1)