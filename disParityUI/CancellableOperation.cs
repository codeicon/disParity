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
    private DateTime startTime;

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
      startTime = DateTime.Now;
      inProgress = true;
      drive = selectedDrive;
      errorMessages.Clear();
      if (ScanFirst && viewModel.Drives.Count > 0) {
        scanning = true;
        Status = "Scanning drives...";
        runningScans = 0;
        scanProgress = new double[viewModel.Drives.Count];
        bool scansStarted = false;
        foreach (DataDriveViewModel vm in viewModel.Drives) {
          // don't scan drives that have not recorded any activity since the last scan
          if (viewModel.Config.MonitorDrives && !ForceScan && !vm.DataDrive.ChangesDetected) {
            if (vm.DataDrive.DriveStatus == DriveStatus.UpdateRequired)
              anyDriveNeedsUpdate = true;
            continue;
          }
          if ((scanDrive != null && vm == scanDrive) || (scanDrive == null && vm != skipDrive)) {
            Interlocked.Increment(ref runningScans);
            vm.PropertyChanged += HandleDataDrivePropertyChanged;
            vm.DataDrive.ScanCompleted += HandleScanCompleted;
            vm.Scan(auto); // runs in a separate Task
            scansStarted = true;
          }
        }
        // if no scans were started (since no drives have changed) then run the main operation now
        if (!scansStarted)
          Run();
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
            if (vm.DataDrive.AnalyzingResults)
              scanProgress[i] = 1.0;
            else if (vm.Progress > 0.0)
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
        Status = Name + " cancelled";
        End();
        return;
      }

      if (scanError && AbortIfScanErrors) {
        // FIXME: Need to report what the errors were!
        Status = "Error(s) encountered during scan";
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
        LogFile.Log(Name + " cancelled.");
        Status = Name + " cancelled";
        End();
        return;
      }

      viewModel.StartProgress();
      Status = Name + " in progress...";

      Task.Factory.StartNew(() =>
      {
        try {
          DoOperation();
          if (cancelled) {
            LogFile.Log(Name + " cancelled.");
            Status = Name + " cancelled.";
          }
          else
            LogFile.Log(Name + " complete (operation took " + Utils.SmartTime(DateTime.Now - startTime) + ")");
        }
        catch (Exception e) {
          App.LogCrash(e);
          Status = Name + " failed: " + e.Message;
        }
        finally {
          End();
        }
      });

    }

    protected void End()
    {
      try {
        CheckForErrors();
        viewModel.ParitySet.ErrorMessage -= HandleErrorMessage;
        viewModel.StopProgress();
        inProgress = false;
        if (Finished != null)
          Finished();
      }
      catch (Exception e) {
        App.LogCrash(e);
      }
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
        Status = "Cancelling scan...";
        foreach (DataDriveViewModel vm in viewModel.Drives)
          vm.DataDrive.CancelScan();
      }
      else {
        LogFile.Log("Cancelling " + Name);
        Status = "Cancelling " + Name + "...";        
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
      //long totalSize = 0;
      //int totalFiles = 0;
      //foreach (DataDriveViewModel vm in viewModel.Drives) {
      //  totalSize += vm.DataDrive.TotalFileSize;
      //  totalFiles += vm.DataDrive.FileCount;
      //}
      //Status = String.Format("{1:N0} files ({0}) protected.  All drives up to date.",
      //  Utils.SmartSize(totalSize), totalFiles);
      viewModel.UpdateStatus();
    }

    public bool Running { get { return inProgress; } }

    /// <summary>
    /// Name of this operation
    /// </summary>
    abstract public string Name { get; }

    protected virtual string LowerCaseName { get { return Name.ToLower(); } }

    /// <summary>
    /// Whether or not this operation should be preceded by a scan
    /// </summary>
    protected virtual bool ScanFirst { get { return true; } }

    /// <summary>
    /// Whether drives should be scanned whether they have changed or not
    /// </summary>
    protected virtual bool ForceScan { get { return false; } }

    /// <summary>
    /// Whether or not the operation should be aborted if any errors occur during the scan
    /// </summary>
    protected virtual bool AbortIfScanErrors { get { return true; } }

    public string Status { get; protected set; }

  }


}
