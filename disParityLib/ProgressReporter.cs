using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace disParity
{

  /// <summary>
  /// Base class for classes that can report progress on lengthy operations
  /// </summary>
  public class ProgressReporter
  {

    private DateTime lastReport;
    private double lastProgress;
    private const double MIN_PROGRESS_DELTA = 0.001;
    private TimeSpan minTimeDelta = TimeSpan.FromMilliseconds(100); // max. 10x per second

    public event EventHandler<ProgressReportEventArgs> ProgressReport;

    public void ReportProgress(double progress, string message = "", bool force = false)
    {
      if (ProgressReport == null)
        return;

      DateTime now = DateTime.Now;

      if (!force && progress != 0.0)
        if (TimeBasedProgressThrottling) {
          if (now - lastReport < minTimeDelta)
            return;
        }
        else if ((progress - lastProgress) < MIN_PROGRESS_DELTA)
          return;

      ProgressReport(this, new ProgressReportEventArgs(progress, message));
      lastProgress = progress;
      lastReport = now;

    }

    protected bool TimeBasedProgressThrottling { set; private get; }

  }

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
