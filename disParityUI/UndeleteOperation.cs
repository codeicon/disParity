using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using disParity;

namespace disParityUI
{

  class UndeleteOperation : CancellableOperation
  {

    private List<string> filesToRestore;

    public UndeleteOperation(MainWindowViewModel vm) : base(vm) { }

    protected override bool PrepareOperation()
    {
      if (drive.DataDrive.Edits.Count == 0 && drive.DataDrive.Deletes.Count == 0)
        return false;

      List<string> files = new List<string>();
      foreach (FileRecord r in drive.DataDrive.Edits)
        files.Add(r.FullPath);
      foreach (FileRecord r in drive.DataDrive.Deletes)
        files.Add(r.FullPath);

      UndeleteWindowViewModel vm = new UndeleteWindowViewModel(files);

      UndeleteWindow window = new UndeleteWindow();
      window.Owner = viewModel.Owner;
      window.DataContext = vm;
      bool? dialogResult = window.ShowDialog();

      if (dialogResult == null || dialogResult == false)
        return false;

      filesToRestore = vm.SelectedFiles;

      return true;
    }

    protected override void DoOperation()
    {
      viewModel.ParitySet.Undelete(drive.DataDrive, filesToRestore);
    }

    protected override void CancelOperation()
    {
      viewModel.ParitySet.CancelUndelete();
    }

    // not sure if we should scan first before undelete or not.  For now, not.
    protected override bool ScanFirst { get { return false; } }

    protected override string Name { get { return "Undelete"; } }

    protected override string LowerCaseName { get { return "undelete"; } }

  }

}
