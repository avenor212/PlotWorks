Imports WPF.Ui
Imports WPF.Ui.Abstractions
Imports WPF.Ui.Controls
Class MainWindow : Implements INavigationWindow


    'Public Property ViewModel As MainViewModel

    'Public Property svgXML As XmlDocument

    Public Sub New(navigationService As INavigationService, serviceProvider As IServiceProvider, snackbarService As SnackbarService, contentDialogService As IContentDialogService)

        DataContext = Application.GetService(Of MainViewModel)()

        InitializeComponent()

        navigationService.SetNavigationControl(NavigationView)
        NavigationView.SetServiceProvider(serviceProvider)
        snackbarService.SetSnackbarPresenter(RootSnackbar)
        contentDialogService.SetDialogHost(RootContentDialog)

        AddHandler NavigationView.Navigated, Sub(s, e)
                                                 If TypeOf (e.Page) Is SVGPage Then
                                                     UndoButtonGlobal.Visibility = Visibility.Visible
                                                     RedoButtonGlobal.Visibility = Visibility.Visible
                                                 Else
                                                     UndoButtonGlobal.Visibility = Visibility.Hidden
                                                     RedoButtonGlobal.Visibility = Visibility.Hidden

                                                 End If

                                                 Dim isHome As Boolean = TypeOf (e.Page) Is SVGPage
                                                 Dim isPreview As Boolean = TypeOf (e.Page) Is PreviewPage

                                                 GenerateGCodeButton.Visibility = If(isHome OrElse isPreview, Visibility.Visible, Visibility.Hidden)
                                                 GenerateActionButton.Visibility = If(isHome, Visibility.Visible, Visibility.Collapsed)
                                                 UploadActionButton.Visibility = If(isPreview, Visibility.Visible, Visibility.Collapsed)

                                             End Sub

        AddHandler Closing, AddressOf OnWindowClosing
    End Sub

    ''' <summary>True once the user has decided what to do with unsaved work.</summary>
    Private _exitConfirmed As Boolean

    ''' <summary>Ask before throwing away a non-empty workspace. Closing is cancelled
    ''' first, the question is asked asynchronously, and only then is the window closed
    ''' for real - a modal dialog inside Closing cannot be awaited.</summary>
    Private Sub OnWindowClosing(sender As Object, e As System.ComponentModel.CancelEventArgs)
        If _exitConfirmed Then Return

        Dim vm = Application.GetService(Of MainViewModel)()
        If vm Is Nothing OrElse vm.Document Is Nothing OrElse vm.Document.Drawables Is Nothing Then Return
        If vm.Document.Drawables.Count = 0 Then Return

        e.Cancel = True
        Dispatcher.BeginInvoke(New Action(AddressOf PromptExit))
    End Sub

    Private Async Sub PromptExit()
        Dim vm = Application.GetService(Of MainViewModel)()
        If vm Is Nothing Then Return

        Dim box As New Wpf.Ui.Controls.MessageBox With {
            .Title = "退出前保存？",
            .Content = "当前工作区还有内容。要保存后再退出吗？",
            .IsPrimaryButtonEnabled = True,
            .PrimaryButtonText = "保存并退出",
            .IsSecondaryButtonEnabled = True,
            .SecondaryButtonText = "不保存退出",
            .CloseButtonText = "取消",
            .Owner = Me,
            .WindowStartupLocation = WindowStartupLocation.CenterOwner
        }

        Dim res = Await box.ShowDialogAsync()

        If res = Wpf.Ui.Controls.MessageBoxResult.None Then Return

        If res = Wpf.Ui.Controls.MessageBoxResult.Primary Then
            If Not vm.TrySaveProjectBeforeExit() Then Return
        End If

        _exitConfirmed = True
        Close()
    End Sub


    Public Function GetNavigation() As INavigationView Implements INavigationWindow.GetNavigation
        Throw New NotImplementedException()
    End Function

    Public Function Navigate(pageType As Type) As Boolean Implements INavigationWindow.Navigate
        Throw New NotImplementedException()
    End Function

    Public Sub SetServiceProvider(serviceProvider As IServiceProvider) Implements INavigationWindow.SetServiceProvider
        Throw New NotImplementedException()
    End Sub


    Public Sub ShowWindow() Implements INavigationWindow.ShowWindow
        Throw New NotImplementedException()
    End Sub

    Public Sub CloseWindow() Implements INavigationWindow.CloseWindow
        Throw New NotImplementedException()
    End Sub

    Public Sub SetPageService(navigationViewPageProvider As INavigationViewPageProvider) Implements INavigationWindow.SetPageService
        Throw New NotImplementedException()
    End Sub

    Dim svg_file As String = ""

End Class
