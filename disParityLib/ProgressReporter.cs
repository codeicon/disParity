using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace disParity
{

  /// <summary>
  /// Base class for classes that can report progress on lengthy operations
  /// </summary>
  public class ProgressReporter : NotifyPropertyChanged
  {

    private const double MIN_PROGRESS_DELTA = 0.001;
    private TimeSpan minTimeDelta = TimeSpan.FromMilliseconds(100); // max. 10x per second

    public void ReportProgress(double progress, bool force = false)
    {
      DateTime now = DateTime.Now;

      if (!force && progress != 0.0 && ((progress - this.progress) < MIN_PROGRESS_DELTA))
          return;

      Progress = progress;

    }

    private double progress;
    public double Progress
    {
      get
      {
        return progress;
      }
      set
      {
        SetProperty(ref progress, "Progress", value);
      }
    }

  }

}
