using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using AlexPilotti.FTPS.Client;
using AlexPilotti.FTPS.Common;
using Assimilated.DMVRU.Properties;
using Assimilated.DMVRU.Util;

namespace Assimilated.DMVRU
{
    class Program
    {
        private static int stepCounter = 0;

        static void Main(string[] commandLineArgs)
        {
            Helpers.PrintIntro();
            Init(commandLineArgs);
            SetupWorkingDirectory();

            try
            {
                using (var client = new FTPSClient())
                {
                    // connect to server
                    Helpers.PrintStep("Connecting to server.");
                    client.Connect(RuntimeConfig.Host,
                                   new NetworkCredential(RuntimeConfig.UserName, RuntimeConfig.Password),
                                   ESSLSupportMode.ClearText);

                    Helpers.PrintResult(client.WelcomeMessage);

                    // verify siteroot exists
                    try
                    {
                        Helpers.PrintStep("Verifying SiteRoot exists.");
                        client.SetCurrentDirectory(RuntimeConfig.SiteRoot);
                    }
                    catch (FTPException)
                    {
                        Helpers.PrintResult("SiteRoot '{0}' does not exists on server.", RuntimeConfig.SiteRoot);
                        Helpers.PrintResult("Cannot continue.");
                        CleanUp();
                        Environment.Exit(1);
                    }

                    // verify sitedirectory exists
                    try
                    {
                        Helpers.PrintStep("Verifying existing Drupal installations 'sites' directory exists.");
                        client.SetCurrentDirectory("/");
                        client.SetCurrentDirectory(RuntimeConfig.SitesDirectory);
                    }
                    catch (FTPException)
                    {
                        Helpers.PrintResult("Sites directory '{0}' does not exists on server.", RuntimeConfig.SitesDirectory);
                        Helpers.PrintResult("Cannot continue.");
                        CleanUp();
                        Environment.Exit(1);
                    }

                    client.SetCurrentDirectory("/");

                    // create backup of existing config files
                    // e.g. .htaccess, robots.txt.
                    try
                    {
                        Helpers.PrintStep("Creating backup folder.");
                        client.MakeDir(RuntimeConfig.BackupDirectory);
                    }
                    catch (FTPCommandException exc)
                    {
                        Console.WriteLine("         Backup directory already exists, existing content will be overridden.");
                        Console.Write("         Press [Enter] or [Y] to continue, press [N] to abort. ");
                        if (!Helpers.ShouldContinue())
                        {
                            CleanUp();
                            Console.WriteLine();
                            Console.WriteLine("Existing!");
                            Environment.Exit(0);
                        }
                    }

                    Helpers.PrintStep("Backing up (customized) files.");
                    foreach (var file in RuntimeConfig.CustomizedFiles)
                    {
                        var source = RuntimeConfig.SiteRoot + "/" + file;
                        var target = RuntimeConfig.BackupDirectory + "/" + file;
                        client.MoveFile(source, target);
                    }

                    // upload maintenance files
                    Helpers.PrintStep("Uploading 'Maintenance' .htaccess and .php files to server.");
                    client.PutFile(RuntimeConfig.HTAccessFile.FullName, RuntimeConfig.SiteRoot + "/.htaccess");
                    client.PutFile(RuntimeConfig.MaintenanceFile.FullName, RuntimeConfig.SiteRoot + "/maintenance.php");

                    // upload drupal upgrade package
                    Helpers.PrintStep("Uploading Drupal upgrade.");
                    Console.Write("         Uploading ");
                    var spinner = ConsoleSpinner.Create();
                    spinner.Start();
                    client.PutFiles(RuntimeConfig.UpgradeDirectory.FullName, "", "*", EPatternStyle.Wildcard, true, null);
                    spinner.Stop();
                    Console.WriteLine();

                    // assumes name of drupal upgrade file is the same as the catalog inside it
                    var remoteUpgradeDir = RuntimeConfig.UpgradeDirectory.GetDirectories().First().Name;
                    var remoteSitesUpgradeDir = remoteUpgradeDir + "/sites";

                    // delete sites folder in upgrade
                    Helpers.PrintStep("Deleting 'sites' folder in Drupal upgrade directory ({0}).", remoteSitesUpgradeDir);
                    Helpers.RecrusiveDelete(client, remoteUpgradeDir + "/sites");

                    // move sites folder from website to upgrade
                    Helpers.PrintStep("Move '{0}' folder from siteroot to upgrade folder.", RuntimeConfig.SitesDirectory);
                    client.RenameFile(RuntimeConfig.SitesDirectory, remoteSitesUpgradeDir);

                    Helpers.PrintStep("Restore backed up files?");
                    Console.Write("         Press [Enter] or [Y] to continue, press [N] to abort. ");
                    if (Helpers.ShouldContinue())
                    {
                        Helpers.PrintStep("Restoring backed up files.");
                        foreach (var file in RuntimeConfig.CustomizedFiles)
                        {
                            var source = RuntimeConfig.BackupDirectory + "/" + file;
                            var target = remoteUpgradeDir + "/" + file;
                            client.MoveFile(source, target);                          
                        }
                    }

                    // rename website to website-old
                    var backupName = RuntimeConfig.BackupDirectory + "/" + string.Format("drupal-old-{0}", DateTime.UtcNow.ToString("yyyy-mm-dd"));
                    Helpers.PrintStep("Rename {0} to '{1}'.", RuntimeConfig.SiteRoot, backupName);
                    client.RenameFile(RuntimeConfig.SiteRoot, backupName);


                    // rename upgrade to website
                    Helpers.PrintStep("Rename upgrade folder to siteroot.");
                    client.RenameFile(remoteUpgradeDir, RuntimeConfig.SiteRoot);

                    Console.WriteLine();
                    Console.WriteLine("DONE!");
                    Console.WriteLine();
                    Console.WriteLine("Remember to reapply changes rebots.txt, .htaccess, and settings.php if the upgrade requires it.");
                    Console.WriteLine("Feel free to remove the backup directory once done.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("An error ocurred!{0}{1}", Environment.NewLine, ex.Message);
            }
            finally
            {
                CleanUp();
                Environment.Exit(1);
            }
        }
        
        static void Init(string[] commandLineArgs)
        {
            var args = new Arguments(commandLineArgs);

            if (args["help"] != null)
            {
                Helpers.PrintHelp();
                Environment.Exit(0);
            }

            // get ftp connection info            
            if (string.IsNullOrWhiteSpace(args["host"])) Helpers.ExitWithInputError("host");
            RuntimeConfig.Host = args["host"];

            int port;
            RuntimeConfig.Port = !string.IsNullOrWhiteSpace(args["port"]) && int.TryParse(args["port"], out port) ? port : 21;

            if (string.IsNullOrWhiteSpace(args["username"])) Helpers.ExitWithInputError("username");
            RuntimeConfig.UserName = args["username"];

            if (string.IsNullOrWhiteSpace(args["password"])) Helpers.ExitWithInputError("password");
            RuntimeConfig.Password = args["password"];

            // get directory info
            if (string.IsNullOrWhiteSpace(args["siteroot"])) Helpers.ExitWithInputError("siteroot");
            RuntimeConfig.SiteRoot = args["siteroot"];

            RuntimeConfig.BackupDirectory = string.IsNullOrWhiteSpace(args["backupdirectory"]) ? "backup" : args["backupdirectory"];

            // get sitesdirectory
            //RuntimeConfig.SitesDirectory = !string.IsNullOrWhiteSpace(args["sitesdirectory"]) ? args["sitesdirectory"] : RuntimeConfig.SiteRoot + "/sites";
            RuntimeConfig.SitesDirectory = RuntimeConfig.SiteRoot + "/sites";

            // get upgrade file
            if (string.IsNullOrWhiteSpace(args["upgradefile"])) Helpers.ExitWithInputError("upgradefile");
            if (!File.Exists(args["upgradefile"])) Helpers.ExitWithInputError("upgradefile", "File does not exists.");
            RuntimeConfig.UpgradeFile = new FileInfo(args["upgradefile"]);

            // create list of customized files
            RuntimeConfig.CustomizedFiles = new List<string>() { ".htaccess", "robots.txt" };
        }

        private static void SetupWorkingDirectory()
        {
            // create maintenenace files
            Helpers.PrintStep("Creating maintenance files");
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
            Helpers.PrintStep("Unzipping drupal upgrade");
            RuntimeConfig.UpgradeDirectory = new DirectoryInfo(Helpers.GetTemporaryDirectory());
            Helpers.Unzip(RuntimeConfig.UpgradeDirectory, RuntimeConfig.UpgradeFile);
        }

        private static void CleanUp()
        {
            Directory.SetCurrentDirectory(Path.GetTempPath());
            if (RuntimeConfig.HTAccessFile.Exists) RuntimeConfig.HTAccessFile.Delete();
            if (RuntimeConfig.MaintenanceFile.Exists) RuntimeConfig.MaintenanceFile.Delete();
            if (RuntimeConfig.UpgradeDirectory.Exists) RuntimeConfig.UpgradeDirectory.Delete(true);
        }
    }
}
