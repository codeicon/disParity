using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using disParity;

namespace disParityUI
{
  internal class ScanOperation : CancellableOperation
  {

    public ScanOperation(MainWindowViewModel vm) : base(vm) { }

    public override void Begin(DataDriveViewModel selectedDrive = null)
    {
      if (viewModel.Drives.Count == 0)
        End();  // nothing to do
      else {
        scanDrive = selectedDrive;
        base.Begin();
      }
    }

    protected override void DoOperation()
    {
      if (anyDriveNeedsUpdate)
        Status = "Changes detected.  Update required."; 
      else
        DisplayUpToDateStatus();
    }

    protected override void CancelOperation()
    {
      // nothing to cancel
    }

    protected override string Name { get { return "Scan"; } }

    protected override string LowerCaseName { get { return "scan"; } }

  }
}
