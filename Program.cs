using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using AlexPilotti.FTPS.Client;
using AlexPilotti.FTPS.Common;
using Assimilated.DMVRU.Properties;
using Assimilated.DMVRU.Util;
using ICSharpCode.SharpZipLib.Zip;

namespace Assimilated.DMVRU
{
    class Program
    {
        private static int stepCounter = 0;

        static void Main(string[] commandLineArgs)
        {
            PrintIntro();
            Init(commandLineArgs);
            SetupWorkingDirectory();

            try
            {
                using (var client = new FTPSClient())
                {
                    // connect to server
                    PrintStep("Connecting to server.");
                    client.Connect(RuntimeConfig.Host,
                                   new NetworkCredential(RuntimeConfig.UserName, RuntimeConfig.Password),
                                   ESSLSupportMode.ClearText).WriteLine();

                    client.PushCurrentDirectory();

                    // verify siteroot exists
                    try
                    {
                        PrintStep("Verifying if SiteRoot exists.");
                        client.SetCurrentDirectory(RuntimeConfig.SiteRoot);
                    }
                    catch (FTPException)
                    {
                        ExitWithError("SiteRoot '{0}' does not exists on server.", RuntimeConfig.SiteRoot);
                    }

                    // create backup of existing config files
                    // e.g. .htaccess, robots.txt.
                    client.PopCurrentDirectory();

                    try
                    {
                        PrintStep("Creating backup folder.");
                        client.MakeDir(RuntimeConfig.BackupFolder);
                    }
                    catch (FTPCommandException exc)
                    {
                        Console.WriteLine();
                        Console.WriteLine("Backup directory already exists, existing content will be overridden." + Environment.NewLine +
                                          "Press Enter to continue, press CTRL+C to abort.");
                        Console.ReadLine();
                    }

                    PrintStep("Backing up files.");
                    foreach (var filePair in RuntimeConfig.GetFilesToBackup())
                    {
                        Console.WriteLine("\tMoving {0} to {1}", filePair.Item1, filePair.Item2);
                        try
                        {
                            client.RenameFile(filePair.Item1, filePair.Item2);
                        }
                        catch (FTPCommandException exc)
                        {
                            Console.WriteLine("\t{0}", exc.Message);
                        }
                    }

                    // upload maintenance files
                    PrintStep("Uploading 'Maintenance' .htaccess and .php files to server.");
                    client.PutFile(RuntimeConfig.HTAccessFile.FullName, "www/.htaccess");
                    client.PutFile(RuntimeConfig.MaintenanceFile.FullName, "www/maintenance.php");

                    // upload drupal upgrade package
                    PrintStep("Uploading Drupal upgrade.");
                    client.PutFiles(RuntimeConfig.UpgradeDirectory.FullName, "", "*", EPatternStyle.Wildcard, true, TransferCallback);
                    Console.WriteLine();

                    // assumes name of drupal upgrade file is the same as the catalog inside it
                    var remoteUpgradeDir = RuntimeConfig.UpgradeDirectory.GetDirectories().First().Name;

                    // delete sites folder in upgrade
                    PrintStep("Deleting 'sites' folder in Drupal upgrade");
                    RecrusiveDelete(client, remoteUpgradeDir + "/sites");

                    // move sites folder from website to upgrade
                    PrintStep("Move 'sites' folder from siteroot to upgrade folder.");
                    client.RenameFile(RuntimeConfig.SiteRoot + "/sites", remoteUpgradeDir + "/sites");

                    // rename website to website-old
                    var date = DateTime.UtcNow;
                    var backupName = string.Format("drupal-old-{0}", DateTime.UtcNow.ToString("yyyy-mm-dd"));
                    PrintStep("Rename siteroot to '" + backupName + "'.");                    
                    client.RenameFile(RuntimeConfig.SiteRoot, backupName);

                    // rename upgrade to website
                    PrintStep("Rename upgrade folder to siteroot.");                    
                    client.RenameFile(remoteUpgradeDir, RuntimeConfig.SiteRoot);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                ExitWithError("An error ocurred!{0}{1}", Environment.NewLine, ex.Message);
            }
            finally
            {
                CleanUp();
            }

            Console.WriteLine();
            Console.WriteLine("DONE!");
        }

        private static void RecrusiveDelete(FTPSClient client, string remoteDir)
        {
            var content = client.GetDirectoryList(remoteDir);
            foreach (var file in content.Where(x => !x.IsDirectory))
            {
                client.DeleteFile(remoteDir + "/" + file.Name);
            }
            foreach (var directory in content.Where(x => x.IsDirectory))
            {
                RecrusiveDelete(client, remoteDir + "/" + directory.Name);
            }
            client.RemoveDir(remoteDir);
        }

        private static void TransferCallback(FTPSClient sender, ETransferActions action, string localObjectName, string remoteObjectName, ulong fileTransmittedBytes, ulong? fileTransferSize, ref bool cancel)
        {
            if (action == ETransferActions.FileUploaded || action == ETransferActions.RemoteDirectoryCreated) Console.Write(".");
        }

        private static void CleanUp()
        {
            Directory.SetCurrentDirectory(Path.GetTempPath());
            if (RuntimeConfig.HTAccessFile.Exists) RuntimeConfig.HTAccessFile.Delete();
            if (RuntimeConfig.MaintenanceFile.Exists) RuntimeConfig.MaintenanceFile.Delete();
            if (RuntimeConfig.UpgradeDirectory.Exists) RuntimeConfig.UpgradeDirectory.Delete(true);
        }

        private static void SetupWorkingDirectory()
        {
            // create maintenenace files
            PrintStep("Creating maintenance files");
            RuntimeConfig.HTAccessFile = new FileInfo(Path.GetTempFileName());
            using (var outfile = File.CreateText(RuntimeConfig.HTAccessFile.FullName))
            {
                outfile.Write(Resources.HTAccessFile);
            }
            RuntimeConfig.MaintenanceFile = new FileInfo(Path.GetTempFileName());
            using (var outfile = File.CreateText(RuntimeConfig.MaintenanceFile.FullName))
            {
                outfile.Write(Resources.MaintenancePage);
            }

            // unzip drupal upgrade
            PrintStep("Unzipping drupal upgrade");
            RuntimeConfig.UpgradeDirectory = new DirectoryInfo(GetTemporaryDirectory());
            Unzip(RuntimeConfig.UpgradeDirectory, RuntimeConfig.UpgradeFile);
        }

        static void PrintIntro()
        {
            Console.WriteLine(" ########################################");
            Console.WriteLine(" # Drupal Minor Version Remote Upgrader #");
            Console.WriteLine(" #                                      #");
            Console.WriteLine(" # Version: 0.01                        #");
            Console.WriteLine(" # Egil Hansen (http://egilhansen.com)  #");
            Console.WriteLine(" ########################################");
            Console.WriteLine();
        }

        static void PrintStep(string step)
        {
            stepCounter++;
            Console.WriteLine("Step {0}: {1}", stepCounter, step);
        }

        static void PrintHelp()
        {
            Console.WriteLine("HELP!");
        }

        static string GetTemporaryDirectory()
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDirectory);
            return tempDirectory;
        }

        static void Unzip(DirectoryInfo to, FileInfo zip)
        {
            Directory.SetCurrentDirectory(to.FullName);
            using (var s = new ZipInputStream(zip.OpenRead()))
            {
                ZipEntry entry;
                while ((entry = s.GetNextEntry()) != null)
                {
                    Console.Write(".");
                    if (entry.IsDirectory)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(entry.Name));
                    }
                    else if (entry.IsFile)
                    {
                        //var target = Path.Combine(to.FullName, entry.Name);
                        using (var streamWriter = File.Create(entry.Name))
                        {
                            var size = 2048;
                            var data = new byte[2048];
                            while (true)
                            {
                                size = s.Read(data, 0, data.Length);
                                if (size > 0)
                                {
                                    streamWriter.Write(data, 0, size);
                                }
                                else
                                {
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            Console.WriteLine();
        }

        static void Init(string[] commandLineArgs)
        {
            var args = new Arguments(commandLineArgs);

            if (args["help"] != null)
            {
                PrintHelp();
                Environment.Exit(0);
            }

            // get ftp connection info            
            if (string.IsNullOrWhiteSpace(args["host"])) ExitWithInputError("host");
            RuntimeConfig.Host = args["host"];

            int port;
            RuntimeConfig.Port = !string.IsNullOrWhiteSpace(args["port"]) && int.TryParse(args["port"], out port) ? port : 21;

            if (string.IsNullOrWhiteSpace(args["username"])) ExitWithInputError("username");
            RuntimeConfig.UserName = args["username"];

            if (string.IsNullOrWhiteSpace(args["password"])) ExitWithInputError("password");
            RuntimeConfig.Password = args["password"];

            // get directory info
            if (string.IsNullOrWhiteSpace(args["siteroot"])) ExitWithInputError("siteroot");
            RuntimeConfig.SiteRoot = args["siteroot"];
            RuntimeConfig.BackupFolder = string.IsNullOrWhiteSpace(args["backupfolder"]) ? "backup" : args["backupfolder"];

            // get upgrade file
            if (string.IsNullOrWhiteSpace(args["upgradefile"])) ExitWithInputError("upgradefile");
            if (!File.Exists(args["upgradefile"])) ExitWithInputError("upgradefile", "File does not exists.");
            RuntimeConfig.UpgradeFile = new FileInfo(args["upgradefile"]);
        }

        static void ExitWithError(params string[] args)
        {
            Console.WriteLine(args);
            Console.WriteLine();
            Environment.Exit(1);
        }

        static void ExitWithInputError(string arg, string msg = null)
        {
            Console.WriteLine(Resources.ErrorInArgument, arg, msg, Environment.NewLine);
            Console.WriteLine();
            Environment.Exit(1);
        }
    }

    internal static class RuntimeConfig
    {
        public static string Host { get; set; }
        public static int Port { get; set; }
        public static string UserName { get; set; }
        public static string Password { get; set; }
        public static string SiteRoot { get; set; }
        public static string BackupFolder { get; set; }
        public static FileInfo UpgradeFile { get; set; }
        public static FileInfo HTAccessFile { get; set; }
        public static FileInfo MaintenanceFile { get; set; }
        public static DirectoryInfo UpgradeDirectory { get; set; }

        public static List<Tuple<string, string>> GetFilesToBackup()
        {
            var res = new List<Tuple<string, string>>();

            res.Add(CreateFileBackupPair(".htaccess"));
            res.Add(CreateFileBackupPair("robots.txt"));

            return res;
        }

        private static Tuple<string, string> CreateFileBackupPair(string relativeFilePath)
        {
            return new Tuple<string, string>(SiteRoot + "/" + relativeFilePath, BackupFolder + "/" + relativeFilePath);
        }

        public static void WriteLine(this string output)
        {
            Console.WriteLine(output);
        }

        public static void WriteLine(this IList<string> output)
        {
            foreach (var line in output)
            {
                Console.WriteLine(line);
            }
        }
    }
}
