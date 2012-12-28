using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using disParity;

namespace disParityUI
{

  internal class UpdateOperation : CancellableOperation
  {

    private bool scanFirst = true;

    public UpdateOperation(bool scanFirst) : base() 
    {
      this.scanFirst = scanFirst;
    }

    protected override void DoOperation()
    {
      if (scanFirst && !anyDriveNeedsUpdate)
        DisplayUpToDateStatus();
      else {
        viewModel.ParitySet.Update();
        if (cancelled)
          Status = "Update cancelled";
        else if (errorMessages.Count > 0) {
          Status = ((errorMessages.Count == 1) ? "An error" : "Errors") + " occurred during update";
          if (MessageWindow.Show(viewModel.Owner, "Errors detected", "Errors occurred during the update.  Would you like to see a list of errors?", MessageWindowIcon.Error, MessageWindowButton.YesNo) == true)
            ReportWindow.Show(viewModel.Owner, errorMessages);
        } else
          DisplayUpToDateStatus();
      }

    }

    protected override void CancelOperation()
    {
      viewModel.ParitySet.CancelUpdate();
    }

    public override string Name { get { return "Update"; } }

    protected override string LowerCaseName { get { return "update"; } }

    protected override bool ScanFirst { get { return scanFirst; } }
  }

}
