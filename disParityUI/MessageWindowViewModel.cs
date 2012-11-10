using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace disParityUI
{

  internal class MessageWindowViewModel : ViewModel
  {

    public MessageWindowViewModel(string caption, string message, MessageWindowIcon icon, MessageWindowButton buttons)
    {
      Caption = caption;
      Message = message;

      switch (icon) {
        case MessageWindowIcon.OK:
          Icon = Icons.Good;
          break;
        case MessageWindowIcon.Caution:
          Icon = Icons.Caution;
          break;
        case MessageWindowIcon.Error:
          Icon = Icons.Urgent;
          break;
        case MessageWindowIcon.Question:
          Icon = Icons.Unknown;
          break;
      }

      switch (buttons) {
        case MessageWindowButton.OK:
          YesButtonVisibility = Visibility.Collapsed;
          NoButtonVisibility = Visibility.Collapsed;
          CancelButtonVisibility = Visibility.Collapsed;
          OKButtonVisibility = Visibility.Visible;
          break;
        case MessageWindowButton.OKCancel:
          YesButtonVisibility = Visibility.Collapsed;
          NoButtonVisibility = Visibility.Collapsed;
          CancelButtonVisibility = Visibility.Visible;
          OKButtonVisibility = Visibility.Visible;
          break;
        case MessageWindowButton.YesNo:
          YesButtonVisibility = Visibility.Visible;
          NoButtonVisibility = Visibility.Visible;
          CancelButtonVisibility = Visibility.Collapsed;
          OKButtonVisibility = Visibility.Collapsed;
          break;
      }
    }

    #region Properties

    private ImageSource icon;
    public ImageSource Icon
    {
      get
      {
        return icon;
      }
      set
      {
        SetProperty(ref icon, "Icon", value);
      }
    }

    private string caption;
    public string Caption
    {
      get
      {
        return caption;
      }
      set
      {
        SetProperty(ref caption, "Caption", value);
      }
    }

    private string message;
    public string Message
    {
      get
      {
        return message;
      }
      set
      {
        SetProperty(ref message, "Message", value);
      }
    }

    private Visibility yesButtonVisibility;
    public Visibility YesButtonVisibility
    {
      get
      {
        return yesButtonVisibility;
      }
      set
      {
        SetProperty(ref yesButtonVisibility, "YesButtonVisibility", value);
      }
    }

    private Visibility noButtonVisibility;
    public Visibility NoButtonVisibility
    {
      get
      {
        return noButtonVisibility;
      }
      set
      {
        SetProperty(ref noButtonVisibility, "NoButtonVisibility", value);
      }
    }

    private Visibility okButtonVisibility;
    public Visibility OKButtonVisibility
    {
      get
      {
        return okButtonVisibility;
      }
      set
      {
        SetProperty(ref okButtonVisibility, "OKButtonVisibility", value);
      }
    }

    private Visibility cancelButtonVisibility;
    public Visibility CancelButtonVisibility
    {
      get
      {
        return cancelButtonVisibility;
      }
      set
      {
        SetProperty(ref cancelButtonVisibility, "CancelButtonVisibility", value);
      }
    }

    #endregion

  }

  internal enum MessageWindowIcon
  {
    OK,
    Caution,
    Error,
    Question
  }

  internal enum MessageWindowButton
  {
    OK,
    OKCancel,
    YesNo
  }

}
