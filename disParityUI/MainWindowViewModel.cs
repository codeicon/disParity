using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Shell; // for TaskbarItem stuff
using disParity;

namespace disParityUI
{

  class MainWindowViewModel : NotifyPropertyChanged
  {

    private ObservableCollection<DataDriveViewModel> drives = new ObservableCollection<DataDriveViewModel>();
    private ParitySet paritySet;
    private Window owner;
    private OptionsDialogViewModel optionsViewModel;
    private Config config;
    private CancellableOperation operationInProgress;

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
      LogFile.Log("Application launched");

      LoadConfig(appDataPath);

      paritySet = new ParitySet(config);
      AddDrives();
      paritySet.PropertyChanged += HandlePropertyChanged;

      Left = config.MainWindowX;
      Top = config.MainWindowY;
      Height = config.MainWindowHeight;
      Width = config.MainWindowWidth;

      UpdateStartupMessage();
      UpdateParityStatus();

    }

    private void LoadConfig(string appDataPath)
    {
      string ConfigPath = Path.Combine(appDataPath, "Config.xml");
      // let generic crash logging handle any exceptions loading the Config
      config = new Config(ConfigPath);
      config.Load();
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
        App.LogCrash(e);
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
      if (optionsViewModel.ConfigImported) {
        paritySet.ReloadDrives();
        AddDrives();
        optionsViewModel.ConfigImported = false;
      }

      UpdateStartupMessage();
      UpdateParityStatus();
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

    private void UpdateParityStatus()
    {
      if (String.IsNullOrEmpty(config.ParityDir)) {
        ParityStatus = "Parity drive not set";
        return;
      }
      try {
        DriveInfo driveInfo = new DriveInfo(Path.GetPathRoot(config.ParityDir));
        ParityStatus = String.Format("{0} used {1} free",
          Utils.SmartSize(driveInfo.TotalSize - driveInfo.TotalFreeSpace),
          Utils.SmartSize(driveInfo.TotalFreeSpace));   
      }
      catch {
        ParityStatus = "Unknown";
      }
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
      LogFile.Log("Application shutdown");
      LogFile.Close();
    }

    /// <summary>
    /// Adds a new drive with the given path to the parity set
    /// </summary>
    public void AddDrive(string path)
    {
      string root = Path.GetPathRoot(path);

      if (String.Compare(root, Path.GetPathRoot(Config.ParityDir), true) == 0) {
        bool? result = MessageWindow.Show(owner, "Duplicate drives detected!", "The path you selected appears to be on same drive as your parity.\n\n" +
          "This is not recommended.  If the drive fails, disParity will not be able to recover any of your data.\n\n" +
          "Are you sure you want to add this drive?", MessageWindowIcon.Error, MessageWindowButton.YesNo);
        if (result == false)
          return;
      }

      foreach (DataDrive d in paritySet.Drives)
        if (String.Compare(root, Path.GetPathRoot(d.Root), true) == 0) {
          bool? result = MessageWindow.Show(owner, "Duplicate drives detected!", "The path you selected appears to be on a drive that is already part of the array.\n\n" +
            "This is not recommended.  If the drive fails, disParity will not be able to recover any of your data.\n\n" +
            "Are you sure you want to add this drive?", MessageWindowIcon.Error, MessageWindowButton.YesNo);
          if (result == false)
            return;
          else if (result == true)
            break;
        }

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
      DataDriveViewModel vm = new DataDriveViewModel(drive);
      drives.Add(vm);
    }

    /// <summary>
    /// Callback from RemoveDriveOperation when a drive has been removed
    /// </summary>
    public void DriveRemoved(DataDriveViewModel drive)
    {
      // Can only modify the drives collection on the main thread
      Application.Current.Dispatcher.Invoke(new Action(() =>
      {
        drives.Remove(drive);
      }));
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

    private void HandlePropertyChanged(object sender, PropertyChangedEventArgs e)
    {
      if (e.PropertyName == "Progress") {
        ProgressState = TaskbarItemProgressState.Normal;
        Progress = paritySet.Progress;
      }
      else if (e.PropertyName == "Status")
        Status = paritySet.Status;
    }

    public void ScanAll()
    {
      operationInProgress = new ScanOperation(this);
      operationInProgress.Finished += HandleOperationFinished;
      operationInProgress.Begin();
    }

    public void ScanDrive(DataDriveViewModel drive)
    {
      operationInProgress = new ScanOperation(this);
      operationInProgress.Finished += HandleOperationFinished;
      operationInProgress.Begin(drive);
    }

    public void Recover(DataDriveViewModel drive)
    {
      operationInProgress = new RecoverOperation(this);
      operationInProgress.Finished += HandleOperationFinished;
      operationInProgress.Begin(drive);
    }

    public void RemoveDrive(DataDriveViewModel drive)
    {
      operationInProgress = new RemoveDriveOperation(this);
      operationInProgress.Finished += HandleOperationFinished;
      operationInProgress.Begin(drive);
    }

    public void Update()
    {
      operationInProgress = new UpdateOperation(this);
      operationInProgress.Finished += HandleOperationFinished;
      operationInProgress.Begin();
    }

    public void Hashcheck(DataDriveViewModel drive = null)
    {
      operationInProgress = new HashcheckOperation(this);
      operationInProgress.Finished += HandleOperationFinished;
      operationInProgress.Begin(drive);
    }

    public void Undelete(DataDriveViewModel drive)
    {
      operationInProgress = new UndeleteOperation(this);
      operationInProgress.Finished += HandleOperationFinished;
      operationInProgress.Begin(drive);
    }

    public void Verify()
    {
      operationInProgress = new VerifyOperation(this);
      operationInProgress.Finished += HandleOperationFinished;
      operationInProgress.Begin();
    }

    private void HandleOperationFinished()
    {
      operationInProgress.Finished -= HandleOperationFinished;
      UpdateParityStatus();
    }

    public void Cancel()
    {
      if (operationInProgress != null)
        operationInProgress.Cancel();
    }

    public void StartProgress()
    {
      Progress = 0;
      ProgressState = TaskbarItemProgressState.Normal;
    }
 
    public void StopProgress()
    {
      Progress = 0;
      ProgressState = TaskbarItemProgressState.None;
    }

    #region Properties

    public Config Config { get { return config; } }

    public ParitySet ParitySet { get { return paritySet; } }

    public Window Owner { get { return owner; } }

    public ObservableCollection<DataDriveViewModel> Drives
    {
      get
      {
        return drives;
      }
    }

    private string parityStatus;
    public string ParityStatus
    {
      get
      {
        return parityStatus;
      }
      set
      {
        SetProperty(ref parityStatus, "ParityStatus", value);
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
        return (operationInProgress != null && operationInProgress.Running);
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

