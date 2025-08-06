#!/usr/bin/env python3
"""
Comprehensive test file that loads ALL feature files from ZeroBuffer.Harmony.Tests

This ensures all 75+ test scenarios are discoverable and executable.
"""

import sys
import os
from pathlib import Path

# Add parent directory to path
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

# Import pytest-bdd
from pytest_bdd import scenarios

# Find the feature directory
FEATURE_DIR = Path(__file__).parent.parent.parent / "ZeroBuffer.Harmony.Tests" / "Features"
if not FEATURE_DIR.exists():
    FEATURE_DIR = Path(__file__).parent.parent.parent / "csharp" / "ZeroBuffer.Tests" / "Features"

if not FEATURE_DIR.exists():
    raise RuntimeError(f"Feature directory not found. Looked in: {FEATURE_DIR}")

# Load each feature file individually
# This ensures all scenarios are discovered
for feature_file in sorted(FEATURE_DIR.glob("*.feature")):
    scenarios(str(feature_file))