using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Windows;
using System.IO;
using disParity;

namespace disParityUI
{

  public partial class App : Application
  {

    protected override void OnStartup(StartupEventArgs e)
    {
      // Don't install the unhandled exception handler in debug builds, we want to be
      // able to catch those in the debugger
#if !DEBUG
      AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(HandleUnhandledException);
#endif
      base.OnStartup(e);
    }

    static void HandleUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
      Exception e = (Exception)args.ExceptionObject;

      LogFile.Log("Exiting due to unhandled exception: " + e.Message);
      LogFile.Log(e.StackTrace);
      LogFile.Close();

      LogCrash(e);
      Environment.Exit(0);
    }

    public static void LogCrash(Exception e)
    {
      string appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "disParity");
      if (!Directory.Exists(appDataPath))
        Directory.CreateDirectory(appDataPath);

      using (StreamWriter s = new StreamWriter(Path.Combine(appDataPath, "crash.txt"))) {
        s.WriteLine("Crash log generated {0}", DateTime.Now);
        s.WriteLine();
        while (e != null) {
          s.WriteLine("Message: " + e.Message);
          s.WriteLine("Stack: " + e.StackTrace);
          e = e.InnerException;
          if (e != null) {
            s.WriteLine();
            s.WriteLine("Inner Exception");
            s.WriteLine();
          }
        }

      }
    }

  }
}
