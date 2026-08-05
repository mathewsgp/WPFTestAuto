"""
Wild-Card XPath Engine
=====================
Provides flexible XPath matching with wild-card support, similar to Ranorex's RanoreXPath.

Supported wild-cards:
- `/?` or `/*` - Match any element (one level)
- `//?` or `//*` - Match any descendant element
- `*` at end - Prefix matching (e.g., `btn*` matches `btnSubmit`, `btnCancel`)
- `*` at start - Suffix matching
- `*` in middle - Contains matching
- `[regex:pattern]` - Regex pattern matching

Example:
    //Window/?/Button - Any Button under Window
    //Window//* - Any descendant under Window
    //Button[@AutomationId='btn*'] - Buttons starting with 'btn'
    //Button[regex:btn(Id)?] - Buttons matching regex

Usage:
    from wildcard_xpath import WildcardXPathMatcher
    
    matcher = WildcardXPathMatcher()
    result = matcher.matches("//Window/?/Button", element)
"""

import re
from typing import Optional, List, Tuple, Any
from dataclasses import dataclass
from enum import Enum


class MatchMode(Enum):
    """XPath match mode."""
    EXACT = "exact"
    PREFIX = "prefix"
    SUFFIX = "suffix"
    CONTAINS = "contains"
    WILDCARD = "wildcard"
    REGEX = "regex"


@dataclass
class MatchResult:
    """Result of an XPath match operation."""
    matched: bool
    confidence: float  # 0.0 to 1.0
    matched_path: Optional[str]
    match_mode: MatchMode
    details: str = ""


class WildcardXPathMatcher:
    """Wild-card XPath matching engine.
    
    Supports:
    - Standard XPath 1.0 syntax
    - Wild-cards: /?, / *
    - Prefix/suffix matching
    - Regex patterns
    - Partial AutomationId matching
    """
    
    def __init__(self):
        self._compiled_patterns = {}
    
    def parse_wildcard_xpath(self, xpath: str) -> dict:
        """Parse an XPath with wild-cards into components.
        
        Args:
            xpath: XPath string potentially containing wild-cards
            
        Returns:
            dict with parsed components:
                - has_wildcard: bool
                - segments: list of (tag, conditions, is_wildcard)
                - has_regex: bool
                - patterns: dict of attribute -> pattern
        """
        if not xpath or not xpath.startswith("/"):
            return {"valid": False, "error": "Invalid XPath: must start with /"}
        
        result = {
            "valid": True,
            "has_wildcard": False,
            "segments": [],
            "has_regex": False,
            "patterns": {},
            "original": xpath
        }
        
        # Check for wild-cards
        result["has_wildcard"] = "/?" in xpath or "/*/" in xpath or "//?" in xpath
        
        # Check for regex
        result["has_regex"] = "[regex:" in xpath
        
        # Parse segments
        segments = []
        current = ""
        in_predicate = False
        predicate_depth = 0
        
        i = 0
        while i < len(xpath):
            char = xpath[i]
            
            if char == "[":
                in_predicate = True
                predicate_depth = 1
                current += char
            elif char == "]":
                predicate_depth -= 1
                if predicate_depth == 0:
                    in_predicate = False
                current += char
            elif char == "/" and not in_predicate:
                if current:
                    segments.append(current)
                current = "/"
            else:
                current += char
            
            i += 1
        
        if current:
            segments.append(current)
        
        # Parse each segment
        for segment in segments:
            if segment == "/":
                continue
                
            # Remove leading /
            segment = segment.lstrip("/")
            
            # Check if wild-card segment
            is_wildcard = segment in ["*", "?"] or segment == "*"
            
            # Parse tag name and conditions
            conditions = {}
            tag_name = segment
            
            # Extract predicate conditions
            predicate_match = re.search(r'\[(.*?)\]', segment)
            if predicate_match:
                predicate_content = predicate_match.group(1)
                tag_name = segment[:predicate_match.start()].strip()
                
                # Parse @attribute='value' conditions
                attr_matches = re.findall(r'@(\w+)=([\'"])(.*?)\2', predicate_content)
                for attr, quote, value in attr_matches:
                    conditions[attr] = value
                    
                # Check for regex
                regex_match = re.search(r'regex:(.+)', predicate_content)
                if regex_match:
                    conditions["_regex"] = regex_match.group(1)
                    result["has_regex"] = True
                
                # Check for wild-card in value
                for attr, value in list(conditions.items()):
                    if "_regex" not in attr and ("*" in value or "?" in value):
                        conditions[f"_{attr}_wildcard"] = True
            
            result["segments"].append({
                "tag": tag_name if tag_name != "*" else "*",
                "conditions": conditions,
                "is_wildcard": is_wildcard or tag_name == "*"
            })
        
        return result
    
    def matches_wildcard_pattern(self, value: str, pattern: str) -> bool:
        """Check if a value matches a wild-card pattern.
        
        Supports:
        - * - matches everything
        - prefix* - matches if value starts with prefix
        - *suffix - matches if value ends with suffix
        - prefix*suffix - matches if value contains prefix and ends with suffix
        - *prefix* - matches if value contains prefix
        """
        if pattern == "*" or pattern == "?":
            return True
        
        # Determine match mode
        if pattern.startswith("*") and pattern.endswith("*"):
            # Contains: *middle*
            return pattern[1:-1] in value
        elif pattern.startswith("*"):
            # Suffix: *suffix
            return value.endswith(pattern[1:])
        elif pattern.endswith("*"):
            # Prefix: prefix*
            return value.startswith(pattern[:-1])
        else:
            # Exact with internal wild-cards
            regex_pattern = pattern.replace("*", ".*").replace("?", ".")
            return re.match(f"^{regex_pattern}$", value) is not None
    
    def matches_regex_pattern(self, value: str, pattern: str) -> bool:
        """Check if a value matches a regex pattern."""
        try:
            return re.search(pattern, value) is not None
        except re.error:
            return False
    
    def matches_attribute(
        self,
        element_attr: str,
        pattern: str,
        match_mode: str = "exact"
    ) -> Tuple[bool, float]:
        """Check if an element attribute matches a pattern.
        
        Returns:
            Tuple of (matched, confidence)
            confidence is 0.0-1.0 based on how specific the match is
        """
        if not element_attr:
            return False, 0.0
        
        if match_mode == "wildcard":
            matched = self.matches_wildcard_pattern(element_attr, pattern)
            # Calculate confidence based on pattern specificity
            if pattern == "*":
                confidence = 0.1
            elif "*" in pattern:
                confidence = 0.7
            else:
                confidence = 0.9 if matched else 0.0
            return matched, confidence
        
        elif match_mode == "regex":
            matched = self.matches_regex_pattern(element_attr, pattern)
            confidence = 0.85 if matched else 0.0
            return matched, confidence
        
        elif match_mode == "contains":
            matched = pattern in element_attr
            confidence = 0.6 if matched else 0.0
            return matched, confidence
        
        elif match_mode == "prefix":
            matched = element_attr.startswith(pattern)
            confidence = 0.75 if matched else 0.0
            return matched, confidence
        
        elif match_mode == "suffix":
            matched = element_attr.endswith(pattern)
            confidence = 0.75 if matched else 0.0
            return matched, confidence
        
        else:  # exact
            matched = element_attr == pattern
            confidence = 1.0 if matched else 0.0
            return matched, confidence
    
    def build_flexible_xpath(self, element_info: dict) -> str:
        """Build a flexible XPath for an element with multiple matching options.
        
        Args:
            element_info: dict with keys:
                - tag: element tag/type
                - automation_id: AutomationId value
                - name: Name property
                - class_name: Full class name
                
        Returns:
            XPath string with wild-card matching options
        """
        parts = []
        
        tag = element_info.get("tag", "*")
        automation_id = element_info.get("automation_id") or element_info.get("AutomationId")
        name = element_info.get("name") or element_info.get("Name")
        class_name = element_info.get("class_name") or element_info.get("ClassName")
        
        # Build conditions
        conditions = []
        
        if automation_id:
            # Use AutomationId with prefix wild-card for flexibility
            conditions.append(f"@AutomationId='{automation_id}'")
            # Add prefix version for UI changes that add prefixes
            if not automation_id.endswith("*"):
                conditions.append(f"@AutomationId='*{automation_id}'")
        
        if name and name != automation_id:
            conditions.append(f"@Name='{name}'")
        
        if conditions:
            conditions_str = " and ".join(conditions)
            xpath = f"//{tag}[{conditions_str}]"
        else:
            xpath = f"//{tag}"
        
        return xpath
    
    def expand_xpath_alternatives(self, xpath: str, element_info: dict) -> List[str]:
        """Generate multiple XPath alternatives for an element.
        
        Useful for creating fallback strategies in the element repository.
        
        Args:
            xpath: Original XPath
            element_info: Element properties for generating alternatives
            
        Returns:
            List of XPath alternatives, ordered by preference
        """
        alternatives = [xpath]
        
        automation_id = element_info.get("automation_id") or element_info.get("AutomationId")
        name = element_info.get("name") or element_info.get("Name")
        tag = element_info.get("tag", "*")
        
        if automation_id:
            # Exact match
            alternatives.append(f"//{tag}[@AutomationId='{automation_id}']")
            # Prefix match (if ID changed)
            if not automation_id.startswith("*"):
                alternatives.append(f"//{tag}[@AutomationId='*{automation_id}']")
            # Suffix match
            if not automation_id.endswith("*"):
                alternatives.append(f"//{tag}[@AutomationId='{automation_id}*']")
            # Contains match
            alternatives.append(f"//{tag}[contains(@AutomationId,'{automation_id}')]")
        
        if name:
            alternatives.append(f"//{tag}[@Name='{name}']")
            # Contains name
            alternatives.append(f"//{tag}[contains(@Name,'{name}')]")
        
        # Fall back to any element of this type
        alternatives.append(f"//{tag}")
        
        # Remove duplicates while preserving order
        seen = set()
        unique = []
        for alt in alternatives:
            if alt not in seen:
                seen.add(alt)
                unique.append(alt)
        
        return unique


def expand_repository_xpaths(repository: dict) -> dict:
    """Expand element repository with flexible XPaths.
    
    Args:
        repository: Element repository dict
        
    Returns:
        Repository with expanded strategies
    """
    matcher = WildcardXPathMatcher()
    
    expanded = repository.copy()
    
    for alias, element in repository.items():
        if "strategies" not in element:
            element["strategies"] = {}
        
        # Get element info for expansion
        element_info = {
            "tag": element.get("controlType", "*"),
            "automation_id": element.get("automationId") or element.get("AutomationId"),
            "name": element.get("name") or element.get("Name"),
        }
        
        # Generate flexible XPaths
        alternatives = matcher.expand_xpath_alternatives("", element_info)
        
        # Add as additional strategies
        if "WPFSpy" not in element["strategies"]:
            element["strategies"]["WPFSpy"] = []
        
        for i, alt_xpath in enumerate(alternatives[1:], start=2):  # Skip original (index 0)
            # Check if already exists
            exists = any(
                s.get("value") == alt_xpath 
                for strategies in element["strategies"].values() 
                for s in strategies
            )
            
            if not exists:
                element["strategies"]["WPFSpy"].append({
                    "searchBy": "XPath",
                    "value": alt_xpath,
                    "priority": i,
                    "wildcard": True,
                    "source": "auto_expanded"
                })
    
    return expanded


# CLI tool for testing
if __name__ == "__main__":
    import sys
    
    matcher = WildcardXPathMatcher()
    
    if len(sys.argv) > 1:
        xpath = sys.argv[1]
        print(f"Parsing: {xpath}")
        result = matcher.parse_wildcard_xpath(xpath)
        print(f"Result: {result}")
    else:
        # Test cases
        test_cases = [
            "//Window/?/Button",
            "//Window//*",
            "//Button[@AutomationId='btn*']",
            "//Button[@Name='Submit*']",
            "//*[regex:btn[0-9]+]",
            "//Window/?/Grid/?/Button[@AutomationId='btnSubmit']",
        ]
        
        print("Wild-Card XPath Parser Test")
        print("=" * 50)
        
        for xpath in test_cases:
            print(f"\nInput:  {xpath}")
            result = matcher.parse_wildcard_xpath(xpath)
            print(f"Parsed:  {result}")
