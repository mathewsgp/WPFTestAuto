*** Settings ***
Documentation    Layer 1 — Test Scripts: Order creation & login flows.
Library          ../api/DriverAgnosticApi.py
Resource         ../modules/login_module.robot
Resource         ../modules/order_module.robot
Resource         ../modules/order_verifications.robot
Test Setup       Reset Application

*** Test Cases ***
Create And Confirm New Order
    [Documentation]    Happy-path: log in, create an order, verify confirmation.
    [Tags]    smoke    orders
    Login To Application    user1    Pass@123
    Create New Order    SKU-1001    2
    Verify Order Confirmation Displayed    SKU-1001    2
    Verify Orders Grid Row Count    SKU-1001    2
    Logout From Orders

Verify Orders Grid Content Via Ocr
    [Documentation]    Extracts DataGrid content via OCR and verifies it contains expected data.
    [Tags]    orders    ocr    verification
    Login To Application    user1    Pass@123
    Create New Order    SKU-1001    2
    ${ocr_csv}=    Get Data Grid Content Ocr    OrdersPage.OrdersWindow.gridOrders
    Log    DataGrid OCR CSV content:${ocr_csv}    console=True
    Should Contain    ${ocr_csv}    SKU-1001
    Logout From Orders

Reject Invalid Login
    [Documentation]    Negative path: wrong password shows the error label.
    [Tags]    smoke    login
    Login To Application    user1    WrongPassword
    Verify Login Error Displayed    Invalid username or password

Create Order Without Sku Shows Prompt
    [Documentation]    Negative path: creating an order with no SKU selected
    ...    shows a prompt instead of a confirmation.
    [Tags]    orders    negative
    Login To Application    user1    Pass@123
    Set Element Value    OrdersPage.OrdersWindow.txtQty    2
    Click Element    OrdersPage.OrdersWindow.btnCreateOrder
    Verify Element Text    OrdersPage.OrdersWindow.lblConfirmation    Please select a SKU
    Logout From Orders
