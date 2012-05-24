using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using Microsoft.Win32;
using System.Threading;

namespace disParity.CmdLine
{

  public class Usage
  {
    static string url;
    static Thread thread;
    static string shutdownMsg = "";

    public static void Log(string msg)
    {
      bool firstRun;
      if (NoPing())
        return;
      UInt32 id = GetID(out firstRun);
      url = @"http://www.vilett.com/disParity/ping.php?cmd=" + msg + "&id=" +
        id.ToString() + "&firstRun=" + (firstRun ? "1" : "0") +
        "&ver=" + Program.VERSION;
      thread = new Thread(GetURL);
      thread.Start();
    }

    public static void GetURL()
    {
      try {
        WebClient webClient = new WebClient();
        byte[] buf = webClient.DownloadData(new System.Uri(url));
        double currentVersion = Convert.ToDouble(Program.VERSION);
        double latestVersion = Convert.ToDouble(Encoding.ASCII.GetString(buf));
        if (latestVersion > 0 && latestVersion > currentVersion)
          shutdownMsg = "Note: Version " + Encoding.ASCII.GetString(buf) +
            " of disParity is now available for download from www.vilett.com/disParity/\r\n";
      }
      catch {
        return;
      }
    }

    public static void Close()
    {
      if (shutdownMsg != "")
        Program.logFile.Write(shutdownMsg);
      if (thread != null && thread.IsAlive)
        thread.Abort();
    }

    static UInt32 GetID(out bool firstRun)
    {
      firstRun = false;
      try {
        UInt32 id;
        Object entry = 
          Registry.GetValue("HKEY_CURRENT_USER\\Software\\disParity", "ID", 0);
        if (entry == null || (int)entry == 0) {
          firstRun = true;
          Random r = new Random();
          id = (UInt32)r.Next();
          Registry.SetValue("HKEY_CURRENT_USER\\Software\\disParity", "ID", id,
            RegistryValueKind.DWord);
        } else
          id = (UInt32)(int)entry;
        return id;
      }
      catch {
        return 0;
      }
    }

    static bool NoPing()
    {
      Object entry =
        Registry.GetValue("HKEY_CURRENT_USER\\Software\\disParity", "noping", 0);
      if (entry != null && (int)entry == 1)
        return true;
      return false;
    }


  }

}
