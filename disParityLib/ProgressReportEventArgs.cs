using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace disParity
{

  public class ProgressReportEventArgs : EventArgs
  {

    public ProgressReportEventArgs(double progress, string message = "")
    {
      Message = message;
      Progress = progress;
    }

    public string Message { get; private set; }
    public double Progress { get; private set; }

  }

}
