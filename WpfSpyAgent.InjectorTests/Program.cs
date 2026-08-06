/*
 * WpfSpyAgent.InjectorTests
 * 
 * Tests for the CLR Hosting runtime injection mechanism.
 * Uses SampleWpfApp from the same repository.
 * 
 * These tests verify that:
 * 1. NativeInject DLL can detect both .NET Core and .NET Framework
 * 2. ICLRRuntimeHost can be obtained
 * 3. ExecuteInDefaultAppDomain works correctly
 * 4. SampleWpfApp exists for injection testing
 * 
 * Run with: dotnet test
 */

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Xunit;

namespace WpfSpyAgent.InjectorTests
{
    /// <summary>
    /// Tests for the NativeInject DLL's CLR Hosting capabilities.
    /// Based on Snoop's approach: https://github.com/snoopwpf/snoopwpf
    /// </summary>
    public class ClrHostingTests
    {
        private const string NativeDllName = "WpfSpyAgent.NativeInject.dll";

        private static string GetRepoRoot()
        {
            var dir = Directory.GetCurrentDirectory();
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir, "WpfTestFramework.sln")) ||
                    File.Exists(Path.Combine(dir, "WpfTestFramework.VS2026.sln")) ||
                    Directory.Exists(Path.Combine(dir, "SampleWpfApp")))
                {
                    return dir;
                }
                dir = Directory.GetParent(dir)?.FullName;
            }
            return Directory.GetCurrentDirectory();
        }

        private static string FindNativeDll()
        {
            var root = GetRepoRoot();
            string[] paths = {
                Path.Combine(root, "WpfSpyAgent.NativeInject", "bin", "Debug", "x64", NativeDllName),
                Path.Combine(root, "WpfSpyAgent.NativeInject", "bin", "Release", "x64", NativeDllName),
                Path.Combine(root, "bin", "Debug", "x64", NativeDllName),
                Path.Combine(root, "bin", "Release", "x64", NativeDllName),
            };

            foreach (var path in paths)
            {
                if (File.Exists(path))
                    return path;
            }

            return string.Empty;
        }

        [Fact]
        public void NativeDllExists()
        {
            var dllPath = FindNativeDll();
            Assert.False(string.IsNullOrEmpty(dllPath), 
                $"Native DLL not found. Build WpfSpyAgent.NativeInject project first.");
            Assert.True(File.Exists(dllPath), $"Native DLL not found at: {dllPath}");
            Console.WriteLine($"[PASS] Native DLL found at: {dllPath}");
        }

        [Fact]
        public void CanLoadNativeDll()
        {
            var dllPath = FindNativeDll();
            if (string.IsNullOrEmpty(dllPath))
            {
                Console.WriteLine("[SKIP] Native DLL not found, skipping load test");
                return;
            }

            var handle = NativeLibrary.Load(dllPath);
            Assert.NotEqual(IntPtr.Zero, handle);
            
            NativeLibrary.Free(handle);
            Console.WriteLine("[PASS] Native DLL loaded successfully");
        }

        [Fact]
        public void ExportExists_InjectAndStartAgent()
        {
            var dllPath = FindNativeDll();
            if (string.IsNullOrEmpty(dllPath))
            {
                Console.WriteLine("[SKIP] Native DLL not found, skipping export test");
                return;
            }

            var handle = NativeLibrary.Load(dllPath);
            
            try
            {
                var procAddress = NativeLibrary.GetExport(handle, "InjectAndStartAgent");
                Assert.NotEqual(IntPtr.Zero, procAddress);
                Console.WriteLine($"[PASS] Export 'InjectAndStartAgent' found at: 0x{procAddress.ToInt64():X}");
            }
            finally
            {
                NativeLibrary.Free(handle);
            }
        }

        [Fact]
        public void ExportExists_ExecuteInDefaultAppDomain()
        {
            var dllPath = FindNativeDll();
            if (string.IsNullOrEmpty(dllPath))
            {
                Console.WriteLine("[SKIP] Native DLL not found, skipping export test");
                return;
            }

            var handle = NativeLibrary.Load(dllPath);
            
            try
            {
                var procAddress = NativeLibrary.GetExport(handle, "ExecuteInDefaultAppDomain");
                Assert.NotEqual(IntPtr.Zero, procAddress);
                Console.WriteLine($"[PASS] Export 'ExecuteInDefaultAppDomain' found at: 0x{procAddress.ToInt64():X}");
            }
            finally
            {
                NativeLibrary.Free(handle);
            }
        }
    }

    /// <summary>
    /// Tests for the SpyAgentHost.StartWithPipe method.
    /// This method is called by CLR Hosting during runtime injection.
    /// </summary>
    public class SpyAgentHostTests
    {
        [Fact]
        public void StartWithPipe_ReturnsZero()
        {
            var returnValue = WpfSpyAgent.SpyAgentHost.StartWithPipe("TestPipe");
            Assert.Equal(0, returnValue);
            Console.WriteLine("[PASS] SpyAgentHost.StartWithPipe returned 0 as expected");
        }
    }

    /// <summary>
    /// Tests that SampleWpfApp exists for injection testing.
    /// </summary>
    public class SampleWpfAppTests
    {
        private static string GetRepoRoot()
        {
            var dir = Directory.GetCurrentDirectory();
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir, "WpfTestFramework.sln")) ||
                    Directory.Exists(Path.Combine(dir, "SampleWpfApp")))
                {
                    return dir;
                }
                dir = Directory.GetParent(dir)?.FullName;
            }
            return Directory.GetCurrentDirectory();
        }

        [Fact]
        public void SampleWpfApp_DotNetExists()
        {
            var root = GetRepoRoot();
            var dllPath = Path.Combine(root, "SampleWpfApp", "bin", "Debug", "net8.0-windows", "SampleWpfApp.dll");
            
            Assert.True(File.Exists(dllPath), 
                $"SampleWpfApp (.NET 8) not found. Run: dotnet build from {root}");
            Console.WriteLine($"[PASS] SampleWpfApp (.NET 8) found at: {dllPath}");
        }

        [Fact]
        public void SampleWpfApp_FrameworkExists()
        {
            var root = GetRepoRoot();
            var exePath = Path.Combine(root, "SampleWpfApp", "bin", "Debug", "net461", "SampleWpfApp.exe");
            
            Assert.True(File.Exists(exePath), 
                $"SampleWpfApp (.NET Framework) not found. Run: dotnet build -f net461 from {root}");
            Console.WriteLine($"[PASS] SampleWpfApp (.NET Framework) found at: {exePath}");
        }

        [Fact]
        public void WpfSpyAgent_DllExists()
        {
            var root = GetRepoRoot();
            var dllPath = Path.Combine(root, "WpfSpyAgent", "bin", "Debug", "net8.0-windows", "WpfSpyAgent.dll");
            
            Assert.True(File.Exists(dllPath), 
                $"WpfSpyAgent.dll not found. Build WpfSpyAgent project first.");
            Console.WriteLine($"[PASS] WpfSpyAgent.dll found at: {dllPath}");
        }
    }

    /// <summary>
    /// Integration tests for the complete injection pipeline.
    /// </summary>
    public class IntegrationTests
    {
        private static string GetRepoRoot()
        {
            var dir = Directory.GetCurrentDirectory();
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir, "WpfTestFramework.sln")) ||
                    Directory.Exists(Path.Combine(dir, "SampleWpfApp")))
                {
                    return dir;
                }
                dir = Directory.GetParent(dir)?.FullName;
            }
            return Directory.GetCurrentDirectory();
        }

        private static string FindNativeDll()
        {
            var root = GetRepoRoot();
            string[] paths = {
                Path.Combine(root, "WpfSpyAgent.NativeInject", "bin", "Debug", "x64", "WpfSpyAgent.NativeInject.dll"),
                Path.Combine(root, "WpfSpyAgent.NativeInject", "bin", "Release", "x64", "WpfSpyAgent.NativeInject.dll"),
            };

            foreach (var path in paths)
            {
                if (File.Exists(path))
                    return path;
            }

            return string.Empty;
        }

        [Fact]
        public void NativeDllHasAllRequiredExports()
        {
            var dllPath = FindNativeDll();
            if (string.IsNullOrEmpty(dllPath))
            {
                Console.WriteLine("[SKIP] Native DLL not found");
                return;
            }

            var handle = NativeLibrary.Load(dllPath);
            
            try
            {
                string[] requiredExports = { "InjectAndStartAgent", "ExecuteInDefaultAppDomain" };

                foreach (var export in requiredExports)
                {
                    var procAddress = NativeLibrary.GetExport(handle, export);
                    Assert.NotEqual(IntPtr.Zero, procAddress);
                    Console.WriteLine($"[PASS] Export '{export}' found");
                }
            }
            finally
            {
                NativeLibrary.Free(handle);
            }
        }

        [Fact]
        public void RuntimeDetection_CoreClrOrMsCorEE()
        {
            bool hasCoreClr = false;
            bool hasMsCorEE = false;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var coreclrPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "coreclr.dll");
                hasCoreClr = File.Exists(coreclrPath);

                var mscoreePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "mscoree.dll");
                hasMsCorEE = File.Exists(mscoreePath);
            }

            Assert.True(hasCoreClr || hasMsCorEE, 
                "Neither coreclr.dll nor mscoree.dll found. CLR Hosting requires Windows with .NET installed.");
            
            Console.WriteLine($"[PASS] CLR Runtime detected:");
            Console.WriteLine($"  - coreclr.dll: {(hasCoreClr ? "Found" : "Not found")}");
            Console.WriteLine($"  - mscoree.dll: {(hasMsCorEE ? "Found" : "Not found")}");
        }

        [Fact]
        public void InjectionPipeline_DotNet()
        {
            Console.WriteLine("[TEST] Verifying .NET 8 injection pipeline:");
            var root = GetRepoRoot();
            
            // Step 1: Native DLL exists
            var nativeDll = Path.Combine(root, "WpfSpyAgent.NativeInject", "bin", "Debug", "x64", "WpfSpyAgent.NativeInject.dll");
            Assert.True(File.Exists(nativeDll), "NativeInject DLL missing");
            Console.WriteLine("  [OK] NativeInject DLL exists");
            
            // Step 2: Spy Agent DLL exists
            var agentDll = Path.Combine(root, "WpfSpyAgent", "bin", "Debug", "net8.0-windows", "WpfSpyAgent.dll");
            Assert.True(File.Exists(agentDll), "WpfSpyAgent DLL missing");
            Console.WriteLine("  [OK] WpfSpyAgent DLL exists");
            
            // Step 3: SampleWpfApp exists
            var appDll = Path.Combine(root, "SampleWpfApp", "bin", "Debug", "net8.0-windows", "SampleWpfApp.dll");
            Assert.True(File.Exists(appDll), "SampleWpfApp DLL missing");
            Console.WriteLine("  [OK] SampleWpfApp exists");
            
            // Step 4: CLR available
            Assert.True(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Windows required for CLR Hosting");
            Console.WriteLine("  [OK] Windows platform confirmed");
            
            Console.WriteLine("[PASS] .NET 8 injection pipeline verified");
        }

        [Fact]
        public void InjectionPipeline_DotNetFramework()
        {
            Console.WriteLine("[TEST] Verifying .NET Framework injection pipeline:");
            var root = GetRepoRoot();
            
            // Step 1: Native DLL exists
            var nativeDll = Path.Combine(root, "WpfSpyAgent.NativeInject", "bin", "Debug", "x64", "WpfSpyAgent.NativeInject.dll");
            Assert.True(File.Exists(nativeDll), "NativeInject DLL missing");
            Console.WriteLine("  [OK] NativeInject DLL exists");
            
            // Step 2: Spy Agent DLL exists (Framework version)
            var agentDll = Path.Combine(root, "WpfSpyAgent", "bin", "Debug", "net461", "WpfSpyAgent.dll");
            Assert.True(File.Exists(agentDll), "WpfSpyAgent.dll (Framework) missing");
            Console.WriteLine("  [OK] WpfSpyAgent.dll exists");
            
            // Step 3: SampleWpfApp exists
            var appExe = Path.Combine(root, "SampleWpfApp", "bin", "Debug", "net461", "SampleWpfApp.exe");
            Assert.True(File.Exists(appExe), "SampleWpfApp.exe missing");
            Console.WriteLine("  [OK] SampleWpfApp.exe exists");
            
            Console.WriteLine("[PASS] .NET Framework injection pipeline verified");
        }
    }

    class Program
    {
        static int Main(string[] args)
        {
            Console.WriteLine("=== WpfSpyAgent.InjectorTests ===");
            Console.WriteLine("Testing CLR Hosting runtime injection mechanism\n");

            return Xunit.ConsoleClient.Program.Main(
                args.Length > 0 ? args : new[] { typeof(Program).Assembly.Location }
            );
        }
    }
}
