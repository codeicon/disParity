using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Windows;
using System.IO;
using System.Threading;
using disParity;

namespace disParityUI
{

  public partial class App : Application
  {

    private static Application app;

    protected override void OnStartup(StartupEventArgs e)
    {
      // Don't install the unhandled exception handler in debug builds, we want to be
      // able to catch those in the debugger
#if !DEBUG
      AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(HandleUnhandledException);
#endif
      base.OnStartup(e);
      app = this;
    }

    static void HandleUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
      Exception e = (Exception)args.ExceptionObject;

      LogFile.Log("Exiting due to unhandled exception: " + e.Message);
      LogFile.Log(e.StackTrace);
      LogFile.Close();

      LogCrash(e, true);

      try {
        CrashWindow crashWindow = new CrashWindow(app.MainWindow, new CrashWindowViewModel());
        crashWindow.ShowDialog();
      }
      catch {
        // hide any problems showing the crash window, but wait 5 seconds to give the crash log a chance to upload
        Thread.Sleep(5000);
      }

      Environment.Exit(0);
    }

    public static void LogCrash(Exception e, bool unhandled = false)
    {
      CrashLog.Create(e, true, unhandled);
    }

  }
}
