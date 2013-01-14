using AlexPilotti.FTPS.Client;
using AlexPilotti.FTPS.Common;
using Assimilated.DMVRU.Properties;
using ICSharpCode.SharpZipLib.Zip;
using System;
using System.IO;
using System.Linq;

namespace Assimilated.DMVRU.Util
{
    internal static class Helpers
    {
        #region print to console methods

        public static void ExitWithInputError(string arg, string msg = null)
        {
            Console.WriteLine(Resources.ErrorInArgument, arg, msg, Environment.NewLine);
            Console.WriteLine();
            Environment.Exit(1);
        }

        public static void PrintIntro()
        {
            Console.WriteLine(" ########################################");
            Console.WriteLine(" # Drupal Minor Version Remote Upgrader #");
            Console.WriteLine(" #                                      #");
            Console.WriteLine(" # Version: 0.1 (BETA)                  #");
            Console.WriteLine(" # Egil Hansen (http://egilhansen.com)  #");
            Console.WriteLine(" ########################################");
            Console.WriteLine();
        }

        private static int stepCounter = 0;
        public static void PrintStep(string format, params object[] args)
        {
            stepCounter++;
            Console.WriteLine("Step {0,2}: {1}", stepCounter, string.Format(format, args));
        }

        public static void PrintStep(string step)
        {
            stepCounter++;
            Console.WriteLine("Step {0,2}: {1}", stepCounter, step);
        }

        public static void PrintResult(string result)
        {
            Console.WriteLine("         {0}", result);
        }

        public static void PrintResult(string format, params object[] args)
        {
            Console.WriteLine("         {0}", string.Format(format, args));
        }

        public static void PrintHelp()
        {
            Console.WriteLine("Commandline arguments:");
            Console.WriteLine();
            Console.WriteLine("Host \t\t= Hostname or IP of FTP ser" + "ver (required)");
            Console.WriteLine("Port \t\t= Port number for FTP server (optional, default 21)");
            Console.WriteLine("Username \t= Username to use when connecting to the server (required)");
            Console.WriteLine("Password \t= Password to use whne connection to the server (required)");
            Console.WriteLine("SiteRoot \t= The Drupal installations root on the webserver, e.g. www (required)");
            Console.WriteLine("BackupDirectory \t= Directory on FTP server to backup .htaccess and robots.txt to (optional, default \"backup\")");
            Console.WriteLine("UpgradeFile \t= The zip file containing the minor upgrade to Drupal (required)");
            Console.WriteLine();
            Console.WriteLine("Example: dmvru.exe -host:127.0.0.1 -username:user -password:pass -siteroot:\"www/egilhansen.com\" -backupdirectory:\"backupdir\" -upgradefile:\"C:\\drupal-7.18.zip\"");
        }

        #endregion

        public static bool ShouldContinue()
        {
            var input = Console.ReadKey();
            Console.Write(Environment.NewLine);
            while (input.Key != ConsoleKey.Y && input.Key != ConsoleKey.N && input.Key != ConsoleKey.Enter)
            {
                Console.WriteLine("Unknown response, try again.");
                input = Console.ReadKey();
                Console.Write(Environment.NewLine);
            }
            return input.Key != ConsoleKey.N;
        }

        public static string GetTemporaryDirectory()
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDirectory);
            return tempDirectory;
        }

        public static void MoveFile(this FTPSClient client, string source, string target, bool overwrite = true)
        {
            try
            {
                PrintResult("Deleting {0}", target);
                client.DeleteFile(target);
            }
            catch (FTPCommandException exc)
            {
                PrintResult(exc.Message);
            }

            try
            {
                PrintResult("Moving {0} to {1}", source, target);
                client.RenameFile(source, target);
            }
            catch (FTPCommandException exc)
            {
                PrintResult(exc.Message);
            }
        }

        public static void RecrusiveDelete(FTPSClient client, string remoteDir)
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

        public static void Unzip(DirectoryInfo to, FileInfo zip)
        {
            Directory.SetCurrentDirectory(to.FullName);
            using (var s = new ZipInputStream(zip.OpenRead()))
            {
                Console.Write("         Unzipping ");
                var spinner = ConsoleSpinner.Create();
                spinner.Start();

                ZipEntry entry;
                while ((entry = s.GetNextEntry()) != null)
                {
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
                spinner.Stop();
                Console.Write("done.");
            }
            Console.WriteLine();
        }
    }
}
