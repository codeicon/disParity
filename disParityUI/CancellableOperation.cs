using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using disParity;

namespace disParityUI
{

  /// <summary>
  /// Base class for all long-running operations invoked from the main window
  /// </summary>
  abstract class CancellableOperation
  {

    private int runningScans;
    private double[] scanProgress;
    private object syncObject = new object();
    private bool scanError;
    private bool auto;

    protected MainWindowViewModel viewModel;
    protected bool cancelled;
    protected bool scanning;
    protected bool inProgress;
    protected bool anyDriveNeedsUpdate;
    protected bool suppressErrorCheck;
    protected List<string> errorMessages = new List<string>();
    protected DataDriveViewModel drive; // the drive this operation is running on, if any
    protected DataDriveViewModel scanDrive; // scan ONLY this drive
    protected DataDriveViewModel skipDrive; // scan all drives EXCEPT this drive

    public delegate void FinishedDelegate();
    public event FinishedDelegate Finished;

    public CancellableOperation(bool auto = false)
    {
      inProgress = false;
      scanning = false;
      anyDriveNeedsUpdate = false;
      scanError = false;
      suppressErrorCheck = false;
      this.auto = auto;
    }

    public virtual void Begin(MainWindowViewModel viewModel, DataDriveViewModel selectedDrive = null)
    {
      cancelled = false;
      this.viewModel = viewModel;
      viewModel.ParitySet.ErrorMessage += HandleErrorMessage;
      viewModel.StartProgress();
      inProgress = true;
      drive = selectedDrive;
      if (ScanFirst && viewModel.Drives.Count > 0) {
        scanning = true;
        viewModel.Status = "Scanning drives...";
        runningScans = 0;
        scanProgress = new double[viewModel.Drives.Count];
        foreach (DataDriveViewModel vm in viewModel.Drives) 
          if (vm != skipDrive) {
            Interlocked.Increment(ref runningScans);
            vm.PropertyChanged += HandleDataDrivePropertyChanged;
            vm.DataDrive.ScanCompleted += HandleScanCompleted;
            vm.Scan(auto); // runs in a separate Task
          }
      }
      else
        Run();
    }

    // for updating overall progress bar during a scan
    private void HandleDataDrivePropertyChanged(object sender, PropertyChangedEventArgs args)
    {
      double progress = 0;
      if (inProgress && args.PropertyName == "Progress") {
        if (scanDrive != null)
          progress = ((DataDriveViewModel)sender).Progress;
        else {
          int i = 0;
          foreach (DataDriveViewModel vm in viewModel.Drives) {
            if (vm.Progress > 0.0)
              scanProgress[i] = vm.Progress;
            i++;
          }
          foreach (double p in scanProgress)
            progress += p;
          progress /= viewModel.Drives.Count;
        }
        viewModel.Progress = progress;
      }
    }

    private void HandleScanCompleted(object sender, ScanCompletedEventArgs args)
    {
      ((DataDrive)sender).ScanCompleted -= HandleScanCompleted;
      foreach (DataDriveViewModel vm in viewModel.Drives) 
        if (vm.DataDrive == sender)
          vm.PropertyChanged -= HandleDataDrivePropertyChanged;

      if (args.UpdateNeeded)
        anyDriveNeedsUpdate = true;

      if (args.Error)
        scanError = true;

      Interlocked.Decrement(ref runningScans);
      if (runningScans > 0)
        return;

      scanning = false;
      viewModel.StopProgress();

      if (cancelled) {
        viewModel.Status = Name + " cancelled";
        End();
        return;
      }

      if (scanError && AbortIfScanErrors) {
        // FIXME: Need to report what the errors were!
        viewModel.Status = "Error(s) encountered during scan";
        End();
        return;
      }

      // I don't like calling Run() on the ScanCompleted callback from DataDrive's scan thread,
      // so run it on the main UI thread instead.
      Application.Current.Dispatcher.BeginInvoke(new Action(() =>
      {
        Run();
      }));

    }

    private void HandleErrorMessage(object sender, ErrorMessageEventArgs args)
    {
      lock (syncObject) {
        errorMessages.Add(args.Message);
      }
    }

    /// <summary>
    /// Runs the actual operation itself in a separate Task
    /// </summary>
    private void Run()
    {

      LogFile.Log("Beginning " + Name);
      if (!PrepareOperation()) {
        Status = Name + " cancelled";
        End();
        return;
      }

      viewModel.StartProgress();
      viewModel.Status = Name + " in progress...";
      errorMessages.Clear();

      Task.Factory.StartNew(() =>
      {
        try {
          DoOperation();
          if (cancelled) {
            LogFile.Log(Name + " cancelled.");
            viewModel.Status = Name + " cancelled.";
          }
          else
            LogFile.Log(Name + " complete.");
        }
        catch (Exception e) {
          App.LogCrash(e);
          viewModel.Status = Name + " failed: " + e.Message;
        }
        finally {
          End();
        }
      });

    }

    protected void End()
    {
      CheckForErrors();
      viewModel.ParitySet.ErrorMessage -= HandleErrorMessage;
      viewModel.StopProgress();
      inProgress = false;
      if (Finished != null)
        Finished();
    }

    protected virtual bool PrepareOperation()
    {
      return true;
    }

    protected abstract void DoOperation();

    protected abstract void CancelOperation();

    public void Cancel()
    {
      cancelled = true;
      if (scanning) {
        viewModel.Status = "Cancelling scan...";
        foreach (DataDriveViewModel vm in viewModel.Drives)
          vm.DataDrive.CancelScan();
      }
      else {
        LogFile.Log("Cancelling " + Name);
        viewModel.Status = "Cancelling " + Name + "...";        
        CancelOperation();
      }
    }

    protected virtual bool CheckForErrors()
    {
      if (errorMessages.Count == 0 || suppressErrorCheck)
        return false;
      if (MessageWindow.Show(viewModel.Owner, "Errors detected", "Errors were encountered during the " + LowerCaseName + 
        ".  Would you like to see a list of errors?", MessageWindowIcon.Error, MessageWindowButton.YesNo) == true)
        ReportWindow.Show(viewModel.Owner, errorMessages);
      return true;
    }


    protected void DisplayUpToDateStatus()
    {
      long totalSize = 0;
      int totalFiles = 0;
      foreach (DataDriveViewModel vm in viewModel.Drives) {
        totalSize += vm.DataDrive.TotalFileSize;
        totalFiles += vm.DataDrive.FileCount;
      }
      Status = String.Format("{1:N0} files ({0}) protected.  All drives up to date.",
        Utils.SmartSize(totalSize), totalFiles);
    }

    public bool Running { get { return inProgress; } }

    /// <summary>
    /// Name of this operation
    /// </summary>
    abstract public string Name { get; }

    abstract protected string LowerCaseName { get; }

    /// <summary>
    /// Whether or not this operation should be preceded by a scan
    /// </summary>
    protected virtual bool ScanFirst { get { return true; } }

    /// <summary>
    /// Whether or not the operation should be aborted if any errors occur during the scan
    /// </summary>
    protected virtual bool AbortIfScanErrors { get { return true; } }

    protected string Status
    {
      set
      {
        viewModel.Status = value;
      }
    }

  }


}
