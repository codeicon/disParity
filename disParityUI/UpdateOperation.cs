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
        try {
          viewModel.ParitySet.Update();
        }
        catch (Exception e) {
          App.LogCrash(e);
          MessageWindow.Show(viewModel.Owner, "Update failed!", "Sorry, a fatal error interrupted the update:\n\n" +
            e.Message + "\n\n" +
            "The update could not be completed.", MessageWindowIcon.Error, MessageWindowButton.OK);
          suppressErrorCheck = true;
          throw e;
        }
        if (!cancelled && errorMessages.Count == 0)
          DisplayUpToDateStatus();
      }

    }

    protected override void CancelOperation()
    {
      viewModel.ParitySet.CancelUpdate();
    }

    public override string Name { get { return "Update"; } }

    protected override bool ScanFirst { get { return scanFirst; } }
  }

}
