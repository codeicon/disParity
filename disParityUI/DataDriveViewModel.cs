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
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace disParityUI
{

  class DataDriveViewModel : ViewModel
  {

    private DispatcherTimer updateStatusTimer;

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
      updateStatusTimer.Tick += HandleUpdateStatusTimerTick;
      updateStatusTimer.Stop();
      UpdateAdditionalInfo();
    }

    public void Scan()
    {
      Task.Factory.StartNew(() =>
      {
        try {
          DataDrive.Scan();
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

    private void HandleUpdateStatusTimerTick(object sender, EventArgs args)
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
      if (!String.IsNullOrEmpty(args.Message))
        Status = args.Message;
      Progress = args.Progress;
    }

    private void HandleStatusChanged(object sender, StatusChangedEventArgs args)
    {
      /*
      if (args.Status == DriveStatus.UpdateRequired) {
        Status = String.Format("Update Required ({0} new, {1} deleted, {2} moved)",
          args.AddCount, args.DeleteCount, args.MoveCount);
        if (args.DeleteCount > 0 || args.MoveCount > 0)
          StatusIcon = Icons.Urgent;
        else
          StatusIcon = Icons.Caution;
      } 
      else
        UpdateStatus();
       */
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

    public void UpdateStatus()
    {
      switch (DataDrive.Status) {
        case DriveStatus.ScanRequired:
          Status = "Unknown (scan required)";
          StatusIcon = Icons.Unknown;
          break;
        case DriveStatus.UpdateRequired:
          Status = String.Format("Update Required ({0} new, {1} deleted, {2} moved)",
            DataDrive.Adds.Count, DataDrive.Deletes.Count, DataDrive.Moves.Count());
          if (DataDrive.Deletes.Count > 0 || DataDrive.Moves.Count() > 0)
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
      }
    }

  }

}
