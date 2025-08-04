#!/bin/bash

# Make all test scripts executable

find . -name "*.sh" -type f -exec chmod +x {} \;

echo "Made all .sh files executable"