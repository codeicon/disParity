using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace disParity
{

  internal class Utils
  {

    public static string MakeFullPath(string path, string name)
    {
      if (path == "")
        return name;
      if (path[path.Length - 1] == '\\')
        return path + name;
      else
        return path + "\\" + name;
    }

    public static string StripRoot(string root, string path)
    {
      if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        return path;
      path = path.Remove(0, root.Length);
      while (path.Length > 0 && path[0] == Path.DirectorySeparatorChar)
        path = path.Remove(0, 1);
      return path;
    }

    public static bool HashCodesMatch(byte[] h1, byte[] h2)
    {
      if (h1.Length != h2.Length)
        return false;
      for (int i = 0; i < h1.Length; i++)
        if (h1[i] != h2[i])
          return false;
      return true;
    }

    public static string SmartSize(long size)
    {
      const long KB = 1024;
      const long MB = KB * 1024;
      const long GB = MB * 1024;
      const long TB = GB * 1024;

      string units;
      double result;

      if (size < KB) {
        if (size == 1)
          return "1 byte";
        else
          return size.ToString() + " bytes";
      }
      else if (size < MB) {
        result = (double)size / KB;
        units = "KB";
      }
      else if (size < GB) {
        result = (double)size / MB;
        units = "MB";
      }
      else if (size < TB) {
        result = (double)size / GB;
        units = "GB";
      }
      else {
        result = (double)size / TB;
        units = "TB";
      }
      return String.Format("{0:F2} {1}", result, units);
    }

  }

}
