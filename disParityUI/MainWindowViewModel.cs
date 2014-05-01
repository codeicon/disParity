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
using System.Security.Cryptography;
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
    private System.Timers.Timer pingTimer;
    private OperationManager operationManager;
    private bool upgradeNotified;
    private Exception configLoadException;
    private DateTime nextHourlyUpdate = DateTime.MinValue;
    private DateTime nextDailyUpdate = DateTime.MinValue;
    private DateTime updateSoonBaseTime = DateTime.MinValue;
    private LogWindow logWindow;

    public MainWindowViewModel(Window owner)
    {
      this.owner = owner;
      // Set up application data and log folders
      string appDataPath = Utils.AppDataFolder;
      if (!Directory.Exists(appDataPath))
        Directory.CreateDirectory(appDataPath);
      string logPath = Path.Combine(appDataPath, "logs");
      if (!Directory.Exists(logPath))
        Directory.CreateDirectory(logPath);
      LogWindowViewModel.Initialize(this);
      LogFile.Open(Path.Combine(logPath, "disParity.log"), false);
      LogFile.Log("Application launched (version {0})", disParity.Version.VersionString);

      updateTimer = new System.Timers.Timer(1000);
      updateTimer.AutoReset = true;
      updateTimer.Elapsed += HandleUpdateTimer;

      updateParityStatusTimer = new System.Timers.Timer(1000);
      updateParityStatusTimer.AutoReset = true;
      updateParityStatusTimer.Elapsed += UpdateParityStatus;

      pingTimer = new System.Timers.Timer(TimeSpan.FromHours(24).TotalMilliseconds);
      pingTimer.AutoReset = true;
      pingTimer.Elapsed += HandlePingTimer;

      operationManager = new OperationManager(this);
      operationManager.OperationFinished += HandleOperationFinished;
      
      // Try to load the config.xml now, we need it to set the main window position.
      // If it fails, use default values for everything and display a dialog in Loaded()
      try {
        config = new Config(Path.Combine(Utils.AppDataFolder, "Config.xml"));
        if (config.Exists) {
          config.Load();
          config.Validate();
        }
        config.LastHourlyUpdate = DateTime.Now;
        config.Save();
      }
      catch (Exception e) {
        configLoadException = e;
        config.MakeBackup();
        config.Reset();
      }

      Left = config.MainWindowX;
      Top = config.MainWindowY;
      Height = config.MainWindowHeight;
      Width = config.MainWindowWidth;
    }

    /// <summary>
    /// Called from View when main window has loaded
    /// </summary>
    public void Loaded()
    {
      // Check for exception loading config file
      if (configLoadException != null) {
        App.LogCrash(configLoadException, "configLoadException");
        MessageWindow.Show(owner, "Invalid config file", "An error occurred loading the config file: \n\n" +
          configLoadException.Message + "\n\nDefault values will be used for most settings.",
          MessageWindowIcon.Error, MessageWindowButton.OK);
      }

      // test for FIPS Certified Cryptography
      try {
        MD5 md5 = new MD5CryptoServiceProvider();
      }
      catch (Exception e) {
        App.LogCrash(e, "MD5 test");
        MessageWindow.Show(owner, "FIPS requirement detected", "It appears that this system is configured to require FIPS certified cryptography. " +
          "Unfortunately disParity cannot run on a system with this requirement set.",
          MessageWindowIcon.Error, MessageWindowButton.OK);
        owner.Close();
      }

      // Make sure parity folder exists
      if (!String.IsNullOrEmpty(config.ParityDir)) {
        try {
          Directory.CreateDirectory(config.ParityDir);
        }
        catch (Exception e) {
          LogFile.Log("Could not create parity folder " + config.ParityDir + ": " + e.Message);
          App.LogCrash(e, "Create parity folder");
          MessageWindow.Show(owner, "Could not access parity folder", "Unable to access the parity location " + config.ParityDir + "\n\n" +
            "You will need to set a valid parity location (under Options) before proceeding.",
            MessageWindowIcon.Error, MessageWindowButton.OK);
        }
      }

      // the ParitySet constructor really, really should not fail.  If it does, there's nothing we can do,
      // so just let the global unhandled exception handler catch it, report it, and close the app.
      paritySet = new ParitySet(config, new disParityUI.Environment());

      try {
        paritySet.ReloadDrives();
      }
      catch (Exception e) {
        App.LogCrash(e, "paritySet.ReloadDrives");
        MessageWindow.Show(owner, "Can't load backup information", "An error occurred while trying to load the backup: \n\n" +
          e.Message, MessageWindowIcon.Error, MessageWindowButton.OK);
      }


      AddDrives();
      paritySet.PropertyChanged += HandleParitySetPropertyChanged;

      UpdateStartupMessage();
      UpdateParityStatus();

      try {
        if (!disParity.License.Accepted) {
          if (!ShowLicenseAgreement()) {
            owner.Close();
            return;
          }
          disParity.License.Accepted = true;
        }

        // check for new version now and again every 24 hours
        disParity.Version.DoUpgradeCheck(HandleNewVersionAvailable);
        pingTimer.Start();

        ScanAll();
      }
      catch (Exception e) {
        App.LogCrash(e, "Loaded() final");
        LogFile.Log("Exception in MainWindow.Loaded: " + e.Message);
      }


      // Start the update timer.  This regularly updates the main status text, and also
      // launches automatic updates if needed.
      updateTimer.Start();

      // set up for next automatic update
      if (config.MonitorDrives) {
        SetNextHourlyUpdateTime();
        SetNextDailyUpdateTime();
      }

    }

    private void SetNextHourlyUpdateTime()
    {
      if (Config.UpdateMode != UpdateMode.UpdateHourly)
        return;
      DateTime now = DateTime.Now;
      DateTime lastUpdate = config.LastHourlyUpdate;
      if (lastUpdate == DateTime.MinValue)
        // hourly update has never happened, pretend the last one was at midnight
        lastUpdate = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0);
      else
        // strip minutes and seconds from last hourly update
        lastUpdate = new DateTime(lastUpdate.Year, lastUpdate.Month, lastUpdate.Day, lastUpdate.Hour, 0, 0);
      nextHourlyUpdate = lastUpdate + TimeSpan.FromHours(Config.UpdateHours);
      // if that's still in the past, keep bumping it forward by UpdateHours until it's in the future
      while (nextHourlyUpdate < now)
        nextHourlyUpdate += TimeSpan.FromHours(Config.UpdateHours);
    }

    private void SetNextDailyUpdateTime()
    {
      if (Config.UpdateMode != UpdateMode.UpdateDaily)
        return;
      DateTime now = DateTime.Now;
      // next daily update is today, or tomorrow if daily time has already passed
      nextDailyUpdate = new DateTime(now.Year, now.Month, now.Day, Config.UpdateDaily.Hour, Config.UpdateDaily.Minute, 0);
      if (nextDailyUpdate < now)
        nextDailyUpdate += TimeSpan.FromHours(24);
    }

    private bool ShowLicenseAgreement()
    {
      LicenseWindow window = new LicenseWindow(owner, new LicenseWindowViewModel());
      bool? result = window.ShowDialog();
      return result ?? false;
    }

    private void HandlePingTimer(object sender, System.Timers.ElapsedEventArgs args)
    {
      disParity.Version.DoUpgradeCheck(HandleNewVersionAvailable);
    }

    private void HandleNewVersionAvailable(string newVersion)
    {
      // only bring up the new version dialog once per session no matter how long they leave the app running
      if (upgradeNotified)
        return;
      upgradeNotified = true;
      if (MessageWindow.Show(owner, "New version available", "There is a new " + (disParity.Version.Beta ? "beta " : "") + "version of disParity available.\r\n\r\n" +
        "Would you like to download the latest version now?", MessageWindowIcon.Caution, MessageWindowButton.YesNo) == true) {
        //Process.Start("http://www.vilett.com/disParity/beta.html");
        Process.Start("http://www.vilett.com/disParity/upgrade.html");
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

      if (StartupMessage == "" && optionsViewModel.IgnoresChanged)
        ScanAll();

      if (Config.MonitorDrives) {
        SetNextHourlyUpdateTime();
        SetNextDailyUpdateTime();
      }
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
      bool warned = false;
      if (Utils.PathsAreOnSameDrive(path, Config.ParityDir)) {
        bool? result = MessageWindow.Show(owner, "Duplicate drives detected!", "The path you selected appears to be on same drive as your parity.\n\n" +
          "This is not recommended.  If the drive fails, disParity will not be able to recover any of your data.\n\n" +
          "Are you sure you want to add this drive?", MessageWindowIcon.Error, MessageWindowButton.YesNo);
        if (result == false)
          return;
        warned = true;
      }

      if (!warned) // so we don't warn twice if the data and parity drives are already on the same drive as this one
        foreach (DataDrive d in paritySet.Drives)
          if (Utils.PathsAreOnSameDrive(path, d.Root)) {
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
      // drive.PropertyChanged += HandleDateDrivePropertyChanged;
      return vm;
    }

    /// <summary>
    /// Callback from RemoveDriveOperation when a drive has been removed
    /// </summary>
    public void DriveRemoved(DataDriveViewModel drive)
    {
      // drive.DataDrive.PropertyChanged -= HandleDateDrivePropertyChanged;
      // Can only modify the drives collection on the main thread
      Application.Current.Dispatcher.Invoke(new Action(() =>
      {
        drives.Remove(drive);
      }));
      // Make sure the FileSystemWatcher for this drive (if any) is disabled
      drive.DataDrive.DisableWatcher();
      // Update this to the current time to reset the update countdown timer; otherwise we might start an update immediately 
      updateSoonBaseTime = DateTime.Now;
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
      //if (e.PropertyName == "ChangesDetected") {
      //  if (!config.MonitorDrives)
      //    return;
      //  if (drive.ChangesDetected) {
      //    if (!updateTimer.Enabled)
      //      updateTimer.Start();
      //  }
      //  else if (updateTimer.Enabled) {
      //    if (AllDrivesUpToDate())
      //      updateTimer.Stop();
      //  }
      //}
    }

    private bool AllDrivesUpToDate()
    {
      foreach (DataDriveViewModel d in drives)
        if (d.DataDrive.DriveStatus != DriveStatus.UpToDate)
          return false;
      return true;
    }

    private bool AnyDriveHasError()
    {
      return drives.Where(d => d.DataDrive.DriveStatus == DriveStatus.AccessError).Count() > 0;
    }

    private void HandleUpdateTimer(object sender, System.Timers.ElapsedEventArgs args)
    {
      if (operationManager.Busy)
        return;
      UpdateStatus();
      if (!config.MonitorDrives || AllDrivesUpToDate() || AnyDriveHasError())
        return;

      DateTime now = DateTime.Now;
      if (now > NextAutoUpdate()) {
        Update(true);
        if (config.UpdateMode == UpdateMode.UpdateHourly) {
          Config.LastHourlyUpdate = now;
          Config.Save();
          nextHourlyUpdate += TimeSpan.FromHours(Config.UpdateHours);
        }
        else if (config.UpdateMode == UpdateMode.UpdateDaily) {
          nextDailyUpdate += TimeSpan.FromHours(24);
        }
      }
    }

    private DateTime NextAutoUpdate()
    {
      switch (Config.UpdateMode) {
        case UpdateMode.UpdateSoon:
          {
            DateTime start = (updateSoonBaseTime > paritySet.LastChange) ? updateSoonBaseTime : paritySet.LastChange;
            return start + TimeSpan.FromMinutes(config.UpdateDelay);
          }
        case UpdateMode.UpdateHourly:
          return nextHourlyUpdate;
        case UpdateMode.UpdateDaily:
          return nextDailyUpdate;
        default:
          return DateTime.MaxValue;
      }
    }

    /// <summary>
    /// Updates the main UI status message based on the current state of the app
    /// </summary>
    public void UpdateStatus()
    {
      // if an operation is currently running, the status is the status of the operation
      if (operationManager.Busy) 
      {
        Status = operationManager.Status;
        return;
      }
      // determine whether any drive needs a scan or an update
      bool scanRequired = false;
      bool updateRequired = false;
      foreach (DataDriveViewModel d in drives)
        if (d.DataDrive.DriveStatus == DriveStatus.ScanRequired)
          scanRequired = true;
        else if (d.DataDrive.DriveStatus == DriveStatus.UpdateRequired)
          updateRequired = true;

      if (!scanRequired && !updateRequired) 
      {
        long totalSize = 0;
        int totalFiles = 0;
        foreach (DataDriveViewModel d in drives) {
          totalSize += d.DataDrive.TotalFileSize;
          totalFiles += d.DataDrive.FileCount;
        }
        Status = String.Format("{1:N0} files ({0}) protected.  All drives up to date.",
          Utils.SmartSize(totalSize), totalFiles);
        return;
      }

      // If drive monitoring is on, we may be counting down to an update
      if (config.MonitorDrives) 
      {
        if (config.UpdateMode == UpdateMode.NoAction) 
        {
          Status = "Changes detected on one or more drives.  An update may be required.";
          return;
        }
        if (AnyDriveHasError())
        {
          Status = "Automatic updates disabled due to drive error(s)";
          return;
        }
        DateTime nextAutoUpdate = NextAutoUpdate();
        string status = updateRequired ? "Update required" : "Changes detected";
        if (nextAutoUpdate > DateTime.Now) 
        {
          if ((nextAutoUpdate - DateTime.Now).Duration() < TimeSpan.FromHours(1))
            Status = String.Format("{0}.  Backup will update automatically in {1}...", status, (nextAutoUpdate - DateTime.Now).ToString(@"m\:ss"));
          else
            Status = String.Format("{0}.  Next automatic update will occur {1} at {2}.", status, 
              (nextAutoUpdate.Day == DateTime.Now.Day) ? "today" : "tomorrow",  nextAutoUpdate.ToShortTimeString());
        }
      }
      else
        Status = "Update required.";


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

    public void Update(bool automatic = false)
    {
      updateParityStatusTimer.Start();
      operationManager.Begin(new UpdateOperation(automatic));
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
      updateSoonBaseTime = DateTime.Now;
      UpdateStatus();
      UpdateParityStatus();
      updateParityStatusTimer.Stop();
    }

    public void Cancel()
    {
      operationManager.Cancel();
      updateSoonBaseTime = DateTime.Now;
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

    public void ShowLogWindow()
    {
      logWindow = new LogWindow();
      logWindow.Show();
    }

    public bool LogWindowVisible()
    {
      if (logWindow == null)
        return false;
      else
        return logWindow.IsVisible;
    }

    internal void SetLogWindowState(WindowState state)
    {
      if (logWindow != null)
        logWindow.WindowState = state;
    }

    #region Properties

    public Config Config { get { return config; } }

    public ParitySet ParitySet { get { return paritySet; } }

    public Window Owner { get { return owner; } }

    public bool Empty { get { return (paritySet != null) && paritySet.Empty; } }

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
      private set
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

    //private DateTime NextAutoUpdate
    //{
    //  get
    //  {
    //    DateTime now = DateTime.Now;
    //    DateTime nextUpdate;
    //    switch (Config.UpdateMode) {

    //      case UpdateMode.UpdateSoon:
    //        return paritySet.LastChange + TimeSpan.FromMinutes(config.UpdateDelay);

    //      case UpdateMode.UpdateHourly:
    //        if (lastHourlyUpdate == DateTime.MinValue)
    //          nextUpdate = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0);
    //        else
    //          nextUpdate = lastUpdate;
    //        while (nextUpdate < now)
    //          nextUpdate += TimeSpan.FromHours(config.UpdateHours);
    //        return nextUpdate;

    //      case UpdateMode.UpdateDaily:
    //        nextUpdate = new DateTime(now.Year, now.Month, now.Day, config.UpdateDaily.Hour, config.UpdateDaily.Minute, 0);
    //        return nextUpdate;

    //      default:
    //        return DateTime.MinValue;
    //    }
    //  }
    //}

    #endregion

  }

}

