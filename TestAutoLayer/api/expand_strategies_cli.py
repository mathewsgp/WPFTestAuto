#!/usr/bin/env python3
"""
Expand Strategies CLI Tool
=========================
Command-line tool for expanding element strategies to include more locator methods.

Usage:
    python expand_strategies_cli.py --suggest        # Show suggested strategies
    python expand_strategies_cli.py --expand        # Expand all elements
    python expand_strategies_cli.py --element <alias> # Show suggestions for element
    python expand_strategies_cli.py --add <alias> --method Name # Add strategy to element
    python expand_strategies_cli.py --export           # Export repository with expanded strategies
"""

import argparse
import json
import os
import sys
import glob

# Add api directory to path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from repository_access import (
    load_elements, 
    get_strategies, 
    get_all_driver_strategies_sorted,
    expand_strategies,
    suggest_additional_strategies,
    SUPPORTED_SEARCH_METHODS
)


def print_header(title: str):
    """Print a formatted header."""
    print(f"\n{'=' * 60}")
    print(f" {title}")
    print(f"{'=' * 60}\n")


def cmd_suggest():
    """Show suggested strategies for all elements."""
    print_header("Strategy Suggestions")
    
    elements = load_elements()
    total_suggestions = 0
    
    for alias in sorted(elements.keys()):
        suggestions = suggest_additional_strategies(alias)
        if suggestions:
            total_suggestions += sum(len(s) for s in suggestions.values())
            print(f"\n{alias}")
            for driver, driver_suggestions in suggestions.items():
                print(f"  {driver}:")
                for sug in driver_suggestions:
                    print(f"    + {sug['searchBy']}='{sug['value']}' (priority {sug['priority']})")
                    print(f"      {sug['reason']}")
    
    if total_suggestions == 0:
        print("No additional strategies to suggest.")
    else:
        print(f"\n{'=' * 40}")
        print(f"Total suggestions: {total_suggestions}")
        print("Run with --expand to apply all suggestions")


def cmd_expand(dry_run: bool = True):
    """Expand all elements with additional strategies."""
    if dry_run:
        print_header("DRY RUN: Strategy Expansion Preview")
        print("No files will be modified. Use --no-dry-run to apply changes.\n")
    else:
        print_header("Expanding Strategies")
    
    elements = load_elements()
    expanded_count = 0
    modified_files = {}
    
    for alias in sorted(elements.keys()):
        expanded = expand_strategies(alias)
        current = get_all_driver_strategies_sorted(alias)
        
        # Check if expansion changes anything
        changed = False
        for driver in expanded:
            current_driver = current.get(driver, [])
            expanded_driver = expanded[driver]
            
            if len(expanded_driver) > len(current_driver):
                changed = True
                break
        
        if changed:
            expanded_count += 1
            
            # Find which file contains this element
            repo_root = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "repository", "elements")
            for yaml_file in glob.glob(os.path.join(repo_root, "*.yaml")):
                with open(yaml_file, "r") as f:
                    content = f.read()
                    if alias in content:
                        if yaml_file not in modified_files:
                            modified_files[yaml_file] = []
                        modified_files[yaml_file].append(alias)
                        break
            
            if dry_run:
                print(f"\n{alias}")
                for driver in expanded:
                    current_driver = current.get(driver, [])
                    expanded_driver = expanded[driver]
                    
                    if len(expanded_driver) > len(current_driver):
                        print(f"  {driver}:")
                        for i, strategy in enumerate(expanded_driver):
                            marker = " [NEW]" if i >= len(current_driver) else ""
                            print(f"    {i+1}. {strategy['searchBy']}='{strategy['value']}' (priority {strategy.get('priority', 99)}){marker}")
    
    print(f"\n{'=' * 40}")
    print(f"Elements with expansion: {expanded_count}")
    print(f"Files to modify: {len(modified_files)}")
    
    if dry_run and expanded_count > 0:
        print(f"\n{'=' * 40}")
        print(f"Run with --no-dry-run to apply changes")


def cmd_element(alias: str):
    """Show suggestions for a specific element."""
    print_header(f"Element: {alias}")
    
    try:
        elements = load_elements()
        if alias not in elements:
            print(f"Element '{alias}' not found in repository")
            return
        
        element = elements[alias]
        print(f"\nProperties:")
        print(f"  automationId: {element.get('automationId') or element.get('AutomationId')}")
        print(f"  name: {element.get('name') or element.get('Name')}")
        print(f"  className: {element.get('className') or element.get('ClassName')}")
        print(f"  controlType: {element.get('controlType') or element.get('ControlType')}")
        print(f"  displayName: {element.get('displayName')}")
        
        print(f"\nCurrent Strategies:")
        strategies = get_all_driver_strategies_sorted(alias)
        for driver, driver_strategies in sorted(strategies.items()):
            print(f"  {driver}:")
            for strategy in driver_strategies:
                print(f"    - {strategy['searchBy']}='{strategy['value']}' (priority {strategy.get('priority', 99)})")
        
        print(f"\nSuggested Additional Strategies:")
        suggestions = suggest_additional_strategies(alias)
        if suggestions:
            for driver, driver_suggestions in sorted(suggestions.items()):
                print(f"  {driver}:")
                for sug in driver_suggestions:
                    print(f"    + {sug['searchBy']}='{sug['value']}' (priority {sug['priority']})")
                    print(f"      {sug['reason']}")
        else:
            print("  (No additional strategies suggested)")
        
        print(f"\nExpanded Strategies (with additions):")
        expanded = expand_strategies(alias)
        for driver, driver_strategies in sorted(expanded.items()):
            print(f"  {driver}:")
            for i, strategy in enumerate(driver_strategies):
                marker = " [NEW]" if strategy.get("source") == "expanded" else ""
                print(f"    {i+1}. {strategy['searchBy']}='{strategy['value']}' (priority {strategy.get('priority', 99)}){marker}")
    
    except KeyError as e:
        print(f"Error: {e}")


def cmd_export(output_file: str = None):
    """Export repository with expanded strategies."""
    print_header("Exporting Expanded Repository")
    
    elements = load_elements()
    repository = {"elements": {}}
    
    for alias in sorted(elements.keys()):
        expanded = expand_strategies(alias)
        element = elements[alias].copy()
        
        # Update strategies
        element["strategies"] = {}
        for driver, strategies in expanded.items():
            element["strategies"][driver] = [
                {"searchBy": s["searchBy"], "value": s["value"], "priority": s.get("priority", 99)}
                for s in strategies
            ]
        
        repository["elements"][alias] = element
    
    import yaml
    output = yaml.dump(repository, default_flow_style=False, sort_keys=False)
    
    if output_file:
        with open(output_file, "w") as f:
            f.write(output)
        print(f"Exported to: {output_file}")
    else:
        print(output)


def cmd_add_strategy(alias: str, driver: str, method: str, value: str, priority: int = None):
    """Add a specific strategy to an element."""
    print_header(f"Adding Strategy to {alias}")
    
    # Find the file containing this element
    repo_root = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "repository", "elements")
    yaml_file = None
    
    for f in glob.glob(os.path.join(repo_root, "*.yaml")):
        with open(f, "r") as file:
            content = file.read()
            if alias in content:
                yaml_file = f
                break
    
    if not yaml_file:
        print(f"Element '{alias}' not found in repository")
        return
    
    import yaml
    with open(yaml_file, "r") as f:
        data = yaml.safe_load(f)
    
    if "elements" not in data:
        data = {"elements": {}}
    
    if alias not in data["elements"]:
        print(f"Element '{alias}' not found")
        return
    
    element = data["elements"][alias]
    
    # Ensure strategies structure
    if "strategies" not in element:
        element["strategies"] = {}
    if driver not in element["strategies"]:
        element["strategies"][driver] = []
    
    # Check if strategy already exists
    for strategy in element["strategies"][driver]:
        if strategy.get("searchBy") == method:
            print(f"Strategy {driver}:{method} already exists")
            return
    
    # Add new strategy
    if priority is None:
        priority = len(element["strategies"][driver]) + 1
    
    element["strategies"][driver].append({
        "searchBy": method,
        "value": value,
        "priority": priority
    })
    
    # Sort by priority
    element["strategies"][driver] = sorted(element["strategies"][driver], key=lambda s: s.get("priority", 99))
    
    # Write back
    with open(yaml_file, "w") as f:
        yaml.dump(data, f, default_flow_style=False, sort_keys=False)
    
    print(f"Added {driver}:{method}='{value}' (priority {priority})")
    print(f"Updated file: {yaml_file}")


def main():
    parser = argparse.ArgumentParser(
        description="Expand Strategies CLI Tool",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Examples:
  %(prog)s --suggest                   # Show suggested strategies
  %(prog)s --expand                    # Preview expansion
  %(prog)s --expand --no-dry-run      # Apply expansion
  %(prog)s --element LoginPage.btnSubmit # Show element suggestions
  %(prog)s --export expanded.yaml      # Export to YAML
  %(prog)s --add LoginPage.btnSubmit --driver FlaUI --method Name --value Submit
        """
    )
    
    parser.add_argument(
        "--suggest", "-s",
        action="store_true",
        help="Show suggested strategies for all elements"
    )
    
    parser.add_argument(
        "--expand", "-e",
        action="store_true",
        help="Expand all elements with additional strategies"
    )
    
    parser.add_argument(
        "--no-dry-run",
        action="store_true",
        help="Actually apply changes (without this, runs in dry-run mode)"
    )
    
    parser.add_argument(
        "--element", "-i",
        metavar="ALIAS",
        help="Show suggestions for a specific element"
    )
    
    parser.add_argument(
        "--export", "-o",
        metavar="OUTPUT_FILE",
        help="Export repository with expanded strategies"
    )
    
    parser.add_argument(
        "--add",
        metavar="ALIAS",
        help="Add a strategy to an element"
    )
    
    parser.add_argument(
        "--driver", "-d",
        choices=["FlaUI", "WPFSpy", "Sikuli"],
        help="Driver for the strategy"
    )
    
    parser.add_argument(
        "--method", "-m",
        choices=["AutomationId", "Name", "ClassName", "XPath", "Index", "Text", "ImageTag"],
        help="Search method"
    )
    
    parser.add_argument(
        "--value", "-v",
        help="Search value"
    )
    
    parser.add_argument(
        "--priority", "-p",
        type=int,
        help="Priority (optional)"
    )
    
    parser.add_argument(
        "--methods",
        action="store_true",
        help="List all supported search methods"
    )
    
    args = parser.parse_args()
    
    if args.methods:
        print_header("Supported Search Methods")
        for driver, methods in SUPPORTED_SEARCH_METHODS.items():
            print(f"\n{driver}:")
            for method in methods:
                print(f"  - {method}")
        return
    
    if args.suggest:
        cmd_suggest()
    elif args.expand:
        cmd_expand(dry_run=not args.no_dry_run)
    elif args.element:
        cmd_element(args.element)
    elif args.export:
        cmd_export(args.export)
    elif args.add:
        if not args.driver or not args.method or not args.value:
            print("Error: --add requires --driver, --method, and --value")
            parser.print_help()
            return
        cmd_add_strategy(args.add, args.driver, args.method, args.value, args.priority)
    else:
        parser.print_help()


if __name__ == "__main__":
    main()
