using System;
using System.Collections.Generic;
using System.IO;

namespace Assimilated.DMVRU
{
    internal static class RuntimeConfig
    {
        public static string Host { get; set; }
        public static int Port { get; set; }
        public static string UserName { get; set; }
        public static string Password { get; set; }
        public static string SiteRoot { get; set; }
        public static string SitesDirectory { get; set; }
        public static string BackupDirectory { get; set; }
        public static FileInfo UpgradeFile { get; set; }
        public static FileInfo HTAccessFile { get; set; }
        public static FileInfo MaintenanceFile { get; set; }
        public static DirectoryInfo UpgradeDirectory { get; set; }
        public static List<string> CustomizedFiles { get; set; }
        
    }
}