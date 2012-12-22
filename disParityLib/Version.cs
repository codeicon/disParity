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
#if !DEBUG
      Task.Factory.StartNew(() =>
      {
        try {
          LogFile.Log("Checking for upgrade...");
          bool firstRun;
          UInt32 id = GetID(out firstRun);
          int dc, mpb;
          GetStats(out dc, out mpb);
          string url = @"http://www.vilett.com/disParity/ping.php?id=" + id.ToString() + (firstRun ? "&firstRun=1" : "") +
            "&dc=" + dc + "&mpb=" + mpb + "&ver=" + Version.VersionString;
          WebClient webClient = new WebClient();
          byte[] buf = webClient.DownloadData(new System.Uri(url));
          double currentVersion = Convert.ToDouble(Version.VersionNum);
          double latestVersion = Convert.ToDouble(Encoding.ASCII.GetString(buf));
          LogFile.Log("Current version: {0} Latest version: {1}", currentVersion, latestVersion);
          if (latestVersion > 0 && latestVersion > currentVersion)
            callback(Encoding.ASCII.GetString(buf));
        }
        catch (Exception e) {
          LogFile.Log("Error checking for upgrade: " + e.Message);
        }
      });
#endif
    }

    private static void GetStats(out int dc, out int mpb)
    {
      dc = 0;
      mpb = 0;
      try {
        Object entry = Registry.GetValue("HKEY_CURRENT_USER\\Software\\disParity", "dc", 0);
        if (entry != null)
          dc = (int)entry;
        entry = Registry.GetValue("HKEY_CURRENT_USER\\Software\\disParity", "mpb", 0);
        if (entry != null)
          mpb = (int)entry;
      }
      catch (Exception e) {
        LogFile.Log("Error accessing registry: " + e.Message);
      }
    }

    private static UInt32 GetID(out bool firstRun)
    {
      firstRun = false;
      try {
        UInt32 id;
        Object entry = Registry.GetValue("HKEY_CURRENT_USER\\Software\\disParity", "ID", 0);
        if (entry == null || (int)entry == 0) {
          firstRun = true;
          Random r = new Random();
          id = (UInt32)r.Next();
          Registry.SetValue("HKEY_CURRENT_USER\\Software\\disParity", "ID", id, RegistryValueKind.DWord);
        }
        else
          id = (UInt32)(int)entry;
        return id;
      }
      catch (Exception e) {
        LogFile.Log("Error accessing registry: " + e.Message);
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
