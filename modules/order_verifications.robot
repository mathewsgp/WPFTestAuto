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
    [Documentation]    Verifies the orders grid reports the expected row count text.
    [Arguments]    ${expected_text}
    Verify Element Text    OrdersPage.OrdersWindow.gridOrders    ${expected_text}

Verify Login Error Displayed
    [Documentation]    Verifies the login error label shows the expected message.
    [Arguments]    ${expected_message}
    Verify Element Text    LoginPage.MainWindow.lblError    ${expected_message}
