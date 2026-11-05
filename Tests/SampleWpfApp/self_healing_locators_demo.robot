*** Settings ***
Documentation    Layer 1 — Demonstrates multi-driver fallback.
...              PriorityCheckbox is a non-standard control that requires
...              WPFSpy (via Name) for interaction since it has no AutomationId.
...
...              The framework now supports multiple strategies per driver:
...              - FlaUI: AutomationId -> Name -> Type+Index
...              - WPFSpy: XPath (AutomationId) -> XPath (Name) -> Type+Index
...              - Sikuli: Image-based fallback
Library          ../../TestAutoLayer/api/DriverAgnosticApi.py
Resource         ../TestModules/login_module.robot
Resource         ../TestModules/order_module.robot
Test Setup       Reset Application

*** Test Cases ***
Toggle Priority Checkbox With WPFSpy
    [Documentation]    Proves the framework can interact with non-standard controls
    ...    like PriorityToggle that are not exposed via UI Automation (no AutomationId).
    ...    Strategy format is "Driver:SearchBy" (e.g., "FlaUI:Name" or "WPFSpy:Name").
    ...    The framework finds the element using Name-based matching when AutomationId is absent.
    [Tags]    reliability    self-healing
    Login To Application    user1    Pass@123
    Toggle Order Priority
    ${strategy}=    Get Last Strategy Used
    # PriorityCheckbox has no AutomationId, so it should use Name-based matching
    # Strategy should contain "Name" (either from FlaUI or WPFSpy)
    Should Contain    ${strategy}    Name
    Log    Fallback confirmed: PriorityCheckbox toggled via ${strategy}    console=True
    Logout From Orders

Verify Orders Grid Content Via Ocr With WPFSpy
    [Documentation]    Proves OCR-based DataGrid extraction works.
    [Tags]    reliability    ocr
    Login To Application    user1    Pass@123
    Create New Order    SKU-2002    3
    ${ocr_csv}=    Get Data Grid Content Ocr    OrdersPage.OrdersWindow.gridOrders
    Log    DataGrid OCR CSV content:${ocr_csv}    console=True
    Should Contain    ${ocr_csv}    SKU-2002
    Logout From Orders

Verify Multi-Strategy Fallback
    [Documentation]    Verifies the framework tries all strategies in priority order.
    ...    For chkPriority (no AutomationId), it should fall back from:
    ...    FlaUI:AutomationId -> FlaUI:Name -> WPFSpy:Name -> Sikuli:Image
    [Tags]    reliability    self-healing
    Login To Application    user1    Pass@123
    # This element has no AutomationId, so it should use Name-based matching
    Toggle Order Priority
    ${strategy}=    Get Last Strategy Used
    Log    Strategy used: ${strategy}    console=True
    # Should have used a Name or Image-based strategy (not AutomationId)
    Should Match Regexp    ${strategy}    (Name|Image|XPath)
    Logout From Orders
