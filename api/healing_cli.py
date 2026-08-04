#!/usr/bin/env python3
"""
Healing Metadata CLI Tool
==========================
Command-line tool for managing the healing metadata store and applying
post-run repository updates.

Usage:
    python healing_cli.py --status                    # Show overall health status
    python healing_cli.py --suggestions               # Generate update suggestions
    python healing_cli.py --apply                     # Apply suggestions to repository
    python healing_cli.py --report [output.json]     # Export healing report
    python healing_cli.py --element <alias>          # Show element health
    python healing_cli.py --clear <alias>            # Clear element metadata
    python healing_cli.py --clear-all                # Clear all metadata
    python healing_cli.py --baseline <alias>        # Capture baseline for element
"""

import argparse
import json
import os
import sys
from datetime import datetime

# Add api directory to path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from healing_metadata_store import HealingMetadataStore, get_healing_store


def print_header(title: str):
    """Print a formatted header."""
    print(f"\n{'=' * 60}")
    print(f" {title}")
    print(f"{'=' * 60}\n")


def cmd_status(store: HealingMetadataStore):
    """Show overall health status of all tracked elements."""
    print_header("Healing Metadata Status")
    
    if not store._metadata:
        print("No metadata found. Elements will be tracked on next test run.")
        return
    
    # Aggregate stats
    total_elements = len(store._metadata)
    healthy = 0
    stable = 0
    degraded = 0
    unstable = 0
    
    element_health = []
    
    for alias, metadata in store._metadata.items():
        health = store.get_element_health(alias)
        status = health["status"]
        
        if status == "healthy":
            healthy += 1
        elif status == "stable":
            stable += 1
        elif status == "degraded":
            degraded += 1
        elif status == "unstable":
            unstable += 1
        
        element_health.append((alias, health))
    
    # Print summary
    print(f"Total Elements Tracked: {total_elements}")
    print(f"\nHealth Distribution:")
    print(f"  ✅ Healthy:  {healthy}")
    print(f"  👍 Stable:   {stable}")
    print(f"  ⚠️  Degraded: {degraded}")
    print(f"  ❌ Unstable: {unstable}")
    
    # Show elements needing attention
    if element_health:
        needs_attention = [(a, h) for a, h in element_health if h["status"] in ("degraded", "unstable")]
        if needs_attention:
            print(f"\n{'=' * 40}")
            print("Elements Needing Attention:")
            print(f"{'=' * 40}")
            for alias, health in sorted(needs_attention, key=lambda x: x[1]["consecutive_failures"], reverse=True):
                print(f"\n  {alias}")
                print(f"    Status: {health['status'].upper()} - {health['reason']}")
                print(f"    Success Rate: {health['success_rate']*100:.1f}%")
                print(f"    Consecutive Failures: {health['consecutive_failures']}")
                print(f"    Last Interaction: {health['last_interaction'][:19]}")
                if health['healing_count'] > 0:
                    print(f"    Healing Count: {health['healing_count']}")
    
    # Show healing summary
    total_heals = sum(len(m.healing_history) for m in store._metadata.values())
    successful_heals = sum(
        sum(1 for h in m.healing_history if h.healing_successful)
        for m in store._metadata.values()
    )
    
    print(f"\n{'=' * 40}")
    print("Healing Summary:")
    print(f"{'=' * 40}")
    print(f"  Total Healing Attempts: {total_heals}")
    print(f"  Successful Heals: {successful_heals}")
    print(f"  Healing Success Rate: {successful_heals/total_heals*100:.1f}%" if total_heals > 0 else "  Healing Success Rate: N/A")


def cmd_suggestions(store: HealingMetadataStore, min_heals: int = 2):
    """Generate repository update suggestions."""
    print_header("Repository Update Suggestions")
    
    suggestions = store.generate_update_suggestions(min_healing_count=min_heals)
    
    if not suggestions:
        print("No update suggestions at this time.")
        print("\nSuggestions are generated when elements have healed successfully")
        print("multiple times using a different strategy than the primary.")
        return
    
    print(f"Found {len(suggestions)} suggestion(s):\n")
    
    # Group by type
    by_type = {}
    for sug in suggestions:
        t = sug["type"]
        if t not in by_type:
            by_type[t] = []
        by_type[t].append(sug)
    
    for sug_type, sugs in by_type.items():
        print(f"\n{'─' * 50}")
        type_label = {
            "add_strategy": "📝 Add Strategy",
            "deprecate_strategy": "⚠️ Deprecate Strategy",
            "update_locator": "🔄 Update Locator"
        }.get(sug_type, sug_type)
        
        print(f"{type_label} ({len(sugs)} suggestion(s))")
        print(f"{'─' * 50}")
        
        for sug in sugs:
            print(f"\n  Element: {sug['alias']}")
            print(f"  Reason: {sug['reason']}")
            print(f"  Confidence: {sug['confidence']*100:.0f}%")
            
            if sug["type"] == "add_strategy":
                s = sug["suggestion"]
                print(f"  Action: Add {s['driver']} strategy")
                print(f"    searchBy: {s['searchBy']}")
                print(f"    value: {s['value']}")
                print(f"    priority: {s['priority']}")
            elif sug["type"] == "deprecate_strategy":
                s = sug["suggestion"]
                print(f"  Action: Lower priority of {s['driver']}:{s['searchBy']}")
                print(f"  Current success rate: {sug.get('success_rate', 0)*100:.0f}%")
    
    print(f"\n{'=' * 60}")
    print(f"Run with --apply to apply these suggestions to the repository")
    print(f"{'=' * 60}")


def cmd_apply(store: HealingMetadataStore, dry_run: bool = True, min_heals: int = 2):
    """Apply suggestions to the repository."""
    if dry_run:
        print_header("DRY RUN: Repository Update Preview")
        print("No files will be modified. Use --no-dry-run to apply changes.\n")
    else:
        print_header("Applying Repository Updates")
    
    suggestions = store.generate_update_suggestions(min_healing_count=min_heals)
    
    if not suggestions:
        print("No suggestions to apply.")
        return
    
    results = store.apply_updates(suggestions, dry_run=dry_run)
    
    print(f"\nChanges {'would be' if dry_run else 'were'} applied:")
    print(f"  Applied: {len(results['applied'])}")
    print(f"  Errors: {len(results['errors'])}")
    
    if results['backups']:
        print(f"  Backups created: {len(results['backups'])}")
        for backup in results['backups']:
            print(f"    - {backup}")
    
    if results['applied']:
        print("\nApplied changes:")
        for change in results['applied']:
            print(f"  - {change}")
    
    if results['errors']:
        print("\nErrors encountered:")
        for error in results['errors']:
            print(f"  - {error['file']}: {error['error']}")


def cmd_report(store: HealingMetadataStore, output_file: str = None):
    """Export healing report."""
    print_header("Generating Healing Report")
    
    if output_file:
        result = store.export_healing_report(output_file)
        print(f"Report saved to: {result}")
    else:
        report = store.export_healing_report()
        print(report)


def cmd_element(store: HealingMetadataStore, alias: str):
    """Show detailed health for a specific element."""
    print_header(f"Element Health: {alias}")
    
    health = store.get_element_health(alias)
    
    print(f"Status: {health['status'].upper()}")
    print(f"Reason: {health['reason']}")
    print(f"\nMetrics:")
    print(f"  Success Rate: {health['success_rate']*100:.1f}%")
    print(f"  Total Interactions: {health['total_interactions']}")
    print(f"  Consecutive Failures: {health['consecutive_failures']}")
    print(f"  Consecutive Successes: {health['consecutive_successes']}")
    print(f"  Healing Count: {health['healing_count']}")
    print(f"  Baseline Captured: {'Yes' if health['baseline_captured'] else 'No'}")
    print(f"  First Seen: {health['first_seen'][:19]}")
    print(f"  Last Interaction: {health['last_interaction'][:19]}")
    
    # Show detailed metadata
    if alias in store._metadata:
        metadata = store._metadata[alias]
        
        # Strategy stats
        if metadata.strategy_stats:
            print(f"\n{'─' * 40}")
            print("Strategy Statistics:")
            print(f"{'─' * 40}")
            for strategy, stats in sorted(metadata.strategy_stats.items()):
                rate = stats.success_rate() * 100
                status_icon = "✅" if rate >= 80 else "⚠️" if rate >= 50 else "❌"
                print(f"\n  {status_icon} {strategy}")
                print(f"      Successes: {stats.success_count}")
                print(f"      Failures: {stats.failure_count}")
                print(f"      Success Rate: {rate:.1f}%")
                print(f"      Avg Duration: {stats.avg_duration_ms:.1f}ms")
        
        # Recent healing history
        if metadata.healing_history:
            print(f"\n{'─' * 40}")
            print("Recent Healing History:")
            print(f"{'─' * 40}")
            for heal in metadata.healing_history[-5:]:
                status_icon = "✅" if heal.healing_successful else "❌"
                print(f"\n  {status_icon} {heal['timestamp'][:19]}")
                print(f"      Primary Failed: {heal['primary_driver']}:{heal['primary_search_method']}={heal['primary_search_value']}")
                print(f"      Reason: {heal['failure_reason']}")
                print(f"      Healing Driver: {heal['healing_driver']}:{heal['healing_search_method']}")
                if heal['healing_successful']:
                    print(f"      Healing Value: {heal['healing_search_value']}")
        
        # Baseline
        if metadata.baseline:
            print(f"\n{'─' * 40}")
            print("Captured Baseline:")
            print(f"{'─' * 40}")
            b = metadata.baseline
            if b.automation_id:
                print(f"  AutomationId: {b.automation_id}")
            if b.name:
                print(f"  Name: {b.name}")
            if b.control_type:
                print(f"  ControlType: {b.control_type}")
            if b.xpath:
                print(f"  XPath: {b.xpath}")
            if b.text:
                print(f"  Text: {b.text[:50]}..." if len(b.text) > 50 else f"  Text: {b.text}")
            print(f"  Driver Used: {b.driver_used}")
            print(f"  Search Method: {b.search_method}")
            print(f"  Captured At: {b.captured_at[:19]}")


def cmd_capture_baseline(store: HealingMetadataStore, alias: str):
    """Capture baseline for an element (used during test execution)."""
    print_header(f"Capturing Baseline: {alias}")
    
    # In a real implementation, this would query the running app
    # For now, we'll create an empty baseline that gets populated
    # during the next successful interaction
    
    store.capture_baseline(
        alias=alias,
        properties={},
        driver="Pending",
        search_method="Pending",
        search_value="Pending"
    )
    
    print(f"Baseline capture initiated for {alias}")
    print("The baseline will be populated on the next successful interaction.")


def cmd_clear(store: HealingMetadataStore, alias: str = None):
    """Clear metadata for elements."""
    if alias:
        print(f"Clearing metadata for: {alias}")
        store.clear_metadata(alias)
        print("Done.")
    else:
        print("Use --element to specify an element, or --clear-all to clear all.")


def cmd_clear_all(store: HealingMetadataStore):
    """Clear all metadata."""
    print("Clearing all metadata...")
    store.clear_metadata()
    print("All metadata cleared.")


def main():
    parser = argparse.ArgumentParser(
        description="Healing Metadata Store CLI Tool",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Examples:
  %(prog)s --status                    # Show health status
  %(prog)s --suggestions                # Generate update suggestions
  %(prog)s --apply                     # Preview and apply updates
  %(prog)s --apply --no-dry-run        # Actually apply updates
  %(prog)s --report healing.json       # Export report
  %(prog)s --element LoginPage.btnSubmit # Show element health
  %(prog)s --clear LoginPage.btnSubmit  # Clear element metadata
  %(prog)s --clear-all                  # Clear all metadata
        """
    )
    
    parser.add_argument(
        "--status", "-s",
        action="store_true",
        help="Show overall health status of all tracked elements"
    )
    
    parser.add_argument(
        "--suggestions", "-g",
        action="store_true",
        help="Generate repository update suggestions"
    )
    
    parser.add_argument(
        "--apply", "-a",
        action="store_true",
        help="Apply suggestions to the repository"
    )
    
    parser.add_argument(
        "--no-dry-run",
        action="store_true",
        help="Actually apply changes (without this, runs in dry-run mode)"
    )
    
    parser.add_argument(
        "--min-heals",
        type=int,
        default=2,
        help="Minimum healing count for suggestions (default: 2)"
    )
    
    parser.add_argument(
        "--report", "-r",
        nargs="?",
        const="healing_report.json",
        metavar="OUTPUT_FILE",
        help="Export healing report to JSON"
    )
    
    parser.add_argument(
        "--element", "-e",
        metavar="ALIAS",
        help="Show detailed health for a specific element"
    )
    
    parser.add_argument(
        "--capture-baseline", "-c",
        metavar="ALIAS",
        help="Initiate baseline capture for an element"
    )
    
    parser.add_argument(
        "--clear",
        metavar="ALIAS",
        help="Clear metadata for a specific element"
    )
    
    parser.add_argument(
        "--clear-all",
        action="store_true",
        help="Clear all metadata"
    )
    
    parser.add_argument(
        "--metadata-dir",
        help="Custom metadata directory path"
    )
    
    args = parser.parse_args()
    
    # Initialize store
    store = HealingMetadataStore(metadata_dir=args.metadata_dir)
    
    # Execute commands
    if args.status:
        cmd_status(store)
    elif args.suggestions:
        cmd_suggestions(store, args.min_heals)
    elif args.apply:
        cmd_apply(store, dry_run=not args.no_dry_run, min_heals=args.min_heals)
    elif args.report is not None:
        cmd_report(store, args.report)
    elif args.element:
        cmd_element(store, args.element)
    elif args.capture_baseline:
        cmd_capture_baseline(store, args.capture_baseline)
    elif args.clear:
        cmd_clear(store, args.clear)
    elif args.clear_all:
        cmd_clear_all(store)
    else:
        parser.print_help()


if __name__ == "__main__":
    main()
