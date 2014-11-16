using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace disParity
{

  internal class OldConfig
  {

    public OldConfig(string path)
    {
      TempDir = "";
      Ignores = new string[0];
      using (StreamReader f = new StreamReader(path))
      {
        string s;
        List<string> data = new List<string>();
        while ((s = f.ReadLine()) != null)
        {
          s = s.Trim();
          if (s.Length == 0 || s[0] == '#')
            continue;
          string[] t = s.Split('=');
          string left = t[0].ToLower();
          if (left == "parity")
          {
            ParityDir = t[1];
            if (ParityDir[ParityDir.Length - 1] != '\\')
              ParityDir += '\\';
            continue;
          }
          else if (left == "temp")
          {
            TempDir = t[1];
            if (TempDir[TempDir.Length - 1] != '\\')
              TempDir += '\\';
            continue;
          }
          else if (left == "tempram")
          {
            MaxTempRAM = Convert.ToUInt32(t[1]);
            continue;
          }
          else if (left == "ignorehidden")
          {
            if (t[1] == "1")
              IgnoreHidden = true;
          }
          else if (left == "ignore")
          {
            Ignores = t[1].Split('|');
          }
          if (left.Substring(0, 4) != "data")
            continue;
          Int32 num = Convert.ToInt32(left.Substring(4));
          if (backupDirs == null)
            backupDirs = new string[num];
          else if (num > backupDirs.Length)
            Array.Resize<string>(ref backupDirs, num);
          backupDirs[num - 1] = t[1];
          if (backupDirs[num - 1][backupDirs[num - 1].Length - 1] != '\\')
            backupDirs[num - 1] += '\\';
        }
      }
    }

    public string ParityDir { get; private set; }

    public string TempDir { get; private set; }

    public UInt32 MaxTempRAM { get; private set; }

    public bool IgnoreHidden { get; private set; }

    private string[] backupDirs;
    public string[] BackupDirs { get { return backupDirs; } }

    public string[] Ignores { get; private set; }

  }

}
