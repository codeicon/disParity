using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Timers;

namespace disParity
{
  class Monitor
  {

    static System.Timers.Timer timer;
    static AutoResetEvent timerEvent;

    public static void Start()
    {

      timer = new System.Timers.Timer();
      timer.Stop();
      timer.Interval = 5000;
      timer.Elapsed += HandleTimer;
      timerEvent = new AutoResetEvent(false);

      foreach (DataPath d in Program.drives) {
        d.watcher = new FileSystemWatcher(d.root);
        d.watcher.NotifyFilter = NotifyFilters.Attributes |
          NotifyFilters.DirectoryName | NotifyFilters.FileName |
          NotifyFilters.LastWrite | NotifyFilters.Size;
        d.watcher.Filter = "*";
        d.watcher.Created += HandleFileCreated;
        d.watcher.Changed += HandleFileChanged;
        d.watcher.Deleted += HandleFileDeleted;
        d.watcher.Renamed += HandleFileRenamed;
        d.watcher.IncludeSubdirectories = true;
        d.watcher.EnableRaisingEvents = true;
        d.eventQueue = new ArrayList();
      }

      for (; ; ) {
        timerEvent.WaitOne();
        Program.logFile.Write("{0} timerEvent SIGNALLED\r\n", DateTime.Now);

        /* Cases to think about:
         * 
         * File is added, then renamed before add can be processed.  Add fails 
         * because the old file name is no longer valid.  Rename fails because 
         * the file isn't known.  HandleFileRenamed currently handles this by
         * checking for ADD events on a rename and updating the ADD to the new name.
         * 
         * File is renamed, then deleted before rename can be processed.  Rename
         * fails because new name doesn't exist, then delete fails because it
         * refers to an unknown file.
         * 
         * File move within a drive looks like:
         *   * A DELETE of the old file
         *   * A CREATE of the new file
         *   * A CHANGE of the new folder (currently ignored)
         *   * A CHANGE of the new file
         */

        foreach (DataPath d in Program.drives)
          ProcessEventQueue(d);

        Program.logFile.Flush();

      }
    }

    static void HandleTimer(object source, ElapsedEventArgs e)
    {
      timer.Stop();
      Program.logFile.Write("{0} timerEvent SET\r\n", DateTime.Now);
      timerEvent.Set();
    }

    static void ProcessEventQueue(DataPath d)
    {
      /* Work on a copy of the current queue and clear the real one, so that
       * new events can continue to be queued while this code is running. */
      ArrayList copy;
      lock (d.eventQueue.SyncRoot) {
        if (d.eventQueue.Count == 0)
          return;
        copy = (ArrayList)d.eventQueue.Clone();
        d.eventQueue.Clear();
      }
      Program.logFile.Write("Processing {0} new event{1} for {2}...\r\n", 
        copy.Count, copy.Count == 1 ? "" : "s", d.root);
      foreach (FileEvent e in copy)
        switch (e.eventType) {
          case FileEventType.ADD:
            ProcessAddEvent(d, e);
            break;
          case FileEventType.DELETE:
            ProcessDeleteEvent(d, e);
            break;
          case FileEventType.EDIT:
            ProcessEditEvent(d, e);
            break;
          case FileEventType.RENAME:
            ProcessRenameEvent(d, e);
            break;
        }
    }

    static void ProcessAddEvent(DataPath d, FileEvent e)
    {
      if (!File.Exists(e.fullPath)) {
        Program.logFile.Write("Cannot process ADD event: {0} does not exist.\r\n",
          e.fullPath);
        return;
      }
      FileInfo info = new FileInfo(e.fullPath);
      string path = DataPath.StripRoot(d.root, info.DirectoryName);
      FileRecord r = new FileRecord(info, path, DriveNum(d));
      Program.AddFileToParity(d, r);
    }

    static void ProcessDeleteEvent(DataPath d, FileEvent e)
    {
      FileRecord r = d.FindFile(e.fullPath);
      if (r == null) {
        Program.logFile.Write("Cannot process DELETE event: {0} is unknown.\r\n",
          e.fullPath);
        return;
      }
      Program.RemoveFileFromParity(d, r);
    }

    static void ProcessEditEvent(DataPath d, FileEvent e)
    {
    }

    static void ProcessRenameEvent(DataPath d, FileEvent e)
    {
      if (Directory.Exists(e.fullPath))
        // a directory was renamed, handle differently 
        return;
      FileRecord r = d.FindFile(e.oldPath);
      if (r == null) {
        Program.logFile.Write("Cannot process RENAME event: {0} is unknown.\r\n",
          e.fullPath);
        return;
      }
      d.RenameFile(r, e.fullPath);
    }

    static Int32 DriveNum(DataPath d)
    {
      for (Int32 i = 0; i < Program.drives.Length; i++)
        if (d == Program.drives[i])
          return i;
      return -1;
    }

    static DataPath DataPathFromWatcher(Object watcher)
    {
      foreach (DataPath d in Program.drives)
        if (d.watcher == watcher)
          return d;
      return null;
    }

    static void HandleFileCreated(Object sender, FileSystemEventArgs args)
    {
      Program.logFile.Write("{0} event: {1}\r\n",
        args.ChangeType, args.FullPath);
      FileEvent e = new FileEvent(FileEventType.ADD, args.FullPath);
      DataPath d = DataPathFromWatcher(sender);
      lock (d.eventQueue.SyncRoot) {
        d.eventQueue.Add(e);
      }
    }

    static void HandleFileDeleted(Object sender, FileSystemEventArgs args)
    {
      Program.logFile.Write("{0} event: {1}\r\n",
        args.ChangeType, args.FullPath);
      DataPath d = DataPathFromWatcher(sender);

      /* If there is an unprocessed add for this file, the two cancel each
       * other out. */
      lock (d.eventQueue.SyncRoot) {
        foreach (FileEvent e in d.eventQueue)
          if (e.IsAdd && (e.fullPath == args.FullPath)) {
            d.eventQueue.Remove(e); 
            return;
          }
      }

      /* If there is an unprocessed edit for this file, the edit can be
       * discarded since we are about to delete it. */
      lock (d.eventQueue.SyncRoot) {
        foreach (FileEvent e in d.eventQueue)
          if (e.IsEdit && (e.fullPath == args.FullPath)) {
            d.eventQueue.Remove(e);
            break;
          }
      }

      FileEvent ev = new FileEvent(FileEventType.DELETE, args.FullPath);
      lock (d.eventQueue.SyncRoot) {
        d.eventQueue.Add(ev);
      }

      timer.Stop();
      timer.Start();

    }

    static void HandleFileChanged(Object sender, FileSystemEventArgs args)
    {
      Program.logFile.Write("{0} event: {1}\r\n",
        args.ChangeType, args.FullPath);
      DataPath d = DataPathFromWatcher(sender);
      FileEventType eventType = FileEventType.EDIT;

      /* Ignore change events for folders */
      if (Directory.Exists(args.FullPath))
        return;

      /* Most new files appear as a create followed by a change.  So if there 
       * is still an unprocessed add for this file, remove that from the queue,
       * and add this changed event as an add instead.  */
      lock (d.eventQueue.SyncRoot) {
        foreach (FileEvent e in d.eventQueue)
          if (e.IsAdd && (e.fullPath == args.FullPath)) {
            d.eventQueue.Remove(e);
            eventType = FileEventType.ADD;
            break;
          }
      }

      FileEvent ev = new FileEvent(eventType, args.FullPath);
      lock (d.eventQueue.SyncRoot) {
        d.eventQueue.Add(ev);
      }
      timer.Stop();
      timer.Start();

    }

    static void HandleFileRenamed(Object sender, RenamedEventArgs args)
    {
      Program.logFile.Write("Renamed event: From {0} to {1}\r\n",
        args.OldFullPath, args.FullPath);
      DataPath d = DataPathFromWatcher(sender);

      /* See if there are any unprocessed adds for this file */
      lock (d.eventQueue.SyncRoot) {
        foreach (FileEvent e in d.eventQueue)
          if (e.IsAdd && (e.fullPath == args.OldFullPath)) {
            e.fullPath = args.FullPath;
            return;
          }
      }

      FileEvent ev = new FileEvent(FileEventType.RENAME, args.FullPath);
      ev.oldPath = args.OldFullPath;
      lock (d.eventQueue.SyncRoot) {
        d.eventQueue.Add(ev);
      }

      timer.Stop();
      timer.Start();

    }

    static void HandleError(Object sender, ErrorEventArgs args)
    {
      Program.logFile.Write("ERROR EVENT: {0}\r\n",
        args.GetException().Message);
    }

  }

  public enum FileEventType
  {
    ADD, DELETE, EDIT, RENAME
  }

  class FileEvent
  {

    public FileEventType eventType;
    public string fullPath;
    public string oldPath;
    public DateTime timestamp;

    public FileEvent(FileEventType eventType, string fullPath)
    {
      this.eventType = eventType;
      this.fullPath = fullPath;
      this.timestamp = DateTime.Now;
    }

    public bool IsAdd
    {
      get { return eventType == FileEventType.ADD; }
    }

    public bool IsEdit
    {
      get { return eventType == FileEventType.EDIT; }
    }

  }
}
