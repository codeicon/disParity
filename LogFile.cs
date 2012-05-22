using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace disParity
{
  public class LogFile
  {
    static StreamWriter f = null;
    static bool verbose;

    public LogFile(string name, bool verbose)
    {
      string filename = name + ".txt";
      if (File.Exists(filename)) {
        int i = 1;
        while (File.Exists(filename)) {
          filename = name + "." + i.ToString() + ".txt";
          i++;
        }
      }
      f = new StreamWriter(filename);
      Verbose = verbose;
    }

    public void Write(string msg)
    {
      lock (f) {
        Console.Write(msg);
        f.Write(msg);
      }
    }

    public void Write(string msg, params object[] args)
    {
      lock (f) {
        Console.Write(msg, args);
        f.Write(msg, args);
      }
    }

    public static void Log(string msg, params object[] args)
    {
      Console.WriteLine(msg, args);
      if (f != null)
        lock (f) {
          f.WriteLine(msg, args);
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

    public void Flush()
    {
      f.Flush();
    }

    public void Close()
    {
      f.Close();
    }

    public static bool Verbose
    {
      private set { verbose = value; }
      get { return verbose; }
    }

  }
}
