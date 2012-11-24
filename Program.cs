using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;
using System.Diagnostics;
using disParity;

namespace disParity.CmdLine
{

  public enum Command
  {
    None,
    Update,
    Recover,
    Test,
    Verify,
    List,
    Stats,
    HashCheck,
    Monitor,
    Undelete
  }

  class Program
  { 
    public static bool ignoreHidden = false;

    static Int32 driveNum = -1;
    static string shutdownMsg;

    static void Main(string[] args)
    {
      string recoverDir = "";
      Command cmd = Command.None;
      bool verbose = false;

      if (args.Length > 0) {
        if (args[0].ToLower() == "update")
          cmd = Command.Update;
        else if (args[0].ToLower() == "verify") {
          cmd = Command.Verify;
        }
        else if ((args.Length == 3) && args[0].ToLower() == "recover") {
          cmd = Command.Recover;
          if (!ReadDriveNum(args))
            return;
          recoverDir = args[2];
        }
        else if ((args.Length == 2) && args[0].ToLower() == "test") {
          cmd = Command.Test;
          if (!ReadDriveNum(args))
            return;
        }
        else if (args[0].ToLower() == "list") {
          cmd = Command.List;
          if (args.Length == 2) {
            if (!ReadDriveNum(args))
              return;
          }
        }
        else if (args[0].ToLower() == "stats")
          cmd = Command.Stats;
        else if (args[0].ToLower() == "hashcheck") {
          cmd = Command.HashCheck;
          if (args.Length == 2) {
            if (!ReadDriveNum(args))
              return;
          }
        }
        else if (args[0].ToLower() == "monitor") {
          verbose = true;
          cmd = Command.Monitor;
        }
        else if (args[0].ToLower() == "undelete") {
          cmd = Command.Undelete;
          if (!ReadDriveNum(args))
            return;
        }

      }

      if (args.Length > 1 && (cmd == Command.Update || cmd == Command.Verify)) {
        if (args[1].ToLower() == "-v")
          verbose = true;
        else {
          PrintUsage();
          return;
        }
      }

      if (cmd == Command.None) {
        PrintUsage();
        return;
      }

      if (SingleInstance.AlreadyRunning()) {
        Console.WriteLine("Another instance of disParity is currently running.");
        return;
      }

      disParity.Version.DoUpgradeCheck(HandleNewVersionAvailable);

      string logFileName = "disParity log " + DateTime.Now.ToString("yy-MM-dd HH.mm.ss");
      LogFile.Open(logFileName, verbose);
      LogFile.Write("Beginning \"{0}\" command at {1} on {2}\r\n", args[0].ToLower(),
        DateTime.Now.ToShortTimeString(), DateTime.Today.ToLongDateString());

      try {
        ParitySet set = new ParitySet(@".\");
        switch (cmd) {
          case Command.Update:
            set.Update(true);
            break;

          case Command.Recover:
            int successes;
            int failures;
            set.Recover(set.Drives[driveNum], recoverDir, out successes, out failures);
            break;

          case Command.HashCheck:
            if (driveNum != -1)
              set.HashCheck(set.Drives[driveNum]);
            else
              set.HashCheck();
            break;
        }
      }
      catch (Exception e) {
        LogFile.Log("Fatal error encountered during {0}: {1}",
          args[0].ToLower(), e.Message);
        LogFile.Log("Stack trace: {0}", e.StackTrace);
      }
      finally {
        if (!String.IsNullOrEmpty(shutdownMsg))
          LogFile.Write(shutdownMsg);
        LogFile.Close();
      }

    }

    private static void HandleNewVersionAvailable(string newVersion)
    {
      shutdownMsg = "Note: Version " + newVersion +
        " of disParity is now available for download from www.vilett.com/disParity/\r\n";
    }

    static bool ReadDriveNum(string[] args)
    {
      if (args.Length < 2) {
        PrintUsage();
        return false;
      }
      try {
        driveNum = Convert.ToInt32(args[1]) - 1;
        return true;
      }
      catch {
        PrintUsage();
        return false;
      }
    }

    static void PrintUsage()
    {
      Console.WriteLine("disParity Snapshot Parity Utility Version " + Version.VersionString +
        "\r\n\r\n" +
        "Usage:\r\n\r\n" +
        "  disparity update [-v]          Create or update parity to reflect latest file data\r\n" +
        "                                 since the last snapshot\r\n " +
        "  disparity recover [num] [dir]  Recover drive [num] to directory [dir]\r\n" +
        "  disparity test [num]           Simulate a recovery of drive [num]\r\n" +
        "  disparity verify [-v]          Verify that all file data matches the\r\n" +
        "                                 parity data in the current snapshot\r\n" +
        "  disparity list [num]           Output a list of all files currently\r\n" +
        "                                 protected.  Specify optional drive [num]\r\n" +
        "                                 to restrict output to a single drive.\r\n" +
        "  disparity stats                List file counts and total data size\r\n" +
        "  disparity hashcheck [num]      Check hash of every file on drive [num]\r\n" +
        "  disparity undelete [num]       Restores any deleted files on drive [num]\r\n" +
        "\r\nSpecify optional -v to enable verbose logging.");
    }

  }


}
