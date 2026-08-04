using System;
using System.IO;

namespace TestHook
{
    public class Hook : AppDomainManager
    {
        public override void InitializeNewDomain(AppDomainSetup appDomainInfo)
        {
            base.InitializeNewDomain(appDomainInfo);
            File.WriteAllText(@"D:\testpgms\WPFTestAutoClaudeNew\WpfTestFramework\TestAppDomainManager\test_log.txt", "AppDomainManager initialized!");
        }
    }
}
