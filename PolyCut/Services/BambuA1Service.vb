Option Strict On
Option Explicit On

Imports System.Globalization
Imports System.IO
Imports System.Security.Authentication
Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks

Imports CommunityToolkit.Mvvm.ComponentModel

Imports FluentFTP
Imports MQTTnet
Imports MQTTnet.Client

''' <summary>
''' Handles the local network connection to a Bambu Lab A1 (MQTT over TLS on port 8883).
''' The printer must be in LAN-only mode with developer mode enabled, and the 8 character
''' access code is used as the MQTT password (username is always "bblp").
''' </summary>
Public Class BambuA1Service
    Inherits ObservableObject

    Private Const MqttPort As Integer = 8883
    Private Const FtpPort As Integer = 990
    Private Const MqttUsername As String = "bblp"

    Private ReadOnly _logGate As New Object()
    Private ReadOnly _log As New StringBuilder()
    Private ReadOnly _client As IMqttClient

    Private _host As String = String.Empty
    Private _accessCode As String = String.Empty
    Private _serialNumber As String = String.Empty
    Private _sequenceId As Integer = 100
    Private _lastLoggedState As String = String.Empty

    <ObservableProperty> Private _IsConnected As Boolean = False
    <ObservableProperty> Private _ConnectionStatus As String = "未连接"
    <ObservableProperty> Private _DeviceAddress As String = "-"
    <ObservableProperty> Private _GCodeState As String = "-"
    <ObservableProperty> Private _NozzleTemperature As String = "-"
    <ObservableProperty> Private _BedTemperature As String = "-"
    <ObservableProperty> Private _PrintProgress As String = "-"
    <ObservableProperty> Private _LogText As String = String.Empty
    <ObservableProperty> Private _LastError As String = String.Empty

    Public Sub New()
        _client = New MqttFactory().CreateMqttClient()
        AddHandler _client.ApplicationMessageReceivedAsync, AddressOf OnApplicationMessageReceived
        AddHandler _client.DisconnectedAsync, AddressOf OnDisconnected
        AppendLog("Bambu Lab A1 service ready. Configure the printer and press Connect.")
    End Sub

    Public ReadOnly Property IsConfigured As Boolean
        Get
            Return Not String.IsNullOrWhiteSpace(_host) AndAlso
                   Not String.IsNullOrWhiteSpace(_accessCode) AndAlso
                   Not String.IsNullOrWhiteSpace(_serialNumber)
        End Get
    End Property

    ''' <summary>Writes an informational line to the monitor log.</summary>
    Public Sub Report(message As String)
        AppendLog(message)
    End Sub

    ''' <summary>Clears the monitor log.</summary>
    Public Sub ClearLog()
        UpdateUi(Sub()
                     SyncLock _logGate
                         _log.Clear()
                         LogText = String.Empty
                     End SyncLock
                 End Sub)
    End Sub

    Public Async Function ConnectAsync(host As String, accessCode As String, serialNumber As String) As Task
        If String.IsNullOrWhiteSpace(host) OrElse String.IsNullOrWhiteSpace(accessCode) OrElse String.IsNullOrWhiteSpace(serialNumber) Then
            AppendLog("Connect failed: host address, access code and serial number are all required.")
            Return
        End If

        If _client.IsConnected Then
            Await DisconnectAsync()
        End If

        _host = host.Trim()
        _accessCode = accessCode.Trim()
        _serialNumber = serialNumber.Trim()

        UpdateUi(Sub()
                     DeviceAddress = $"{_host}:{MqttPort}"
                     ConnectionStatus = "连接中..."
                 End Sub)
        AppendLog($"Connecting to Bambu Lab A1 at {_host}:{MqttPort} (SN {_serialNumber}) ...")

        Try
            Dim options As MqttClientOptions = New MqttClientOptionsBuilder() _
                .WithClientId($"PolyCut-A1-{Guid.NewGuid().ToString("N").Substring(0, 12)}") _
                .WithTcpServer(_host, MqttPort) _
                .WithCredentials(MqttUsername, _accessCode) _
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(60)) _
                .WithCleanSession(True) _
                .WithTlsOptions(Sub(o As MqttClientTlsOptionsBuilder)
                                    o.UseTls()
                                    o.WithAllowUntrustedCertificates(True)
                                    o.WithIgnoreCertificateChainErrors(True)
                                    o.WithIgnoreCertificateRevocationErrors(True)
                                    o.WithCertificateValidationHandler(Function(context As MqttClientCertificateValidationEventArgs) True)
                                End Sub) _
                .Build()

            Await _client.ConnectAsync(options, CancellationToken.None)

            Dim subscribeOptions As MqttClientSubscribeOptions = New MqttClientSubscribeOptionsBuilder() _
                .WithTopicFilter($"device/{_serialNumber}/report") _
                .Build()
            Await _client.SubscribeAsync(subscribeOptions, CancellationToken.None)

            UpdateUi(Sub()
                         IsConnected = True
                         ConnectionStatus = "已连接"
                     End Sub)
            AppendLog($"Connected. Subscribed to device/{_serialNumber}/report")

            Await RequestFullStatusAsync()
        Catch ex As Exception
            UpdateUi(Sub()
                         IsConnected = False
                         ConnectionStatus = "未连接"
                     End Sub)
            AppendLog("Connection failed: " & ex.Message)
        End Try
    End Function

    Public Async Function DisconnectAsync() As Task
        Try
            If _client.IsConnected Then
                Await _client.DisconnectAsync()
            End If
        Catch ex As Exception
            AppendLog("Disconnect error: " & ex.Message)
        Finally
            UpdateUi(Sub()
                         IsConnected = False
                         ConnectionStatus = "未连接"
                     End Sub)
            AppendLog("Disconnected from printer.")
        End Try
    End Function

    ''' <summary>Homes all axes on the printer (G28).</summary>
    Public Async Function HomeAsync() As Task
        If Not _client.IsConnected Then
            AppendLog("Home ignored: printer is not connected.")
            Return
        End If
        AppendLog("Homing all axes (G28).")
        Await SendGCodeAsync("G28")
    End Function

    ''' <summary>
    ''' Raises Z to the supplied travel height and moves the toolhead to the requested XY
    ''' position in absolute machine coordinates.
    ''' </summary>
    Public Async Function MoveToAsync(x As Double, y As Double, travelZ As Double, feedRate As Double) As Task
        If Not _client.IsConnected Then
            AppendLog("Move ignored: printer is not connected.")
            Return
        End If

        Dim ci As CultureInfo = CultureInfo.InvariantCulture
        Dim safeZ As Double = Math.Max(travelZ, 0.0)
        Dim feed As Double = If(feedRate > 0, feedRate, 3000.0)

        AppendLog($"Moving to X{x.ToString("F1", ci)} Y{y.ToString("F1", ci)} (Z{safeZ.ToString("F1", ci)}, F{feed.ToString("F0", ci)})")
        Await SendGCodeAsync(String.Format(ci, "G90{0}G1 Z{1:F3} F600", Environment.NewLine, safeZ))
        Await SendGCodeAsync(String.Format(ci, "G0 X{0:F3} Y{1:F3} F{2:F0}", x, y, feed))
    End Function

    ''' <summary>Sends a raw G-code snippet through the MQTT gcode_line command.</summary>
    Public Async Function SendGCodeAsync(gcode As String) As Task
        If Not _client.IsConnected Then
            AppendLog("Cannot send G-code: printer is not connected.")
            Return
        End If

        Dim snippet As String = If(gcode, String.Empty).Trim()
        If snippet.Length = 0 Then Return

        Dim sequence As String = NextSequenceId()
        Dim payload As String = JsonSerializer.Serialize(New With {
            .print = New With {
                .sequence_id = sequence,
                .command = "gcode_line",
                .param = snippet
            }
        })

        Try
            Await PublishAsync(payload)
            AppendLog("TX: " & snippet.Replace(Environment.NewLine, " ; ").Replace(vbLf, " ; "))
        Catch ex As Exception
            AppendLog("Send failed: " & ex.Message)
        End Try
    End Function

    ''' <summary>
    ''' Uploads G-code to the printer over the implicit-TLS FTPS server and optionally starts
    ''' printing it through MQTT. Returns True when the upload succeeded.
    ''' </summary>
    Public Async Function UploadGCodeAsync(gcode As String, fileName As String, autoPrint As Boolean) As Task(Of Boolean)
        LastError = String.Empty

        If String.IsNullOrWhiteSpace(_host) OrElse String.IsNullOrWhiteSpace(_accessCode) Then
            LastError = "Connect to the printer first."
            AppendLog("Upload failed: connect to the printer first.")
            Return False
        End If

        If String.IsNullOrWhiteSpace(gcode) Then
            LastError = "There is no G-code to send."
            AppendLog("Upload failed: there is no G-code to send.")
            Return False
        End If

        Dim safeName As String = Path.GetFileName(If(fileName, String.Empty))
        If String.IsNullOrWhiteSpace(safeName) Then safeName = "PolyCut.gcode"
        If Not safeName.EndsWith(".gcode", StringComparison.OrdinalIgnoreCase) Then
            safeName = Path.GetFileNameWithoutExtension(safeName) & ".gcode"
        End If
        Dim remotePath As String = "/" & safeName

        Try
            AppendLog($"Uploading {safeName} to the printer over FTPS (port {FtpPort}) ...")
            Dim uploadResult As Tuple(Of Boolean, String) = Await Task.Run(Function()
                                                                               Using ftp As New FtpClient(_host, MqttUsername, _accessCode, FtpPort)
                                                                                   ftp.Config.EncryptionMode = FtpEncryptionMode.Implicit
                                                                                   ftp.Config.ValidateAnyCertificate = True
                                                                                   ftp.Config.DataConnectionEncryption = True
                                                                                   ftp.Config.SslProtocols = SslProtocols.Tls12
                                                                                   'The printer's storage contains file names that FluentFTP's injection
                                                                                   'guard rejects, so disable URL-encoding sanitising for uploads.
                                                                                   ftp.Config.SanitizeUrlEncoding = False
                                                                                   ftp.Connect()

                                                                                   'A1 series usually expose their internal storage as /model over
                                                                                   'FTPS (the touchscreen file list reads it); some firmware builds
                                                                                   'only offer /cache, which is the slicer cache. Probe in order.
                                                                                   Dim storagePath As String = DetectStoragePath(ftp)
                                                                                   Dim targetPath As String = If(storagePath = "/", "/" & safeName, storagePath.TrimEnd("/"c) & "/" & safeName)
                                                                                   Dim status As FtpStatus = ftp.UploadBytes(Encoding.UTF8.GetBytes(gcode), targetPath, FtpRemoteExists.Overwrite, True)
                                                                                   ftp.Disconnect()
                                                                                   Return Tuple.Create(status = FtpStatus.Success, targetPath)
                                                                               End Using
                                                                           End Function)

            remotePath = uploadResult.Item2
            If Not uploadResult.Item1 Then
                LastError = "The printer rejected the file transfer."
                AppendLog("Upload failed: the printer rejected the file transfer.")
                Return False
            End If

            AppendLog($"Uploaded to {remotePath}")

            'A1 firmware rejects the MQTT gcode_file command and project_file only unpacks
            'the container without starting. Callers therefore pass autoPrint:=False and the
            'print is started from the printer's touchscreen. This branch is kept only as a
            'hook for firmware that may support it later.
            If autoPrint Then
                If Not _client.IsConnected Then
                    LastError = "The file was uploaded, but MQTT is not connected so the print could not start."
                    AppendLog("Print was not started because MQTT is not connected.")
                    Return True
                End If

                'The printer's gcode_file command expects the file system path, not the
                'FTPS path: /model is mounted as /sdcard/model on the printer.
                Dim printerPath As String = ToPrinterPath(remotePath)
                Dim payload As String = JsonSerializer.Serialize(New With {
                    .print = New With {
                        .sequence_id = NextSequenceId(),
                        .command = "gcode_file",
                        .param = printerPath
                    }
                })
                Await PublishAsync(payload)
                AppendLog("TX: start printing " & printerPath)
            End If

            Return True
        Catch ex As Exception
            LastError = ex.Message
            AppendLog("Upload failed: " & ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Returns the FTPS directory the A1 touchscreen file manager reads. On this firmware
    ''' that is the FTPS root ("/"), which is where files uploaded by other tools (and the
    ''' printer's own "page_1.gcode" style jobs) appear. /model, /sdcard and /cache are kept
    ''' as fallbacks for other firmware layouts.
    ''' </summary>
    Private Shared Function DetectStoragePath(ftp As FtpClient) As String
        'The touchscreen lists the FTPS root on A1 firmware, so upload there.
        Try
            ftp.SetWorkingDirectory("/")
            Return "/"
        Catch
            'Fall through to the other layouts.
        End Try

        For Each candidate As String In {"/model", "/sdcard", "/cache"}
            Try
                ftp.SetWorkingDirectory(candidate)
                If String.Equals(ftp.GetWorkingDirectory(), candidate, StringComparison.OrdinalIgnoreCase) Then
                    ftp.SetWorkingDirectory("/")
                    Return candidate
                End If
            Catch
                'Try the next candidate.
            End Try
        Next

        Return "/cache"
    End Function

    ''' <summary>Maps an FTPS upload path to the file system path the printer expects.</summary>
    Private Shared Function ToPrinterPath(remotePath As String) As String
        If remotePath.StartsWith("/model/", StringComparison.OrdinalIgnoreCase) Then
            Return "/sdcard/model/" & remotePath.Substring("/model/".Length)
        End If
        If remotePath.StartsWith("/", StringComparison.Ordinal) AndAlso Not remotePath.StartsWith("/cache/", StringComparison.OrdinalIgnoreCase) AndAlso Not remotePath.StartsWith("/sdcard/", StringComparison.OrdinalIgnoreCase) Then
            'Files at the FTPS root live on the printer's SD card root.
            Return "/sdcard" & remotePath
        End If
        Return remotePath
    End Function

    Private Async Function RequestFullStatusAsync() As Task
        If Not _client.IsConnected Then Return

        Dim payload As String = JsonSerializer.Serialize(New With {
            .pushing = New With {
                .sequence_id = NextSequenceId(),
                .command = "pushall",
                .version = 1,
                .push_target = 1
            }
        })

        Try
            Await PublishAsync(payload)
            AppendLog("TX: pushall (requesting full status)")
        Catch ex As Exception
            AppendLog("Status request failed: " & ex.Message)
        End Try
    End Function

    Private Async Function PublishAsync(payload As String) As Task
        If Not _client.IsConnected Then Return
        Dim topic As String = $"device/{_serialNumber}/request"
        Await _client.PublishStringAsync(topic, payload)
    End Function

    Private Function NextSequenceId() As String
        Return Interlocked.Increment(_sequenceId).ToString(CultureInfo.InvariantCulture)
    End Function

    Private Function OnApplicationMessageReceived(e As MqttApplicationMessageReceivedEventArgs) As Task
        Dim payload As String = e.ApplicationMessage.ConvertPayloadToString()
        If String.IsNullOrWhiteSpace(payload) Then Return Task.CompletedTask

        Try
            Using document As JsonDocument = JsonDocument.Parse(payload)
                If document.RootElement.ValueKind <> JsonValueKind.Object Then Return Task.CompletedTask

                Dim printElement As JsonElement
                If document.RootElement.TryGetProperty("print", printElement) AndAlso printElement.ValueKind = JsonValueKind.Object Then
                    ParsePrintState(printElement)
                End If
            End Using
        Catch ex As Exception
            AppendLog("Could not parse a status message: " & ex.Message)
        End Try

        Return Task.CompletedTask
    End Function

    Private Sub ParsePrintState(printElement As JsonElement)
        Dim state As String = TryGetString(printElement, "gcode_state")
        Dim nozzle As Double? = TryGetDouble(printElement, "nozzle_temper")
        Dim bed As Double? = TryGetDouble(printElement, "bed_temper")
        Dim percent As Double? = TryGetDouble(printElement, "mc_percent")
        Dim remaining As Double? = TryGetDouble(printElement, "mc_remaining_time")

        UpdateUi(Sub()
                     If state IsNot Nothing Then GCodeState = state
                     If nozzle.HasValue Then NozzleTemperature = nozzle.Value.ToString("F0", CultureInfo.InvariantCulture) & " °C"
                     If bed.HasValue Then BedTemperature = bed.Value.ToString("F0", CultureInfo.InvariantCulture) & " °C"
                     If percent.HasValue Then
                         If remaining.HasValue AndAlso remaining.Value > 0 Then
                             PrintProgress = $"{percent.Value:F0}% ({remaining.Value:F0} min left)"
                         Else
                             PrintProgress = $"{percent.Value:F0}%"
                         End If
                     End If
                 End Sub)

        If state IsNot Nothing AndAlso Not String.Equals(state, _lastLoggedState, StringComparison.Ordinal) Then
            _lastLoggedState = state
            AppendLog("Printer state changed: " & state)
        End If
    End Sub

    Private Function OnDisconnected(e As MqttClientDisconnectedEventArgs) As Task
        UpdateUi(Sub()
                     IsConnected = False
                     ConnectionStatus = "未连接"
                 End Sub)

        If e IsNot Nothing AndAlso e.Exception IsNot Nothing Then
            AppendLog("Connection lost: " & e.Exception.Message)
        Else
            AppendLog("Connection closed.")
        End If

        Return Task.CompletedTask
    End Function

    Private Shared Function TryGetString(element As JsonElement, name As String) As String
        Dim value As JsonElement
        If element.TryGetProperty(name, value) AndAlso value.ValueKind = JsonValueKind.String Then
            Return value.GetString()
        End If
        Return Nothing
    End Function

    Private Shared Function TryGetDouble(element As JsonElement, name As String) As Double?
        Dim value As JsonElement
        If element.TryGetProperty(name, value) AndAlso value.ValueKind = JsonValueKind.Number Then
            Dim number As Double
            If value.TryGetDouble(number) Then Return number
        End If
        Return Nothing
    End Function

    Private Sub AppendLog(message As String)
        Dim line As String = $"[{DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}] {message}"
        UpdateUi(Sub() AppendLogCore(line))
    End Sub

    Private Sub AppendLogCore(line As String)
        SyncLock _logGate
            _log.AppendLine(line)
            If _log.Length > 200000 Then
                _log.Remove(0, 50000)
            End If
            LogText = _log.ToString()
        End SyncLock

        WriteLogFile(line)
    End Sub

    ''' <summary>Mirrors the in-memory log to a file so the UI no longer needs a log panel.</summary>
    Private Shared Sub WriteLogFile(line As String)
        Try
            Dim path As String = IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                SettingsHandler.AppDataFolderName, "a1.log")

            Dim dir = IO.Path.GetDirectoryName(path)
            If Not String.IsNullOrEmpty(dir) AndAlso Not IO.Directory.Exists(dir) Then
                IO.Directory.CreateDirectory(dir)
            End If

            If IO.File.Exists(path) AndAlso New IO.FileInfo(path).Length > 2000000 Then
                IO.File.WriteAllText(path, String.Empty)
            End If

            IO.File.AppendAllText(path, line & Environment.NewLine)
        Catch
            'Logging must never break the app.
        End Try
    End Sub

    Private Sub UpdateUi(action As Action)
        Dim app As System.Windows.Application = System.Windows.Application.Current
        If app Is Nothing OrElse app.Dispatcher Is Nothing Then
            action()
            Return
        End If

        If app.Dispatcher.CheckAccess() Then
            action()
        Else
            app.Dispatcher.BeginInvoke(action)
        End If
    End Sub
End Class
