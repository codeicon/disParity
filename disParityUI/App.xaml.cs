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
      CrashLog.Create(e);
    }

  }
}
