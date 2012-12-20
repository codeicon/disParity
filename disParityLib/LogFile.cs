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

    const int MAX_FILE_SIZE = 1000000;

    public static string LogPath { set; private get; }

    public static void Open(string filename, bool verbose)
    {
      if (File.Exists(filename)) {
        FileInfo info = new FileInfo(filename);
        if (info.Length > MAX_FILE_SIZE) {
          string newName = filename + ".old";
          if (File.Exists(newName))
            File.Delete(newName);
          File.Move(filename, newName);
        }
      }
      try {
        f = new StreamWriter(filename, true);
      }
      catch {
        // suppress any errors opening the log file
      }
      Verbose = verbose;
    }

    public static void Write(string msg)
    {
      if (f != null)
        lock (f) {
          Console.Write(msg);
          f.Write(msg);
        }
    }

    public static void Write(string msg, params object[] args)
    {
      if (f != null)
        lock (f) {
          Console.Write(msg, args);
          f.Write(msg, args);
        }
    }

    public static void Log(string msg, params object[] args)
    {
      try {
        Console.WriteLine(msg, args);
        if (f != null)
          lock (f) {
            f.Write(DateTime.Now + " ");
            f.WriteLine(msg, args);
            f.Flush();
          }
      }
      catch {
        // suppress any errors writing to log file
      }
    }

    public static void Log(string msg)
    {
      try {
        Console.WriteLine(msg);
        if (f != null)
          lock (f) {
            f.Write(DateTime.Now + " ");
            f.WriteLine(msg);
            f.Flush();
          }
      }
      catch {
        // suppress any errors writing to log file
      }
    }

    public static void VerboseLog(string msg, params object[] args)
    {
      if (!Verbose)
        return;
      Console.WriteLine(msg, args);
      if (f != null)
        lock (f) {
          f.WriteLine(msg, args);
        }
    }

    public static void Flush()
    {
      if (f != null)
        lock (f)
          f.Flush();
    }

    public static void Close()
    {
      if (f != null) {
        f.Dispose();
        f = null;
      }        
    }

    public static bool Verbose
    {
      private set { verbose = value; }
      get { return verbose; }
    }

  }
}
