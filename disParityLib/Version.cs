using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Net;

namespace disParity
{

  public static class Version
  {

    private static string version;

    private static void GetVersion()
    {
      if (String.IsNullOrEmpty(version)) {
        Assembly thisAssembly = Assembly.GetExecutingAssembly();
        object[] attributes = thisAssembly.GetCustomAttributes(typeof(AssemblyFileVersionAttribute), false);
        if (attributes.Length == 1)
          version = ((AssemblyFileVersionAttribute)attributes[0]).Version;
      }
    }

    public delegate void NewVersionAvailablekDelegate(string newVersion);

    public static void DoUpgradeCheck(NewVersionAvailablekDelegate callback)
    {
      Task.Factory.StartNew(() =>
      {
        try {
          bool firstRun;
          UInt32 id = GetID(out firstRun);
          string url = @"http://www.vilett.com/disParity/ping.php?id=" + id.ToString() + 
            "&firstRun=" + (firstRun ? "1" : "0") +"&ver=" + Version.VersionString;
          WebClient webClient = new WebClient();
          byte[] buf = webClient.DownloadData(new System.Uri(url));
          double currentVersion = Convert.ToDouble(Version.VersionNum);
          double latestVersion = Convert.ToDouble(Encoding.ASCII.GetString(buf));
          if (latestVersion > 0 && latestVersion > currentVersion)
            callback(Encoding.ASCII.GetString(buf));
        }
        catch {
        }
      });
    }

    private static UInt32 GetID(out bool firstRun)
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
        }
        else
          id = (UInt32)(int)entry;
        return id;
      }
      catch {
        return 0;
      }
    }

    public static string VersionString
    {
      get
      {
        GetVersion();
        return version;
      }
    }

    public static double VersionNum
    {
      get
      {
        GetVersion();
        return Convert.ToDouble(version);
      }
    }

  }

}
