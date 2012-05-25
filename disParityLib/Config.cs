using System;
using System.Xml;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace disParity
{

  public class Config
  {

    private string filename;

    const string DEFAULT_TEMP_DIR = ".\\";
    const UInt32 DEFAULT_MAX_TEMP_RAM = 512;
    const bool DEFAULT_IGNORE_HIDDEN = true;

    public Config(string filename)
    {
      this.filename = filename;
      MaxTempRAM = DEFAULT_MAX_TEMP_RAM;
      IgnoreHidden = DEFAULT_IGNORE_HIDDEN;
      TempDir = DEFAULT_TEMP_DIR;
      Ignores = new List<string>();
      Drives = new List<string>();
    }

    public void Load()
    {
    }

    public void Save()
    {
      XmlWriterSettings settings = new XmlWriterSettings();
      settings.Indent = true; 
      using (XmlWriter writer = XmlWriter.Create(filename, settings)) {
        writer.WriteStartDocument();
        writer.WriteStartElement("disParity");
        writer.WriteStartElement("Options");

        if (!String.Equals(TempDir, DEFAULT_TEMP_DIR))
          writer.WriteElementString("TempDir", TempDir);

        if (MaxTempRAM != DEFAULT_MAX_TEMP_RAM)
          writer.WriteElementString("MaxTempRAM", MaxTempRAM.ToString());

        if (IgnoreHidden != DEFAULT_IGNORE_HIDDEN)
          writer.WriteElementString("IgnoreHidden", IgnoreHidden ? "true" : "false");

        if (Ignores.Count > 0) {
          writer.WriteStartElement("Ignores");
          foreach (string i in Ignores)
            writer.WriteElementString("Ignore", i);
          writer.WriteEndElement();
        }

        writer.WriteEndElement(); // Options

        writer.WriteStartElement("Parity");
        writer.WriteAttributeString("Path", ParityDir);
        writer.WriteEndElement();

        writer.WriteStartElement("Drives");
        foreach (string s in Drives) {
          writer.WriteStartElement("Drive");
          writer.WriteAttributeString("Path", s);
          writer.WriteEndElement();
        }
        writer.WriteEndElement();



        writer.WriteEndElement();

        writer.WriteEndDocument();
      }
    }

    public string ParityDir { get; set; }

    public string TempDir { get; set; }

    public UInt32 MaxTempRAM { get; set; }

    public bool IgnoreHidden { get; set; }

    public List<string> Drives { get; set; }

    public List<string> Ignores { get; set; }

  }

}
