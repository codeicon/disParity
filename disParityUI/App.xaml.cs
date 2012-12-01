using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Windows;
using System.IO;

namespace disParityUI
{
  /// <summary>
  /// Interaction logic for App.xaml
  /// </summary>
  public partial class App : Application
  {
    protected override void OnStartup(StartupEventArgs e)
    {
      AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(HandleUnhandledException);
    }

    static void HandleUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
      Exception e = (Exception)args.ExceptionObject;

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
