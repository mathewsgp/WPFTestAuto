#!/bin/bash
# Run all Robot Framework tests
# Usage: ./run_tests.sh [test_file]
#   - No args: runs all tests in Tests/ directory
#   - With arg: runs specific test file

cd "$(dirname "$0")"
export PYTHONPATH="TestAutoLayer:$PYTHONPATH"

if [ -z "$1" ]; then
    echo "Running all tests..."
    robot --outputdir output Tests/
else
    echo "Running: $1"
    robot --outputdir output "$1"
fi

echo ""
echo "========================================"
echo "Tests complete!"
