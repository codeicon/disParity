using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace disParity
{

  public static class License
  {

    public static bool Accepted
    {

      get
      {
        try {
          Object entry =
            Registry.GetValue("HKEY_CURRENT_USER\\Software\\disParity", "License", 0);
          if (entry == null || (int)entry == 0)
            return false;
          else
            return true;
        }
        catch (Exception e) {
          LogFile.Log("Registry.GetValue failed: " + e.Message);
          return false;
        }
      }

      set
      {
        try {
          Registry.SetValue("HKEY_CURRENT_USER\\Software\\disParity", "License", 
            value ? 1 : 0, RegistryValueKind.DWord);
        }
        catch (Exception e) {
          LogFile.Log("Registry.SetValue failed: " + e.Message);
        }
      }

    }



  }

}
