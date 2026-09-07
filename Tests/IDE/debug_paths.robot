*** Settings ***
Library    ../../TestAutoLayer/api/DriverAgnosticApi.py

*** Test Cases ***
Debug Paths
    Log    sys.path has  entries
    =    Evaluate    sys.path    sys
    Log    
