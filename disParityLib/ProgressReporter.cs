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

    private double lastProgress;
    private const double MIN_PROGRESS_DELTA = 0.001;

    public event EventHandler<ProgressReportEventArgs> ProgressReport;

    public void ReportProgress(double progress, string message = "")
    {
      if (ProgressReport != null) {
        // only fire the event if the messages is non-empty or if the progress has advanced
        // by MIN_PROGRESS_DELTA
        if (message != "" || progress == 0.0 || (progress - lastProgress) >= MIN_PROGRESS_DELTA) {
          ProgressReport(this, new ProgressReportEventArgs(progress, message));
          lastProgress = progress;
        }
      }
    }

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
