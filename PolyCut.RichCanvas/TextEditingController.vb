Imports System.ComponentModel
Imports System.Windows.Threading

Imports PolyCut.Shared

Public Class TextEditingController
    Private _activeTextBox As TextBox
    Private _styleSource As TextBox
    Private _sessionStart As TextEditState
    Private _suppressStyleSourceApply As Boolean

    Public ReadOnly Property ActiveTextBox As TextBox
        Get
            Return _activeTextBox
        End Get
    End Property

    Public Property SuppressStyleSourceApply As Boolean
        Get
            Return _suppressStyleSourceApply
        End Get
        Set(value As Boolean)
            _suppressStyleSourceApply = value
        End Set
    End Property

    Public Event SessionFinished(element As UIElement)
    Public Event EditRequested(sender As Object, textBox As TextBox)
    Public Event TextEdited(sender As Object, textBox As TextBox, before As TextEditState, after As TextEditState)

    Public Sub BeginEditingText(textBox As TextBox, pcanvas As PolyCanvas)
        If textBox Is Nothing OrElse _activeTextBox Is textBox Then Return
        BeginSession(textBox, pcanvas)
        FocusTextBox(textBox)
    End Sub

    Public Sub StartNewText(startPos As Point, chars As TextCharacteristics, pcanvas As PolyCanvas, Optional width As Double = Double.NaN, Optional height As Double = Double.NaN)
        If _activeTextBox IsNot Nothing Then Return
        pcanvas.SelectionManager.ClearSelection()
        Dim textBox = CreateTextBox(startPos, chars, width, height)
        pcanvas.Children.Add(textBox)
        BeginSession(textBox, pcanvas)
        FocusTextBox(textBox)
    End Sub

    ''' <summary>Why an edit session ended. Commit keeps what was typed; Cancel restores
    ''' the box to the state it had when the session started.</summary>
    Private Enum SessionEnd
        Commit
        Cancel
    End Enum

    Public Sub CommitActiveText(pcanvas As PolyCanvas)
        EndSession(pcanvas, SessionEnd.Commit)
    End Sub

    Public Sub CancelActiveText(pcanvas As PolyCanvas)
        EndSession(pcanvas, SessionEnd.Cancel)
    End Sub

    Public Sub EvaluateTextCommit()
        If _activeTextBox Is Nothing OrElse IsFocusRetained() Then Return
        CommitActiveText(TryCast(_activeTextBox.Parent, PolyCanvas))
    End Sub

    Public Sub RefocusActiveTextBox()
        If _activeTextBox Is Nothing Then Return
        FocusTextBox(_activeTextBox)
    End Sub

    Public Sub AttachTextStyleSource(source As TextBox)
        If source Is Nothing OrElse _styleSource Is source Then Return
        _styleSource = source
        DependencyPropertyDescriptor.FromProperty(TextBox.FontSizeProperty, GetType(TextBox)).AddValueChanged(source, AddressOf OnStyleSourceSizeChanged)
        DependencyPropertyDescriptor.FromProperty(TextBox.FontFamilyProperty, GetType(TextBox)).AddValueChanged(source, AddressOf OnStyleSourceFamilyChanged)
        DependencyPropertyDescriptor.FromProperty(TextBox.FontStyleProperty, GetType(TextBox)).AddValueChanged(source, AddressOf OnStyleSourceStyleChanged)
        DependencyPropertyDescriptor.FromProperty(TextBox.FontWeightProperty, GetType(TextBox)).AddValueChanged(source, AddressOf OnStyleSourceWeightChanged)
        DependencyPropertyDescriptor.FromProperty(TextBox.TextAlignmentProperty, GetType(TextBox)).AddValueChanged(source, AddressOf OnStyleSourceAlignmentChanged)
    End Sub



    Public Function HandleTextMouseDown(mode As CanvasMode, e As MouseButtonEventArgs) As Boolean
        If mode <> CanvasMode.Text OrElse e.ChangedButton <> MouseButton.Left Then Return False
        Dim source = TryCast(e.OriginalSource, DependencyObject)
        If source Is Nothing Then Return False
        Dim textBox = FindEditableTextBox(source)
        If textBox Is Nothing Then Return False
        If textBox IsNot _activeTextBox Then
            RaiseEditRequested(textBox)
            e.Handled = True
        End If
        Return True
    End Function

    Public Sub RaiseEditRequested(textBox As TextBox)
        RaiseEvent EditRequested(Me, textBox)
    End Sub

    Public Function FindEditableTextBox(start As DependencyObject) As TextBox
        Dim current = start
        While current IsNot Nothing
            Dim textBox = TryCast(current, TextBox)
            If textBox IsNot Nothing Then Return textBox
            Dim wrapper = TryCast(current, ContentControl)
            If wrapper IsNot Nothing AndAlso TypeOf wrapper.Content Is TextBox Then
                Return CType(wrapper.Content, TextBox)
            End If
            current = VisualTreeHelper.GetParent(current)
        End While
        Return Nothing
    End Function

    Private Sub BeginSession(textBox As TextBox, pcanvas As PolyCanvas)
        TextGeometryHelper.UseDefaultContextMenuFont(textBox)
        CommitActiveText(pcanvas)
        _activeTextBox = textBox
        _sessionStart = New TextEditState(textBox)
        TextEditHelper.SetIsEditing(textBox, True)

        'Freeze the width on re-edit: existing text boxes carry Width=NaN and would size
        'themselves to the whole line instead of wrapping inside the drawn box.
        If Double.IsNaN(textBox.Width) AndAlso textBox.ActualWidth > 0 Then
            textBox.Width = textBox.ActualWidth
        End If

        'Hide the outline geometry while typing so only the live text is visible.
        textBox.Background = Brushes.Transparent
        textBox.Cursor = Cursors.IBeam
        AddHandler textBox.TextChanged, AddressOf OnSessionTextChanged
        AddHandler textBox.SizeChanged, AddressOf OnSessionSizeChanged
        AddHandler textBox.LostFocus, AddressOf OnActiveTextBoxLostFocus
        AddHandler textBox.PreviewKeyDown, AddressOf OnActiveTextBoxKeyDown
        UpdateSessionInk()
    End Sub

    Private Sub OnSessionTextChanged(sender As Object, e As TextChangedEventArgs)
        UpdateSessionInk()
    End Sub

    Private Sub OnSessionSizeChanged(sender As Object, e As SizeChangedEventArgs)
        UpdateSessionInk()
    End Sub

    Private Sub UpdateSessionInk()
        Dim textBox = _activeTextBox
        If textBox Is Nothing OrElse TextEditHelper.GetIsRestoring(textBox) Then Return

        'Fixed-size boxes wrap, so their width must not be fitted to the text.
        If Not Double.IsNaN(textBox.Width) AndAlso textBox.Width > 0 Then Return

        TextGeometryHelper.FitToInk(textBox)
        textBox.UpdateLayout()
    End Sub

    Private Sub EndSession(pcanvas As PolyCanvas, reason As SessionEnd)
        If _activeTextBox Is Nothing Then Return
        Dim textBox = _activeTextBox
        textBox.Cursor = Cursors.Arrow
        Dim wasEditingExisting = TypeOf textBox.Parent Is ContentControl
        _activeTextBox = Nothing
        RemoveHandler textBox.LostFocus, AddressOf OnActiveTextBoxLostFocus
        RemoveHandler textBox.PreviewKeyDown, AddressOf OnActiveTextBoxKeyDown
        RemoveHandler textBox.TextChanged, AddressOf OnSessionTextChanged
        RemoveHandler textBox.SizeChanged, AddressOf OnSessionSizeChanged
        TextEditHelper.SetIsEditing(textBox, False)
        If pcanvas IsNot Nothing AndAlso pcanvas.Children.Contains(textBox) Then
            pcanvas.Children.Remove(textBox)
        End If
        If wasEditingExisting Then
            If reason = SessionEnd.Cancel Then
                ' Esc on an existing box puts it back exactly as it was. It used to take the
                ' same path as Enter and commit the edit, so there was no way to abandon a
                ' change once you had typed over the original text.
                _sessionStart.ApplyTo(textBox)
                textBox.UpdateLayout()
                ' ApplyTo restores the text with IsRestoring set, which suppresses the text
                ' changed handler, so nothing asks the box to repaint itself. Without this
                ' the element kept the right name but drew as an empty box.
                textBox.InvalidateVisual()
                Dim host = TryCast(textBox.Parent, ContentControl)
                If host IsNot Nothing Then
                    host.UpdateLayout()
                    host.InvalidateVisual()
                End If
            Else
                ' Finish layout once, before recording history. Undo and redo restore these exact snapshots rather than repeating auto-fit and moving the pivot again.
                Dim wrapper = DirectCast(textBox.Parent, ContentControl)
                textBox.UpdateLayout()
                wrapper.Width = If(Double.IsNaN(textBox.Width), wrapper.ActualWidth, textBox.Width)
                wrapper.Height = If(Double.IsNaN(textBox.Height), wrapper.ActualHeight, textBox.Height)
                If textBox.IsKeyboardFocusWithin Then wrapper.Focus()
                wrapper.UpdateLayout()
                Dim after As New TextEditState(textBox)
                If _sessionStart.HasChanges(after) Then
                    RaiseEvent TextEdited(Me, textBox, _sessionStart, after)
                Else
                    _sessionStart.ApplyTo(textBox)
                End If
            End If
        End If
        _sessionStart = Nothing

        ' A brand-new box that has text in it becomes an element whichever key ended the
        ' session. Esc used to throw the whole box away, so typing a caption and pressing
        ' Esc lost the caption and the box together.
        If Not wasEditingExisting AndAlso Not String.IsNullOrEmpty(textBox.Text) Then
            RaiseEvent SessionFinished(textBox)
        End If
    End Sub

    Private Sub FocusTextBox(textBox As TextBox)
        If Not textBox.Focus() Then
            textBox.Dispatcher.BeginInvoke(Sub()
                                              If _activeTextBox Is textBox Then textBox.Focus()
                                          End Sub, DispatcherPriority.Input)
        End If
    End Sub

    Private Sub OnActiveTextBoxLostFocus(sender As Object, e As RoutedEventArgs)
        Dim textBox = DirectCast(sender, TextBox)
        If _activeTextBox IsNot textBox Then Return
        If IsFocusRetained() Then
            If Keyboard.FocusedElement Is textBox.Parent Then textBox.Focus()
            Return
        End If
        CommitActiveText(TryCast(textBox.Parent, PolyCanvas))
    End Sub

    Private Sub OnActiveTextBoxKeyDown(sender As Object, e As KeyEventArgs)
        Dim textBox = DirectCast(sender, TextBox)
        If e.Key = Key.Escape Then
            e.Handled = True
            CancelActiveText(TryCast(textBox.Parent, PolyCanvas))
        ElseIf e.Key = Key.Enter AndAlso Not textBox.AcceptsReturn Then
            e.Handled = True
            CommitActiveText(TryCast(textBox.Parent, PolyCanvas))
        End If
    End Sub

    Private Sub OnStyleSourceSizeChanged(sender As Object, e As EventArgs)
        If _activeTextBox Is Nothing OrElse _suppressStyleSourceApply Then Return
        TextGeometryHelper.SetEffectiveFontSize(_activeTextBox, _styleSource.FontSize)
    End Sub

    Private Sub OnStyleSourceFamilyChanged(sender As Object, e As EventArgs)
        If _activeTextBox Is Nothing OrElse _suppressStyleSourceApply Then Return
        _activeTextBox.FontFamily = _styleSource.FontFamily
    End Sub

    Private Sub OnStyleSourceStyleChanged(sender As Object, e As EventArgs)
        If _activeTextBox Is Nothing OrElse _suppressStyleSourceApply Then Return
        _activeTextBox.FontStyle = _styleSource.FontStyle
    End Sub

    Private Sub OnStyleSourceWeightChanged(sender As Object, e As EventArgs)
        If _activeTextBox Is Nothing OrElse _suppressStyleSourceApply Then Return
        _activeTextBox.FontWeight = _styleSource.FontWeight
    End Sub

    Private Sub OnStyleSourceAlignmentChanged(sender As Object, e As EventArgs)
        If _activeTextBox Is Nothing OrElse _suppressStyleSourceApply Then Return
        _activeTextBox.TextAlignment = _styleSource.TextAlignment
    End Sub

    Private Function IsFocusRetained() As Boolean
        If _activeTextBox Is Nothing Then Return False
        If _activeTextBox.IsKeyboardFocusWithin Then Return True
        If IsFocusWithinTextStyleHost() Then Return True
        Return Keyboard.FocusedElement Is _activeTextBox.Parent
    End Function

    Private Shared Function IsFocusWithinTextStyleHost() As Boolean
        Dim focused = Keyboard.FocusedElement
        While focused IsNot Nothing
            If TextEditHelper.GetIsTextStyleHost(focused) Then Return True
            focused = VisualTreeHelper.GetParent(focused)
        End While
        Return False
    End Function

    Private Shared Function CreateTextBox(p As Point, chars As TextCharacteristics, Optional width As Double = Double.NaN, Optional height As Double = Double.NaN) As TextBox

        'Width comes from the drawn box; height follows the wrapped content.
        Dim hasWidth As Boolean = Not Double.IsNaN(width) AndAlso width > 0
        Dim boxWidth As Double = If(hasWidth, width, 160)

        Dim tb As New TextBox With {
            .Width = boxWidth,
            .Height = Double.NaN,
            .MinHeight = If(Not Double.IsNaN(height) AndAlso height > 0, height, 24),
            .TextWrapping = TextWrapping.Wrap,
            .AcceptsReturn = True,
            .Background = Brushes.Transparent,
            .BorderBrush = Brushes.Transparent,
            .Foreground = Brushes.Black,
            .BorderThickness = New Thickness(1),
            .Style = Nothing,
            .Text = "",
            .AcceptsTab = True,
            .Padding = New Thickness(0)
        }

        chars?.ApplyTo(tb)
        TextBoxBorderOverlay.PrepareTextBox(tb)

        Canvas.SetLeft(tb, p.X)
        Canvas.SetTop(tb, p.Y)

        Return tb
    End Function
End Class
