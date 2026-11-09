Imports WPF.Ui.Abstractions.Controls

Class MonitorPage : Implements INavigableView(Of MainViewModel)

    Public ReadOnly Property ViewModel As MainViewModel Implements INavigableView(Of MainViewModel).ViewModel

    Public Sub New(viewmodel As MainViewModel)

        Me.ViewModel = viewmodel
        DataContext = viewmodel

        InitializeComponent()

        AddHandler LogBox.TextChanged, Sub() LogBox.ScrollToEnd()

    End Sub

    Private Sub Page_Loaded(sender As Object, e As RoutedEventArgs)
        ViewModel.A1Service.Report("Monitor ready. Video stream is intentionally not connected yet.")
    End Sub

    Private Async Sub Connect_Click(sender As Object, e As RoutedEventArgs)
        Dim printer As Printer = ViewModel.Printer
        If printer Is Nothing Then Return

        Await ViewModel.A1Service.ConnectAsync(printer.HostAddress, printer.AccessCode, printer.SerialNumber)
    End Sub

    Private Async Sub Disconnect_Click(sender As Object, e As RoutedEventArgs)
        Await ViewModel.A1Service.DisconnectAsync()
    End Sub

    Private Sub JogStepCombo_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
        Dim item = TryCast(JogStepCombo.SelectedItem, ComboBoxItem)
        If item Is Nothing Then Return

        Dim value As Double
        If Double.TryParse(CStr(item.Tag), Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture, value) AndAlso value > 0 Then
            ViewModel.JogStep = value
        End If
    End Sub

    Private Sub ClearLog_Click(sender As Object, e As RoutedEventArgs)
        ViewModel.A1Service.ClearLog()
    End Sub

End Class
