using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using disParity;

namespace disParityUI
{

  internal static class CrashLog
  {

    public static string FullPath
    {
      get
      {
        return Path.Combine(disParity.Utils.AppDataFolder, "crash.txt");
      }
    }

    public static void Create(Exception e, string context, bool upload, bool unhandled)
    {
      try
      {
        string crashLog = FullPath;
        if (!Directory.Exists(Path.GetDirectoryName(crashLog)))
          Directory.CreateDirectory(Path.GetDirectoryName(crashLog));

        using (StreamWriter s = new StreamWriter(crashLog))
        {
          s.WriteLine("Crash log generated {0}", DateTime.Now);
          s.WriteLine("Version: {0}", disParity.Version.VersionString);
          s.WriteLine("Unhandled: {0}", unhandled ? "Yes" : "No");
          s.WriteLine("Context: {0}", context);
          s.WriteLine();
          while (e != null)
          {
            s.WriteLine("Exception: " + e.GetType().ToString());
            s.WriteLine("Message: " + e.Message);
            s.WriteLine("Stack: " + e.StackTrace);
            e = e.InnerException;
            if (e != null)
            {
              s.WriteLine();
              s.WriteLine("Inner Exception");
              s.WriteLine();
            }
          }
        }
        if (upload)
          UploadCrashLog();
      }
      catch
      {
        // prevent any problems with creating the crash log from taking down the app
      }
    }

    private static void UploadCrashLog()
    {
      string crashLog = FullPath;
      if (File.Exists(crashLog))
      {
        Task.Factory.StartNew(() =>
        {
          try
          {
            string crashText = File.ReadAllText(crashLog);
            //uncomment if we want to rename the crash log after uploading it
            //string oldPath = Path.ChangeExtension(crashLog, ".old");
            //if (File.Exists(oldPath))
            //  File.Delete(oldPath);
            //File.Move(crashLog, oldPath);
            using (var wb = new WebClient())
            {
              var data = new NameValueCollection();
              data["id"] = disParity.Version.GetID().ToString();
              data["crash"] = crashText;
              Uri uri = new Uri(@"http://www.vilett.com/disParity/crash.php");
              byte[] response = wb.UploadValues(uri, "POST", data);
            }
          }
          catch (Exception e)
          {
            LogFile.Log("Error uploading crash log: " + e.Message);
          }
        });
      }
    }

  }

}
