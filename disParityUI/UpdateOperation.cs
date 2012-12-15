using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using disParity;

namespace disParityUI
{

  internal class UpdateOperation : CancellableOperation
  {

    public UpdateOperation(MainWindowViewModel vm) : base(vm) { }

    protected override void DoOperation()
    {
      if (anyDriveNeedsUpdate) {
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
      else
        DisplayUpToDateStatus();

    }

    protected override void CancelOperation()
    {
      viewModel.ParitySet.CancelUpdate();
    }

    protected override string Name { get { return "Update"; } }

  }

}
