using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Input;

namespace disParityUI
{

  internal class Commands
  {

    public static RoutedUICommand AddDrive;
    public static RoutedUICommand ScanDrive;
    public static RoutedUICommand ScanAll;
    public static RoutedUICommand UpdateAll;
    public static RoutedUICommand RecoverDrive;
    public static RoutedUICommand Options;
    public static RoutedUICommand About;

    static Commands()
    {
      AddDrive = new RoutedUICommand("Add Drive...", "AddDrive", typeof(MainWindow));
      ScanDrive = new RoutedUICommand("Scan Drive", "ScanDrive", typeof(MainWindow));
      ScanAll = new RoutedUICommand("Scan All", "ScanAll", typeof(MainWindow));
      UpdateAll = new RoutedUICommand("Update All", "UpdateAll", typeof(MainWindow));
      RecoverDrive = new RoutedUICommand("Recover Drive...", "RecoverDrive", typeof(MainWindow));
      Options = new RoutedUICommand("Options...", "Options", typeof(MainWindow));
      About = new RoutedUICommand("About...", "About", typeof(MainWindow));
    }

  }

}
