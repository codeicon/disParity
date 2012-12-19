using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace disParityUI
{

  class HashcheckOperation : CancellableOperation
  {

    public HashcheckOperation(MainWindowViewModel vm) : base(vm) { }

    protected override void DoOperation()
    {
      if (drive != null)
        viewModel.ParitySet.HashCheck(drive.DataDrive);
      else
        viewModel.ParitySet.HashCheck();
    }

    protected override void CancelOperation()
    {
      viewModel.ParitySet.CancelHashcheck();
    }

    protected override string Name { get { return "Hash check"; } }

    protected override string LowerCaseName { get { return "hash check"; } }

    protected override bool ScanFirst { get { return false; } }

  }

}
