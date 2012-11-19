using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace disParityUI
{

  public partial class LicenseWindow : Window
  {

    public LicenseWindow(Window owner, LicenseWindowViewModel viewModel)
    {
      this.Owner = owner;
      InitializeComponent();
      Loaded += HandleLoaded;

      string licenseText = viewModel.LicenseText;
      FlowDocument flowDoc = new FlowDocument();
      flowDoc.Blocks.Add(new Paragraph(new Run(licenseText)));
      LicenseText.Document = flowDoc;
    }

    private void HandleLoaded(object sender, EventArgs args)
    {
      WindowUtils.RemoveCloseButton(this);
    }

    public void HandleAcceptClick(object Sender, RoutedEventArgs args)
    {
      DialogResult = true;
    }

    public void HandleDontAcceptClick(object Sender, RoutedEventArgs args)
    {
      DialogResult = false;
    }

  }
}
