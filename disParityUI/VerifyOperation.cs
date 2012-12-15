using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using disParity;

namespace disParityUI
{

  internal class VerifyOperation : CancellableOperation
  {

    public VerifyOperation(MainWindowViewModel vm) : base(vm) { }

    protected override void DoOperation()
    {

      if (anyDriveNeedsUpdate)
        foreach (DataDrive d in viewModel.ParitySet.Drives)
          if (d.Deletes.Count > 0 || d.Edits.Count > 0) {
            MessageWindow.Show(viewModel.Owner, "Update before verify", "One or more drives have changes that must be processed before a Verify can be run.  Please Update first.", MessageWindowIcon.Caution, MessageWindowButton.OK);
            return;
          }

      viewModel.ParitySet.Verify();

      if (cancelled)
        Status = "Verify cancelled." + ((errorMessages.Count == 0) ? "" : (" Errors found: " + errorMessages.Count));
      else if (errorMessages.Count == 0)
        Status = "Verify complete.  " + ((errorMessages.Count == 0) ? "No errors found." : (" Errors found: " + errorMessages.Count));
      if (errorMessages.Count > 0) {
        if (MessageWindow.Show(viewModel.Owner, "Errors detected", "Errors were found during the verify.  Would you like to see a list of errors?", MessageWindowIcon.Error, MessageWindowButton.YesNo) == true)
          ReportWindow.Show(viewModel.Owner, errorMessages);
      }

    }

    protected override void CancelOperation()
    {
      viewModel.ParitySet.CancelVerify();
    }

    protected override string Name { get { return "Verify"; } }

  }

}
