// WpfSpyAgent.NativeInject.cpp
// Native DLL for runtime injection into WPF processes.
// This DLL is injected into the target process using CreateRemoteThread + LoadLibrary.
// It then bootstraps the .NET runtime and loads the Spy Agent using CLR Hosting APIs.

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <iostream>
#include <string>
#include <vector>
#include <thread>
#include <atomic>

// CLR Hosting interfaces - these are already defined in mscoree.h
#include <metahost.h>
#pragma comment(lib, "mscoree.lib")

// Global state
static std::atomic<bool> g_agentStarted(false);
static HANDLE g_agentThread = nullptr;
static std::wstring g_pipeName = L"WPFSpyAgentPipe";

// Helper function to get the directory containing this DLL
static std::wstring GetDllDirectory() {
    wchar_t path[MAX_PATH];
    GetModuleFileName(nullptr, path, MAX_PATH);
    std::wstring dllPath = path;
    size_t pos = dllPath.rfind(L'\\');
    if (pos != std::wstring::npos) {
        return dllPath.substr(0, pos);
    }
    return L"";
}

// Write debug log
static void Log(const wchar_t* msg) {
    wchar_t path[MAX_PATH];
    GetModuleFileName(nullptr, path, MAX_PATH);
    std::wstring logPath = path;
    size_t pos = logPath.rfind(L'\\');
    if (pos != std::wstring::npos) {
        logPath = logPath.substr(0, pos) + L"\\wpfspy_inject_log.txt";
    }
    
    SYSTEMTIME st;
    GetLocalTime(&st);
    FILE* f = nullptr;
    _wfopen_s(&f, logPath.c_str(), L"a");
    if (f) {
        fwprintf(f, L"[%02d:%02d:%02d] %s\n", 
            st.wHour, st.wMinute, st.wSecond, msg);
        fclose(f);
    }
    OutputDebugString(msg);
}

// Thread function for agent startup (must be static/free to convert to function pointer)
static DWORD WINAPI AgentThreadProc(LPVOID param) {
    const wchar_t* pipe = (const wchar_t*)param;
    
    // Wait a bit for the process to stabilize
    Sleep(500);
    
    wchar_t msg[512];
    swprintf(msg, 512, L"[Inject] Thread started, pipe=%s", pipe);
    Log(msg);
    
    // Get the target app directory
    wchar_t dllPath[MAX_PATH];
    GetModuleFileName(nullptr, dllPath, MAX_PATH);
    std::wstring dllDir = dllPath;
    size_t pos = dllDir.rfind(L'\\');
    if (pos != std::wstring::npos) {
        dllDir = dllDir.substr(0, pos);
    }
    
    // Check for .NET Framework (FrameworkHook) or .NET Core/5+ (StartupHook)
    std::wstring hookDllPath;
    bool isNetFramework = false;
    
    // First try FrameworkHook for .NET Framework apps
    hookDllPath = dllDir + L"\\WpfSpyAgent.FrameworkHook.dll";
    swprintf(msg, 512, L"[Inject] Looking for FrameworkHook: %s", hookDllPath.c_str());
    Log(msg);
    
    if (GetFileAttributes(hookDllPath.c_str()) != INVALID_FILE_ATTRIBUTES) {
        isNetFramework = true;
        swprintf(msg, 512, L"[Inject] Found FrameworkHook at: %s", hookDllPath.c_str());
        Log(msg);
    } else {
        // Try StartupHook for .NET Core/5+
        hookDllPath = dllDir + L"\\WpfSpyAgent.StartupHook.dll";
        swprintf(msg, 512, L"[Inject] FrameworkHook not found, looking for StartupHook: %s", hookDllPath.c_str());
        Log(msg);
        
        if (GetFileAttributes(hookDllPath.c_str()) == INVALID_FILE_ATTRIBUTES) {
            swprintf(msg, 512, L"[Inject] StartupHook DLL not found at %s", hookDllPath.c_str());
            Log(msg);
            
            // Try parent directories
            for (int i = 0; i < 3; i++) {
                pos = dllDir.rfind(L'\\');
                if (pos != std::wstring::npos) {
                    dllDir = dllDir.substr(0, pos);
                    if (!isNetFramework) {
                        hookDllPath = dllDir + L"\\WpfSpyAgent.FrameworkHook.dll";
                        if (GetFileAttributes(hookDllPath.c_str()) != INVALID_FILE_ATTRIBUTES) {
                            isNetFramework = true;
                            swprintf(msg, 512, L"[Inject] Found at: %s", hookDllPath.c_str());
                            Log(msg);
                            break;
                        }
                    }
                    hookDllPath = dllDir + L"\\WpfSpyAgent.StartupHook.dll";
                    if (GetFileAttributes(hookDllPath.c_str()) != INVALID_FILE_ATTRIBUTES) {
                        swprintf(msg, 512, L"[Inject] Found at: %s", hookDllPath.c_str());
                        Log(msg);
                        break;
                    }
                }
            }
        } else {
            swprintf(msg, 512, L"[Inject] Found StartupHook at: %s", hookDllPath.c_str());
            Log(msg);
        }
    }
    
    // Set environment variables
    SetEnvironmentVariable(L"WPFSPY_PIPE_NAME", pipe);
    SetEnvironmentVariable(L"WPFSPY_AGENT_ENABLED", L"1");
    
    swprintf(msg, 512, L"[Inject] Environment set. DLL path: %s", hookDllPath.c_str());
    Log(msg);
    
    // Write config file for the agent to pick up
    wchar_t configPath[MAX_PATH];
    GetEnvironmentVariable(L"LOCALAPPDATA", configPath, MAX_PATH);
    std::wstring configDir = configPath;
    configDir += L"\\WpfSpyAgent";
    CreateDirectory(configDir.c_str(), nullptr);
    configDir += L"\\agent_config.txt";
    
    FILE* cfg = nullptr;
    _wfopen_s(&cfg, configDir.c_str(), L"w");
    if (cfg) {
        fwprintf(cfg, L"PIPE_NAME=%s\n", pipe);
        fwprintf(cfg, L"AGENT_ENABLED=1\n");
        fwprintf(cfg, L"HOOK_DLL=%s\n", hookDllPath.c_str());
        fclose(cfg);
        swprintf(msg, 512, L"[Inject] Config written to: %s", configDir.c_str());
        Log(msg);
    }
    
    // For .NET Core/.NET 5+ apps, we need to use the startup hook approach
    // Set DOTNET_STARTUP_HOOKS to point to our hook
    if (GetFileAttributes(hookDllPath.c_str()) != INVALID_FILE_ATTRIBUTES) {
        SetEnvironmentVariable(L"DOTNET_STARTUP_HOOKS", hookDllPath.c_str());
        swprintf(msg, 512, L"[Inject] DOTNET_STARTUP_HOOKS set to: %s", hookDllPath.c_str());
        Log(msg);
    }
    
    Log(L"[Inject] Agent initialization complete. The Spy Agent should be available.");
    
    // Note: Starting a new AppDomain or loading .NET assemblies into an already-running
    // process requires COM/CLR hosting APIs (ICLRRuntimeHost, etc.) which is complex.
    // The simpler approach is to set environment variables and let the user restart the app,
    // OR to use cooperative hosting where the app itself calls SpyAgentHost.Start().
    
    return 0;
}

// Try to start the Spy Agent using CLR Hosting
// Supports both .NET Framework (mscoree.dll) and .NET Core (coreclr.dll)
// Based on Snoop's approach: https://github.com/snoopwpf/snoopwpf
static bool TryStartSpyAgentCLR(const wchar_t* pipeName) {
    if (g_agentStarted.exchange(true)) {
        return true; // Already started
    }
    
    wchar_t msg[512];
    swprintf(msg, 512, L"[Inject] Attempting CLR Hosting to start Spy Agent...");
    Log(msg);
    
    // Get the path to our DLL directory
    std::wstring dllDir = GetDllDirectory();
    
    // Determine target framework by checking for framework-specific DLLs
    // WpfSpyAgent.dll should be in the same directory as the target app's DLLs
    std::wstring agentDllPath = dllDir + L"\\WpfSpyAgent.dll";
    std::wstring agentDllFwPath = dllDir + L"\\net461\\WpfSpyAgent.dll";  // .NET Framework version
    
    // Check which version of WpfSpyAgent.dll exists
    bool hasFwAgent = (GetFileAttributes(agentDllFwPath.c_str()) != INVALID_FILE_ATTRIBUTES);
    bool hasDefaultAgent = (GetFileAttributes(agentDllPath.c_str()) != INVALID_FILE_ATTRIBUTES);
    
    if (hasFwAgent) {
        agentDllPath = agentDllFwPath;
    }
    
    swprintf(msg, 512, L"[Inject] Looking for Spy Agent DLL: %s", agentDllPath.c_str());
    Log(msg);
    
    if (GetFileAttributes(agentDllPath.c_str()) == INVALID_FILE_ATTRIBUTES) {
        swprintf(msg, 512, L"[Inject] Spy Agent DLL not found at: %s", agentDllPath.c_str());
        Log(msg);
        return false;
    }
    
    ICLRRuntimeHost* runtimeHost = nullptr;
    
    // Try .NET Framework first (mscoree.dll) - more common for WPF apps
    swprintf(msg, 512, L"[Inject] Trying .NET Framework (mscoree.dll)...");
    Log(msg);
    
    HMODULE mscoree = LoadLibrary(L"mscoree.dll");
    if (mscoree) {
        swprintf(msg, 512, L"[Inject] mscoree.dll loaded");
        Log(msg);
        
        // Get CLRCreateInstance function
        typedef HRESULT (STDAPICALLTYPE* FnCLRCreateInstance)(REFCLSID clsid, REFIID riid, LPVOID* ppInterface);
        FnCLRCreateInstance CLRCreateInstance = (FnCLRCreateInstance)GetProcAddress(mscoree, "CLRCreateInstance");
        
        if (CLRCreateInstance) {
            // Get ICLRMetaHost
            ICLRMetaHost* metaHost = nullptr;
            HRESULT hr = CLRCreateInstance(CLSID_CLRMetaHost, IID_ICLRMetaHost, (LPVOID*)&metaHost);
            if (SUCCEEDED(hr) && metaHost) {
                // Get runtime info (v4.0 for .NET Framework 4.x)
                ICLRRuntimeInfo* runtimeInfo = nullptr;
                hr = metaHost->GetRuntime(L"v4.0.30319", IID_ICLRRuntimeInfo, (LPVOID*)&runtimeInfo);
                if (SUCCEEDED(hr) && runtimeInfo) {
                    // Get ICLRRuntimeHost
                    hr = runtimeInfo->GetInterface(CLSID_CLRRuntimeHost, IID_ICLRRuntimeHost, (LPVOID*)&runtimeHost);
                    if (SUCCEEDED(hr) && runtimeHost) {
                        swprintf(msg, 512, L"[Inject] .NET Framework CLR Runtime Host obtained!");
                        Log(msg);
                    }
                    runtimeInfo->Release();
                }
                metaHost->Release();
            }
        }
        
        if (!runtimeHost) {
            FreeLibrary(mscoree);
        }
    }
    
    // If .NET Framework didn't work, try .NET Core (coreclr.dll)
    if (!runtimeHost) {
        swprintf(msg, 512, L"[Inject] Trying .NET Core (coreclr.dll)...");
        Log(msg);
        
        HMODULE coreclr = LoadLibrary(L"coreclr.dll");
        if (coreclr) {
            swprintf(msg, 512, L"[Inject] coreclr.dll loaded");
            Log(msg);
            
            // Get GetCLRRuntimeHost from coreclr.dll
            typedef HRESULT (STDAPICALLTYPE* FnGetCLRRuntimeHost)(REFIID riid, IUnknown** pUnk);
            FnGetCLRRuntimeHost pfnGetCLRRuntimeHost = (FnGetCLRRuntimeHost)GetProcAddress(coreclr, "GetCLRRuntimeHost");
            
            if (pfnGetCLRRuntimeHost) {
                swprintf(msg, 512, L"[Inject] GetCLRRuntimeHost found");
                Log(msg);
                
                HRESULT hr = pfnGetCLRRuntimeHost(IID_ICLRRuntimeHost, (IUnknown**)&runtimeHost);
                if (SUCCEEDED(hr) && runtimeHost) {
                    swprintf(msg, 512, L"[Inject] .NET Core CLR Runtime Host obtained!");
                    Log(msg);
                }
            }
            
            if (!runtimeHost) {
                FreeLibrary(coreclr);
            }
        }
    }
    
    if (!runtimeHost) {
        swprintf(msg, 512, L"[Inject] Failed to get CLR Runtime Host!");
        Log(msg);
        return false;
    }
    
    // Set environment variables for the agent
    SetEnvironmentVariable(L"WPFSPY_PIPE_NAME", pipeName);
    SetEnvironmentVariable(L"WPFSPY_AGENT_ENABLED", L"1");
    
    // ExecuteInDefaultAppDomain expects wide strings
    const wchar_t* dllPathW = agentDllPath.c_str();
    const wchar_t* typeNameW = L"WpfSpyAgent.SpyAgentHost";
    
    // Execute SpyAgentHost.Start() in the default app domain
    // The method will be called with the pipe name as argument
    DWORD exitCode = 0;
    HRESULT hr = runtimeHost->ExecuteInDefaultAppDomain(
        dllPathW,
        typeNameW,
        L"StartWithPipe",  // Method that takes pipe name as parameter
        pipeName,
        &exitCode);
    
    if (FAILED(hr)) {
        swprintf(msg, 512, L"[Inject] ExecuteInDefaultAppDomain failed: 0x%08X", hr);
        Log(msg);
        runtimeHost->Release();
        return false;
    }
    
    swprintf(msg, 512, L"[Inject] Spy Agent started via CLR Hosting! Exit code: %d", exitCode);
    Log(msg);
    
    // Clean up
    runtimeHost->Release();
    
    g_pipeName = pipeName;
    return true;
}

// Try to start the Spy Agent using .NET hosting
static bool TryStartSpyAgent(const wchar_t* pipeName) {
    if (g_agentStarted.exchange(true)) {
        return true; // Already started
    }
    
    g_pipeName = pipeName;
    
    // Allocate a buffer for the pipe name (must be valid until thread starts)
    size_t pipeLen = wcslen(pipeName) + 1;
    wchar_t* pipeCopy = (wchar_t*)malloc(pipeLen * sizeof(wchar_t));
    if (pipeCopy) {
        wcscpy_s(pipeCopy, pipeLen, pipeName);
    }
    
    Log(L"[Inject] Starting Spy Agent thread...");
    
    // Run agent startup in a separate thread
    g_agentThread = CreateThread(nullptr, 0, AgentThreadProc, pipeCopy, 0, nullptr);
    
    return g_agentThread != nullptr;
}

// ============================================================
// Exported function called by the injector
// This is called from RuntimeInjector.InjectAsync()
// ============================================================
extern "C" __declspec(dllexport) 
void __stdcall InjectAndStartAgent(const char* pipeName) {
    // Convert ANSI pipe name to wide string
    wchar_t widePipe[256];
    MultiByteToWideChar(CP_ACP, 0, pipeName, -1, widePipe, 256);
    
    wchar_t msg[256];
    swprintf(msg, 256, L"[Inject] InjectAndStartAgent called with pipe: %s", widePipe);
    Log(msg);
    
    // Try CLR Hosting first (for .NET Framework)
    if (TryStartSpyAgentCLR(widePipe)) {
        swprintf(msg, 256, L"[Inject] Agent started via CLR Hosting!");
        Log(msg);
        return;
    }
    
    // Fall back to environment variable approach
    swprintf(msg, 256, L"[Inject] CLR Hosting failed, using environment variables...");
    Log(msg);
    TryStartSpyAgent(widePipe);
}

// ============================================================
// Exported function for Snoop-style injection
// Called via CreateRemoteThread + GetProcAddress("ExecuteInDefaultAppDomain")
// Parameters: "dllPath|assemblyPath|typeName|methodName|pipeName"
// ============================================================
extern "C" __declspec(dllexport)
DWORD WINAPI ExecuteInDefaultAppDomain(LPCWSTR args) {
    wchar_t msg[512];
    swprintf(msg, 512, L"[Inject] ExecuteInDefaultAppDomain called with args: %s", args);
    Log(msg);
    
    // Parse the parameters (separated by |)
    std::wstring argsStr(args);
    std::wstring delimiter = L"|";
    
    size_t pos = 0;
    std::vector<std::wstring> tokens;
    while ((pos = argsStr.find(delimiter)) != std::wstring::npos) {
        tokens.push_back(argsStr.substr(0, pos));
        argsStr.erase(0, pos + delimiter.length());
    }
    tokens.push_back(argsStr);
    
    if (tokens.size() < 5) {
        swprintf(msg, 512, L"[Inject] Invalid arguments count: %d (expected 5)", tokens.size());
        Log(msg);
        return E_FAIL;
    }
    
    std::wstring& dllPath = tokens[0];
    std::wstring& assemblyPath = tokens[1];
    std::wstring& typeName = tokens[2];
    std::wstring& methodName = tokens[3];
    std::wstring& pipeName = tokens[4];
    
    swprintf(msg, 512, L"[Inject] Starting agent: %s.%s in %s", typeName.c_str(), methodName.c_str(), assemblyPath.c_str());
    Log(msg);
    
    // Use the agent thread approach with the pipe name
    if (TryStartSpyAgentCLR(pipeName.c_str())) {
        swprintf(msg, 512, L"[Inject] Agent started successfully!");
        Log(msg);
        return S_OK;
    }
    
    swprintf(msg, 512, L"[Inject] Failed to start agent via CLR Hosting");
    Log(msg);
    return E_FAIL;
}

// DllMain - called when our DLL is loaded/unloaded
BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved) {
    wchar_t msg[256];
    const wchar_t* reason;
    
    switch (ul_reason_for_call) {
        case DLL_PROCESS_ATTACH:
            reason = L"DLL_PROCESS_ATTACH";
            DisableThreadLibraryCalls(hModule);
            Log(L"[Inject] Native DLL loaded into target process!");
            
            // Try to auto-start the agent using CLR Hosting
            TryStartSpyAgentCLR(L"WPFSpyAgentPipe");
            break;
        case DLL_THREAD_ATTACH:
            reason = L"DLL_THREAD_ATTACH";
            break;
        case DLL_THREAD_DETACH:
            reason = L"DLL_THREAD_DETACH";
            break;
        case DLL_PROCESS_DETACH:
            reason = L"DLL_PROCESS_DETACH";
            Log(L"[Inject] Native DLL unloaded");
            break;
    }
    
    swprintf(msg, 256, L"[Inject] DllMain reason: %s", reason);
    Log(msg);
    
    return TRUE;
}
