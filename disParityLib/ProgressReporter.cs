using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace disParity
{

  internal class ProgressReporter
  {
    private double progress;
    private double progressPerPhase;
    private ProgressReporter phaseProgress;
    private int numPhases;
    private int currentPhase;

    public void Reset(int phases)
    {
      progress = 0.0f;
      numPhases = phases;
      if (phases > 0)
        progressPerPhase = 1.0 / phases;
      else
        progressPerPhase = 0.0;
      currentPhase = 0;
      phaseProgress = null;
    }

    public double Progress
    {
      get
      {
        if (phaseProgress == null)
          return progress;
        else
          return progress + progressPerPhase * phaseProgress.Progress;
      }
    }

    public void EndPhase()
    {
      currentPhase++;
      if (currentPhase <= numPhases)
        progress = (double)currentPhase / (double)numPhases;
      if (phaseProgress != null)
        phaseProgress = null;
    }

    public ProgressReporter BeginSubPhase(int phases)
    {
      phaseProgress = new ProgressReporter();
      phaseProgress.Reset(phases);
      return phaseProgress;
    }

  }

}
