using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using disParity;

namespace disParityUI
{

  internal class RecoverOperation : CancellableOperation
  {

    private string recoverPath;

    public override void Begin(MainWindowViewModel viewModel, DataDriveViewModel selectedDrive = null)
    {
      skipDrive = selectedDrive;
      base.Begin(viewModel, selectedDrive);
    }

    protected override bool PrepareOperation()
    {
      // check if there are changes on other drives that could mess up the recover
      foreach (DataDriveViewModel vm in viewModel.Drives)
        if (vm != drive && (vm.DataDrive.Deletes.Count > 0)) {
          if (MessageWindow.Show(viewModel.Owner, "Changes detected", "Other drives have changes which may prevent a complete recovery.  Would you like to recover anyway?", MessageWindowIcon.Caution, MessageWindowButton.OKCancel) != true)
            return false;
        }

      // prompt for location to place recovered files
      recoverPath = "";
      Application.Current.Dispatcher.Invoke(new Action(() =>
      {
        System.Windows.Forms.FolderBrowserDialog d = new System.Windows.Forms.FolderBrowserDialog();
        d.Description = "Choose a location to place recovered files:";
        System.Windows.Forms.DialogResult r = d.ShowDialog();
        if (r == System.Windows.Forms.DialogResult.OK)
          recoverPath = d.SelectedPath;
      }));

      if (recoverPath == "")
        return false;

      return true;
    }

    protected override void DoOperation()
    {
      Status = "Recovering " + drive.Root + " to " + recoverPath + "...";
      int successes;
      int failures;
      suppressErrorCheck = true; // tell base class to not report errors, we'll do that here
      viewModel.ParitySet.Recover(drive.DataDrive, recoverPath, out successes, out failures);
      if (cancelled) {
        Status = "Recover cancelled";
        return;
      }
      if (failures == 0) {
        string msg = String.Format("{0} file{1} successfully recovered!",
          successes, successes == 1 ? "" : "s");
        MessageWindow.Show(viewModel.Owner, "Recovery complete", msg);
      }
      else {
        string msg =
          String.Format("{0} file{1} recovered successfully.\r\n\r\n", successes, successes == 1 ? " was" : "s were") +
          String.Format("{0} file{1} encountered errors during the recovery.", failures, failures == 1 ? "" : "s") +
          "\r\n\r\nWould you like to see a list of errors?";
        if (MessageWindow.Show(viewModel.Owner, "Recovery complete", msg, MessageWindowIcon.Error, MessageWindowButton.YesNo) == true)
          ReportWindow.Show(viewModel.Owner, errorMessages);
      }
      Status = String.Format("{0} file{1} recovered ({2} failure{3})",
        successes, successes == 1 ? "" : "s", failures, failures == 1 ? "" : "s");
    }

    protected override void CancelOperation()
    {
      viewModel.ParitySet.CancelRecover();
    }

    protected override string Name { get { return "Recover"; } }

    protected override string LowerCaseName { get { return "recover"; } }

    protected override bool AbortIfScanErrors { get { return false; } }

  }

}
