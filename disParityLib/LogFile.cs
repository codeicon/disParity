using System;
using System.Collections.Generic;
using System.Text;
using System.IO;


namespace disParity
{
  public static class LogFile
  {
    static StreamWriter f = null;
    static bool verbose;
    static string filename;
    static object syncObj = new object();

    const int MAX_FILE_SIZE = 5000000;

    public static string LogPath { set; private get; }

    public static void Open(string filenameIn, bool verbose)
    {
      filename = filenameIn;
      Verbose = verbose;
      Open();
    }

    private static void Open()
    {
      try {
        f = new StreamWriter(filename, true);
      }
      catch {
        f = null;
        // suppress any errors opening the log file
      }
    }

    // Used in console mode only
    public static void Write(string msg)
    {
      lock (syncObj) {
        if (f != null) {
          Console.Write(msg);
          f.Write(msg);
        }
      }
    }

    // Used in console mode only
    public static void Write(string msg, params object[] args)
    {
      lock (syncObj) {
        if (f != null) {
          Console.Write(msg, args);
          f.Write(msg, args);
        }
      }
    }

    public static void Log(string msg, params object[] args)
    {
      try {
        lock (syncObj) {
          Console.WriteLine(msg, args);
          if (f != null) {
            f.Write(DateTime.Now + " ");
            f.WriteLine(msg, args);
            f.Flush();
            MaybeRotate();
          }
        }
      }
      catch {
        // suppress any errors writing to log file
      }
    }

    public static void Log(string msg)
    {
      try {
        lock (syncObj) {
          Console.WriteLine(msg);
          if (f != null) {
            f.Write(DateTime.Now + " ");
            f.WriteLine(msg);
            f.Flush();
            MaybeRotate();
          }
        }
      }
      catch {
        // suppress any errors writing to log file
      }
    }

    public static void VerboseLog(string msg, params object[] args)
    {
      if (Verbose)
        Log(msg, args);
    }

    // assumed to be called from inside a lock() so does not take a lock of its own
    private static void MaybeRotate()
    {
      FileInfo info = new FileInfo(filename);
      if (info.Length > MAX_FILE_SIZE) {
        Close();
        string newName = Path.ChangeExtension(filename, "old");
        if (File.Exists(newName))
          File.Delete(newName);
        File.Move(filename, newName);
        Open();
      }
    }

    private static void Flush()
    {
      lock (syncObj) {
        if (f != null)
          f.Flush();
      }
    }

    public static void Close()
    {
      lock (syncObj) {
        if (f != null) {
          f.Dispose();
          f = null;
        }
      }
    }

    public static bool Verbose
    {
      private set { verbose = value; }
      get { return verbose; }
    }

  }
}
