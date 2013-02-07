using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace disParity
{

  public interface IEnvironment
  {
    void LogCrash(Exception e);
  }

}
