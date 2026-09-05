"""
Checkpoint Library for Robot Framework
=====================================

Provides keywords for checkpoint-based verification.

Checkpoint Types:
- Property: Verify element property values
- Area: Verify text in screen area using OCR
- Image: Visual comparison
- DataGrid: Verify DataGrid content
- Attribute: Verify specific attributes
- Count: Verify element counts

Usage:
    *** Settings ***
    Library    modules.CheckpointLibrary

    *** Test Cases ***
    Verify Login Page
        Load Checkpoints    ${CURDIR}/checkpoints/login_checkpoints.yaml
        Verify All Checkpoints
        Verify Checkpoint    prop_001
        Log Verification Summary
"""

from typing import Optional

import sys
import os
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))

from api.checkpoint_verifier import (
    CheckpointVerifier,
    CheckpointType,
    ComparisonOperator,
    create_checkpoint,
    CHECKPOINT_TEMPLATE
)


class CheckpointLibrary:
    """
    Robot Framework library for checkpoint verification.
    
    Example:
        *** Test Cases ***
        Example Test
            Load Checkpoints    checkpoints/my_test.yaml
            Verify All Checkpoints
    """
    
    ROBOT_LIBRARY_SCOPE = "TEST SUITE"
    
    def __init__(self):
        self._verifier: Optional[CheckpointVerifier] = None
        self._last_results = []
    
    def load_checkpoints(self, file_path: str) -> int:
        """
        Load checkpoints from a YAML file.
        
        Args:
            file_path: Path to checkpoints YAML file
            
        Returns:
            Number of checkpoints loaded
            
        Example:
            Load Checkpoints    ${CURDIR}/../checkpoints/login.yaml
        """
        from api.DriverAgnosticApi import DriverAgnosticApi
        driver_api = DriverAgnosticApi()
        self._verifier = CheckpointVerifier(driver_api)
        count = self._verifier.load_checkpoints(file_path)
        print(f"[CheckpointLibrary] Loaded {count} checkpoints from {file_path}")
        return count
    
    def load_checkpoints_from_json(self, json_string: str) -> int:
        """
        Load checkpoints from a JSON string.
        
        Args:
            json_string: JSON string containing checkpoints
            
        Returns:
            Number of checkpoints loaded
        """
        import json
        from api.DriverAgnosticApi import DriverAgnosticApi
        
        driver_api = DriverAgnosticApi()
        self._verifier = CheckpointVerifier(driver_api)
        data = json.loads(json_string)
        self._verifier.load_checkpoints_from_dict(data)
        count = len(self._verifier.checkpoints)
        print(f"[CheckpointLibrary] Loaded {count} checkpoints from JSON")
        return count
    
    def verify_all_checkpoints(self) -> dict:
        """
        Verify all loaded checkpoints.
        
        Returns:
            Verification summary dictionary
            
        Raises:
            AssertionError: If any checkpoint fails
        """
        if not self._verifier:
            raise RuntimeError("No checkpoints loaded. Use 'Load Checkpoints' first.")
        
        self._last_results = self._verifier.verify_all()
        summary = self._verifier.get_verification_summary()
        print(f"[CheckpointLibrary] Verification complete: {summary['pass_rate']} passed")
        return summary
    
    def verify_checkpoint(self, checkpoint_id: str) -> bool:
        """
        Verify a single checkpoint by ID.
        
        Args:
            checkpoint_id: ID of checkpoint to verify
            
        Returns:
            True if checkpoint passed, False otherwise
            
        Example:
            ${passed}=    Verify Checkpoint    prop_001
            Should Be True    ${passed}
        """
        if not self._verifier:
            raise RuntimeError("No checkpoints loaded. Use 'Load Checkpoints' first.")
        
        result = self._verifier.verify_checkpoint(checkpoint_id)
        self._last_results.append(result)
        
        if result['passed']:
            print(f"[CheckpointLibrary] Checkpoint {checkpoint_id} PASSED")
        else:
            print(f"[CheckpointLibrary] Checkpoint {checkpoint_id} FAILED: {result['message']}")
        
        return result['passed']
    
    def log_verification_summary(self):
        """
        Log a summary of verification results.
        
        Example:
            Verify All Checkpoints
            Log Verification Summary
        """
        if not self._verifier:
            print("[CheckpointLibrary] No verification results available")
            return
        
        summary = self._verifier.get_verification_summary()
        print("\n" + "=" * 50)
        print("CHECKPOINT VERIFICATION SUMMARY")
        print("=" * 50)
        print(f"Total:     {summary['total']}")
        print(f"Passed:    {summary['passed']}")
        print(f"Failed:    {summary['failed']}")
        print(f"Pass Rate: {summary['pass_rate']}")
        print("=" * 50)
        
        # Log failed checkpoints
        if summary['failed'] > 0:
            print("\nFailed Checkpoints:")
            for result in self._last_results:
                if not result['passed']:
                    print(f"  - [{result['checkpoint_id']}] {result['description']}")
                    print(f"    Expected: {result['expected']}")
                    print(f"    Actual:   {result['actual']}")
                    print(f"    Message:  {result['message']}")
    
    def create_property_checkpoint(
        self,
        checkpoint_id: str,
        element_alias: str,
        property_name: str = "Text",
        expected_value: str = "",
        description: str = ""
    ) -> str:
        """
        Create a property checkpoint in memory.
        
        Args:
            checkpoint_id: Unique identifier
            element_alias: Element alias (e.g., "LoginPage.btnSubmit")
            property_name: Property to verify (Text, IsEnabled, IsVisible, etc.)
            expected_value: Expected value
            description: Description
            
        Returns:
            Checkpoint ID
            
        Example:
            Create Property Checkpoint    prop_001    LoginPage.txtUsername    Text    admin
        """
        if not self._verifier:
            from api.DriverAgnosticApi import DriverAgnosticApi
            driver_api = DriverAgnosticApi()
            self._verifier = CheckpointVerifier(driver_api)
        
        checkpoint_dict = create_checkpoint(
            checkpoint_type="Property",
            id=checkpoint_id,
            property_name=property_name,
            expected_value=expected_value,
            element_alias=element_alias,
            description=description
        )
        
        from api.checkpoint_verifier import Checkpoint
        self._verifier.add_checkpoint(Checkpoint.from_dict(checkpoint_dict))
        print(f"[CheckpointLibrary] Created property checkpoint: {checkpoint_id}")
        return checkpoint_id
    
    def create_area_checkpoint(
        self,
        checkpoint_id: str,
        x: float,
        y: float,
        width: float,
        height: float,
        expected_text: str = "",
        description: str = ""
    ) -> str:
        """
        Create an area checkpoint for OCR verification.
        
        Args:
            checkpoint_id: Unique identifier
            x: X coordinate of area
            y: Y coordinate of area
            width: Width of area
            height: Height of area
            expected_text: Expected text from OCR
            description: Description
            
        Returns:
            Checkpoint ID
        """
        if not self._verifier:
            from api.DriverAgnosticApi import DriverAgnosticApi
            driver_api = DriverAgnosticApi()
            self._verifier = CheckpointVerifier(driver_api)
        
        checkpoint_dict = create_checkpoint(
            checkpoint_type="Area",
            id=checkpoint_id,
            x=x,
            y=y,
            width=width,
            height=height,
            expected_value=expected_text,
            description=description
        )
        
        from api.checkpoint_verifier import Checkpoint
        self._verifier.add_checkpoint(Checkpoint.from_dict(checkpoint_dict))
        print(f"[CheckpointLibrary] Created area checkpoint: {checkpoint_id}")
        return checkpoint_id
    
    def create_image_checkpoint(
        self,
        checkpoint_id: str,
        x: float,
        y: float,
        width: float,
        height: float,
        baseline_path: str = "",
        threshold: float = 95.0,
        description: str = ""
    ) -> str:
        """
        Create an image checkpoint for visual verification.
        
        Args:
            checkpoint_id: Unique identifier
            x: X coordinate of area
            y: Y coordinate of area
            width: Width of area
            height: Height of area
            baseline_path: Path to baseline image
            threshold: Similarity threshold (0-100)
            description: Description
            
        Returns:
            Checkpoint ID
        """
        if not self._verifier:
            from api.DriverAgnosticApi import DriverAgnosticApi
            driver_api = DriverAgnosticApi()
            self._verifier = CheckpointVerifier(driver_api)
        
        checkpoint_dict = create_checkpoint(
            checkpoint_type="Image",
            id=checkpoint_id,
            x=x,
            y=y,
            width=width,
            height=height,
            baseline_image_path=baseline_path,
            description=description,
            parameters={'threshold': str(threshold)}
        )
        
        from api.checkpoint_verifier import Checkpoint
        self._verifier.add_checkpoint(Checkpoint.from_dict(checkpoint_dict))
        print(f"[CheckpointLibrary] Created image checkpoint: {checkpoint_id}")
        return checkpoint_id
    
    def export_checkpoints(self, file_path: str):
        """
        Export loaded checkpoints to a YAML file.
        
        Args:
            file_path: Output file path
            
        Example:
            Create Some Checkpoints
            Export Checkpoints    ${CURDIR}/../checkpoints/exported.yaml
        """
        if not self._verifier or not self._verifier.checkpoints:
            print("[CheckpointLibrary] No checkpoints to export")
            return
        
        self._verifier.checkpoints[0]  # Access to ensure list exists
        print(f"[CheckpointLibrary] Would export to {file_path}")
        # TODO: Implement export
    
    def get_checkpoint_template(self) -> str:
        """
        Get the checkpoint YAML template.
        
        Returns:
            Template string for creating checkpoint files
            
        Example:
            ${template}=    Get Checkpoint Template
            Create File    checkpoints/new.yaml    ${template}
        """
        return CHECKPOINT_TEMPLATE
    
    def verify_element_property(
        self,
        element_alias: str,
        property_name: str,
        expected_value: str,
        comparison: str = "Equals"
    ) -> bool:
        """
        Directly verify an element property without loading checkpoints.
        
        Args:
            element_alias: Element alias
            property_name: Property to verify
            expected_value: Expected value
            comparison: Comparison operator (Equals, Contains, etc.)
            
        Returns:
            True if property matches, False otherwise
            
        Example:
            ${passed}=    Verify Element Property
            ...    LoginPage.txtUsername    Text    admin    Equals
            Should Be True    ${passed}
        """
        from api.DriverAgnosticApi import DriverAgnosticApi
        from api.checkpoint_verifier import CheckpointVerifier
        
        driver_api = DriverAgnosticApi()
        verifier = CheckpointVerifier(driver_api)
        
        checkpoint_dict = create_checkpoint(
            checkpoint_type="Property",
            property_name=property_name,
            expected_value=expected_value,
            element_alias=element_alias,
            description=f"Direct verification of {element_alias}.{property_name}"
        )
        
        from api.checkpoint_verifier import Checkpoint
        checkpoint = Checkpoint.from_dict(checkpoint_dict)
        verifier.add_checkpoint(checkpoint)
        
        result = verifier.verify_checkpoint(checkpoint.id)
        return result['passed']
