using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Shell; // for TaskbarItem stuff
using disParity;

namespace disParityUI
{

  class MainWindowViewModel : ViewModel
  {

    private ParitySet paritySet;
    private ObservableCollection<DataDriveViewModel> drives = new ObservableCollection<DataDriveViewModel>();
    private int runningScans;
    private DataDriveViewModel recoverDrive; // current drive being recovered, if any
    private List<string> errorMessages = new List<string>();
    private Window owner;
    private OptionsDialogViewModel optionsViewModel;
    private Config config;
    private double[] scanProgress;
    private bool updateAfterScan;
    private bool verifyAfterScan;
    private bool scanInProgress;
    private bool updateInProgress;
    private bool recoverInProgress;
    private bool verifyInProgress;
    private bool removeDriveInProgress;
    private bool singleDriveScan;
    private bool cancelled;

    public MainWindowViewModel(Window owner)
    {
      this.owner = owner;
      // Set up application data and log folders
      string appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "disParity");
      if (!Directory.Exists(appDataPath))
        Directory.CreateDirectory(appDataPath);
      string logPath = Path.Combine(appDataPath, "logs");
      if (!Directory.Exists(logPath))
        Directory.CreateDirectory(logPath);
      LogFile.Open(Path.Combine(logPath, "disParity.log"), false);
      LogFile.Log("Application launched at " + DateTime.Now);

      LoadConfig(appDataPath);

      paritySet = new ParitySet(config);
      AddDrives();
      paritySet.ProgressReport += HandleProgressReport;
      paritySet.ErrorMessage += HandleErrorMessage;

      Left = config.MainWindowX;
      Top = config.MainWindowY;
      Height = config.MainWindowHeight;
      Width = config.MainWindowWidth;

      UpdateStartupMessage();
      ParityLocation = config.ParityDir;

    }

    private void LoadConfig(string appDataPath)
    {
      string ConfigPath = Path.Combine(appDataPath, "Config.xml");
      try {
        config = new Config(ConfigPath);
        config.Load();
      }
      catch (Exception e) {
        throw new Exception("Could not load Config file: " + e.Message);
      }
    }

    /// <summary>
    /// Called from View when main window has loaded
    /// </summary>
    public void Loaded()
    {
      try {
        if (!disParity.License.Accepted) {
          if (!ShowLicenseAgreement()) {
            owner.Close();
            return;
          }
          disParity.License.Accepted = true;
        }

        disParity.Version.DoUpgradeCheck(HandleNewVersionAvailable);
        ScanAll();
      }
      catch (Exception e) {
        LogFile.Log("Exception in MainWindow.Loaded: " + e.Message);
      }
    }

    private bool ShowLicenseAgreement()
    {
      LicenseWindow window = new LicenseWindow(owner, new LicenseWindowViewModel());
      bool? result = window.ShowDialog();
      return result ?? false;
    }

    private void HandleNewVersionAvailable(string newVersion)
    {
      if (MessageWindow.Show(owner, "New version available", "There is a new version of disParity available.\r\n\r\n" +
        "Would you like to download the latest version now?", MessageWindowIcon.Caution, MessageWindowButton.YesNo) == true) {
        Process.Start("http://www.vilett.com/disParity/beta.html");
        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
          {
            Application.Current.MainWindow.Close();
          }));
      }
    }

    public void OptionsChanged()
    {
      ParityLocation = config.ParityDir;

      if (optionsViewModel.ConfigImported) {
        paritySet.ReloadDrives();
        AddDrives();
        optionsViewModel.ConfigImported = false;
      }

      UpdateStartupMessage();
      if (StartupMessage == "")
        ScanAll();
    }

    private void UpdateStartupMessage()
    {
      if (String.IsNullOrEmpty(config.ParityDir))
        StartupMessage = "Welcome to disParity!\r\n\r\n" +
          "To use disParity you must first specify a location where the parity data will be stored.  This location should be a dedicated drive which is at least as large as the largest drive you want to protect.\r\n\r\n" +
          "Press the 'Options...' button on the right.";
      else if (drives.Count == 0)
        StartupMessage = "Add one or more drives to be backed up by pressing the 'Add Drive' button.";
      else
        StartupMessage = "";
    }

    public OptionsDialogViewModel GetOptionsDialogViewModel()
    {
      if (optionsViewModel == null)
        optionsViewModel = new OptionsDialogViewModel(paritySet);
      return optionsViewModel;
    }

    /// <summary>
    /// Called from the view when the app is closing
    /// </summary>
    public void Shutdown()
    {
      // save the main window position and size so it can be restored on next run
      config.MainWindowX = (int)left;
      config.MainWindowY = (int)top;
      config.MainWindowWidth = (int)Width;
      config.MainWindowHeight = (int)Height;
      paritySet.Close();
      LogFile.Log("Application shutdown at " + DateTime.Now);
      LogFile.Close();
    }

    /// <summary>
    /// Adds a new drive with the given path to the parity set
    /// </summary>
    public void AddDrive(string path)
    {
      AddDrive(paritySet.AddDrive(path));
      UpdateStartupMessage();
    }

    private void AddDrives()
    {
      drives.Clear();
      foreach (DataDrive d in paritySet.Drives)
        AddDrive(d);
    }

    private void AddDrive(DataDrive drive)
    {
      drive.ScanCompleted += HandleScanCompleted;
      DataDriveViewModel vm = new DataDriveViewModel(drive);
      vm.PropertyChanged += HandleDataDrivePropertyChanged;
      drives.Add(vm);
    }

    /// <summary>
    /// Remove an entire drive from parity.  Called when user presses "Remove Drive" button.
    /// </summary>
    public void RemoveDrive(DataDriveViewModel vm)
    {
      DataDrive drive = vm.DataDrive;
      if (drive.FileCount > 0) {
        string message = String.Format("Are you sure you want to remove {0} from the backup?", drive.Root);
        if (MessageWindow.Show(owner, "Confirm drive removal", message, MessageWindowIcon.Question, MessageWindowButton.YesNo) == false)
          return;

        Status = "Removing " + drive.Root + "...";
        // start process of removing files
        Task.Factory.StartNew(() =>
        {
          cancelled = false;
          removeDriveInProgress = true;
          try {
            paritySet.RemoveAllFiles(drive);
            if (!cancelled)
              RemoveEmptyDrive(vm);
            else
              Status = "Remove drive cancelled";
          }
          catch (Exception e) {
            Status = "Remove drive failed: " + e.Message;
            return;
          }
          finally {
            removeDriveInProgress = false;
          }
        });


      }
      else
        RemoveEmptyDrive(vm);
    }

    /// <summary>
    /// Removes a drive from the parity set which has already been confirmed to be empty
    /// </summary>
    private void RemoveEmptyDrive(DataDriveViewModel vm)
    {
      try {
        paritySet.RemoveEmptyDrive(vm.DataDrive);
      }
      catch (Exception e) {
        MessageWindow.ShowError(owner, "Error removing drive", e.Message);
        return;
      }
      vm.PropertyChanged -= HandleDataDrivePropertyChanged;
      // Can only modify the drives collection on the main thread
      Application.Current.Dispatcher.Invoke(new Action(() =>
      {
        drives.Remove(vm);
      }));
      Status = vm.DataDrive.Root + " removed";
    }

    /// <summary>
    /// Scan all drives for changes.  Called when user presses "Scan All" button or "Update All" button.
    /// </summary>
    public void ScanAll()
    {
      if (drives.Count == 0)
        return;
      scanInProgress = true;
      cancelled = false;
      singleDriveScan = false;
      Status = "Scanning drives...";
      runningScans = drives.Count;
      scanProgress = new double[drives.Count];
      foreach (DataDriveViewModel vm in drives)
        vm.Scan();
    }

    /// <summary>
    /// Scan an individual drive for changes.  Called when user presses "Scan Drive" button.
    /// </summary>
    public void ScanDrive(DataDriveViewModel vm)
    {
      scanInProgress = true;
      singleDriveScan = true;
      cancelled = false;
      Status = "Scanning " + vm.DataDrive.Root + "...";
      runningScans = 1;
      vm.Scan();
    }

    private void HandleScanCompleted(object sender, EventArgs args)
    {
      runningScans--;
      if (runningScans == 0) {
        scanInProgress = false;
        if (!cancelled) {
          bool anyDriveNeedsUpdate = false;
          foreach (DataDrive d in paritySet.Drives)
            if (d.Status == DriveStatus.AccessError) {
              Status = "Error(s) encountered during scan!";
              return;
            }
            else if (d.Status == DriveStatus.UpdateRequired)
              anyDriveNeedsUpdate = true;
          if (updateAfterScan) {
            updateAfterScan = false;
            if (anyDriveNeedsUpdate)
              Update();
          }
          else if (verifyAfterScan) {
            verifyAfterScan = false;
            if (anyDriveNeedsUpdate) {
              foreach (DataDrive d in paritySet.Drives)
                if (d.Deletes.Count > 0 || d.Moves.Count() > 0) {
                  MessageWindow.Show(owner, "Update before verify", "One or more drives have changes that must be processed before a Verify can be run.  Please Update first.", MessageWindowIcon.Caution, MessageWindowButton.OK);
                  return;
                }
            }
            DoVerify();
          }
          else if (anyDriveNeedsUpdate)
            Status = "Changes detected.  Update required.";
          else
            DisplayUpToDateStatus();
        } else
          Status = "Scan cancelled";
        Progress = 0;
        ProgressState = TaskbarItemProgressState.None;
      }
    }

    private void DisplayUpToDateStatus()
    {
      long totalSize = 0;
      int totalFiles = 0;
      foreach (DataDriveViewModel vm in drives) {
        totalSize += vm.DataDrive.TotalFileSize;
        totalFiles += vm.DataDrive.FileCount;
      }
      Status = String.Format("{1:N0} files ({0}) protected.  All drives up to date.",
        Utils.SmartSize(totalSize), totalFiles);
    }

    public void UpdateAll()
    {
      updateAfterScan = true;
      ScanAll();
    }

    private void Update()
    {
      updateInProgress = true;
      cancelled = false;
      Status = "Update In Progress...";
      Task.Factory.StartNew(() =>
      {
        try {
          paritySet.Update();
          if (cancelled)
            Status = "Update cancelled";
          else
            DisplayUpToDateStatus();
        }
        catch (Exception e) {
          Status = "Update failed: " + e.Message;
        }
        finally {
          Progress = 0;
          ProgressState = TaskbarItemProgressState.None;
          updateInProgress = false;
        }
      }
      );
    }

    public void RecoverDrive(DataDriveViewModel drive, string path)
    {
      cancelled = false;
      recoverInProgress = true;
      Status = "Recovering " + drive.Root + " to " + path + "...";
      recoverDrive = drive;
      errorMessages.Clear();
      // must run actual recover as a separate Task so UI can still update
      Task.Factory.StartNew(() =>
      {
        try {
          int successes;
          int failures;
          paritySet.Recover(drive.DataDrive, path, out successes, out failures);
          if (cancelled) {
            Status = "Recover cancelled";
            return;
          }
          if (failures == 0) {
            string msg = String.Format("{0} file{1} successfully recovered!",
              successes, successes == 1 ? "" : "s");
            MessageWindow.Show(owner, "Recovery complete", msg);
          }
          else {
            string msg =
              String.Format("{0} file{1} recovered successfully.\r\n\r\n", successes, successes == 1 ? " was" : "s were") +
              String.Format("{0} file{1} encountered errors during the recovery.", failures, failures == 1 ? "" : "s") +
              "\r\n\r\nWould you like to see a list of errors?";
            if (MessageWindow.Show(owner, "Recovery complete", msg, MessageWindowIcon.Error, MessageWindowButton.YesNo) == true)
              ReportWindow.Show(owner, errorMessages);
          }
          Status = String.Format("{0} file{1} recovered ({2} failure{3})",
            successes, successes == 1 ? "" : "s", failures, failures == 1 ? "" : "s");
        }
        catch (Exception e) {
          MessageWindow.Show(owner, "Recover failed!", 
            "Sorry, an unexpected error occurred while recovering the drive:\r\n\r\n" + e.Message);
        }
        finally {
          Progress = 0;
          ProgressState = TaskbarItemProgressState.None;
          recoverInProgress = false;
        }
      }
      );
    }

    private void HandleProgressReport(object sender, ProgressReportEventArgs args)
    {
      ProgressState = TaskbarItemProgressState.Normal;
      Progress = args.Progress;
      if (args.Message != "")
        Status = args.Message;
    }


    private void HandleErrorMessage(object sender, ErrorMessageEventArgs args)
    {
      errorMessages.Add(args.Message);
    }

    // for updating overall progress bar during a scan
    private void HandleDataDrivePropertyChanged(object sender, PropertyChangedEventArgs args)
    {
      double progress = 0;
      if (scanInProgress && args.PropertyName == "Progress") {
        if (singleDriveScan)
          progress = ((DataDriveViewModel)sender).Progress;
        else {
          int i = 0;
          foreach (DataDriveViewModel vm in drives) {
            if (vm.Progress > 0.0)
              scanProgress[i] = vm.Progress;
            i++;
          }
          foreach (double p in scanProgress)
            progress += p;
          progress /= drives.Count;
        }
        ProgressState = TaskbarItemProgressState.Normal;
        Progress = progress;
      }
    }

    public void Verify()
    {
      verifyAfterScan = true;
      ScanAll();
    }

    /// <summary>
    /// Does the actual Verify.  Called from HandleScanCompleted.
    /// </summary>
    private void DoVerify()
    {
      cancelled = false;
      verifyInProgress = true;
      errorMessages.Clear();
      Status = "Verify In Progress...";
      Task.Factory.StartNew(() =>
      {
        try {
          paritySet.Verify();
          if (cancelled)
            Status = "Verify cancelled";
          else
            if (errorMessages.Count == 0)
              Status = "Verify complete.  No errors found.";
            else {
              Status = "Verify complete.  Errors found: " + errorMessages.Count;
              if (MessageWindow.Show(owner, "Verify failed", "Errors were found during the verify.  Would you like to see a list of errors?", MessageWindowIcon.Error, MessageWindowButton.YesNo) == true)
                ReportWindow.Show(owner, errorMessages);
            }
        }
        catch (Exception e) {
          Status = "Verify failed: " + e.Message;
        }
        finally {
          Progress = 0;
          ProgressState = TaskbarItemProgressState.None;
          verifyInProgress = false;
        }
      }
      );
    }

    public void Cancel()
    {
      if (scanInProgress) {
        Status = "Cancelling scan...";
        cancelled = true;
        foreach (DataDriveViewModel vm in drives)
          vm.DataDrive.CancelScan();
      }
      else if (updateInProgress) {
        Status = "Cancelling update...";
        cancelled = true;
        paritySet.CancelUpdate();
      }
      else if (recoverInProgress) {
        Status = "Cancelling recover...";
        cancelled = true;
        paritySet.CancelRecover();
      }
      else if (removeDriveInProgress) {
        Status = "Cancelling remove drive...";
        cancelled = true;
        paritySet.CancelRemoveAll();
      }
      else if (verifyInProgress) {
        Status = "Cancelling verify...";
        cancelled = true;
        paritySet.CancelVerify();
      }
    }

    public ObservableCollection<DataDriveViewModel> Drives
    {
      get
      {
        return drives;
      }
    }

    #region Properties

    private string parityLocation;
    public string ParityLocation
    {
      get
      {
        return parityLocation;
      }
      set
      {
        SetProperty(ref parityLocation, "ParityLocation", value);
      }
    }

    private string startupMessage;
    public string StartupMessage
    {
      get
      {
        return startupMessage;
      }
      set
      {
        SetProperty(ref startupMessage, "StartupMessage", value);
        if (startupMessage != "")
          StartupMessageVisibility = Visibility.Visible;
        else
          StartupMessageVisibility = Visibility.Hidden;
      }
    }

    private Visibility startupMessageVisibility;
    public Visibility StartupMessageVisibility
    {
      get
      {
        return startupMessageVisibility;
      }
      set
      {
        SetProperty(ref startupMessageVisibility, "StartupMessageVisibility", value);
      }
    }

    public bool Busy
    {
      get
      {
        return scanInProgress || updateInProgress || recoverInProgress || removeDriveInProgress || verifyInProgress;
      }
    }

    private string status = "";
    public string Status
    {
      get
      {
        return status;
      }
      set
      {
        SetProperty(ref status, "Status", value);
      }
    }

    private double progress = 0.0;
    public double Progress
    {
      get
      {
        return progress;
      }
      set
      {
        SetProperty(ref progress, "Progress", value);
      }
    }

    private double top;
    public double Top
    {
      get
      {
        return top;
      }
      set
      {
        SetProperty(ref top, "Top", value);
      }
    }

    private double left;
    public double Left
    {
      get
      {
        return left;
      }
      set
      {
        SetProperty(ref left, "Left", value);
      }
    }

    private double height;
    public double Height
    {
      get
      {
        return height;
      }
      set
      {
        SetProperty(ref height, "Height", value);
      }
    }

    private double width;
    public double Width
    {
      get
      {
        return width;
      }
      set
      {
        SetProperty(ref width, "Width", value);
      }
    }

    /// <summary>
    /// This is for the taskbar icon's progress indicator
    /// </summary>
    private TaskbarItemProgressState progressState = TaskbarItemProgressState.None;
    public TaskbarItemProgressState ProgressState
    {
      get
      {
        return progressState;
      }
      set
      {
        SetProperty(ref progressState, "ProgressState", value);
      }
    }

    #endregion

  }

}

