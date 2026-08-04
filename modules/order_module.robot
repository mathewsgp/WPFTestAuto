*** Settings ***
Documentation    Layer 2 - Reusable Action Modules: Orders
Library          ../api/DriverAgnosticApi.py

*** Keywords ***
Create New Order
    [Documentation]    Creates a new order for the given SKU and quantity.
    [Arguments]    ${sku}    ${qty}
    Set Element Value    OrdersPage.OrdersWindow.cmbSku    ${sku}
    Set Element Value    OrdersPage.OrdersWindow.txtQty    ${qty}
    Click Element    OrdersPage.OrdersWindow.btnCreateOrder

Toggle Order Priority
    [Documentation]    Toggles the (non-standard, custom-rendered) priority
    ...    checkbox - demonstrates the self-healing WPFSpy XPath strategy.
    Toggle Element    OrdersPage.OrdersWindow.chkPriority

Logout From Orders
    [Documentation]    Logs out from the Orders page and returns to the login page.
    Click Element    OrdersPage.OrdersWindow.btnLogout
