Imports System.IO
Imports System.Text.Json
Imports System.Text.Json.Serialization

Imports CommunityToolkit.Mvvm.ComponentModel

Imports PolyCut.Core

Public Class SettingsBase : Implements ISettingsService

    <JsonIgnore> Public Property SettingsFiles As New List(Of FileInfo) Implements ISettingsService.SettingsFiles
    <JsonIgnore> Public Property SettingsFolder As DirectoryInfo Implements ISettingsService.SettingsFolder

    Public Async Function SetValue(settingName As String, setting As Object) As Task Implements ISettingsService.SetValue
        Dim fileN As String = settingName & ".json"

        Dim js As New JsonSerializerOptions With {
        .IncludeFields = True, .IgnoreReadOnlyProperties = True,
        .WriteIndented = True}

        Dim output = JsonSerializer.Serialize(setting, js)

        Dim oldPath = IO.Path.Combine(SettingsFolder.FullName, fileN)


        Await IO.File.WriteAllTextAsync(oldPath, output)
        Dim exists = SettingsFiles.Any(Function(f) String.Equals(f.FullName, oldPath, StringComparison.OrdinalIgnoreCase))
        If Not exists Then
            SettingsFiles.Add(New FileInfo(oldPath))
        End If
    End Function

    Public Async Function InitialiseSettings(Of T As {ISaveable, New})(appName As String, Subfolder As String) As Task Implements ISettingsService.InitialiseSettings

        SettingsFolder = New IO.DirectoryInfo(IO.Path.Combine(SettingsHandler.DataFolder.FullName, Subfolder))

        If Not SettingsFolder.Exists Then SettingsFolder.Create()

        Dim files = SettingsFolder.GetFiles("*.json")
        If files.Count = 0 Then
            Dim p = New T()
            Await SetValue(p.Name, p)
            SettingsFiles.Add(New FileInfo(IO.Path.Combine(SettingsFolder.FullName, p.Name & ".json")))
        Else
            SettingsFiles.AddRange(files)
        End If

        For Each file In SettingsFiles.ToList()
            Dim p = GetValue(Of T)(file.FullName)
            Await SetValue(p.Name, p)
        Next


    End Function

    Public Function GetValue(Of T)(settingName As String) As T Implements ISettingsService.GetValue

        Dim path As String = IO.Path.Combine(SettingsFolder.FullName, settingName)

        Try
            If Not IO.File.Exists(path) Then Return CreateDefault(Of T)()
            Dim text = IO.File.ReadAllText(path)
            If String.IsNullOrWhiteSpace(text) Then Return CreateDefault(Of T)()

            Dim p = JsonSerializer.Deserialize(Of T)(text, New JsonSerializerOptions With {.IncludeFields = True})
            Return If(p IsNot Nothing, p, CreateDefault(Of T)())
        Catch ex As Exception
            ' A corrupt/unreadable settings file must not stop the app from starting.
            ' Keep the bad file for diagnosis, then fall back to defaults so the caller
            ' rewrites a clean copy.
            Diagnostics.Debug.WriteLine($"Failed to read settings '{path}': {ex.Message}")
            Try
                IO.File.Copy(path, path & ".corrupt", True)
            Catch
            End Try
            Return CreateDefault(Of T)()
        End Try

    End Function

    Private Shared Function CreateDefault(Of T)() As T
        Try
            Return CType(Activator.CreateInstance(GetType(T)), T)
        Catch
            Return Nothing
        End Try
    End Function

    Public Function DeleteValue(settingName As String) As Boolean Implements ISettingsService.DeleteValue
        Dim path As String = IO.Path.Combine(SettingsFolder.FullName, settingName & ".json")
        Dim fileInfo = SettingsFiles.FirstOrDefault(Function(f) String.Equals(f.FullName, path, StringComparison.OrdinalIgnoreCase))
        If fileInfo IsNot Nothing Then
            SettingsFiles.Remove(fileInfo)
        End If
        If IO.File.Exists(path) Then
            IO.File.Delete(path)
            Return True
        End If
        Return False
    End Function


End Class
