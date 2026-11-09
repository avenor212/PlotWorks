Imports System.IO

''' <summary>
''' Crash recovery. When the dispatcher catches an unhandled exception, the current
''' document is written here before the dialog appears, and the next launch offers to
''' restore it. Without this an unexpected failure simply closes the window and every
''' unsaved drawable is gone.
''' </summary>
Public Module RecoveryStore

    Private Const FileName As String = "recovery.polycut"

    Public Function FolderPath() As String
        Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            SettingsHandler.AppDataFolderName)
    End Function

    Public Function FilePath() As String
        Return Path.Combine(FolderPath(), FileName)
    End Function

    Public Function Exists() As Boolean
        Try
            Return File.Exists(FilePath())
        Catch
            Return False
        End Try
    End Function

    ''' <summary>Write the live document to the recovery slot. Returns False when there is
    ''' nothing worth saving or the save itself fails; never throws from a crash handler.
    ''' <paramref name="failureReason"/> carries a short human-readable explanation so the
    ''' crash dialog can say why the snapshot did not happen instead of only that it did
    ''' not.</summary>
    Public Function TrySave(Optional ByRef failureReason As String = Nothing) As Boolean
        failureReason = Nothing
        Try
            Dim vm = PolyCut.Application.GetService(Of MainViewModel)()
            Dim doc = PolyCut.Application.GetService(Of Document)()
            Dim svc = PolyCut.Application.GetService(Of ProjectSerializationService)()

            If vm Is Nothing OrElse doc Is Nothing OrElse svc Is Nothing Then
                failureReason = "应用服务在崩溃后已不可用"
                Return False
            End If
            If doc.Drawables Is Nothing OrElse doc.Drawables.Count = 0 Then
                failureReason = "画布上没有内容（无需恢复）"
                Return False
            End If

            Directory.CreateDirectory(FolderPath())

            If svc.SaveProject(FilePath(), doc.Drawables, doc.ImportRoots, vm.Configuration, vm.ReferencePoints) Then
                Return True
            End If

            failureReason = "写入 " & FilePath() & " 失败"
            Return False
        Catch ex As Exception
            failureReason = ex.GetType().Name & ": " & ex.Message
            Return False
        End Try
    End Function

    Public Sub Clear()
        Try
            If File.Exists(FilePath()) Then File.Delete(FilePath())
        Catch
        End Try
    End Sub

End Module
