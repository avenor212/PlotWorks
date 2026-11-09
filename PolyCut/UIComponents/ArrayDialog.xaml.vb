Imports System.Windows
Imports System.Windows.Controls

Public Class ArrayDialog
    Inherits Wpf.Ui.Controls.FluentWindow

    ''' <summary>Upper bound on copies per run. A mistyped 1000 x 1000 would otherwise lock
    ''' the UI while it materialises a million wrappers.</summary>
    Private Const MaxCells As Integer = 400

    Public Sub New()
        Me.New(60, 60)
    End Sub

    ''' <summary>Seeds the spacing so the first click already produces a readable grid:
    ''' copies are placed one card apart rather than on top of each other.</summary>
    Public Sub New(copyWidth As Double, copyHeight As Double)
        InitializeComponent()

        RowsTextBox.Text = "1"
        ColumnsTextBox.Text = "2"
        ColumnGapTextBox.Text = FormatGap(copyWidth + 10)
        RowGapTextBox.Text = FormatGap(copyHeight + 10)

        AddHandler Loaded, AddressOf OnLoaded
        AddHandler RowsTextBox.TextChanged, AddressOf OnInputChanged
        AddHandler ColumnsTextBox.TextChanged, AddressOf OnInputChanged
        AddHandler ColumnGapTextBox.TextChanged, AddressOf OnInputChanged
        AddHandler RowGapTextBox.TextChanged, AddressOf OnInputChanged

        UpdateSummary()
    End Sub

    Private Shared Function FormatGap(value As Double) As String
        If Double.IsNaN(value) OrElse Double.IsInfinity(value) OrElse value <= 0 Then Return "50"
        Return Math.Round(value, 1).ToString(Globalization.CultureInfo.CurrentCulture)
    End Function

    Private Sub OnLoaded(sender As Object, e As RoutedEventArgs)
        ColumnsTextBox.Focus()
        ColumnsTextBox.SelectAll()
    End Sub

    Private Sub OnInputChanged(sender As Object, e As TextChangedEventArgs)
        UpdateSummary()
    End Sub

    Public ReadOnly Property Rows As Integer
        Get
            Return Math.Max(1, ParseInt(RowsTextBox.Text, 1))
        End Get
    End Property

    Public ReadOnly Property Columns As Integer
        Get
            Return Math.Max(1, ParseInt(ColumnsTextBox.Text, 1))
        End Get
    End Property

    Public ReadOnly Property ColumnGap As Double
        Get
            Return Math.Max(0, ParseDouble(ColumnGapTextBox.Text, 0))
        End Get
    End Property

    Public ReadOnly Property RowGap As Double
        Get
            Return Math.Max(0, ParseDouble(RowGapTextBox.Text, 0))
        End Get
    End Property

    Private ReadOnly Property CellCount As Integer
        Get
            Return Rows * Columns
        End Get
    End Property

    ''' <summary>True when the numbers are usable: at least one copy, and not more than
    ''' <see cref="MaxCells"/> in total.</summary>
    Private Function IsInputValid() As Boolean
        If CellCount <= 1 Then Return False
        If CellCount > MaxCells Then Return False
        If ColumnGap <= 0 AndAlso Columns > 1 Then Return False
        If RowGap <= 0 AndAlso Rows > 1 Then Return False
        Return True
    End Function

    Private Sub UpdateSummary()
        Dim copies = Math.Max(0, CellCount - 1)

        If CellCount <= 1 Then
            SummaryText.Text = "行数和列数都是 1，没有需要复制的内容。"
        ElseIf CellCount > MaxCells Then
            SummaryText.Text = $"一次最多 {MaxCells} 份（现在是 {CellCount} 份）。请减少行数或列数。"
        ElseIf ColumnGap <= 0 AndAlso Columns > 1 Then
            SummaryText.Text = "列间距必须大于 0，否则每一列都会叠在一起。"
        ElseIf RowGap <= 0 AndAlso Rows > 1 Then
            SummaryText.Text = "行间距必须大于 0，否则每一行都会叠在一起。"
        Else
            Dim spanX = (Columns - 1) * ColumnGap
            Dim spanY = (Rows - 1) * RowGap
            SummaryText.Text = $"将生成 {Rows} 行 × {Columns} 列，共 {copies} 份副本。" & Environment.NewLine &
                               $"阵列占用约 {spanX:0.#} × {spanY:0.#} mm（不含原件自身尺寸）。"
        End If

        If OkButton IsNot Nothing Then OkButton.IsEnabled = IsInputValid()
    End Sub

    Private Sub OkButton_Click(sender As Object, e As RoutedEventArgs)
        If Not IsInputValid() Then
            UpdateSummary()
            Return
        End If
        DialogResult = True
    End Sub

    Private Sub CancelButton_Click(sender As Object, e As RoutedEventArgs)
        DialogResult = False
    End Sub

    Private Shared Function ParseInt(text As String, fallback As Integer) As Integer
        Dim value As Integer
        If Integer.TryParse(If(text, String.Empty).Trim(), value) Then Return value
        Return fallback
    End Function

    Private Shared Function ParseDouble(text As String, fallback As Double) As Double
        Dim value As Double
        Dim raw = If(text, String.Empty).Trim()
        If Double.TryParse(raw, Globalization.NumberStyles.Float, Globalization.CultureInfo.CurrentCulture, value) Then Return value
        If Double.TryParse(raw, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture, value) Then Return value
        Return fallback
    End Function

End Class
