Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows

Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.Extensions.Hosting

Imports LazyTranslate

Public Class ApplicationHostService
    Implements IHostedService

    Private ReadOnly _serviceProvider As IServiceProvider

    Public Sub New(serviceProvider As IServiceProvider)
        _serviceProvider = serviceProvider
    End Sub

    ''' <summary>
    ''' Triggered when the application host is ready to start the service.
    ''' </summary>
    ''' <param name="cancellationToken">Indicates that the start process has been aborted.</param>
    Public Async Function StartAsync(cancellationToken As CancellationToken) As Task Implements IHostedService.StartAsync
        Await HandleActivationAsync()
    End Function

    ''' <summary>
    ''' Triggered when the application host is performing a graceful shutdown.
    ''' </summary>
    ''' <param name="cancellationToken">Indicates that the shutdown process should no longer be graceful.</param>
    Public Async Function StopAsync(cancellationToken As CancellationToken) As Task Implements IHostedService.StopAsync
        Await Task.CompletedTask
    End Function

    ''' <summary>
    ''' Creates main window during activation.
    ''' </summary>
    Private Async Function HandleActivationAsync() As Task
        Await Task.CompletedTask

        Dim currentDir = AppContext.BaseDirectory
        ' Portable mode: a folder named PlotWorks (or PolyCut, for layouts carried over
        ' from earlier builds) keeps its config next to the executable.
        Dim dirName = New IO.DirectoryInfo(currentDir).Name.ToLower
        If dirName = "plotworks" OrElse dirName = "polycut" Then
            Await SettingsHandler.InitialiseSettings(True)
        Else
            Await SettingsHandler.InitialiseSettings(False)
        End If
        Await LocalisationService.InitializeAsync()


        If Not Application.Current.Windows.OfType(Of MainWindow)().Any() Then
            Dim navigationWindow = _serviceProvider.GetRequiredService(Of MainWindow)()
            AddHandler navigationWindow.Loaded, AddressOf OnNavigationWindowLoaded
            navigationWindow.Show()
        End If


    End Function

    Private Sub OnNavigationWindowLoaded(sender As Object, e As RoutedEventArgs)
        If TypeOf sender IsNot MainWindow Then
            Return
        End If


        Dim navigationWindow = DirectCast(sender, MainWindow)
        navigationWindow.NavigationView.Navigate(GetType(SVGPage))

        ' Offer to restore anything the previous run saved just before it failed.
        If RecoveryStore.Exists() Then
            navigationWindow.Dispatcher.BeginInvoke(New Action(AddressOf OfferRecovery))
        End If


#If DEBUG Then
        LazyTranslate.LocalisationCatalogueBuilder.Build(sourceCulture:="en-AU")
#End If


    End Sub

    Private Async Sub OfferRecovery()
        Try
            Dim box As New Wpf.Ui.Controls.MessageBox With {
                .Title = "恢复上次的内容",
                .Content = "上次运行时 PolyCut 意外结束，已把当时的工作区保存下来。要恢复吗？",
                .IsPrimaryButtonEnabled = True,
                .PrimaryButtonText = "恢复",
                .CloseButtonText = "丢弃",
                .Owner = System.Windows.Application.Current.MainWindow,
                .WindowStartupLocation = WindowStartupLocation.CenterOwner
            }

            Dim result = Await box.ShowDialogAsync()
            If result = Wpf.Ui.Controls.MessageBoxResult.Primary Then
                Dim vm = PolyCut.Application.GetService(Of MainViewModel)()
                If vm IsNot Nothing Then Await vm.RestoreFromAsync(RecoveryStore.FilePath())
            End If
        Catch
        Finally
            RecoveryStore.Clear()
        End Try
    End Sub
End Class
