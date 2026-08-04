*** Settings ***
Documentation    Layer 1 — Demonstrates WPFSpy-only mode.
...              PriorityCheckbox is a non-standard control that requires
...              WPFSpy for interaction. In WPFSpy-only mode, the framework
...              uses WPFSpy directly for all elements.
Library          ../api/DriverAgnosticApi.py
Resource         ../modules/login_module.robot
Resource         ../modules/order_module.robot
Test Setup       Reset Application

*** Test Cases ***
Toggle Priority Checkbox With WPFSpy
    [Documentation]    Proves WPFSpy can interact with non-standard controls
    ...    like PriorityToggle that are not exposed via UI Automation.
    [Tags]    reliability    wpfspy-only
    Login To Application    user1    Pass@123
    Toggle Order Priority
    ${strategy}=    Get Last Strategy Used
    Should Be Equal As Strings    ${strategy}    WPFSpy
    Log    WPFSpy-only mode confirmed: PriorityCheckbox toggled via WPFSpy    console=True
    Logout From Orders

Verify Orders Grid Content Via Ocr With WPFSpy
    [Documentation]    Proves OCR-based DataGrid extraction works with WPFSpy.
    [Tags]    reliability    ocr    wpfspy-only
    Login To Application    user1    Pass@123
    Create New Order    SKU-2002    3
    ${ocr_csv}=    Get Data Grid Content Ocr    OrdersPage.OrdersWindow.gridOrders
    Log    DataGrid OCR CSV content:${ocr_csv}    console=True
    Should Contain    ${ocr_csv}    SKU-2002
    Logout From Orders
