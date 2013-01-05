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
    private System.Timers.Timer updateTimer;
    private System.Timers.Timer updateParityStatusTimer;
    private OperationManager operationManager;

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
      LogFile.Log("Application launched (version {0})", disParity.Version.VersionString);

      LoadConfig(appDataPath);

      paritySet = new ParitySet(config);
      AddDrives();
      paritySet.PropertyChanged += HandleParitySetPropertyChanged;

      Left = config.MainWindowX;
      Top = config.MainWindowY;
      Height = config.MainWindowHeight;
      Width = config.MainWindowWidth;

      UpdateStartupMessage();
      UpdateParityStatus();

      updateTimer = new System.Timers.Timer(1000);
      updateTimer.AutoReset = true;
      updateTimer.Elapsed += HandleUpdateTimer;

      updateParityStatusTimer = new System.Timers.Timer(1000);
      updateParityStatusTimer.AutoReset = true;
      updateParityStatusTimer.Elapsed += UpdateParityStatus;

      operationManager = new OperationManager(this);
      operationManager.OperationFinished += HandleOperationFinished;
      
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

      foreach (DataDriveViewModel d in drives)
        if (Config.MonitorDrives)
          d.DataDrive.EnableWatcher();
        else
          d.DataDrive.DisableWatcher();

      UpdateStartupMessage();
      UpdateParityStatus();
      if (StartupMessage == "")
        ScanAll();
    }

    private void UpdateStartupMessage()
    {
      if (String.IsNullOrEmpty(config.ParityDir))
        StartupMessage = "Welcome to disParity!\r\n\r\n" +
          "To use disParity you must first specify a location where the parity data will be stored.  This location should be on a dedicated drive which is at least as large as the largest drive you want to protect.\r\n\r\n" +
          "Press the 'Options...' button on the right.";
      else if (drives.Count == 0)
        StartupMessage = "Add one or more drives to be backed up by pressing the 'Add Drive' button.\n\n" +
          "When you are done adding drives, press the 'Update All' button to build the backup.";
      else
        StartupMessage = "";
    }

    private void UpdateParityStatus(object sender = null, System.Timers.ElapsedEventArgs args = null)
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

      bool warned = false;
      if (String.Compare(root, Path.GetPathRoot(Config.ParityDir), true) == 0) {
        bool? result = MessageWindow.Show(owner, "Duplicate drives detected!", "The path you selected appears to be on same drive as your parity.\n\n" +
          "This is not recommended.  If the drive fails, disParity will not be able to recover any of your data.\n\n" +
          "Are you sure you want to add this drive?", MessageWindowIcon.Error, MessageWindowButton.YesNo);
        if (result == false)
          return;
        warned = true;
      }

      if (!warned) // so we don't warn twice if the data and parity drives are already on the same drive as this one
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

      DataDriveViewModel vm = AddDrive(paritySet.AddDrive(path));
      ScanDrive(vm);
      UpdateStartupMessage();
    }

    private void AddDrives()
    {
      drives.Clear();
      foreach (DataDrive d in paritySet.Drives)
        AddDrive(d);
    }

    private DataDriveViewModel AddDrive(DataDrive drive)
    {
      DataDriveViewModel vm = new DataDriveViewModel(drive, config);
      drives.Add(vm);
      drive.PropertyChanged += HandleDateDrivePropertyChanged;
      drive.ScanCompleted += HandleScanCompleted;
      return vm;
    }

    /// <summary>
    /// Callback from RemoveDriveOperation when a drive has been removed
    /// </summary>
    public void DriveRemoved(DataDriveViewModel drive)
    {
      drive.DataDrive.PropertyChanged -= HandleDateDrivePropertyChanged;
      drive.DataDrive.ScanCompleted -= HandleScanCompleted;
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

    private void HandleParitySetPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
      if (e.PropertyName == "Progress") {
        ProgressState = TaskbarItemProgressState.Normal;
        Progress = paritySet.Progress;
      }
      else if (e.PropertyName == "Status")
        Status = paritySet.Status;
    }

    private void HandleDateDrivePropertyChanged(object sender, PropertyChangedEventArgs e)
    {
      //DataDrive drive = (DataDrive)sender;
      //if (e.PropertyName == "DriveStatus") {
      //  if (drive.DriveStatus == DriveStatus.UpdateRequired)
      //    if (config.MonitorDrives && config.UpdateMode == UpdateMode.ScanAndUpdate)
      //      updateTimer.Start();
      //}         
    }

    private void HandleScanCompleted(object sender, ScanCompletedEventArgs args)
    {
      //if (!args.Auto || args.Cancelled || args.Error || !args.UpdateNeeded)
      //  return;
      //if (config.MonitorDrives && config.UpdateMode == UpdateMode.ScanAndUpdate)
      //  updateTimer.Start();
    }

    private void HandleUpdateTimer(object sender, System.Timers.ElapsedEventArgs args)
    {
      if (operationManager.Busy)
        return;
      DateTime nextAutoUpdate = paritySet.LastChanges + TimeSpan.FromMinutes(config.UpdateDelay); 
      if (DateTime.Now > nextAutoUpdate) {
        updateTimer.Stop();
        Update(false); // don't scan first because we know there haven't been any changes
      }
      else {
        Status = String.Format("Update required.  Automatic update in {0}...",
          (nextAutoUpdate - DateTime.Now).ToString(@"m\:ss"));
      }
    }

    public void ScanAll()
    {
      operationManager.Begin(new ScanOperation());
    }

    public void ScanDrive(DataDriveViewModel drive)
    {
      operationManager.Begin(new ScanOperation(), drive);
    }

    public void Recover(DataDriveViewModel drive)
    {
      operationManager.Begin(new RecoverOperation(), drive);
    }

    public void RemoveDrive(DataDriveViewModel drive)
    {
      operationManager.Begin(new RemoveDriveOperation(), drive);
    }

    public void Update(bool scanFirst = true)
    {
      updateParityStatusTimer.Start();
      operationManager.Begin(new UpdateOperation(scanFirst));
    }

    public void Hashcheck(DataDriveViewModel drive = null)
    {
      operationManager.Begin(new HashcheckOperation(), drive);
    }

    public void Undelete(DataDriveViewModel drive)
    {
      operationManager.Begin(new UndeleteOperation(), drive);
    }

    public void Verify()
    {
      operationManager.Begin(new VerifyOperation());
    }

    public void Reset()
    {
      if (MessageWindow.Show(owner, "Confirm parity reset", "Are you sure you want to delete all of your parity data?",
        MessageWindowIcon.Error, MessageWindowButton.YesNo) != true)
        return;
      paritySet.Erase();
      UpdateParityStatus();
    }

    private void HandleOperationFinished(object sender, EventArgs args)
    {
      UpdateParityStatus();
      updateParityStatusTimer.Stop();
    }

    public void Cancel()
    {
      operationManager.Cancel();
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

    public bool Empty { get { return paritySet.Empty; } }

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
        return operationManager.Busy;
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

