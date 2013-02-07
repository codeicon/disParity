using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using disParity;

namespace disParityUI
{

  internal class Environment : IEnvironment
  {

    public void LogCrash(Exception e)
    {
      App.LogCrash(e);
    }

  }

}
