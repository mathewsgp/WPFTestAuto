*** Settings ***
Documentation    Layer 2 — Reusable Verification Modules: Orders
Library          ../api/DriverAgnosticApi.py

*** Keywords ***
Verify Order Confirmation Displayed
    [Documentation]    Verifies the confirmation label shows the expected
    ...    SKU and quantity after an order is created.
    [Arguments]    ${sku}    ${qty}
    Verify Element Text    OrdersPage.OrdersWindow.lblConfirmation    Order confirmed: ${sku} x${qty}

Verify Orders Grid Row Count
    [Documentation]    Verifies the orders grid contains an order with the expected SKU and quantity.
    [Arguments]    ${sku}    ${qty}
    ${grid_text}=    Get Element Text    OrdersPage.OrdersWindow.gridOrders
    Should Contain    ${grid_text}    ${sku}
    Should Contain    ${grid_text}    ${qty}

Verify Orders Grid Contains
    [Documentation]    Verifies the orders grid contains the expected SKU value.
    [Arguments]    ${expected_sku}
    ${grid_text}=    Get Element Text    OrdersPage.OrdersWindow.gridOrders
    Should Contain    ${grid_text}    ${expected_sku}

Verify Login Error Displayed
    [Documentation]    Verifies the login error label shows the expected message.
    [Arguments]    ${expected_message}
    Verify Element Text    LoginPage.MainWindow.lblError    ${expected_message}
