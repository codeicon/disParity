using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace disParityUI
{

  class UndeleteOperation : CancellableOperation
  {

    public UndeleteOperation(MainWindowViewModel vm) : base(vm) { }

    protected override bool PrepareOperation()
    {
      if (drive.DataDrive.Edits.Count == 0 && drive.DataDrive.Deletes.Count == 0)
        return false;

      return true;
    }

    protected override void DoOperation()
    {
      throw new NotImplementedException();
    }

    protected override void CancelOperation()
    {
      throw new NotImplementedException();
    }

    protected override string Name { get { return "Undelete"; } }

    protected override string LowerCaseName { get { return "undelete"; } }

  }

}
