using System;
using System.Xml;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace disParity
{

  public enum UpdateMode
  {
    NoAction,
    ScanOnly,
    ScanAndUpdate
  }

  public class Config
  {

    private string filename;

    const Int32 VERSION = 1;
    const UInt32 DEFAULT_MAX_TEMP_RAM = 512;
    const bool DEFAULT_IGNORE_HIDDEN = true;
    const bool DEFAULT_MONITOR_DRIVES = true;
    const UpdateMode DEFAULT_UPDATE_MODE = UpdateMode.ScanAndUpdate;
    const int DEFAULT_UPDATE_DELAY = 1;
    const int DEFAULT_MAIN_WINDOW_X = 200;
    const int DEFAULT_MAIN_WINDOW_Y = 200;
    const int DEFAULT_MAIN_WINDOW_WIDTH = 640;
    const int DEFAULT_MAIN_WINDOW_HEIGHT = 480;

    public Config(string filename)
    {
      this.filename = filename;
      MaxTempRAM = DEFAULT_MAX_TEMP_RAM;
      IgnoreHidden = DEFAULT_IGNORE_HIDDEN;
      MainWindowX = DEFAULT_MAIN_WINDOW_X;
      MainWindowY = DEFAULT_MAIN_WINDOW_Y;
      MainWindowWidth = DEFAULT_MAIN_WINDOW_WIDTH;
      MainWindowHeight = DEFAULT_MAIN_WINDOW_HEIGHT;
      MonitorDrives = DEFAULT_MONITOR_DRIVES;
      UpdateDelay = DEFAULT_UPDATE_DELAY;
      UpdateMode = DEFAULT_UPDATE_MODE;
      Ignores = new List<string>();
      Drives = new List<Drive>();
    }

    public void ImportOld(string path)
    {
      if (File.Exists(path)) {
        OldConfig oldConfig = new OldConfig(path);
        ParityDir = oldConfig.ParityDir;
        TempDir = oldConfig.TempDir;
        MaxTempRAM = oldConfig.MaxTempRAM;
        IgnoreHidden = oldConfig.IgnoreHidden;
        for (int i = 0; i < oldConfig.BackupDirs.Length; i++)
          Drives.Add(new Drive(oldConfig.BackupDirs[i], String.Format("files{0}.dat", i)));
        foreach (string i in oldConfig.Ignores)
          Ignores.Add(i);
        Save();
      }
    }

    public bool Exists
    {
      get
      {
        return File.Exists(filename);
      }
    }

    public void Load()
    {
      if (!File.Exists(filename))
        return;
      using (XmlReader reader = XmlReader.Create(new StreamReader(filename))) {
        for (; ; ) {
          reader.Read();
          if (reader.EOF)
            break;
          if (reader.NodeType == XmlNodeType.Whitespace)
            continue;
          if (reader.Name == "Options" && reader.IsStartElement()) {
            for (; ; ) {
              if (!reader.Read() || reader.EOF)
                break;
              if (reader.NodeType == XmlNodeType.Whitespace)
                continue;
              else if (reader.NodeType == XmlNodeType.EndElement)
                break;
              if (reader.Name == "TempDir") {
                reader.Read();
                TempDir = reader.Value;
                reader.Read();
              }
              else if (reader.Name == "MaxTempRAM") {
                reader.Read();
                MaxTempRAM = Convert.ToUInt32(reader.Value);
                reader.Read();
              }
              else if (reader.Name == "IgnoreHidden") {
                reader.Read();
                IgnoreHidden = (reader.Value == "true") ? true : false;
                reader.Read();
              }
              else if (reader.Name == "MonitorDrives") {
                reader.Read();
                MonitorDrives = (reader.Value == "true") ? true : false;
                reader.Read();
              }
              else if (reader.Name == "UpdateDelay") {
                reader.Read();
                UpdateDelay = Convert.ToUInt32(reader.Value);
                reader.Read();
              }
              else if (reader.Name == "UpdateMode") {
                reader.Read();
                int mode = Convert.ToInt32(reader.Value);
                reader.Read();
                if (mode == 1)
                  UpdateMode = UpdateMode.NoAction;
                else if (mode == 2)
                  UpdateMode = UpdateMode.ScanOnly;
                else if (mode == 3)
                  UpdateMode = UpdateMode.ScanAndUpdate;
              }
              else if (reader.Name == "Ignores") {
                for (; ; ) {
                  if (!reader.Read() || reader.EOF)
                    break;
                  if (reader.NodeType == XmlNodeType.Whitespace)
                    continue;
                  else if (reader.NodeType == XmlNodeType.EndElement)
                    break;
                  if (reader.Name == "Ignore" && reader.IsStartElement()) {
                    reader.Read();
                    Ignores.Add(reader.Value);
                    reader.Read(); // skip end element
                  }
                }
              }
            }
          }
          else if (reader.Name == "Parity")
            ParityDir = reader.GetAttribute("Path");
          else if (reader.Name == "Layout" && reader.IsStartElement()) {
            for (; ; ) {
              if (!reader.Read() || reader.EOF)
                break;
              if (reader.NodeType == XmlNodeType.Whitespace)
                continue;
              else if (reader.NodeType == XmlNodeType.EndElement)
                break;
              else if (reader.Name == "MainWindowX") {
                reader.Read();
                MainWindowX = Convert.ToInt32(reader.Value);
                reader.Read();
              }
              else if (reader.Name == "MainWindowY") {
                reader.Read();
                MainWindowY = Convert.ToInt32(reader.Value);
                reader.Read();
              }
              else if (reader.Name == "MainWindowWidth") {
                reader.Read();
                MainWindowWidth = Convert.ToInt32(reader.Value);
                reader.Read();
              }
              else if (reader.Name == "MainWindowHeight") {
                reader.Read();
                MainWindowHeight = Convert.ToInt32(reader.Value);
                reader.Read();
              }
            }
          }
          else if (reader.Name == "Drives" && reader.IsStartElement()) {
            for (; ; ) {
              if (!reader.Read() || reader.EOF)
                break;
              if (reader.NodeType == XmlNodeType.Whitespace)
                continue;
              else if (reader.NodeType == XmlNodeType.EndElement)
                break;
              if (reader.Name == "Drive")
                Drives.Add(new Drive(reader.GetAttribute("Path"), reader.GetAttribute("Meta")));
            }
          }
        }
      }
    }

    public void Save()
    {
      XmlWriterSettings settings = new XmlWriterSettings();
      settings.Indent = true; 
      using (XmlWriter writer = XmlWriter.Create(filename, settings)) {
        writer.WriteStartDocument();
        writer.WriteStartElement("disParity");
        writer.WriteAttributeString("Version", VERSION.ToString());
        writer.WriteStartElement("Options");

        if (!String.IsNullOrEmpty(tempDir))
          writer.WriteElementString("TempDir", tempDir);

        if (MaxTempRAM != DEFAULT_MAX_TEMP_RAM)
          writer.WriteElementString("MaxTempRAM", MaxTempRAM.ToString());

        if (IgnoreHidden != DEFAULT_IGNORE_HIDDEN)
          writer.WriteElementString("IgnoreHidden", IgnoreHidden ? "true" : "false");

        if (MonitorDrives != DEFAULT_MONITOR_DRIVES)
          writer.WriteElementString("MonitorDrives", MonitorDrives ? "true" : "false");

        if (UpdateDelay != DEFAULT_UPDATE_DELAY)
          writer.WriteElementString("UpdateDelay", UpdateDelay.ToString());

        if (UpdateMode != DEFAULT_UPDATE_MODE) {
          int mode = 0;
          if (UpdateMode == UpdateMode.NoAction)
            mode = 1;
          else if (UpdateMode == UpdateMode.ScanOnly)
            mode = 2;
          else if (UpdateMode == UpdateMode.ScanAndUpdate)
            mode = 3;
          writer.WriteElementString("UpdateMode", mode.ToString());
        }

        if (Ignores.Count > 0) {
          writer.WriteStartElement("Ignores");
          foreach (string i in Ignores)
            writer.WriteElementString("Ignore", i);
          writer.WriteEndElement();
        }

        writer.WriteEndElement(); // Options

        writer.WriteStartElement("Layout");

        writer.WriteElementString("MainWindowX", MainWindowX.ToString());
        writer.WriteElementString("MainWindowY", MainWindowY.ToString());
        writer.WriteElementString("MainWindowWidth", MainWindowWidth.ToString());
        writer.WriteElementString("MainWindowHeight", MainWindowHeight.ToString());

        writer.WriteEndElement(); // Layout

        writer.WriteStartElement("Parity");
        writer.WriteAttributeString("Path", ParityDir);
        writer.WriteEndElement();

        writer.WriteStartElement("Drives");
        foreach (Drive d in Drives) {
          writer.WriteStartElement("Drive");
          writer.WriteAttributeString("Path", d.Path);
          writer.WriteAttributeString("Meta", d.Metafile);
          writer.WriteEndElement();
        }
        writer.WriteEndElement();

        writer.WriteEndElement();

        writer.WriteEndDocument();
      }
    }

    public string Filename
    {
      get
      {
        return filename;
      }
    }

    public string ParityDir { get; set; }

    private string tempDir;
    public string TempDir 
    {
      get
      {
        if (String.IsNullOrEmpty(tempDir))
          return Path.Combine(Path.GetTempPath(), "disParity");
        else
          return tempDir;
      }
      set
      {
        tempDir = value;
      }
    }

    public UInt32 MaxTempRAM { get; set; } // in megabytes

    public bool IgnoreHidden { get; set; }

    public List<Drive> Drives { get; set; }

    public List<string> Ignores { get; set; }

    public int MainWindowWidth { get; set; }

    public int MainWindowHeight { get; set; }

    public int MainWindowX { get; set; }

    public int MainWindowY { get; set; }

    public bool MonitorDrives { get; set; }

    public UInt32 UpdateDelay { get; set; }

    public UpdateMode UpdateMode { get; set; }

  }

  public class Drive
  {
    public Drive(string path, string metafile)
    {
      Path = path;
      Metafile = metafile;
    }

    public string Path { get; set; }
    public string Metafile { get; set; }
  }

}
