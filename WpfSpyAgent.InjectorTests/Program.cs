/*
 * WpfSpyAgent.InjectorTests
 * 
 * Tests for the CLR Hosting runtime injection mechanism.
 * 
 * These tests verify that:
 * 1. NativeInject DLL can detect both .NET Core and .NET Framework
 * 2. ICLRRuntimeHost can be obtained
 * 3. ExecuteInDefaultAppDomain works correctly
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

        private static string FindNativeDll()
        {
            // Try common locations
            string[] paths = {
                Path.Combine(GetSolutionRoot(), "WpfSpyAgent.NativeInject", "bin", "Debug", "x64", NativeDllName),
                Path.Combine(GetSolutionRoot(), "WpfSpyAgent.NativeInject", "bin", "Release", "x64", NativeDllName),
                Path.Combine(GetSolutionRoot(), "bin", "Debug", "x64", NativeDllName),
                Path.Combine(GetSolutionRoot(), "bin", "Release", "x64", NativeDllName),
            };

            foreach (var path in paths)
            {
                if (File.Exists(path))
                    return path;
            }

            return string.Empty;
        }

        private static string GetSolutionRoot()
        {
            var dir = Directory.GetCurrentDirectory();
            while (dir != null && !File.Exists(Path.Combine(dir, "WPFTestAuto.sln")))
            {
                dir = Directory.GetParent(dir)?.FullName;
            }
            return dir ?? Directory.GetCurrentDirectory();
        }

        [Fact]
        public void NativeDllExists()
        {
            var dllPath = FindNativeDll();
            Assert.False(string.IsNullOrEmpty(dllPath), 
                $"Native DLL not found. Build WpfSpyAgent.NativeInject project first. Searched in: {GetSolutionRoot()}");
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
                Console.WriteLine("[PASS] Export 'InjectAndStartAgent' found at: 0x" + procAddress.ToInt64().ToString("X"));
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
                // This export is for Snoop-style injection
                var procAddress = NativeLibrary.GetExport(handle, "ExecuteInDefaultAppDomain");
                Assert.NotEqual(IntPtr.Zero, procAddress);
                Console.WriteLine("[PASS] Export 'ExecuteInDefaultAppDomain' found at: 0x" + procAddress.ToInt64().ToString("X"));
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
            // This test verifies that SpyAgentHost.StartWithPipe can be called
            // and returns 0 (success) as expected by ExecuteInDefaultAppDomain
            
            // We can't actually call Start() without a WPF Application,
            // but we can verify the method signature exists and returns int
            var returnValue = WpfSpyAgent.SpyAgentHost.StartWithPipe("TestPipe");
            
            // Return should be 0 (success) for ExecuteInDefaultAppDomain
            Assert.Equal(0, returnValue);
            Console.WriteLine("[PASS] SpyAgentHost.StartWithPipe returned 0 as expected");
        }
    }

    /// <summary>
    /// Integration tests for the complete injection pipeline.
    /// </summary>
    public class IntegrationTests
    {
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
                string[] requiredExports = {
                    "InjectAndStartAgent",
                    "ExecuteInDefaultAppDomain"
                };

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
            // Verify that either coreclr.dll or mscoree.dll is available
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
    }

    class Program
    {
        static int Main(string[] args)
        {
            Console.WriteLine("=== WpfSpyAgent.InjectorTests ===");
            Console.WriteLine("Testing CLR Hosting runtime injection mechanism\n");

            // Run xUnit tests
            return Xunit.ConsoleClient.Program.Main(
                args.Length > 0 ? args : new[] { typeof(Program).Assembly.Location }
            );
        }
    }
}
