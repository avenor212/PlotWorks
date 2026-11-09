Imports System.Text.Json

Imports PolyCut.[Shared]
Imports PolyCut.RichCanvas

Public Class ClipboardService

    Public Const ClipFormat As String = "application/x-polycut-projectdata+json"


    Private ReadOnly _selectionManager As SelectionManager
    Private ReadOnly _document As Document
    Private ReadOnly _mainVM As MainViewModel
    Private ReadOnly _undoRedo As UndoRedoService
    Private ReadOnly _projectService As ProjectSerializationService

    Private ReadOnly _jsonOpts As New JsonSerializerOptions With {
        .PropertyNameCaseInsensitive = True,
        .WriteIndented = False
    }

    Public Sub New(mainVM As MainViewModel, undoRedo As UndoRedoService, projectService As ProjectSerializationService, selectionManager As SelectionManager, document As Document)
        _selectionManager = selectionManager
        _document = document
        _mainVM = mainVM
        _undoRedo = undoRedo
        _projectService = projectService
    End Sub

    Public Sub CopySelectionToClipboard()

        Dim selectedDrawables = _selectionManager.SelectedItems
        If selectedDrawables.Count = 0 Then Return

        Dim ret = _projectService.CreateProjectDataForClipboard(selectedDrawables, _document.DrawingGroup)

        Dim json As String = JsonSerializer.Serialize(ret, _jsonOpts)
        Dim data As New DataObject()
        data.SetData(ClipFormat, json)
        Clipboard.SetDataObject(data, copy:=True)

    End Sub

    Public Sub CutSelectionToClipboard()
        Dim selectedDrawables = _selectionManager.SelectedItems
        If selectedDrawables.Count = 0 Then Return

        CopySelectionToClipboard()

        _mainVM.RemoveSelectedDrawables()

    End Sub

    Private _lastData As ProjectData
    Private offset As Integer = 0

    ''' <summary>
    ''' Paste with a safety net. A paste may only add drawables; if the document ends up
    ''' with fewer objects than it started with, the step is rolled back and the user is
    ''' told. This guards the reported "Ctrl+V emptied the canvas" failure even though it
    ''' has never reproduced on this machine.
    ''' </summary>
    Public Sub PasteFromClipboard()
        Dim beforeCount As Integer = 0
        Try
            If _document IsNot Nothing AndAlso _document.Drawables IsNot Nothing Then
                beforeCount = _document.Drawables.Count
            End If
        Catch
        End Try

        Try
            PasteCore()
        Catch ex As Exception
            System.Diagnostics.Debug.WriteLine("Paste failed: " & ex.ToString())
            Try
                PolyCut.Application.GetService(Of SnackbarService)()?.GenerateError(
                    "粘贴失败", ex.Message, 8)
            Catch
            End Try
        Finally
            Try
                If _document IsNot Nothing AndAlso _document.Drawables IsNot Nothing AndAlso
                   _document.Drawables.Count < beforeCount Then
                    _undoRedo.Undo()
                    PolyCut.Application.GetService(Of SnackbarService)()?.GenerateError(
                        "粘贴已撤销",
                        "粘贴后工作区元素反而变少了，已自动撤销这一步。请重新复制后再试。", 10)
                End If
            Catch
            End Try
        End Try
    End Sub

    ''' <summary>Duplicate the selection into a rows x columns grid, offset by the given
    ''' spacing. The original stays where it is, so an N-cell grid yields N-1 copies.
    ''' Returns how many copies were actually created; the whole grid lands on the undo
    ''' stack as a single step, so one Ctrl+Z removes it.</summary>
    Public Function ArraySelection(rows As Integer, columns As Integer,
                                   columnGap As Double, rowGap As Double) As Integer
        Dim selectedDrawables = _selectionManager.SelectedItems
        If selectedDrawables Is Nothing OrElse selectedDrawables.Count = 0 Then Return 0
        If rows < 1 OrElse columns < 1 Then Return 0
        If rows * columns <= 1 Then Return 0

        ' Snapshot the selection once. Every copy is built from this same data, so the grid
        ' is uniform even if the user had mixed element types selected.
        Dim pd = _projectService.CreateProjectDataForClipboard(selectedDrawables, _document.DrawingGroup)
        If pd Is Nothing Then Return 0

        Dim designerStyle = TryCast(Application.Current?.TryFindResource("DesignerItemStyle"), Style)
        Dim steps As New List(Of IUndoableAction)()
        Dim made As Integer = 0

        For r = 0 To rows - 1
            For c = 0 To columns - 1
                If r = 0 AndAlso c = 0 Then Continue For

                Dim pasteAct As New PasteDrawablesAction(_document, _projectService, pd, designerStyle, _selectionManager)
                If Not pasteAct.Execute() Then Continue For

                ' Execute() leaves the freshly pasted items selected, which is what makes
                ' the offset below safe: only this round's copies are moved.
                Dim moves As New List(Of (IDrawable, TransformState, TransformState))()
                For Each sel In _selectionManager.SelectedItems
                    Dim wrapper = TryCast(sel.DrawableElement?.Parent, ContentControl)
                    If wrapper Is Nothing Then Continue For

                    Dim before = TransformAction.CaptureState(wrapper)
                    TransformAction.ApplyMove(wrapper, c * columnGap, r * rowGap)
                    Dim after = TransformAction.CaptureState(wrapper)
                    moves.Add((sel, before, after))
                Next

                ' A copy whose wrapper could not be found still exists in the document, so
                ' the paste itself must stay on the undo stack even when nothing moved.
                If moves.Count > 0 Then
                    steps.Add(New CompositeAction(New List(Of IUndoableAction) From {pasteAct, New TransformAction(moves)}))
                Else
                    steps.Add(pasteAct)
                End If
                made += 1
            Next
        Next

        If steps.Count > 0 Then
            _undoRedo.Push(New CompositeAction(steps))
        End If

        Return made
    End Function

    Private Sub PasteCore()
        Dim data = Clipboard.GetDataObject()
        If data Is Nothing OrElse Not data.GetDataPresent(ClipFormat) Then Return

        Dim json = TryCast(data.GetData(ClipFormat), String)
        If String.IsNullOrWhiteSpace(json) Then Return

        Dim pd = JsonSerializer.Deserialize(Of ProjectData)(json, _jsonOpts)
        If pd Is Nothing Then Return

        ' Nothing to paste - do not touch the document at all.
        Dim hasDrawables = pd.Drawables IsNot Nothing AndAlso pd.Drawables.Count > 0
        Dim hasGroups = pd.Groups IsNot Nothing AndAlso pd.Groups.Count > 0
        If Not hasDrawables AndAlso Not hasGroups Then Return

        Dim designerStyle = TryCast(Application.Current?.TryFindResource("DesignerItemStyle"), Style)

        If _lastData Is Nothing OrElse _lastData.Hash = pd.Hash Then
            offset += 25
        Else
            offset = 25
        End If

        Dim act As New PasteDrawablesAction(_document, _projectService, pd, designerStyle, _selectionManager)


        If act.Execute() Then

            Dim items As New List(Of (IDrawable, TransformState, TransformState))

            For Each sel In _selectionManager.SelectedItems
                Dim wrapper = TryCast(sel.DrawableElement.Parent, ContentControl)
                If wrapper Is Nothing Then Continue For

                Dim before = TransformAction.CaptureState(wrapper)
                TransformAction.ApplyMove(wrapper, offset, offset)
                Dim after = TransformAction.CaptureState(wrapper)
                items.Add((sel, before, after))
            Next
            Dim tfact As New TransformAction(items)

            Dim comboAct As New CompositeAction(New List(Of IUndoableAction) From {act, tfact})

            _undoRedo.Push(comboAct)
        End If
        _lastData = pd
    End Sub


End Class
