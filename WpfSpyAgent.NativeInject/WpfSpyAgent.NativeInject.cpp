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

// Check if coreclr.dll is loaded in this process (indicates .NET Core/5+)
static bool IsCoreClrLoaded() {
    HMODULE hCoreClr = GetModuleHandle(L"coreclr.dll");
    return hCoreClr != NULL;
}

// Get the handle to coreclr.dll if it's loaded
static HMODULE GetCoreClrHandle() {
    return GetModuleHandle(L"coreclr.dll");
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

    // Determine target framework BEFORE picking the agent DLL path. Otherwise
    // a stray net461\WpfSpyAgent.dll in a sibling folder causes us to use the
    // Framework build against a .NET Core app (or vice versa).
    bool isCoreClr = IsCoreClrLoaded();
    swprintf(msg, 512, L"[Inject] .NET Core runtime detected: %s", isCoreClr ? L"YES" : L"NO");
    Log(msg);

    // Determine target framework by checking for framework-specific DLLs.
    // For .NET Core / 5+: WpfSpyAgent.dll is in dllDir.
    // For .NET Framework 4.x: WpfSpyAgent.dll is in dllDir\net461\.
    std::wstring agentDllPath;
    std::wstring agentDllFwPath = dllDir + L"\\net461\\WpfSpyAgent.dll";  // .NET Framework 4.x
    std::wstring agentDllModernPath = dllDir + L"\\WpfSpyAgent.dll";      // .NET Core / 5+

    bool hasFwAgent     = (GetFileAttributes(agentDllFwPath.c_str())     != INVALID_FILE_ATTRIBUTES);
    bool hasModernAgent = (GetFileAttributes(agentDllModernPath.c_str()) != INVALID_FILE_ATTRIBUTES);

    if (isCoreClr) {
        // Prefer the modern build when targeting .NET Core. Fall back to net461
        // only if it is the only one present.
        agentDllPath = hasModernAgent ? agentDllModernPath : agentDllFwPath;
    } else {
        // .NET Framework target — prefer net461 subfolder, fall back to root.
        agentDllPath = hasFwAgent ? agentDllFwPath : agentDllModernPath;
    }

    swprintf(msg, 512, L"[Inject] Looking for Spy Agent DLL: %s", agentDllPath.c_str());
    Log(msg);

    if (GetFileAttributes(agentDllPath.c_str()) == INVALID_FILE_ATTRIBUTES) {
        swprintf(msg, 512, L"[Inject] Spy Agent DLL not found at: %s", agentDllPath.c_str());
        Log(msg);
        return false;
    }
    
    ICLRRuntimeHost* runtimeHost = nullptr;
    
    // For .NET Core apps, we need to use coreclr.dll that's already loaded
    if (isCoreClr) {
        swprintf(msg, 512, L"[Inject] Using coreclr.dll from loaded runtime...");
        Log(msg);
        
        HMODULE hCoreClr = GetCoreClrHandle();
        if (hCoreClr) {
            // Get GetCLRRuntimeHost from coreclr.dll
            typedef HRESULT (STDAPICALLTYPE* FnGetCLRRuntimeHost)(REFIID riid, IUnknown** pUnk);
            FnGetCLRRuntimeHost pfnGetCLRRuntimeHost = (FnGetCLRRuntimeHost)GetProcAddress(hCoreClr, "GetCLRRuntimeHost");
            
            if (pfnGetCLRRuntimeHost) {
                swprintf(msg, 512, L"[Inject] GetCLRRuntimeHost found in coreclr.dll");
                Log(msg);
                
                HRESULT hr = pfnGetCLRRuntimeHost(IID_ICLRRuntimeHost, (IUnknown**)&runtimeHost);
                if (SUCCEEDED(hr) && runtimeHost) {
                    swprintf(msg, 512, L"[Inject] ICLRRuntimeHost obtained from coreclr.dll!");
                    Log(msg);
                } else {
                    swprintf(msg, 512, L"[Inject] GetCLRRuntimeHost failed: 0x%08X", hr);
                    Log(msg);
                }
            } else {
                DWORD err = GetLastError();
                swprintf(msg, 512, L"[Inject] GetProcAddress failed for GetCLRRuntimeHost: %u", err);
                Log(msg);
            }
        }
        
        if (!runtimeHost) {
            swprintf(msg, 512, L"[Inject] Failed to get runtime host from coreclr.dll");
            Log(msg);
            return false;
        }
        
        // For .NET Core, we need to start the runtime if not already started
        // and then execute the managed code
        swprintf(msg, 512, L"[Inject] Attempting to execute in .NET Core runtime...");
        Log(msg);
        
        // Set environment variables for the agent
        SetEnvironmentVariable(L"WPFSPY_PIPE_NAME", pipeName);
        SetEnvironmentVariable(L"WPFSPY_AGENT_ENABLED", L"1");
        
        // IMPORTANT: ExecuteInDefaultAppDomain can block in .NET Core, which freezes the app.
        // To avoid this, we spawn a dedicated thread to call ExecuteInDefaultAppDomain.
        // This thread will remain alive as long as the Spy Agent needs to run.
        
        swprintf(msg, 512, L"[Inject] Spawning thread for ExecuteInDefaultAppDomain...");
        Log(msg);
        
        // Store the parameters we need to pass to the thread
        g_pipeName = pipeName;
        
        // Create a thread that will call ExecuteInDefaultAppDomain
        // This thread will run the Spy Agent and stay alive
        HANDLE execThread = CreateThread(
            NULL,  // default security
            0,     // default stack size
            [](LPVOID lpParam) -> DWORD {
                wchar_t msg[512];
                swprintf(msg, 512, L"[Inject] Execute thread started");
                Log(msg);
                
                ICLRRuntimeHost* host = (ICLRRuntimeHost*)lpParam;
                
                // Get the agent DLL path. Use the same TFM-aware selection as
                // TryStartSpyAgentCLR: .NET Core -> dllDir\WpfSpyAgent.dll,
                // .NET Framework -> dllDir\net461\WpfSpyAgent.dll.
                std::wstring dllDir = GetDllDirectory();
                std::wstring agentDllFwPath     = dllDir + L"\\net461\\WpfSpyAgent.dll";
                std::wstring agentDllModernPath = dllDir + L"\\WpfSpyAgent.dll";
                bool hasFwAgent     = (GetFileAttributes(agentDllFwPath.c_str())     != INVALID_FILE_ATTRIBUTES);
                bool hasModernAgent = (GetFileAttributes(agentDllModernPath.c_str()) != INVALID_FILE_ATTRIBUTES);
                bool coreClr = IsCoreClrLoaded();
                std::wstring agentDllPath = coreClr
                    ? (hasModernAgent ? agentDllModernPath : agentDllFwPath)
                    : (hasFwAgent     ? agentDllFwPath     : agentDllModernPath);

                DWORD exitCode = 0;
                HRESULT hr = host->ExecuteInDefaultAppDomain(
                    agentDllPath.c_str(),
                    L"WpfSpyAgent.SpyAgentHost",
                    L"StartWithPipe",
                    g_pipeName.c_str(),
                    &exitCode);
                
                if (SUCCEEDED(hr)) {
                    swprintf(msg, 512, L"[Inject] ExecuteInDefaultAppDomain succeeded, exit code: %d", exitCode);
                } else {
                    swprintf(msg, 512, L"[Inject] ExecuteInDefaultAppDomain failed: 0x%08X", hr);
                }
                Log(msg);
                
                host->Release();
                return 0;
            },
            (LPVOID)runtimeHost,  // pass runtimeHost as parameter
            0,  // start immediately
            NULL
        );
        
        if (execThread) {
            swprintf(msg, 512, L"[Inject] Execute thread spawned successfully");
            Log(msg);
            // Don't wait for thread - let it run independently
            // The thread holds a reference to runtimeHost so it won't be garbage collected
            CloseHandle(execThread);
            return true;
        } else {
            DWORD err = GetLastError();
            swprintf(msg, 512, L"[Inject] Failed to create execute thread: %u", err);
            Log(msg);
            runtimeHost->Release();
            return false;
        }
    }
    
    // For .NET Framework apps, continue with mscoree.dll approach
    swprintf(msg, 512, L"[Inject] Using mscoree.dll for .NET Framework...");
    Log(msg);
    
    // Try .NET Framework first (mscoree.dll) - more common for WPF apps
    swprintf(msg, 512, L"[Inject] Trying .NET Framework (mscoree.dll)...");
    Log(msg);
    
    HMODULE mscoree = LoadLibrary(L"mscoree.dll");
    if (!mscoree) {
        DWORD err = GetLastError();
        swprintf(msg, 512, L"[Inject] FAILED: mscoree.dll not found, error=%u", err);
        Log(msg);
    } else {
        swprintf(msg, 512, L"[Inject] mscoree.dll loaded");
        Log(msg);
        
        // Get CLRCreateInstance function
        typedef HRESULT (STDAPICALLTYPE* FnCLRCreateInstance)(REFCLSID clsid, REFIID riid, LPVOID* ppInterface);
        FnCLRCreateInstance CLRCreateInstance = (FnCLRCreateInstance)GetProcAddress(mscoree, "CLRCreateInstance");
        
        if (!CLRCreateInstance) {
            DWORD err = GetLastError();
            swprintf(msg, 512, L"[Inject] FAILED: CLRCreateInstance not found, error=%u", err);
            Log(msg);
        } else {
            // Get ICLRMetaHost
            ICLRMetaHost* metaHost = nullptr;
            HRESULT hr = CLRCreateInstance(CLSID_CLRMetaHost, IID_ICLRMetaHost, (LPVOID*)&metaHost);
            if (FAILED(hr) || !metaHost) {
                swprintf(msg, 512, L"[Inject] FAILED: CLRCreateInstance result=0x%08X", hr);
                Log(msg);
            } else {
                swprintf(msg, 512, L"[Inject] ICLRMetaHost obtained");
                Log(msg);
                
                ICLRRuntimeInfo* runtimeInfo = nullptr;
                
                // CRITICAL: For .NET Core/.NET 5+, we MUST use the runtime that's 
                // ALREADY running in the process, not load a new one.
                // GetVersionFromFile returns v4.0.30319 for ALL DLLs for compatibility,
                // but the actual runtime in the process might be .NET Core 6/7/8.
                
                // Method 1: Enumerate loaded runtimes FIRST - this is the correct approach
                // because we want to use the runtime that's already running
                IEnumUnknown* enumRuntimes = nullptr;
                hr = metaHost->EnumerateLoadedRuntimes(GetCurrentProcess(), &enumRuntimes);
                if (SUCCEEDED(hr) && enumRuntimes) {
                    swprintf(msg, 512, L"[Inject] Enumerating loaded runtimes...");
                    Log(msg);
                    
                    IUnknown* enumItem = nullptr;
                    ULONG fetched = 0;
                    while (enumRuntimes->Next(1, &enumItem, &fetched) == S_OK && fetched == 1) {
                        hr = enumItem->QueryInterface(IID_ICLRRuntimeInfo, (LPVOID*)&runtimeInfo);
                        enumItem->Release();
                        
                        if (SUCCEEDED(hr) && runtimeInfo) {
                            // Get the version string
                            wchar_t version[256] = {0};
                            DWORD versionLen = 256;
                            runtimeInfo->GetVersionString(version, &versionLen);
                            swprintf(msg, 512, L"[Inject] Found loaded runtime: %s", version);
                            Log(msg);
                            
                            // Check if this runtime is compatible
                            BOOL loadable = FALSE;
                            runtimeInfo->IsLoadable(&loadable);
                            swprintf(msg, 512, L"[Inject] Runtime loadable: %d", loadable);
                            Log(msg);
                            
                            if (loadable) {
                                // Try to get ICLRRuntimeHost
                                hr = runtimeInfo->GetInterface(CLSID_CLRRuntimeHost, IID_ICLRRuntimeHost, (LPVOID*)&runtimeHost);
                                if (SUCCEEDED(hr) && runtimeHost) {
                                    swprintf(msg, 512, L"[Inject] CLR Runtime Host obtained from loaded runtime!");
                                    Log(msg);
                                    runtimeInfo->Release();
                                    enumRuntimes->Release();
                                    metaHost->Release();
                                    // Execute the Spy Agent
                                    SetEnvironmentVariable(L"WPFSPY_PIPE_NAME", pipeName);
                                    SetEnvironmentVariable(L"WPFSPY_AGENT_ENABLED", L"1");
                                    DWORD exitCode = 0;
                                    hr = runtimeHost->ExecuteInDefaultAppDomain(
                                        agentDllPath.c_str(),
                                        L"WpfSpyAgent.SpyAgentHost",
                                        L"StartWithPipe",
                                        pipeName,
                                        &exitCode);
                                    if (SUCCEEDED(hr)) {
                                        swprintf(msg, 512, L"[Inject] Spy Agent started! Exit code: %d", exitCode);
                                        Log(msg);
                                    } else {
                                        swprintf(msg, 512, L"[Inject] ExecuteInDefaultAppDomain failed: 0x%08X", hr);
                                        Log(msg);
                                    }
                                    runtimeHost->Release();
                                    g_pipeName = pipeName;
                                    return true; // Success!
                                }
                            }
                            runtimeInfo->Release();
                            runtimeInfo = nullptr;
                        }
                    }
                    enumRuntimes->Release();
                }
                
                // Method 2: Try GetVersionFromFile (less reliable for .NET Core)
                wchar_t loadedVersion[256] = {0};
                DWORD versionLen = 256;
                hr = metaHost->GetVersionFromFile(agentDllPath.c_str(), loadedVersion, &versionLen);
                if (SUCCEEDED(hr) && loadedVersion[0]) {
                    swprintf(msg, 512, L"[Inject] DLL metadata version: %s", loadedVersion);
                    Log(msg);
                    hr = metaHost->GetRuntime(loadedVersion, IID_ICLRRuntimeInfo, (LPVOID*)&runtimeInfo);
                    if (SUCCEEDED(hr) && runtimeInfo) {
                        swprintf(msg, 512, L"[Inject] Got runtime from DLL file");
                        Log(msg);
                        
                        // Check if loadable
                        BOOL loadable = FALSE;
                        runtimeInfo->IsLoadable(&loadable);
                        if (loadable) {
                            hr = runtimeInfo->GetInterface(CLSID_CLRRuntimeHost, IID_ICLRRuntimeHost, (LPVOID*)&runtimeHost);
                            if (SUCCEEDED(hr) && runtimeHost) {
                                swprintf(msg, 512, L"[Inject] CLR Runtime Host obtained!");
                                Log(msg);
                                runtimeInfo->Release();
                                metaHost->Release();
                                // Execute the Spy Agent
                                SetEnvironmentVariable(L"WPFSPY_PIPE_NAME", pipeName);
                                SetEnvironmentVariable(L"WPFSPY_AGENT_ENABLED", L"1");
                                DWORD exitCode = 0;
                                hr = runtimeHost->ExecuteInDefaultAppDomain(
                                    agentDllPath.c_str(),
                                    L"WpfSpyAgent.SpyAgentHost",
                                    L"StartWithPipe",
                                    pipeName,
                                    &exitCode);
                                if (SUCCEEDED(hr)) {
                                    swprintf(msg, 512, L"[Inject] Spy Agent started! Exit code: %d", exitCode);
                                    Log(msg);
                                } else {
                                    swprintf(msg, 512, L"[Inject] ExecuteInDefaultAppDomain failed: 0x%08X", hr);
                                    Log(msg);
                                }
                                runtimeHost->Release();
                                g_pipeName = pipeName;
                                return true;
                            }
                        }
                        runtimeInfo->Release();
                        runtimeInfo = nullptr;
                    }
                }
                
                // Method 3: Last resort - try v4.0.30319
                swprintf(msg, 512, L"[Inject] Trying v4.0.30319 as last resort...");
                Log(msg);
                hr = metaHost->GetRuntime(L"v4.0.30319", IID_ICLRRuntimeInfo, (LPVOID*)&runtimeInfo);
                if (SUCCEEDED(hr) && runtimeInfo) {
                    BOOL loadable = FALSE;
                    runtimeInfo->IsLoadable(&loadable);
                    if (loadable) {
                        hr = runtimeInfo->GetInterface(CLSID_CLRRuntimeHost, IID_ICLRRuntimeHost, (LPVOID*)&runtimeHost);
                        if (SUCCEEDED(hr) && runtimeHost) {
                            swprintf(msg, 512, L"[Inject] CLR Runtime Host obtained (v4.0 fallback)!");
                            Log(msg);
                            runtimeInfo->Release();
                            metaHost->Release();
                            // Execute the Spy Agent
                            SetEnvironmentVariable(L"WPFSPY_PIPE_NAME", pipeName);
                            SetEnvironmentVariable(L"WPFSPY_AGENT_ENABLED", L"1");
                            DWORD exitCode = 0;
                            hr = runtimeHost->ExecuteInDefaultAppDomain(
                                agentDllPath.c_str(),
                                L"WpfSpyAgent.SpyAgentHost",
                                L"StartWithPipe",
                                pipeName,
                                &exitCode);
                            if (SUCCEEDED(hr)) {
                                swprintf(msg, 512, L"[Inject] Spy Agent started! Exit code: %d", exitCode);
                                Log(msg);
                            } else {
                                swprintf(msg, 512, L"[Inject] ExecuteInDefaultAppDomain failed: 0x%08X", hr);
                                Log(msg);
                            }
                            runtimeHost->Release();
                            g_pipeName = pipeName;
                            return true;
                        }
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
    
    // If we reach here without starting the agent, no suitable runtime was found
    swprintf(msg, 512, L"[Inject] No suitable runtime found to host the Spy Agent");
    Log(msg);
    return false;
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
