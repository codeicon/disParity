using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using disParity;
using System.Windows.Input;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace disParityUI
{

  class DataDriveViewModel : NotifyPropertyChanged
  {

    private DateTime nextAutoScan;
    private System.Timers.Timer autoScanTimer;
    private Config config;

    private const int AUTO_SCAN_DELAY = 5; // time in seconds between changes detected and automatic scan

    public DataDriveViewModel(DataDrive dataDrive, Config config)
    {
      this.config = config;
      DataDrive = dataDrive;
      DataDrive.PropertyChanged += HandlePropertyChanged;
      DataDrive.ChangesDetected += HandleChangesDetected;
      UpdateStatus();
      UpdateFileCount();
      UpdateAdditionalInfo();

      autoScanTimer = new System.Timers.Timer(1000);
      autoScanTimer.AutoReset = true;
      autoScanTimer.Elapsed += HandleAutoScanTimer;
    }

    public void Scan(bool auto)
    {
      if (DataDrive.Scanning)
        return;
      autoScanTimer.Stop();
      Task.Factory.StartNew(() =>
      {
        try {
          DataDrive.Scan(auto);
          UpdateAdditionalInfo();
        }
        catch (Exception e) {
          LogFile.Log("Error occurred during scan of {0}: {1}", DataDrive.Root, e.Message);
        }
        finally {
          UpdateStatus();
          Progress = 0;
        }
      }
      );      
    }

    public DataDrive DataDrive { get; private set; }

    private void UpdateAdditionalInfo()
    {
      if (DataDrive.DriveType == DriveType.Network)
        AdditionalInfo = "Network drive";
      else
        AdditionalInfo = String.Format("{0} used {1} free",
          Utils.SmartSize(DataDrive.TotalSpace - DataDrive.FreeSpace),
          Utils.SmartSize(DataDrive.FreeSpace));
    }

    private void HandleChangesDetected(object sender, EventArgs e)
    {
      autoScanTimer.Stop();
      if (config.UpdateMode != UpdateMode.NoAction) {
        autoScanTimer.Start();
        nextAutoScan = DateTime.Now + TimeSpan.FromSeconds(AUTO_SCAN_DELAY);
      }
    }

    private void HandleAutoScanTimer(object sender, ElapsedEventArgs args)
    {
      if (nextAutoScan > DateTime.Now)
        DataDrive.Status = String.Format("Changes detected.  Scanning drive in {0}...",
          (nextAutoScan - DateTime.Now).ToString(@"m\:ss"));
      else {
        autoScanTimer.Stop();
        OperationManager.Instance.Begin(new ScanOperation(true));
      }
    }

    private void HandlePropertyChanged(object sender, PropertyChangedEventArgs e)
    {
      DataDrive drive = (DataDrive)sender;
      if (e.PropertyName == "Status")
        if (drive.Status != "")
          Status = drive.Status;
        else
          UpdateStatus();
      else if (e.PropertyName == "Progress")
        Progress = drive.Progress;
      else if (e.PropertyName == "DriveStatus")
        UpdateStatus();
      else if (e.PropertyName == "FileCount")
        UpdateFileCount();
    }

    private void UpdateFileCount()
    {
      if (DataDrive.FileCount == 0)
        FileCount = "0";
      else
        FileCount = String.Format("{0} ({1})", DataDrive.FileCount, Utils.SmartSize(DataDrive.TotalFileSize));
    }

    private void UpdateStatus()
    {
      switch (DataDrive.DriveStatus) {
        case DriveStatus.ScanRequired:
          StatusIcon = Icons.Unknown;
          break;
        case DriveStatus.UpdateRequired:
          int addCount = DataDrive.Adds.Count;
          int deleteCount = DataDrive.Deletes.Count;
          if (addCount == 0 && deleteCount == 0) {
            Status = "Up to date";
            StatusIcon = Icons.Good;
            break;
          }
          Status = String.Format("Update Required ({0} new, {1} deleted)", addCount, deleteCount);
          if (deleteCount > 0)
            StatusIcon = Icons.Urgent;
          else
            StatusIcon = Icons.Caution;
          break;
        case DriveStatus.UpToDate:
          Status = "Up to date";
          StatusIcon = Icons.Good;
          break;
        case DriveStatus.AccessError:
          Status = "Error: " + DataDrive.LastError;
          StatusIcon = Icons.Urgent;
          break;
        case DriveStatus.Scanning:
        case DriveStatus.ReadingFile:
          // don't do anything, DataDrive sets the Status property string for this
          break;
      }
    }

    #region Properties

    public string Root
    {
      get
      {
        string volumeLabel = DataDrive.VolumeLabel;
        if (String.IsNullOrEmpty(volumeLabel))
          return DataDrive.Root;
        else
          return String.Format("{0} ({1})", DataDrive.Root, volumeLabel);
      }
    }

    private ImageSource statusIcon;
    public ImageSource StatusIcon
    {
      get 
      { 
        return statusIcon; 
      }
      set
      {
        SetProperty(ref statusIcon, "StatusIcon", value);
      }
    }

    private string additionalInfo;
    public string AdditionalInfo
    {
      get
      {
        return additionalInfo;
      }
      set
      {
        SetProperty(ref additionalInfo, "AdditionalInfo", value);
      }
    }

    private string fileCount;
    public string FileCount
    {
      get
      {
        return fileCount;
      }
      set
      {
        SetProperty(ref fileCount, "FileCount", value);
      }
    }

    private string status = "Unknown";
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


    private Brush statusColor = Brushes.Black;
    public Brush StatusColor
    {
      get
      {
        return statusColor;
      }
      set
      {
        SetProperty(ref statusColor, "StatusColor", value);
      }
    }

    #endregion


  }

}
