using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Runtime.InteropServices;

namespace disParityUI
{

  public sealed class Startup
  {

    [STAThread]
    public static void Main(string[] args)
    {
      if (args.Length > 0) {
        //AttachConsole(-1);
        Thread.Sleep(100);
        Console.WriteLine("Command line mode is currently disabled.");
        return;
      }

      FreeConsole();

      try {
        App app = new App();
        app.InitializeComponent();
        app.Run();
      }
      catch (Exception e) {
        App.LogCrash(e);
      }

    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    static extern bool FreeConsole();
  }

}
