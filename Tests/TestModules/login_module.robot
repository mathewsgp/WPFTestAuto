*** Settings ***
Documentation    Layer 2 — Reusable Action Modules: Login
Library          ../api/DriverAgnosticApi.py

*** Keywords ***
Login To Application
    [Documentation]    Logs into the application with the given credentials.
    [Arguments]    ${username}    ${password}
    Set Element Value    LoginPage.MainWindow.txtUsername    ${username}
    Set Element Value    LoginPage.MainWindow.txtPassword    ${password}
    Click Element    LoginPage.MainWindow.btnSubmit
