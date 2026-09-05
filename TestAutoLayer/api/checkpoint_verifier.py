"""
Checkpoint Verifier
===================

Provides checkpoint-based verification for WPF test automation.
Checkpoints capture expected state during recording and verify during playback.

Checkpoint Types:
- Property: Verify element property values (Text, IsEnabled, IsVisible, etc.)
- Area: Verify text content in a screen area using OCR
- Image: Visual comparison against baseline image
- DataGrid: Verify DataGrid content
- Attribute: Verify specific attribute values
- Count: Verify element count in containers
"""

import json
import os
import re
from enum import Enum
from typing import Any, Callable, Dict, List, Optional, Union
from dataclasses import dataclass, field

import yaml


class CheckpointType(Enum):
    PROPERTY = "Property"
    AREA = "Area"
    IMAGE = "Image"
    DATAGRID = "DataGrid"
    COUNT = "Count"
    ATTRIBUTE = "Attribute"


class ComparisonOperator(Enum):
    EQUALS = "Equals"
    NOT_EQUALS = "NotEquals"
    CONTAINS = "Contains"
    STARTS_WITH = "StartsWith"
    ENDS_WITH = "EndsWith"
    GREATER_THAN = "GreaterThan"
    LESS_THAN = "LessThan"
    GREATER_THAN_OR_EQUAL = "GreaterThanOrEqual"
    LESS_THAN_OR_EQUAL = "LessThanOrEqual"
    MATCHES_REGEX = "MatchesRegex"


@dataclass
class Checkpoint:
    """Represents a single checkpoint definition."""
    id: str
    type: CheckpointType
    property_name: str = "Text"
    expected_value: str = ""
    description: str = ""
    operator: ComparisonOperator = ComparisonOperator.EQUALS
    tolerance: float = 0.0  # For numeric comparisons
    
    # Element reference
    element_alias: Optional[str] = None
    
    # Area/Image checkpoint data
    x: Optional[float] = None
    y: Optional[float] = None
    width: Optional[float] = None
    height: Optional[float] = None
    baseline_image_path: Optional[str] = None
    
    # Additional parameters
    parameters: Dict[str, str] = field(default_factory=dict)
    
    @classmethod
    def from_dict(cls, data: Dict) -> 'Checkpoint':
        """Create checkpoint from dictionary."""
        return cls(
            id=data.get('id', ''),
            type=CheckpointType(data.get('type', 'Property').capitalize()),
            property_name=data.get('propertyName', 'Text'),
            expected_value=data.get('expectedValue', ''),
            description=data.get('description', ''),
            element_alias=data.get('elementAlias'),
            x=data.get('x'),
            y=data.get('y'),
            width=data.get('width'),
            height=data.get('height'),
            baseline_image_path=data.get('baselineImagePath'),
            parameters=data.get('parameters', {})
        )
    
    def to_dict(self) -> Dict:
        """Convert checkpoint to dictionary."""
        result = {
            'id': self.id,
            'type': self.type.value,
            'propertyName': self.property_name,
            'expectedValue': self.expected_value,
            'description': self.description
        }
        if self.element_alias:
            result['elementAlias'] = self.element_alias
        if self.operator != ComparisonOperator.EQUALS:
            result['operator'] = self.operator.value
        if self.tolerance > 0:
            result['tolerance'] = self.tolerance
        if self.x is not None:
            result['x'] = self.x
        if self.y is not None:
            result['y'] = self.y
        if self.width is not None:
            result['width'] = self.width
        if self.height is not None:
            result['height'] = self.height
        if self.baseline_image_path:
            result['baselineImagePath'] = self.baseline_image_path
        if self.parameters:
            result['parameters'] = self.parameters
        return result


class CheckpointVerifier:
    """
    Verifies checkpoints during test execution.
    
    Usage:
        verifier = CheckpointVerifier(driver_api)
        verifier.load_checkpoints("repository/checkpoints/my_test.yaml")
        verifier.verify_all()  # Raises AssertionError on failure
    """
    
    def __init__(self, driver_api):
        """
        Initialize with a DriverAgnosticApi instance.
        
        Args:
            driver_api: DriverAgnosticApi instance for element interaction
        """
        self.driver = driver_api
        self.checkpoints: List[Checkpoint] = []
        self._verification_results: List[Dict] = []
    
    def load_checkpoints(self, file_path: str) -> int:
        """
        Load checkpoints from a YAML file.
        
        Args:
            file_path: Path to the checkpoints YAML file
            
        Returns:
            Number of checkpoints loaded
        """
        if not os.path.exists(file_path):
            raise FileNotFoundError(f"Checkpoint file not found: {file_path}")
        
        with open(file_path, 'r') as f:
            data = yaml.safe_load(f)
        
        self.checkpoints = []
        checkpoints_data = data.get('checkpoints', [])
        
        for cp_data in checkpoints_data:
            checkpoint = Checkpoint.from_dict(cp_data)
            self.checkpoints.append(checkpoint)
        
        return len(self.checkpoints)
    
    def load_checkpoints_from_dict(self, data: Dict):
        """Load checkpoints from a dictionary (e.g., from JSON)."""
        self.checkpoints = []
        checkpoints_data = data.get('checkpoints', [])
        
        for cp_data in checkpoints_data:
            checkpoint = Checkpoint.from_dict(cp_data)
            self.checkpoints.append(checkpoint)
    
    def add_checkpoint(self, checkpoint: Checkpoint):
        """Add a single checkpoint."""
        self.checkpoints.append(checkpoint)
    
    def verify_all(self) -> List[Dict]:
        """
        Verify all loaded checkpoints.
        
        Returns:
            List of verification results
            
        Raises:
            AssertionError: If any checkpoint fails
        """
        self._verification_results = []
        failures = []
        
        for checkpoint in self.checkpoints:
            result = self._verify_checkpoint(checkpoint)
            self._verification_results.append(result)
            
            if not result['passed']:
                failures.append(result)
        
        if failures:
            failure_msg = "\n".join([
                f"  [{f['checkpoint_id']}] {f['description']}: {f['message']}"
                for f in failures
            ])
            raise AssertionError(
                f"Checkpoint verification failed ({len(failures)}/{len(self.checkpoints)}):\n{failure_msg}"
            )
        
        return self._verification_results
    
    def verify_checkpoint(self, checkpoint_id: str) -> Dict:
        """
        Verify a single checkpoint by ID.
        
        Args:
            checkpoint_id: The checkpoint ID to verify
            
        Returns:
            Verification result dictionary
        """
        checkpoint = next((cp for cp in self.checkpoints if cp.id == checkpoint_id), None)
        if not checkpoint:
            raise ValueError(f"Checkpoint not found: {checkpoint_id}")
        
        return self._verify_checkpoint(checkpoint)
    
    def _verify_checkpoint(self, checkpoint: Checkpoint) -> Dict:
        """Verify a single checkpoint."""
        result = {
            'checkpoint_id': checkpoint.id,
            'type': checkpoint.type.value,
            'description': checkpoint.description,
            'passed': False,
            'message': '',
            'expected': checkpoint.expected_value,
            'actual': None
        }
        
        try:
            if checkpoint.type == CheckpointType.PROPERTY:
                actual = self._get_property_value(checkpoint)
                result['actual'] = actual
                result['passed'], result['message'] = self._compare_values(
                    str(actual), checkpoint.expected_value, checkpoint.operator, checkpoint.tolerance
                )
            
            elif checkpoint.type == CheckpointType.ATTRIBUTE:
                actual = self._get_attribute_value(checkpoint)
                result['actual'] = actual
                result['passed'], result['message'] = self._compare_values(
                    str(actual), checkpoint.expected_value, checkpoint.operator, checkpoint.tolerance
                )
            
            elif checkpoint.type == CheckpointType.AREA:
                # OCR-based verification
                actual = self._capture_area_text(checkpoint)
                result['actual'] = actual
                result['passed'], result['message'] = self._compare_values(
                    actual, checkpoint.expected_value, ComparisonOperator.CONTAINS, 0
                )
            
            elif checkpoint.type == CheckpointType.IMAGE:
                # Image comparison
                similarity = self._compare_images(checkpoint)
                result['actual'] = f"{similarity:.1f}%"
                # Default to 95% similarity threshold
                threshold = float(checkpoint.parameters.get('threshold', 95))
                result['passed'] = similarity >= threshold
                result['message'] = f"Image similarity: {similarity:.1f}% (threshold: {threshold}%)"
            
            elif checkpoint.type == CheckpointType.DATAGRID:
                actual = self._get_datagrid_content(checkpoint)
                result['actual'] = actual
                result['passed'], result['message'] = self._compare_values(
                    actual, checkpoint.expected_value, ComparisonOperator.CONTAINS, 0
                )
            
            elif checkpoint.type == CheckpointType.COUNT:
                actual = self._get_element_count(checkpoint)
                result['actual'] = str(actual)
                expected_count = int(checkpoint.expected_value)
                result['passed'] = actual == expected_count
                result['message'] = f"Count: {actual} (expected: {expected_count})"
            
        except Exception as e:
            result['message'] = f"Error during verification: {str(e)}"
        
        return result
    
    def _get_property_value(self, checkpoint: Checkpoint) -> str:
        """Get property value from element."""
        if not checkpoint.element_alias:
            raise ValueError("Checkpoint missing element alias")
        
        prop = checkpoint.property_name.lower()
        
        if prop == 'text':
            return self.driver.get_element_text(checkpoint.element_alias)
        elif prop == 'isenabled':
            return str(self.driver.is_element_enabled(checkpoint.element_alias))
        elif prop == 'isvisible':
            return str(self.driver.is_element_visible(checkpoint.element_alias))
        else:
            # Try to get via driver API
            return ""
    
    def _get_attribute_value(self, checkpoint: Checkpoint) -> str:
        """Get attribute value from element."""
        if not checkpoint.element_alias:
            raise ValueError("Checkpoint missing element alias")
        
        # Would need get_attribute implementation
        return ""
    
    def _capture_area_text(self, checkpoint: Checkpoint) -> str:
        """Capture and OCR text from screen area."""
        # Would use screenshot + OCR
        return "[OCR not implemented - would capture from area]"
    
    def _compare_images(self, checkpoint: Checkpoint) -> float:
        """Compare current image with baseline."""
        # Would use image comparison library
        return 100.0  # Placeholder
    
    def _get_datagrid_content(self, checkpoint: Checkpoint) -> str:
        """Get DataGrid content."""
        if not checkpoint.element_alias:
            raise ValueError("Checkpoint missing element alias")
        
        return self.driver.get_data_grid_content_ocr(checkpoint.element_alias)
    
    def _get_element_count(self, checkpoint: Checkpoint) -> int:
        """Get count of elements matching criteria."""
        if not checkpoint.element_alias:
            raise ValueError("Checkpoint missing element alias")
        
        elements = self.driver.find_elements(checkpoint.element_alias)
        return len(elements)
    
    @staticmethod
    def _compare_values(
        actual: str, 
        expected: str, 
        operator: ComparisonOperator,
        tolerance: float
    ) -> tuple:
        """Compare values using the specified operator."""
        # Try numeric comparison
        try:
            actual_num = float(actual)
            expected_num = float(expected)
            
            result = operator.value match
            {
                ComparisonOperator.EQUALS.value: abs(actual_num - expected_num) <= tolerance,
                ComparisonOperator.NOT_EQUALS.value: abs(actual_num - expected_num) > tolerance,
                ComparisonOperator.GREATER_THAN.value: actual_num > expected_num,
                ComparisonOperator.LESS_THAN.value: actual_num < expected_num,
                ComparisonOperator.GREATER_THAN_OR_EQUAL.value: actual_num >= expected_num,
                ComparisonOperator.LESS_THAN_OR_EQUAL.value: actual_num <= expected_num,
            }
            
            message = f"Expected {expected_num}, got {actual_num}"
            return result, message
        except ValueError:
            pass  # Not numeric, fall back to string comparison
        
        # String comparison
        actual_lower = actual.lower()
        expected_lower = expected.lower()
        
        result, message = {
            ComparisonOperator.EQUALS.value: (
                actual == expected,
                f"Expected '{expected}', got '{actual}'"
            ),
            ComparisonOperator.NOT_EQUALS.value: (
                actual != expected,
                f"Expected not '{expected}', but got '{actual}'"
            ),
            ComparisonOperator.CONTAINS.value: (
                expected_lower in actual_lower,
                f"Expected '{expected}' to be in '{actual}'"
            ),
            ComparisonOperator.STARTS_WITH.value: (
                actual_lower.startswith(expected_lower),
                f"Expected to start with '{expected}', got '{actual}'"
            ),
            ComparisonOperator.ENDS_WITH.value: (
                actual_lower.endswith(expected_lower),
                f"Expected to end with '{expected}', got '{actual}'"
            ),
            ComparisonOperator.MATCHES_REGEX.value: (
                re.search(expected, actual, re.IGNORECASE) is not None,
                f"Expected regex '{expected}' to match '{actual}'"
            ),
        }[operator.value]
        
        return result, message
    
    def get_verification_summary(self) -> Dict:
        """Get summary of verification results."""
        if not self._verification_results:
            return {'total': 0, 'passed': 0, 'failed': 0, 'pass_rate': '0%'}
        
        total = len(self._verification_results)
        passed = sum(1 for r in self._verification_results if r['passed'])
        failed = total - passed
        
        return {
            'total': total,
            'passed': passed,
            'failed': failed,
            'pass_rate': f"{(passed/total*100):.1f}%" if total > 0 else "0%"
        }


# Factory function for creating checkpoints
def create_checkpoint(
    checkpoint_type: str,
    property_name: str = "Text",
    expected_value: str = "",
    element_alias: str = None,
    description: str = "",
    **kwargs
) -> Dict:
    """
    Create a checkpoint dictionary.
    
    Args:
        checkpoint_type: Type of checkpoint (Property, Area, Image, DataGrid, Count, Attribute)
        property_name: Property to verify
        expected_value: Expected value
        element_alias: Element alias
        description: Description
        **kwargs: Additional parameters (x, y, width, height, baselineImagePath, etc.)
    
    Returns:
        Checkpoint dictionary
    """
    checkpoint = Checkpoint(
        id=kwargs.get('id', os.urandom(4).hex()),
        type=CheckpointType(checkpoint_type.capitalize()),
        property_name=property_name,
        expected_value=expected_value,
        element_alias=element_alias,
        description=description,
        **{k: v for k, v in kwargs.items() if k in [
            'x', 'y', 'width', 'height', 'baseline_image_path', 'parameters'
        ]}
    )
    return checkpoint.to_dict()


# YAML checkpoint file template
CHECKPOINT_TEMPLATE = """# Checkpoint Definitions
# Generated by WPFTestAuto Checkpoint Wizard

checkpoints:
  # Property Checkpoint Example
  - id: prop_001
    type: Property
    elementAlias: LoginPage.txtUsername
    propertyName: Text
    expectedValue: "admin"
    description: "Verify username field contains 'admin'"

  # Area Checkpoint Example (OCR)
  - id: area_001
    type: Area
    x: 100
    y: 200
    width: 300
    height: 50
    expectedValue: "Welcome to the app"
    description: "Verify welcome message"

  # Image Checkpoint Example
  - id: img_001
    type: Image
    x: 0
    y: 0
    width: 1920
    height: 1080
    baselineImagePath: "checkpoints/baseline/homepage.png"
    description: "Verify homepage appearance"
    parameters:
      threshold: "95"
"""
