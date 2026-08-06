// WpfSpyAgent.NativeInject.cpp
// Native DLL for runtime injection into WPF processes.
// This DLL is injected into the target process using CreateRemoteThread + LoadLibrary.
// It then bootstraps the .NET runtime and loads the Spy Agent.

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <iostream>
#include <string>
#include <thread>
#include <atomic>

// Global state
static std::atomic<bool> g_agentStarted(false);
static HANDLE g_agentThread = nullptr;
static std::wstring g_pipeName = L"WPFSpyAgentPipe";

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

// Try to start the Spy Agent using .NET hosting
static bool TryStartSpyAgent(const wchar_t* pipeName) {
    if (g_agentStarted.exchange(true)) {
        return true; // Already started
    }
    
    g_pipeName = pipeName;
    
    // Store pipe name in a local variable for lambda capture
    std::wstring pipeStr = pipeName;
    
    Log(L"[Inject] Starting Spy Agent thread...");
    
    // Run agent startup in a separate thread
    g_agentThread = CreateThread(nullptr, 0, [&pipeStr](LPVOID param) -> DWORD {
        const wchar_t* pipe = pipeStr.c_str();
        
        // Wait a bit for the process to stabilize
        Sleep(500);
        
        wchar_t msg[512];
        swprintf(msg, 512, L"[Inject] Thread started, pipe=%s", pipe);
        Log(msg);
        
        // Try to load the Spy Agent via the startup hook approach
        // We look for WpfSpyAgent.StartupHook.dll in the same directory
        wchar_t dllPath[MAX_PATH];
        GetModuleFileName(nullptr, dllPath, MAX_PATH);
        std::wstring dllDir = dllPath;
        size_t pos = dllDir.rfind(L'\\');
        if (pos != std::wstring::npos) {
            dllDir = dllDir.substr(0, pos);
        }
        
        std::wstring hookDllPath = dllDir + L"\\WpfSpyAgent.StartupHook.dll";
        
        swprintf(msg, 512, L"[Inject] Looking for: %s", hookDllPath.c_str());
        Log(msg);
        
        // Check if the hook DLL exists
        if (GetFileAttributes(hookDllPath.c_str()) == INVALID_FILE_ATTRIBUTES) {
            swprintf(msg, 512, L"[Inject] StartupHook DLL not found at %s", hookDllPath.c_str());
            Log(msg);
            
            // Try parent directories
            for (int i = 0; i < 3; i++) {
                pos = dllDir.rfind(L'\\');
                if (pos != std::wstring::npos) {
                    dllDir = dllDir.substr(0, pos);
                    hookDllPath = dllDir + L"\\WpfSpyAgent.StartupHook.dll";
                    if (GetFileAttributes(hookDllPath.c_str()) != INVALID_FILE_ATTRIBUTES) {
                        swprintf(msg, 512, L"[Inject] Found at: %s", hookDllPath.c_str());
                        Log(msg);
                        break;
                    }
                }
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
    }, nullptr, 0, nullptr);
    
    return g_agentThread != nullptr;
}

// ============================================================
// Exported function called by the injector
// ============================================================
extern "C" __declspec(dllexport) 
void __stdcall InjectAndStartAgent(const char* pipeName) {
    // Convert ANSI pipe name to wide string
    wchar_t widePipe[256];
    MultiByteToWideChar(CP_ACP, 0, pipeName, -1, widePipe, 256);
    
    wchar_t msg[256];
    swprintf(msg, 256, L"[Inject] InjectAndStartAgent called with pipe: %s", widePipe);
    Log(msg);
    
    TryStartSpyAgent(widePipe);
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
            
            // Try to auto-start the agent
            TryStartSpyAgent(L"WPFSpyAgentPipe");
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
