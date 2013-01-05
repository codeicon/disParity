using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace disParity
{

  /// <summary>
  /// Base class for classes that can report progress on lengthy operations
  /// </summary>
  public abstract class ProgressReporter : NotifyPropertyChanged
  {

    private const double MIN_PROGRESS_DELTA = 0.001;

    private double progress;
    public double Progress
    {
      get
      {
        return progress;
      }
      set
      {
        if (value != 0.0 && ((value - progress) < MIN_PROGRESS_DELTA))
          return;
        SetProperty(ref progress, "Progress", value);
      }
    }

  }

}
