#!/usr/bin/env python3
"""
Allow running cross-platform modules directly.
"""

import sys
import os

# Get the module name from the parent directory
module_name = os.path.basename(os.path.dirname(__file__))

if len(sys.argv) < 2:
    print(f"Usage: python -m zerobuffer.cross_platform.<command> [args]")
    print("Commands: writer, reader, relay")
    sys.exit(1)

command = sys.argv[1]
sys.argv = sys.argv[1:]  # Remove the command from argv

if command == "writer":
    from .writer import main
    sys.exit(main())
elif command == "reader":
    from .reader import main
    sys.exit(main())
elif command == "relay":
    from .relay import main
    sys.exit(main())
else:
    print(f"Unknown command: {command}")
    print("Valid commands: writer, reader, relay")
    sys.exit(1)