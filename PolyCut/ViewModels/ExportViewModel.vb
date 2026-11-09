Imports CommunityToolkit.Mvvm.ComponentModel
Imports CommunityToolkit.Mvvm.Input

Imports Microsoft.Win32

Imports PolyCut.Core

Partial Public Class ExportViewModel : Inherits ObservableObject

    <ObservableProperty> Private _MainVM As MainViewModel
    <ObservableProperty> Private _FilePath As String

    Public Sub New(mainvm As MainViewModel)

        Me.MainVM = mainvm
        FilePath = mainvm.PolyCutDocumentName

    End Sub

    <RelayCommand>
    Private Async Sub SaveFile()

        Dim diskexporter As New DiskExporter(MainVM.Configuration)
        Dim fsd As New SaveFileDialog With {
            .Filter = "GCode (*.gcode;*.ngc)|*.gcode;*.ngc",
            .FileName = FilePath
        }

        If fsd.ShowDialog() Then
            Dim ret = Await diskexporter.Export(MainVM.GCode, fsd.FileName)
            If ret = 0 Then
                Application.GetService(Of SnackbarService).GenerateSuccess("File Saved".LT(), "Saved to: {0}".LTF(fsd.FileName))
            Else
                Application.GetService(Of SnackbarService).GenerateError("Error Saving File".LT(), "An unknown error occurred".LT())
            End If
        End If

    End Sub


    <RelayCommand>
    Private Async Sub NetworkUpload()

        Dim sb = Application.GetService(Of SnackbarService)
        Dim service = Application.GetService(Of BambuA1Service)()

        If service Is Nothing Then
            sb.GenerateError("发送到 A1 失败", "Bambu Lab A1 服务不可用")
            Return
        End If

        ' Uploading puts a file on the real printer and the next thing a user does is walk
        ' over and press Go. Ask first: the button sits exactly where "Generate" was.
        Dim host As String = If(MainVM.Printer IsNot Nothing, MainVM.Printer.HostAddress, "打印机")
        Dim confirm As New Wpf.Ui.Controls.MessageBox With {
            .Title = "发送到 A1",
            .Content = "将把 G-code 上传到 " & host & "。上传后需要到打印机屏幕上手动启动。继续吗？",
            .IsPrimaryButtonEnabled = True,
            .PrimaryButtonText = "取消",
            .IsSecondaryButtonEnabled = True,
            .SecondaryButtonText = "上传",
            .CloseButtonText = "取消",
            .Owner = Application.Current.MainWindow,
            .WindowStartupLocation = WindowStartupLocation.CenterOwner
        }
        If Await confirm.ShowDialogAsync() <> Wpf.Ui.Controls.MessageBoxResult.Secondary Then Return

        Await EnsureConnectedAsync(service)

        Dim success = Await service.UploadGCodeAsync(MainVM.GCode, FilePath, False)

        If success Then
            sb.GenerateSuccess("已发送到 A1", "已上传 " & FilePath & " 到 " & MainVM.Printer.HostAddress & "，请在打印机屏幕上手动启动", 8)
        Else
            Dim detail As String = If(String.IsNullOrWhiteSpace(service.LastError),
                                      "请检查主机地址和访问码后重试",
                                      service.LastError)
            sb.GenerateError("发送到 A1 失败", detail)
        End If

    End Sub


    <RelayCommand>
    Private Async Sub NetworkUploadPreview()

        Dim sb = Application.GetService(Of SnackbarService)
        Dim service = Application.GetService(Of BambuA1Service)()

        If service Is Nothing Then
            sb.GenerateError("发送到 A1 失败", "Bambu Lab A1 服务不可用")
            Return
        End If

        Dim generated = MainVM.GeneratedGCode
        If generated Is Nothing OrElse generated.Count = 0 Then
            sb.GenerateError("发送到 A1 失败", "请先生成 G-code")
            Return
        End If

        Dim gcodes = BuildBoundingBoxGCode(generated)
        If String.IsNullOrWhiteSpace(gcodes) Then
            sb.GenerateError("发送到 A1 失败", "请先生成 G-code")
            Return
        End If

        Await EnsureConnectedAsync(service)

        Dim success = Await service.UploadGCodeAsync(gcodes, "PolyCut_Preview.gcode", False)

        If success Then
            sb.GenerateSuccess("已发送到 A1", "预览已上传到 " & MainVM.Printer.HostAddress & "，请在打印机屏幕上手动启动", 8)
        Else
            Dim detail As String = If(String.IsNullOrWhiteSpace(service.LastError),
                                      "请检查主机地址和访问码后重试",
                                      service.LastError)
            sb.GenerateError("发送到 A1 失败", detail)
        End If

    End Sub

    ''' <summary>
    ''' Connects the shared A1 service first so uploads can also start the printer over MQTT.
    ''' </summary>
    Private Async Function EnsureConnectedAsync(service As BambuA1Service) As Task
        If service.IsConnected Then Return

        Dim printer As Printer = MainVM.Printer
        If printer Is Nothing Then Return

        Await service.ConnectAsync(printer.HostAddress, printer.AccessCode, printer.SerialNumber)
    End Function


    Private Function BuildBoundingBoxGCode(generatedGCode As IEnumerable(Of GCode)) As String

        Dim FSpeed = MainVM.Configuration.WorkSpeed * 60 ' mm/min

        Dim points = generatedGCode.Where(Function(gc) gc.X.HasValue AndAlso gc.Y.HasValue).Select(Function(gc) (X:=gc.X.Value, Y:=gc.Y.Value)).ToList()
        If points.Count = 0 Then Return String.Empty

        Dim minX = points.Min(Function(p) p.X)
        Dim minY = points.Min(Function(p) p.Y)
        Dim maxX = points.Max(Function(p) p.X)
        Dim maxY = points.Max(Function(p) p.Y)

        If Double.IsInfinity(minX) OrElse Double.IsInfinity(minY) Then Return String.Empty

        Dim sb As New Text.StringBuilder()
        sb.AppendLine(MainVM.Printer.PreviewStartGCode.Trim)

        Dim lines As New List(Of GCode) From {
            Core.GCode.GZ(MainVM.Configuration.TravelZ),
            Core.GCode.G0(minX, minY, F:=FSpeed),
            Core.GCode.G1(maxX, minY, F:=FSpeed),
            Core.GCode.G1(maxX, maxY, F:=FSpeed),
            Core.GCode.G1(minX, maxY, F:=FSpeed),
            Core.GCode.G1(minX, minY, F:=FSpeed)
        }

        sb.AppendLine(String.Join(Environment.NewLine, lines.Select(Function(g) g.ToString())))
        sb.AppendLine(MainVM.Printer.PreviewEndGCode.Trim)

        Return sb.ToString()

    End Function



End Class
