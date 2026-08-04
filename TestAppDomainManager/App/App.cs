using System;
using System.IO;

namespace TestApp
{
    class Program
    {
        static void Main()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string configFile = AppDomain.CurrentDomain.SetupInformation.ConfigurationFile;
            
            File.WriteAllText(@"D:\testpgms\WPFTestAutoClaudeNew\WpfTestFramework\TestAppDomainManager\diag_log.txt", 
                $"BaseDirectory: {baseDir}{Environment.NewLine}ConfigFile: {configFile}{Environment.NewLine}DomainManager: {AppDomain.CurrentDomain.DomainManager?.GetType().FullName ?? "null"}{Environment.NewLine}");
            
            Console.WriteLine("BaseDirectory: " + baseDir);
            Console.WriteLine("ConfigFile: " + configFile);
            Console.WriteLine("DomainManager: " + (AppDomain.CurrentDomain.DomainManager?.GetType().FullName ?? "null"));
            Console.WriteLine("Press any key...");
            Console.Read();
        }
    }
}
