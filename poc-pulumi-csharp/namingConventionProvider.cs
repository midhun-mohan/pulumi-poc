using System;

namespace pulumi_poc_midhun
{
    public class NamingConventionProvider
    {
        private const string owner = "midhun";
        
        public static string Resourcegroup(string stack)
        {
            return $"{owner}-{stack}-new-rg";
        }

        public static string StorageAccount(string stack)
        {
            return $"{owner}{stack}newsa";
        }

        public static string Keyvault(string stack)
        {
            return $"{owner}-{stack}-new-vault";
        }
        
        public static string GetServiceBusNamespace(string stack)
        {
            return $"{owner}-{stack}-sbns";
        }
        
        public static string GetServiceBusQueue(string stack)
        {
            return $"{owner}-{stack}-sbqueue";
        }
        
        public static string GetServiceBusAuthRuleName(string stack, string ruleType)
        {
            return $"{owner}-{stack}-sbns-{ruleType}";
        }
    }
}