Imports System.Collections.ObjectModel
Imports System.Text.RegularExpressions

Imports CommunityToolkit.Mvvm.ComponentModel

Imports PolyCut.Core


Public Structure GCodeLine
    Public X1 As Single
    Public Y1 As Single
    Public X2 As Single
    Public Y2 As Single
    Public IsRapidMove As Boolean
End Structure


Public Class GCodeGeometry : Inherits ObservableObject

    ' Use List for performance, convert to ObservableCollection later if needed
    Private ReadOnly _paths As New List(Of GCodeLine)
    Public ReadOnly Property Paths As ReadOnlyCollection(Of GCodeLine)
        Get
            Return _paths.AsReadOnly()
        End Get
    End Property

    ' Use List for faster bulk population
    Public ReadOnly Property GCode As List(Of GCode)

    Public Sub New(gcodeCollection As List(Of GCode))
        GCode = New List(Of GCode)(gcodeCollection)
        BuildLines()
    End Sub

    Private Sub BuildLines()
        _paths.Clear()

        Dim lastX As Double = 0
        Dim lastY As Double = 0
        Dim firstLineDrawn As Boolean = False

        Dim cfg = Application.GetService(Of MainViewModel).Configuration
        Dim printer = Application.GetService(Of MainViewModel).Printer

        'The Bambu A1 homes to the bed centre, so the real first move starts there rather
        'than at the work origin (0,0). Mirror that in the preview.
        Dim homeX As Double = If(printer IsNot Nothing, printer.BedWidth / 2, 0)
        Dim homeY As Double = If(printer IsNot Nothing, printer.BedHeight / 2, 0)

        'The preview mirrors the offsets applied during generation: the shared toolhead offset plus the selected tool's offset
        Dim modeOffsetX As Double = If(cfg.SelectedToolMode = ProcessorConfiguration.ToolMode.Cut, cfg.CutOffsetX, cfg.DrawOffsetX)
        Dim modeOffsetY As Double = If(cfg.SelectedToolMode = ProcessorConfiguration.ToolMode.Cut, cfg.CutOffsetY, cfg.DrawOffsetY)

        Dim toolOffsetX As Double = cfg.ToolOffsetX + modeOffsetX
        Dim toolOffsetY As Double = cfg.ToolOffsetY + modeOffsetY

        For i = 0 To GCode.Count - 1
            Dim cmd = GCode(i)
            If cmd.Mode <> "G" OrElse (cmd.Code <> 0 AndAlso cmd.Code <> 1) Then Continue For
            If cmd.X Is Nothing OrElse cmd.Y Is Nothing Then Continue For

            Dim x = cmd.X.Value - toolOffsetX
            Dim y = cmd.Y.Value - toolOffsetY
            Dim isRapid = cmd.Code = 0

            If Not firstLineDrawn Then
                _paths.Add(DrawLine(homeX, homeY, x, y, True))
                firstLineDrawn = True
            Else
                _paths.Add(DrawLine(lastX, lastY, x, y, isRapid))
            End If

            lastX = x
            lastY = y
        Next
    End Sub

    Private Shared Function DrawLine(x1 As Double, y1 As Double, x2 As Double, y2 As Double, Optional isRapidMove As Boolean = False) As GCodeLine
        Return New GCodeLine With {
            .X1 = Math.Round(x1, 2),
            .Y1 = Math.Round(y1, 2),
            .X2 = Math.Round(x2, 2),
            .Y2 = Math.Round(y2, 2),
            .IsRapidMove = isRapidMove
        }
    End Function
End Class
