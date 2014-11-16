using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using disParity;

namespace disParityUI
{
  class OperationManager
  {

    private CancellableOperation operationInProgress;
    private MainWindowViewModel vm;

    private static OperationManager instance;

    public event EventHandler OperationFinished;

    public OperationManager(MainWindowViewModel vm)
    {
      this.vm = vm;
      instance = this;
    }

    public void Begin(CancellableOperation operation, DataDriveViewModel drive = null)
    {
      try
      {
        Debug.Assert(!Busy);
        operation.Finished += HandleOperationFinished;
        operationInProgress = operation;
        operation.Begin(vm, drive);
      }
      catch (Exception e)
      {
        App.LogCrash(e);
        LogFile.Log("Exception trying to begin {0}: {1} ", operation.Name, e.Message);
      }
    }

    public void Cancel()
    {
      if (operationInProgress != null)
        operationInProgress.Cancel();
    }

    private void HandleOperationFinished()
    {
      operationInProgress.Finished -= HandleOperationFinished;
      OperationFinished(operationInProgress, new EventArgs());
      operationInProgress = null;
    }

    public bool Busy
    {
      get
      {
        return operationInProgress != null && operationInProgress.Running;
      }
    }

    public string Status
    {
      get
      {
        if (operationInProgress != null)
          return operationInProgress.Status;
        return "";
      }
    }

    public static OperationManager Instance
    {
      get
      {
        return instance;
      }
    }



  }

}
