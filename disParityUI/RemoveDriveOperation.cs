using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using disParity;

namespace disParityUI
{

  internal class RemoveDriveOperation : CancellableOperation
  {

    public RemoveDriveOperation(MainWindowViewModel vm) : base(vm) { }

    protected override bool PrepareOperation()
    {

      if (drive.DataDrive.FileCount == 0) {
        RemoveEmptyDrive(drive);
        return false;
      }

      string message = String.Format("Are you sure you want to remove {0} from the backup?", drive.Root);
      if (MessageWindow.Show(viewModel.Owner, "Confirm drive removal", message, MessageWindowIcon.Question, MessageWindowButton.YesNo) == false)
        return false;

      return true;

    }

    protected override void DoOperation()
    {
      Status = "Removing " + drive.Root + "...";
      viewModel.ParitySet.RemoveAllFiles(drive.DataDrive);
      if (!cancelled)
        RemoveEmptyDrive(drive);
    }

    protected override void CancelOperation()
    {
      viewModel.ParitySet.CancelRemoveAll();
    }

    /// <summary>
    /// Removes a drive from the parity set which has already been confirmed to be empty
    /// </summary>
    private void RemoveEmptyDrive(DataDriveViewModel vm)
    {
      try {
        viewModel.ParitySet.RemoveEmptyDrive(vm.DataDrive);
      }
      catch (Exception e) {
        App.LogCrash(e);
        MessageWindow.ShowError(viewModel.Owner, "Error removing drive", e.Message);
        return;
      }
      viewModel.DriveRemoved(vm);
      Status = vm.DataDrive.Root + " removed";
    }


    protected override string Name { get { return "Remove drive"; } }

    protected override bool ScanFirst { get { return false; } }

  }

}
