using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace disParity
{

  public class SingleInstance
  {

    private static Mutex mutex;

    /// <summary>
    /// Returns true if another instance of disParity is already running, false otherwise
    /// </summary>
    public static bool AlreadyRunning()
    {
      try {
        if (mutex == null)
          mutex = new Mutex(true, "7546c390-1543-42fd-8b7a-92334e439568");
        if (!mutex.WaitOne(TimeSpan.Zero, true))
          return true;
        else
          return false;
      }
      catch {
        return false;
      }
    }

  }

}
