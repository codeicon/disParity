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
using System.Windows.Threading;

namespace disParityUI
{

  class DataDriveViewModel : INotifyPropertyChanged
  {

    private DispatcherTimer updateStatusTimer;

    public event PropertyChangedEventHandler PropertyChanged;

    public DataDriveViewModel(DataDrive dataDrive)
    {
      DataDrive = dataDrive;
      DataDrive.ProgressReport += HandleProgressReport;
      DataDrive.StatusChanged += HandleStatusChanged;
      DataDrive.UpdateProgress += HandleUpdateProgress;
      DataDrive.ReadingFile += HandleReadingFile;
      UpdateStatus();
      FileCount = String.Format("{0} ({1})", DataDrive.FileCount, Utils.SmartSize(DataDrive.TotalFileSize));
      updateStatusTimer = new DispatcherTimer();
      updateStatusTimer.Interval = TimeSpan.FromSeconds(1);
      updateStatusTimer.Tick += HandleUpdateStatus;
      updateStatusTimer.Stop();
    }

    public void Scan()
    {
      Task.Factory.StartNew(() =>
      {
        DataDrive.Scan();
      }
      );      
    }

    public DataDrive DataDrive { get; private set; }

    private void HandleUpdateStatus(object sender, EventArgs args)
    {
      UpdateStatus();
      updateStatusTimer.Stop();
    }

    private void HandleReadingFile(object sender, ReadingFileEventArgs args)
    {
      Status = "Reading " + args.Filename;
      updateStatusTimer.Start();
    }

    private void HandleProgressReport(object sender, ProgressReportEventArgs args)
    {
      if (!String.IsNullOrEmpty(args.Status))
        Status = args.Status;
      Progress = args.Progress;
    }

    private void HandleStatusChanged(object sender, StatusChangedEventArgs args)
    {
      if (args.Status == DriveStatus.UpdateRequired) {
        Status = String.Format("Update Required ({0} new, {1} deleted, {2} moved)",
          args.AddCount, args.DeleteCount, args.MoveCount);
        WarningLevel = "Medium";
      } 
      else
        UpdateStatus();
      FileCount = String.Format("{0} ({1})", DataDrive.FileCount,
        Utils.SmartSize(DataDrive.TotalFileSize));
      Progress = 0;
    }

    private void HandleUpdateProgress(object sender, UpdateProgressEventArgs args)
    {
      if (!String.IsNullOrEmpty(args.Status))
        Status = args.Status;
      FileCount = String.Format("{0} ({1})", args.Files, Utils.SmartSize(args.Size));
      Progress = args.Progress;
    }

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

    public string AdditionalInfo
    {
      get
      {
        if (DataDrive.DriveType == DriveType.Network)
          return "Network drive";
        else
          return String.Format("{0} used {1} free",
            Utils.SmartSize(DataDrive.TotalSpace - DataDrive.FreeSpace),
            Utils.SmartSize(DataDrive.FreeSpace));
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
        fileCount = value;
        FirePropertyChanged("FileCount");
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
        if (status != value) {
          status = value;
          FirePropertyChanged("Status");
        }
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
        progress = value;
        FirePropertyChanged("Progress");
      }
    }

    private string warningLevel;
    public string WarningLevel
    {
      get
      {
        return warningLevel;
      }
      set
      {
        warningLevel = value;
        FirePropertyChanged("WarningLevel");
      }
    }

    public bool NeedsUpdate
    {
      get
      {
        return (DataDrive.Status == DriveStatus.UpdateRequired);
      }
    }

    public void UpdateStatus()
    {
      switch (DataDrive.Status) {
        case DriveStatus.ScanRequired:
          Status = "Unknown (scan required)";
          WarningLevel = "High";
          break;
        case DriveStatus.UpdateRequired:
          Status = "Update required";
          WarningLevel = "Medium";
          break;
        case DriveStatus.UpToDate:
          Status = "Up to date";
          WarningLevel = "Low";
          break;
        case DriveStatus.AccessError:
          Status = "Error: " + DataDrive.LastError;
          WarningLevel = "High";
          break;
      }
    }

    private void FirePropertyChanged(string name)
    {
      if (PropertyChanged != null)
        PropertyChanged(this, new PropertyChangedEventArgs(name));
    }
  }

}
