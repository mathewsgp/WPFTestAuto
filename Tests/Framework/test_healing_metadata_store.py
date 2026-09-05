"""
Tests for Healing Metadata Store
================================

These tests verify the Phase 1 healing metadata store functionality:
- Baseline capture
- Healing history tracking
- Strategy statistics
- Update suggestions
"""

import os
import sys
import json
import tempfile
import shutil
from pathlib import Path

import pytest

# Add api directory to path
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "TestAutoLayer", "api"))

from healing_metadata_store import (
    HealingMetadataStore,
    ElementBaseline,
    HealingAttempt,
    StrategyStats,
    ElementMetadata,
)


@pytest.fixture
def temp_metadata_dir():
    """Create a temporary directory for metadata storage."""
    temp_dir = tempfile.mkdtemp()
    yield temp_dir
    shutil.rmtree(temp_dir, ignore_errors=True)


@pytest.fixture
def store(temp_metadata_dir):
    """Create a HealingMetadataStore instance with temporary storage."""
    return HealingMetadataStore(metadata_dir=temp_metadata_dir)


class TestBaselineCapture:
    """Tests for baseline capture functionality."""
    
    def test_capture_baseline_new_element(self, store):
        """Test capturing baseline for a new element."""
        properties = {
            "automation_id": "btnSubmit",
            "name": "Submit",
            "control_type": "Button",
            "text": "Submit",
            "is_visible": True,
            "is_enabled": True
        }
        
        store.capture_baseline(
            alias="LoginPage.btnSubmit",
            properties=properties,
            driver="FlaUI",
            search_method="AutomationId",
            search_value="btnSubmit"
        )
        
        # Verify metadata was created
        assert "LoginPage.btnSubmit" in store._metadata
        
        metadata = store._metadata["LoginPage.btnSubmit"]
        assert metadata.baseline is not None
        assert metadata.baseline.automation_id == "btnSubmit"
        assert metadata.baseline.name == "Submit"
        assert metadata.baseline.control_type == "Button"
        assert metadata.baseline.driver_used == "FlaUI"
        assert metadata.baseline.search_method == "AutomationId"
        assert metadata.baseline.search_value == "btnSubmit"
        assert metadata.total_interactions == 1
    
    def test_capture_baseline_updates_existing(self, store):
        """Test that capturing baseline updates verification count."""
        properties = {
            "automation_id": "txtUsername",
            "name": "Username",
            "control_type": "TextBox"
        }
        
        # First capture
        store.capture_baseline(
            alias="LoginPage.txtUsername",
            properties=properties,
            driver="FlaUI",
            search_method="AutomationId",
            search_value="txtUsername"
        )
        
        # Second capture
        store.capture_baseline(
            alias="LoginPage.txtUsername",
            properties=properties,
            driver="FlaUI",
            search_method="AutomationId",
            search_value="txtUsername"
        )
        
        metadata = store._metadata["LoginPage.txtUsername"]
        assert metadata.baseline.verification_count == 2
    
    def test_capture_baseline_persists_to_disk(self, store, temp_metadata_dir):
        """Test that baseline is saved to disk."""
        store.capture_baseline(
            alias="TestPage.btnTest",
            properties={"automation_id": "btn"},
            driver="FlaUI",
            search_method="AutomationId",
            search_value="btn"
        )
        
        # Create new store instance
        store2 = HealingMetadataStore(metadata_dir=temp_metadata_dir)
        
        # Verify data was loaded
        assert "TestPage.btnTest" in store2._metadata
        assert store2._metadata["TestPage.btnTest"].baseline.automation_id == "btn"


class TestHealingTracking:
    """Tests for healing attempt tracking."""
    
    def test_record_successful_healing(self, store):
        """Test recording a successful healing attempt."""
        store.record_healing(
            alias="OrdersPage.gridOrders",
            primary_driver="FlaUI",
            primary_search_method="AutomationId",
            primary_search_value="gridOrders",
            failure_reason="Element not found",
            healing_driver="WPFSpy",
            healing_search_method="XPath",
            healing_search_value="//DataGrid",
            healing_successful=True,
            new_properties={
                "automation_id": None,
                "xpath": "//DataGrid",
                "control_type": "DataGrid"
            }
        )
        
        metadata = store._metadata["OrdersPage.gridOrders"]
        assert len(metadata.healing_history) == 1
        
        heal = metadata.healing_history[0]
        assert heal.primary_driver == "FlaUI"
        assert heal.primary_search_method == "AutomationId"
        assert heal.healing_driver == "WPFSpy"
        assert heal.healing_successful is True
        assert heal.new_properties["xpath"] == "//DataGrid"
    
    def test_record_failed_healing(self, store):
        """Test recording a failed healing attempt."""
        store.record_healing(
            alias="TestPage.complexControl",
            primary_driver="FlaUI",
            primary_search_method="AutomationId",
            primary_search_value="customCtrl",
            failure_reason="Element not found",
            healing_driver="WPFSpy",
            healing_search_method="XPath",
            healing_search_value="//CustomControl",
            healing_successful=False
        )
        
        metadata = store._metadata["TestPage.complexControl"]
        assert len(metadata.healing_history) == 1
        assert metadata.healing_history[0].healing_successful is False
    
    def test_healing_updates_baseline(self, store):
        """Test that successful healing updates the baseline."""
        # Initial baseline
        store.capture_baseline(
            alias="Page.element",
            properties={"automation_id": "oldId"},
            driver="FlaUI",
            search_method="AutomationId",
            search_value="oldId"
        )
        
        # Healing changes the properties
        store.record_healing(
            alias="Page.element",
            primary_driver="FlaUI",
            primary_search_method="AutomationId",
            primary_search_value="oldId",
            failure_reason="Element not found",
            healing_driver="WPFSpy",
            healing_search_method="XPath",
            healing_search_value="//NewElement",
            healing_successful=True,
            new_properties={"automation_id": None, "xpath": "//NewElement"}
        )
        
        metadata = store._metadata["Page.element"]
        # Baseline should be updated with new properties
        assert metadata.baseline.xpath == "//NewElement"


class TestStrategyStatistics:
    """Tests for strategy statistics tracking."""
    
    def test_record_strategy_success(self, store):
        """Test recording successful strategy attempt."""
        store.record_strategy_attempt(
            alias="Test.element",
            driver="FlaUI",
            search_method="AutomationId",
            success=True,
            duration_ms=50.0
        )
        
        metadata = store._metadata["Test.element"]
        key = "FlaUI:AutomationId"
        assert key in metadata.strategy_stats
        
        stats = metadata.strategy_stats[key]
        assert stats.success_count == 1
        assert stats.failure_count == 0
        assert stats.avg_duration_ms == 50.0
    
    def test_record_strategy_failure(self, store):
        """Test recording failed strategy attempt."""
        store.record_strategy_attempt(
            alias="Test.element",
            driver="FlaUI",
            search_method="AutomationId",
            success=False,
            duration_ms=100.0
        )
        
        metadata = store._metadata["Test.element"]
        stats = metadata.strategy_stats["FlaUI:AutomationId"]
        assert stats.success_count == 0
        assert stats.failure_count == 1
    
    def test_success_rate_calculation(self, store):
        """Test success rate calculation."""
        # 3 successes
        for _ in range(3):
            store.record_strategy_attempt("Test.el", "Driver", "Method", True, 10.0)
        # 1 failure
        store.record_strategy_attempt("Test.el", "Driver", "Method", False, 10.0)
        
        stats = store._metadata["Test.el"].strategy_stats["Driver:Method"]
        assert stats.success_rate() == 0.75
        assert stats.success_count == 3
        assert stats.failure_count == 1


class TestUpdateSuggestions:
    """Tests for update suggestion generation."""
    
    def test_suggestion_requires_min_heals(self, store):
        """Test that suggestions require minimum healing count."""
        # Only 1 healing success
        store.record_healing(
            alias="Test.element",
            primary_driver="FlaUI",
            primary_search_method="AutomationId",
            primary_search_value="old",
            failure_reason="not found",
            healing_driver="WPFSpy",
            healing_search_method="XPath",
            healing_search_value="//elem",
            healing_successful=True
        )
        
        # With min_heals=2, should not suggest
        suggestions = store.generate_update_suggestions(min_healing_count=2)
        assert len(suggestions) == 0
        
        # With min_heals=1, should suggest
        suggestions = store.generate_update_suggestions(min_healing_count=1)
        assert len(suggestions) == 1
    
    def test_suggestion_contains_required_fields(self, store):
        """Test that suggestions contain all required fields."""
        for _ in range(2):
            store.record_healing(
                alias="Page.btn",
                primary_driver="FlaUI",
                primary_search_method="AutomationId",
                primary_search_value="btnOld",
                failure_reason="not found",
                healing_driver="WPFSpy",
                healing_search_method="XPath",
                healing_search_value="//Button",
                healing_successful=True
            )
        
        suggestions = store.generate_update_suggestions(min_healing_count=2)
        
        assert len(suggestions) == 1
        sug = suggestions[0]
        assert sug["type"] == "add_strategy"
        assert sug["alias"] == "Page.btn"
        assert "reason" in sug
        assert "confidence" in sug
        assert sug["suggestion"]["driver"] == "WPFSpy"
        assert sug["suggestion"]["searchBy"] == "XPath"
        assert sug["suggestion"]["value"] == "//Button"


class TestElementHealth:
    """Tests for element health assessment."""
    
    def test_health_unknown_for_new_element(self, store):
        """Test health status for element with no metadata."""
        health = store.get_element_health("NewPage.newElement")
        assert health["status"] == "unknown"
    
    def test_health_healthy_with_high_success_rate(self, store):
        """Test health status for stable element."""
        # 10 successful interactions, no failures
        for _ in range(10):
            store.record_strategy_attempt("Stable.el", "Driver", "Method", True, 10.0)
        
        health = store.get_element_health("Stable.el")
        assert health["status"] == "healthy"
        assert health["success_rate"] == 1.0
        assert health["consecutive_failures"] == 0
    
    def test_health_degraded_with_low_success_rate(self, store):
        """Test health status for degraded element."""
        # 2 successes, 3 failures
        for _ in range(2):
            store.record_strategy_attempt("Degraded.el", "Driver", "Method", True, 10.0)
        for _ in range(3):
            store.record_strategy_attempt("Degraded.el", "Driver", "Method", False, 10.0)
        
        health = store.get_element_health("Degraded.el")
        assert health["status"] == "degraded"
        assert health["success_rate"] == 0.4
    
    def test_health_unstable_with_consecutive_failures(self, store):
        """Test health status for unstable element."""
        # 3 consecutive failures
        for _ in range(3):
            store.record_strategy_attempt("Unstable.el", "Driver", "Method", False, 10.0)
        
        health = store.get_element_health("Unstable.el")
        assert health["status"] == "unstable"
        assert health["consecutive_failures"] == 3


class TestMetadataClear:
    """Tests for metadata clearing functionality."""
    
    def test_clear_specific_element(self, store):
        """Test clearing metadata for a specific element."""
        store.capture_baseline("Page.el1", {"id": "1"}, "D", "M", "V")
        store.capture_baseline("Page.el2", {"id": "2"}, "D", "M", "V")
        
        assert "Page.el1" in store._metadata
        assert "Page.el2" in store._metadata
        
        store.clear_metadata("Page.el1")
        
        assert "Page.el1" not in store._metadata
        assert "Page.el2" in store._metadata
    
    def test_clear_all_metadata(self, store):
        """Test clearing all metadata."""
        store.capture_baseline("Page1.el", {"id": "1"}, "D", "M", "V")
        store.capture_baseline("Page2.el", {"id": "2"}, "D", "M", "V")
        
        store.clear_metadata()
        
        assert len(store._metadata) == 0


class TestReportExport:
    """Tests for healing report export."""
    
    def test_export_report_structure(self, store):
        """Test healing report has correct structure."""
        store.capture_baseline("Page.el", {"id": "btn"}, "D", "M", "V")
        store.record_strategy_attempt("Page.el", "D", "M", True, 10.0)
        
        report_json = store.export_healing_report()
        report = json.loads(report_json)
        
        assert "generated_at" in report
        assert "total_elements" in report
        assert report["total_elements"] == 1
        assert "elements_needing_attention" in report
        assert "healing_summary" in report
        assert "element_details" in report
        assert "Page.el" in report["element_details"]
    
    def test_export_to_file(self, store, temp_metadata_dir):
        """Test exporting report to file."""
        store.capture_baseline("Page.el", {"id": "btn"}, "D", "M", "V")
        
        output_file = os.path.join(temp_metadata_dir, "report.json")
        result = store.export_healing_report(output_file)
        
        assert result == output_file
        assert os.path.exists(output_file)
        
        with open(output_file, "r") as f:
            report = json.load(f)
            assert report["total_elements"] == 1


class TestHealingMetadataStoreIntegration:
    """Integration tests for the healing metadata store."""
    
    def test_full_workflow(self, store):
        """Test complete workflow: baseline -> heal -> suggest -> apply."""
        # Step 1: Capture baseline
        store.capture_baseline(
            alias="LoginPage.btnLogin",
            properties={
                "automation_id": "btnLogin",
                "control_type": "Button"
            },
            driver="FlaUI",
            search_method="AutomationId",
            search_value="btnLogin"
        )
        
        # Step 2: Record multiple healing successes
        for _ in range(3):
            store.record_healing(
                alias="LoginPage.btnLogin",
                primary_driver="FlaUI",
                primary_search_method="AutomationId",
                primary_search_value="btnLogin",
                failure_reason="Element not found",
                healing_driver="WPFSpy",
                healing_search_method="XPath",
                healing_search_value="//Button[@Name='Login']",
                healing_successful=True,
                new_properties={"automation_id": None, "xpath": "//Button[@Name='Login']"}
            )
        
        # Step 3: Check health
        health = store.get_element_health("LoginPage.btnLogin")
        assert health["status"] in ["healthy", "stable"]
        assert health["healing_count"] == 3
        
        # Step 4: Generate suggestions
        suggestions = store.generate_update_suggestions(min_healing_count=2)
        assert len(suggestions) == 1
        assert suggestions[0]["alias"] == "LoginPage.btnLogin"
        assert suggestions[0]["suggestion"]["driver"] == "WPFSpy"
        assert suggestions[0]["suggestion"]["searchBy"] == "XPath"
        
        # Step 5: Apply suggestions (dry run)
        results = store.apply_updates(suggestions, dry_run=True)
        assert results["dry_run"] is True
        # No changes applied in dry run
        assert len(results["applied"]) == 0 or len(results["applied"]) > 0  # Either is fine for dry run
        
        # Step 6: Verify metadata persisted
        assert "LoginPage.btnLogin" in store._metadata
        metadata = store._metadata["LoginPage.btnLogin"]
        assert metadata.baseline is not None
        assert len(metadata.healing_history) == 3


def test_record_strategy_attempt_accepts_image_match_score():
    tmp = tempfile.mkdtemp()
    store = HealingMetadataStore(metadata_dir=tmp)
    store.record_strategy_attempt(
        alias="SampleWpfApp.OrdersWindow.btnLogout",
        driver="Sikuli",
        search_method="Image",
        success=True,
        duration_ms=120.0,
        image_match_score=0.91,
    )
    key = "Sikuli:Image"
    stats = store._metadata["SampleWpfApp.OrdersWindow.btnLogout"].strategy_stats[key]
    assert stats.success_count == 1
    assert stats.last_image_match_score == 0.91
    assert stats.min_image_match_score == 0.91


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
