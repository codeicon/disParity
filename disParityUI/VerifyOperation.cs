using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using disParity;

namespace disParityUI
{

  internal class VerifyOperation : CancellableOperation
  {

    protected override void DoOperation()
    {

      if (anyDriveNeedsUpdate)
        foreach (DataDrive d in viewModel.ParitySet.Drives)
          if (d.Deletes.Count > 0) {
            MessageWindow.Show(viewModel.Owner, "Update before verify", "One or more drives have changes that must be processed before a Verify can be run.  Please Update first.", MessageWindowIcon.Caution, MessageWindowButton.OK);
            return;
          }

      viewModel.ParitySet.Verify();

      if (cancelled)
        Status = "Verify cancelled." + ((errorMessages.Count == 0) ? "" : (" Errors found: " + errorMessages.Count));
      else if (errorMessages.Count == 0)
        Status = "Verify complete. No errors found.";
      else
        Status = String.Format("Verify complete. Errors found: {0} Errors fixed: {1}",
          viewModel.ParitySet.VerifyErrors, viewModel.ParitySet.VerifyRecovers);

    }

    protected override void CancelOperation()
    {
      viewModel.ParitySet.CancelVerify();
    }

    public override string Name { get { return "Verify"; } }

  }

}
