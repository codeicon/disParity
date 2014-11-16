using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using disParity;

namespace disParityUI
{

  internal class UpdateOperation : CancellableOperation
  {

    public UpdateOperation(bool automatic)
    {
      if (automatic)
        suppressErrorCheck = true;
    }

    protected override void DoOperation()
    {
      if (!anyDriveNeedsUpdate)
        DisplayUpToDateStatus();
      else
      {
        if (!viewModel.ParitySet.CheckAvailableSpaceForUpdate())
          if (MessageWindow.Show(viewModel.Owner, "Insufficient disk space",
            "There does not appear to be enough disk space on your parity drive to complete this operation.  Are you sure you want to attempt an update?",
            MessageWindowIcon.Error, MessageWindowButton.YesNo) != true)
            return;
        try
        {
          viewModel.ParitySet.Update();
        }
        catch (Exception e)
        {
          App.LogCrash(e);
          MessageWindow.Show(viewModel.Owner, "Update failed!", "Sorry, a fatal error interrupted the update:\n\n" +
            e.Message + "\n\n" + "The update could not be completed.", MessageWindowIcon.Error, MessageWindowButton.OK);
          suppressErrorCheck = true;
          throw e;
        }
        if (!cancelled)
          if (errorMessages.Count == 0)
            DisplayUpToDateStatus();
          else
            Status = "Update failed";
      }

    }

    protected override void CancelOperation()
    {
      viewModel.ParitySet.CancelUpdate();
    }

    protected override bool AllowCancel()
    {
      if (viewModel.ParitySet.Empty)
      {
        if (MessageWindow.Show(viewModel.Owner, "Really cancel update?",
          "Cancelling the initial update before it completes will cause you to lose all parity data generated so far.  Are you sure you want to cancel?",
          MessageWindowIcon.Caution, MessageWindowButton.YesNo) != true)
          return false;
      }
      return true;
    }

    public override string Name { get { return "Update"; } }

  }

}
