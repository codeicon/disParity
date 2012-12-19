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
      viewModel.ParitySet.HashCheck(drive.DataDrive);
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
