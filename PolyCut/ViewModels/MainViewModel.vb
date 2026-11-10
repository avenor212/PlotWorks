Option Strict On
Imports System.Collections.ObjectModel
Imports System.Collections.Specialized
Imports System.IO
Imports System.Net.WebRequestMethods

Imports CommunityToolkit.Mvvm.ComponentModel
Imports CommunityToolkit.Mvvm.Input

Imports PolyCut.Core
Imports PolyCut.RichCanvas
Imports PolyCut.Services.UndoRedo
Imports PolyCut.Shared

Imports Svg

Imports Wpf.Ui
Partial Public Class MainViewModel : Inherits ObservableObject

    ' Services
    Private ReadOnly _snackbarService As SnackbarService
    Private ReadOnly _navigationService As INavigationService
    Private ReadOnly _argsService As CommandLineArgsService
    Private ReadOnly _svgImportService As IVectorImportService
    Private ReadOnly _undoRedoService As UndoRedoService
    Private ReadOnly _projectService As ProjectSerializationService
    Private ReadOnly _contentDialogService As IContentDialogService
    Private ReadOnly _a1Service As BambuA1Service

    ''' <summary>Connection to the Bambu Lab A1 used by the Monitor page.</summary>
    Public ReadOnly Property A1Service As BambuA1Service
        Get
            Return _a1Service
        End Get
    End Property

    'Operating System detection
    <ObservableProperty> Private _IsWindows As Boolean = Not WineDetection.IsRunningUnderWine

    ' State / configuration
    <ObservableProperty> Private _Printers As ObservableCollection(Of Printer)
    <ObservableProperty> Private _Printer As Printer
    <ObservableProperty> Private _Configuration As ProcessorConfiguration
    Public ReadOnly Property UIConfiguration As UIConfiguration

    <ObservableProperty> Private _GCode As String = Nothing
    <ObservableProperty> Private _GCodeGeometry As GCodeGeometry
    <ObservableProperty> Private _GCodePaths As ObservableCollection(Of Line) = New ObservableCollection(Of Line)()
    <ObservableProperty> Private _GeneratedGCode As List(Of GCode)

    Public ReadOnly Property Document As Document

    <ObservableProperty>
    Private _FlatSidebarItems As New ObservableCollection(Of SidebarItemVM)
    Private _sidebarRebuildQueued As Boolean

    Private Sub RebuildFlatSidebarItems()
        Dim d = Application.Current?.Dispatcher
        If d Is Nothing OrElse Not d.CheckAccess() Then
            RebuildFlatSidebarItemsNow()
            Return
        End If
        If _sidebarRebuildQueued Then Return
        _sidebarRebuildQueued = True
        d.BeginInvoke(New Action(AddressOf RebuildFlatSidebarItemsNow), Threading.DispatcherPriority.Background)
    End Sub

    Private Sub RebuildFlatSidebarItemsNow()
        _sidebarRebuildQueued = False
        FlatSidebarItems.Clear()

        For i = Document.ImportRoots.Count - 1 To 0 Step -1
            Dim grp = Document.ImportRoots(i)
            If grp Is Nothing Then Continue For

            Dim children = grp.DisplayChildren
            If children Is Nothing Then Continue For

            ' Hide Drawing Group if empty
            If Document.IsDrawingGroup(grp) AndAlso children.Count = 0 Then Continue For

            For j = children.Count - 1 To 0 Step -1
                Dim ch = children(j)
                If ch Is Nothing Then Continue For
                If TypeOf ch Is DrawableGroup Then Continue For
                FlatSidebarItems.Add(New SidebarItemVM(grp, ch))
            Next
        Next
    End Sub

    ' Suspension flag for transform message handling
    Private _suspendTransformMessageHandling As Boolean = False

    ' UI meta properties
    <NotifyPropertyChangedFor(NameOf(LogarithmicPreviewSpeed))>
    <ObservableProperty> Private _PreviewRenderSpeed As Double = 0.48

    Private Sub OnPreviewRenderSpeedChanged(oldValue As Double, newValue As Double)
        LogarithmicPreviewSpeed = CInt(30 * (Math.Exp(6 * newValue) - 1) + 30)
    End Sub

    <ObservableProperty> Private _LogarithmicPreviewSpeed As Integer = 500


    ' Convenience / computed properties
    Public ReadOnly Property SelectionManager As SelectionManager

    Public ReadOnly Property PolyCutDocumentName As String
        Get
            ' A saved project already knows what it is called. Without this the upload name
            ' was always "PolyCut1.gcode" no matter which project was open, so uploading a
            ' second job silently replaced the first one on the printer - which is exactly
            ' how /PolyCut1.gcode got overwritten before.
            If Not String.IsNullOrWhiteSpace(CurrentProjectPath) Then
                Return Path.GetFileNameWithoutExtension(CurrentProjectPath) & ".gcode"
            End If

            If Document.ImportRoots.Count <> 0 AndAlso Not Document.IsDrawingGroup(Document.ImportRoots.First) Then
                Return Document.ImportRoots.First.Name.Replace(".svg", ".gcode")
            Else
                Return "PlotWorks1.gcode"
            End If
        End Get
    End Property

    Public Event PrinterConfigOpened()
    Public Event PrinterConfigClosed()
    Public Event ResetViewRequested()

    ' Commands

    <RelayCommand> Public Sub MainViewLoaded()
        If _argsService.Args.Length > 0 Then DragSVGs(_argsService.Args)
    End Sub

    <RelayCommand> Public Async Function MainViewClosing() As Task
        SettingsHandler.WriteConfiguration(Configuration)
        Await SettingsHandler.WriteUIConfiguration(UIConfiguration)
    End Function

    <RelayCommand> Public Sub CopyGCodeToClipboard()
        If Not String.IsNullOrEmpty(GCode) Then
            Clipboard.SetText(GCode)
        End If
    End Sub

    ''' <summary>
    ''' Saves the generated G-code to a file, so it can be run from an SD card or any other host
    ''' without going through the network upload path.
    ''' </summary>
    <RelayCommand>
    Private Sub ExportGCodeFile()

        If String.IsNullOrWhiteSpace(GCode) Then
            _snackbarService.GenerateError("无法导出 G-code", "请先点击右上角的生成按钮，再导出 G-code")
            Return
        End If

        Dim suggestedName As String = PolyCutDocumentName
        If String.IsNullOrWhiteSpace(suggestedName) Then suggestedName = "PlotWorks1.gcode"

        Dim fsd As New Microsoft.Win32.SaveFileDialog With {
            .Filter = "GCode (*.gcode;*.ngc)|*.gcode;*.ngc|所有文件 (*.*)|*.*",
            .FileName = suggestedName,
            .DefaultExt = ".gcode",
            .AddExtension = True
        }

        If Not fsd.ShowDialog() Then Return

        Try
            IO.File.WriteAllText(fsd.FileName, GCode, New Text.UTF8Encoding(False))
            _snackbarService.GenerateSuccess("G-code 已保存", fsd.FileName, 8)
        Catch ex As Exception
            _snackbarService.GenerateError("保存 G-code 失败", ex.Message)
        End Try

    End Sub

    <RelayCommand> Public Sub UnionShapes()
        BooleanOperation(GeometryCombineMode.Union)
    End Sub

    <RelayCommand> Public Sub IntersectShapes()
        BooleanOperation(GeometryCombineMode.Intersect)
    End Sub

    <RelayCommand> Public Sub SubtractShapes()
        BooleanOperation(GeometryCombineMode.Exclude)
    End Sub

    <RelayCommand> Public Sub XorShapes()
        BooleanOperation(GeometryCombineMode.Xor)
    End Sub

    <RelayCommand>
    Private Async Function AddWeedBoxes() As Task
        _undoRedoService.Push(Await WeedingBoxTool.AddAsync(
            Document, PolyCanvas.ActiveInstance, SelectionManager, SettingsHandler.WeedingBoxConfiguration, _contentDialogService, _snackbarService))
    End Function

    <RelayCommand>
    Private Async Function RegenerateWeedBoxes() As Task
        _undoRedoService.Push(Await WeedingBoxTool.RegenerateAsync(
            Document, PolyCanvas.ActiveInstance, SelectionManager, SettingsHandler.WeedingBoxConfiguration, _contentDialogService, _snackbarService))
    End Function

    <RelayCommand>
    Private Async Function OpenWeedBoxesConfig() As Task
        Await WeedingBoxTool.OpenOptionsAsync(Document, PolyCanvas.ActiveInstance, SelectionManager, _undoRedoService, _snackbarService)
    End Function

    <RelayCommand> Public Sub Undo()
        PolyCanvas.ActiveInstance?.CommitActiveText()
        _undoRedoService.Undo()
    End Sub

    <RelayCommand> Public Sub Redo()
        PolyCanvas.ActiveInstance?.CommitActiveText()
        _undoRedoService.Redo()
    End Sub


    ' ----- Sidebar reordering -----

    <RelayCommand> Public Sub MoveSelectedItemUp(item As IDrawable)
        If Keyboard.IsKeyDown(Key.LeftCtrl) Then
            ReorderSelectedItem(item, Integer.MaxValue)
        Else
            ReorderSelectedItem(item, 1)
        End If
    End Sub

    <RelayCommand> Public Sub MoveSelectedItemDown(item As IDrawable)
        If Keyboard.IsKeyDown(Key.LeftCtrl) Then
            ReorderSelectedItem(item, Integer.MinValue)
        Else
            ReorderSelectedItem(item, -1)
        End If
    End Sub

    <RelayCommand> Public Sub MoveSelectedItemsCanvasUp()
        ReorderSelectedItems(SelectionManager.SelectedItems, 1)
    End Sub

    <RelayCommand> Public Sub MoveSelectedItemsCanvasDown()
        ReorderSelectedItems(SelectionManager.SelectedItems, -1)
    End Sub

    <RelayCommand> Public Sub MoveSelectedItemsCanvasToBottom()
        ReorderSelectedItems(SelectionManager.SelectedItems, Integer.MinValue)
    End Sub

    <RelayCommand> Public Sub MoveSelectedItemsCanvasToTop()
        ReorderSelectedItems(SelectionManager.SelectedItems, Integer.MaxValue)
    End Sub

    Private Sub ReorderSelectedItem(item As IDrawable, direction As Integer)
        If item Is Nothing OrElse TypeOf item Is DrawableGroup Then Return
        ReorderSelectedItems({item}, direction)
    End Sub

    Private Sub ReorderSelectedItems(items As IEnumerable(Of IDrawable), direction As Integer)
        Dim actions = ReorderPlanner.Move(Document, items, direction, Function(d) TryCast(Document.ParentGroupOf(d), DrawableGroup))
        If actions.Count > 0 Then
            _undoRedoService.Push(New CompositeAction(actions))
        End If
    End Sub

    Public Sub ReorderSelectedItems(sidebarOrder As IEnumerable(Of IDrawable))
        Using ModelBatch.Begin()
            Dim actions As New List(Of IUndoableAction)
            For Each siblings In sidebarOrder.GroupBy(Function(d) TryCast(Document.ParentGroupOf(d), DrawableGroup))
                If siblings.Key Is Nothing Then Continue For
                Dim target = siblings.Key.GroupChildren.Count - 1
                For Each item In siblings
                    actions.AddRange(ReorderPlanner.Move(Document, {item}, target - siblings.Key.GroupChildren.IndexOf(item), Function(d) TryCast(Document.ParentGroupOf(d), DrawableGroup)))
                    target -= 1
                Next
            Next
            If actions.Count > 0 Then _undoRedoService.Push(New CompositeAction(actions))
        End Using
        RebuildFlatSidebarItems()
    End Sub

    ' Constructor & initialization
    Public Sub New(snackbarService As SnackbarService, navigationService As INavigationService, argsService As CommandLineArgsService, svgImportService As IVectorImportService, undoRedoService As UndoRedoService, projectService As ProjectSerializationService, selectionManager As SelectionManager, document As Document, uiConfiguration As UIConfiguration, contentDialogService As IContentDialogService, a1Service As BambuA1Service)
        _snackbarService = snackbarService
        _navigationService = navigationService
        _argsService = argsService
        _svgImportService = svgImportService
        _undoRedoService = undoRedoService
        _projectService = projectService
        _contentDialogService = contentDialogService
        _a1Service = a1Service
        Me.SelectionManager = selectionManager
        Me.Document = document
        Me.UIConfiguration = uiConfiguration


        EventAggregator.Subscribe(Of TransformCompletedMessage)(AddressOf OnTransformCompleted)
        AddHandler document.Changed, AddressOf OnDocumentChanged
        Initialise()
        WireFlatSidebarInvalidation()
    End Sub

    Private Sub AnyDisplayChildrenChanged(sender As Object, e As NotifyCollectionChangedEventArgs)
        RebuildFlatSidebarItems()
    End Sub

    Private Sub WireFlatSidebarInvalidation()
        AddHandler Document.ImportRoots.CollectionChanged, AddressOf ImportedGroups_CollectionChanged

        ' existing
        For Each g In Document.ImportRoots
            If g Is Nothing Then Continue For
            AddHandler g.DisplayChildren.CollectionChanged, AddressOf AnyDisplayChildrenChanged
        Next

        RebuildFlatSidebarItems()

    End Sub

    Private Sub ImportedGroups_CollectionChanged(sender As Object, e As NotifyCollectionChangedEventArgs)
        If e.NewItems IsNot Nothing Then
            For Each g As DrawableGroup In e.NewItems
                AddHandler g.DisplayChildren.CollectionChanged, AddressOf AnyDisplayChildrenChanged
            Next
        End If
        If e.OldItems IsNot Nothing Then
            For Each g As DrawableGroup In e.OldItems
                RemoveHandler g.DisplayChildren.CollectionChanged, AddressOf AnyDisplayChildrenChanged
            Next
        End If

        RebuildFlatSidebarItems()
    End Sub

    Private Sub OnUIConfigurationChanged(sender As Object, e As System.ComponentModel.PropertyChangedEventArgs)
        If e.PropertyName = NameOf(UIConfiguration.UseRasterisedRendering) Then ApplyRasterisedRenderingSetting()
    End Sub

    Private Sub ApplyRasterisedRenderingSetting()
        PerformantRasteriser.Enabled = UIConfiguration.UseRasterisedRendering
    End Sub

    ''' <summary>
    ''' PolyCut used to ship an Ender 3 profile (235x235). This build targets the Bambu Lab A1
    ''' only, so upgrade those legacy default dimensions to the A1 build volume.
    ''' </summary>
    Private Sub ApplyA1DefaultsToPrinters()
        For Each p In Printers
            If p Is Nothing Then Continue For

            If p.BedWidth = 235D AndAlso p.BedHeight = 235D Then
                p.BedWidth = 256D
                p.BedHeight = 256D
            End If

            If p.WorkingWidth = 235D AndAlso p.WorkingHeight = 235D Then
                p.WorkingWidth = 256D
                p.WorkingHeight = 256D
            End If

            p.ClampWorkingArea()
            SettingsHandler.WritePrinter(p)
        Next
    End Sub

    Private Sub Initialise()
        Printers = SettingsHandler.GetPrinters
        ApplyA1DefaultsToPrinters()

        Printer = Printers.First
        Configuration = (SettingsHandler.GetConfigurations).First
        ApplyRasterisedRenderingSetting()
        AddHandler UIConfiguration.PropertyChanged, AddressOf OnUIConfigurationChanged

        If Document.DrawingGroup Is Nothing Then
            Document.DrawingGroup = New DrawableGroup(Document.DrawingGroupName)
            Document.Drawables.Add(Document.DrawingGroup)
            Document.ImportRoots.Add(Document.DrawingGroup)
        End If

        For Each grp In Document.Drawables.OfType(Of DrawableGroup)().ToList()
            If Not Document.IsDrawingGroup(grp) Then
                Document.Drawables.Remove(grp)
                If Not Document.ImportRoots.Contains(grp) Then Document.ImportRoots.Add(grp)
            End If
        Next
    End Sub

    ' -----------------
    ' Clipboard operations
    ' -----------------
    <RelayCommand>
    Private Sub CopySelection()
        Application.GetService(Of ClipboardService)().CopySelectionToClipboard()
    End Sub

    <RelayCommand>
    Private Sub CutSelection()
        Application.GetService(Of ClipboardService)().CutSelectionToClipboard()
    End Sub
    <RelayCommand>
    Private Sub PasteClipboard()
        Application.GetService(Of ClipboardService)().PasteFromClipboard()
    End Sub

    ''' <summary>Copy the selection and immediately paste it with the usual offset (Ctrl+D),
    ''' so building a row of name plates does not need clipboard round-trips.</summary>
    <RelayCommand>
    Private Sub DuplicateSelection()
        If SelectionManager Is Nothing OrElse SelectionManager.SelectedItems.Count = 0 Then Return

        Dim clip = Application.GetService(Of ClipboardService)()
        If clip Is Nothing Then Return

        clip.CopySelectionToClipboard()
        clip.PasteFromClipboard()
    End Sub

    ''' <summary>Duplicate the selection across a rows x columns grid. This is the step that
    ''' turns "20 name plates" from twenty copy-paste-nudge rounds into one action.</summary>
    <RelayCommand>
    Private Sub ArraySelection()
        If SelectionManager Is Nothing OrElse SelectionManager.SelectedItems.Count = 0 Then
            _snackbarService.GenerateError("无法阵列", "请先选中要复制的元素")
            Return
        End If

        Dim bounds = GetSelectionBounds()
        Dim dialog As New ArrayDialog(bounds.Width, bounds.Height)
        If Application.Current?.MainWindow IsNot Nothing Then dialog.Owner = Application.Current.MainWindow

        If dialog.ShowDialog() <> True Then Return

        Dim clip = Application.GetService(Of ClipboardService)()
        If clip Is Nothing Then
            _snackbarService.GenerateError("无法阵列", "剪贴板服务不可用，请重启 PlotWorks")
            Return
        End If

        Dim made As Integer = 0
        Try
            made = clip.ArraySelection(dialog.Rows, dialog.Columns, dialog.ColumnGap, dialog.RowGap)
        Catch ex As Exception
            _snackbarService.GenerateError("阵列失败", ex.Message, 10)
            Return
        End Try

        If made > 0 Then
            _snackbarService.GenerateSuccess("阵列完成", $"已复制 {made} 份，Ctrl+Z 可整体撤销")
        Else
            _snackbarService.GenerateError("没有生成副本", "请检查行数、列数和间距后重试")
        End If
    End Sub

    ''' <summary>Bounding box of the current selection in canvas units, used to seed the
    ''' array spacing so the copies start one card apart instead of overlapping.</summary>
    Private Function GetSelectionBounds() As System.Windows.Rect
        Dim left = Double.MaxValue
        Dim top = Double.MaxValue
        Dim right = Double.MinValue
        Dim bottom = Double.MinValue

        For Each sel In SelectionManager.SelectedItems
            Dim wrapper = TryCast(sel.DrawableElement?.Parent, ContentControl)
            If wrapper Is Nothing Then Continue For

            Dim x = System.Windows.Controls.Canvas.GetLeft(wrapper)
            Dim y = System.Windows.Controls.Canvas.GetTop(wrapper)
            If Double.IsNaN(x) Then x = 0
            If Double.IsNaN(y) Then y = 0

            Dim w = If(Double.IsNaN(wrapper.Width), wrapper.ActualWidth, wrapper.Width)
            Dim h = If(Double.IsNaN(wrapper.Height), wrapper.ActualHeight, wrapper.Height)

            left = Math.Min(left, x)
            top = Math.Min(top, y)
            right = Math.Max(right, x + w)
            bottom = Math.Max(bottom, y + h)
        Next

        If left > right OrElse top > bottom Then Return New System.Windows.Rect(0, 0, 60, 60)
        Return New System.Windows.Rect(left, top, right - left, bottom - top)
    End Function

    ' ---------------------------------------------------------------------------
    ' Batch placement for a multi-selection.
    '
    ' With one element selected the Transform panel edits that element's own X/Y. With
    ' several selected its bindings point at SelectedWrapper, which is Nothing, so every
    ' field goes blank — there was no way to say "put this group at 20, 30". These two
    ' values are the top-left of the group's bounding box; typing one moves the whole set.
    ' ---------------------------------------------------------------------------

    Private _multiLeftText As String = ""
    Private _multiTopText As String = ""

    Public Property MultiSelectionLeftText As String
        Get
            Return _multiLeftText
        End Get
        Set(value As String)
            _multiLeftText = value
            MoveMultiSelection(value, Nothing)
        End Set
    End Property

    Public Property MultiSelectionTopText As String
        Get
            Return _multiTopText
        End Get
        Set(value As String)
            _multiTopText = value
            MoveMultiSelection(Nothing, value)
        End Set
    End Property

    ''' <summary>Re-reads the group bounding box into the two text fields. Called whenever the
    ''' selection or the geometry changes, so the numbers never lag behind the canvas.</summary>
    Public Sub RefreshMultiSelectionBounds()
        If SelectionManager Is Nothing OrElse SelectionManager.Count < 2 Then
            _multiLeftText = ""
            _multiTopText = ""
        Else
            Dim b = GetSelectionBounds()
            _multiLeftText = Math.Round(b.Left, 2).ToString(Globalization.CultureInfo.CurrentCulture)
            _multiTopText = Math.Round(b.Top, 2).ToString(Globalization.CultureInfo.CurrentCulture)
        End If

        OnPropertyChanged(NameOf(MultiSelectionLeftText))
        OnPropertyChanged(NameOf(MultiSelectionTopText))
    End Sub

    ''' <summary>Translate the whole selection so its bounding box lands on the given
    ''' coordinate. Exactly one axis is supplied per call.</summary>
    Private Sub MoveMultiSelection(leftText As String, topText As String)
        If SelectionManager Is Nothing OrElse SelectionManager.Count < 2 Then Return

        Dim bounds = GetSelectionBounds()
        Dim dx As Double = 0
        Dim dy As Double = 0
        Dim target As Double

        If leftText IsNot Nothing Then
            If Not Double.TryParse(leftText, Globalization.NumberStyles.Float,
                                   Globalization.CultureInfo.CurrentCulture, target) Then Return
            dx = target - bounds.Left
        End If

        If topText IsNot Nothing Then
            If Not Double.TryParse(topText, Globalization.NumberStyles.Float,
                                   Globalization.CultureInfo.CurrentCulture, target) Then Return
            dy = target - bounds.Top
        End If

        If Math.Abs(dx) < 0.0001 AndAlso Math.Abs(dy) < 0.0001 Then Return

        Dim items As New List(Of (IDrawable, TransformState, TransformState))()
        For Each sel In SelectionManager.SelectedItems
            Dim wrapper = TryCast(sel.DrawableElement?.Parent, ContentControl)
            If wrapper Is Nothing Then Continue For

            Dim before = TransformAction.CaptureState(wrapper)
            TransformAction.ApplyMove(wrapper, dx, dy)
            Dim after = TransformAction.CaptureState(wrapper)
            items.Add((sel, before, after))
        Next

        If items.Count > 0 Then
            _undoRedoService.Push(New TransformAction(items))
        End If

        RefreshMultiSelectionBounds()
    End Sub

    <RelayCommand>
    Private Sub SelectAll()
        SelectionManager.ApplySelection(Document.Drawables.Where(Function(f) TypeOf f IsNot DrawableGroup))
    End Sub


    ' -----------------
    ' Selection / Transform event handlers
    ' -----------------
    Private Sub OnTransformCompleted(msg As TransformCompletedMessage)
        If _suspendTransformMessageHandling Then
            Debug.WriteLine("[MainViewModel] TransformCompleted ignored because transform recording is suspended")
            Return
        End If


        If msg?.Items Is Nothing OrElse msg.Items.Count = 0 Then Return

        Dim items = msg.Items.
            Where(Function(it) it.Drawable IsNot Nothing).
            Select(Function(it) (drawable:=it.Drawable, before:=TryCast(it.Before, TransformState), after:=TryCast(it.After, TransformState))).
            Where(Function(t) t.before IsNot Nothing AndAlso t.after IsNot Nothing AndAlso Not IsTransformUnchanged(t.before, t.after)).
            Select(Function(t) (t.drawable, t.before, t.after)).
            ToList()

        If items.Count > 0 Then
            _undoRedoService.Push(New TransformAction(items))
            RefreshMultiSelectionBounds()
        End If
    End Sub


    Private Function IsTransformUnchanged(before As TransformState, after As TransformState) As Boolean
        If before Is Nothing OrElse after Is Nothing Then Return True
        If before Is Nothing OrElse after Is Nothing Then Return True

        Return before.Matches(after)
    End Function

    ' -----------------
    ' Printer management
    ' -----------------
    <RelayCommand>
    Public Sub AddPrinter(newPrinter As Printer)
        Printers.Add(newPrinter)
        Printer = newPrinter
        SettingsHandler.WritePrinter(Printer)
        _snackbarService.GenerateSuccess("Added Preset".LT(), Printer.Name)
    End Sub

    <RelayCommand>
    Public Sub SavePrinter(newName As String)
        Dim nameToSave = If(String.IsNullOrWhiteSpace(newName), Printer?.Name, newName)

        If nameToSave Is Nothing Then
            _snackbarService.GenerateError("Error".LT(), "Printer name is empty".LT(), 3)
            Return
        End If

        Dim existingPrinter = Printers.FirstOrDefault(Function(p) p.Name = nameToSave)

        If existingPrinter IsNot Nothing Then
            Dim result = MessageBox.Show("A printer with the name '{0}' already exists. Do you want to overwrite it?".LTF(nameToSave), "Overwrite Printer?".LT(), MessageBoxButton.YesNo, MessageBoxImage.Exclamation)
            If result = MessageBoxResult.No Then
                _snackbarService.GenerateError("Error".LT(), "Printer not saved. Name already exists.".LT(), 3)
                Return
            End If

            existingPrinter.CopyFrom(Printer)
            existingPrinter.Name = newName
            Printer = existingPrinter
        Else
            Dim newPrinter As Printer = Printer.Clone()
            Printers.Add(newPrinter)
            newPrinter.Name = nameToSave
            Printer = newPrinter
        End If

        SettingsHandler.WritePrinter(Printer)
        _snackbarService.GenerateSuccess("Saved Preset".LT(), Printer.Name)
    End Sub

    <RelayCommand>
    Public Sub ConfigurePrinter()
        Dim printerConfigWindow = Application.GetService(Of PrinterConfig)
        RaiseEvent PrinterConfigOpened()
        Dim result = printerConfigWindow.ShowDialog()
        RaiseEvent PrinterConfigClosed()

        For Each p In Printers
            SettingsHandler.WritePrinter(p)
        Next
    End Sub

    <RelayCommand>
    Public Sub DeletePrinter()
        If Printers.Count > 1 Then
            Dim toRemove = Printer
            Printer = Printers((Printers.IndexOf(toRemove) + 1) Mod (Printers.Count))
            Printers.Remove(toRemove)
            SettingsHandler.DeletePrinter(toRemove)
        End If
    End Sub

    ' -----------------
    ' Import / SVG handling
    ' -----------------
    Public Property ImportPlacement As Func(Of String, Task(Of Point?))

    <RelayCommand>
    Private Async Function OpenRecentItem(filePath As String) As Task
        If Not IO.File.Exists(filePath) Then
            UIConfiguration.RecentItems.Remove(filePath)
            _snackbarService.GenerateCaution("Recent Items".LT(), "This file no longer exists and has been removed from the list".LT())
            Return
        End If

        If Path.GetExtension(filePath).Equals(".polycut", StringComparison.OrdinalIgnoreCase) Then
            Await OpenProject(filePath)
        Else
            Dim clickToPlace = (Keyboard.Modifiers And ModifierKeys.Shift) <> 0
            _navigationService.Navigate(GetType(SVGPage))
            Await ImportService.Import(_svgImportService, Document, _undoRedoService, {filePath}, If(clickToPlace, ImportPlacement, Nothing))
        End If
    End Function


    Public Sub AddRecentItem(filePath As String)
        filePath = IO.Path.GetFullPath(filePath)
        Dim existing = UIConfiguration.RecentItems.FirstOrDefault(Function(item) String.Equals(item, filePath, StringComparison.OrdinalIgnoreCase))
        If existing IsNot Nothing Then UIConfiguration.RecentItems.Remove(existing)
        UIConfiguration.RecentItems.Insert(0, filePath)
        While UIConfiguration.RecentItems.Count > 10
            UIConfiguration.RecentItems.RemoveAt(UIConfiguration.RecentItems.Count - 1)
        End While
    End Sub


    <RelayCommand>
    Private Async Sub BrowseSVG()

        Dim files = ImportService.OpenFileImportDialog()
        If files Is Nothing Then Return
        Dim clickToPlace = (Keyboard.Modifiers And ModifierKeys.Shift) <> 0

        _navigationService.Navigate(GetType(SVGPage))
        Await ImportService.Import(_svgImportService, Document, _undoRedoService, files, If(clickToPlace, ImportPlacement, Nothing))

    End Sub

    Public Async Sub DragSVGs(x As String(), Optional dropPosition As Point? = Nothing)
        Dim legalFiles = x.Where(Function(f) IO.File.Exists(f)).Where(Function(f) ImportService.AllSupportedExtensions.Contains(New FileInfo(f).Extension, StringComparer.InvariantCultureIgnoreCase))

        _navigationService.Navigate(GetType(SVGPage))
        Await ImportService.Import(_svgImportService, Document, _undoRedoService, legalFiles, If(dropPosition.HasValue, ImportPlacement, Nothing), dropPosition)
    End Sub


    ' -----------------
    ' Drawable collection management (IDrawableManager)
    ' -----------------

    Public Sub AddDrawableElement(element As FrameworkElement)
        Dim action As New AddDrawableAction(Document, element)
        If action.Execute() Then
            _undoRedoService.Push(action)
        End If
    End Sub

    Private Sub OnDocumentChanged(sender As Object, e As EventArgs)
        OnPropertyChanged(NameOf(FlatSidebarItems))
    End Sub

    Public Sub RemoveSelectedDrawables()
        Dim selectedItems = SelectionManager.SelectedItems.ToList()
        If selectedItems.Count = 0 Then Return

        PerformRemoveDrawables(selectedItems)
        SelectionManager.ClearSelection()
    End Sub

    Private Sub PerformRemoveDrawables(drawables As List(Of IDrawable))
        Dim actions As New List(Of IUndoableAction)()
        Dim parentGroups = New HashSet(Of DrawableGroup)(drawables.Select(Function(d) TryCast(Document.ParentGroupOf(d), DrawableGroup)).Where(Function(pg) pg IsNot Nothing))

        _suspendTransformMessageHandling = True

        Dim removed As New List(Of IDrawable)

        Dim batch = ModelBatch.Begin()
        Document.BeginRemovalDeferral()
        Try
            For Each drawable In drawables
                Dim action As New RemoveDrawableAction(Document, drawable, SelectionManager)
                If action.Execute() Then
                    actions.Add(action)
                    removed.Add(drawable)
                End If
            Next
        Finally
            Document.EndRemovalDeferral()
            batch.Dispose()
        End Try

        If removed.Count > 0 Then
            Document.RemoveDrawablesFromCollection(removed)
            Document.NotifyChanged()
        End If

        For Each grp In parentGroups.Where(Function(g) Not Document.IsDrawingGroup(g) AndAlso Not g.GroupChildren.Any())
            Dim removeGroupAction As New RemoveGroupAction(Document, grp, SelectionManager)
            If removeGroupAction.Execute() Then actions.Add(removeGroupAction)
        Next

        _suspendTransformMessageHandling = False

        If actions.Count > 0 Then
            _undoRedoService.Push(New CompositeAction(actions))
        End If
    End Sub

    <RelayCommand>
    Private Sub RemoveGroup(group As DrawableGroup)
        If group Is Nothing Then Return

        If Document.IsDrawingGroup(group) Then
            Dim children = group.GroupChildren.ToList()
            If children.Count = 0 Then
                _snackbarService.GenerateError("Error".LT(), "Drawing Group is already empty".LT(), 3)
                Return
            End If
            PerformRemoveDrawables(children)
        Else
            Dim action As New RemoveGroupAction(Document, group, SelectionManager)
            If action.Execute() Then
                _undoRedoService.Push(action)
            End If
        End If
    End Sub


    Private Function ValidateShapesForBoolean(shapes As List(Of IDrawable)) As (isValid As Boolean, errorMessage As String)
        For Each drawable In shapes
            Dim element = drawable.DrawableElement
            Dim geometric = TryCast(element, GeometricShape)

            If geometric IsNot Nothing Then
                If geometric.Kind = "Line" OrElse (geometric.Kind = "Path" AndAlso
                    PathGeometry.CreateFromGeometry(geometric.Data).Figures.Any(Function(figure) Not figure.IsClosed)) Then
                    Return (False, "Cannot perform boolean operations on open paths. All shapes must be closed.".LT())
                End If
            End If

        Next
        Return (True, Nothing)
    End Function

    ' -----------------
    ' GCode Generation
    ' -----------------

    Private _generatingGCode As Boolean = False

    ''' <summary>Guards against a second upload while one is still running.</summary>
    Private _uploading As Boolean = False

    ''' <summary>Timestamp of the last successful generation, used to ignore uploads that
    ''' are really the tail of a rapid burst of "generate" clicks.</summary>
    Private _lastGeneratedAt As DateTime = DateTime.MinValue
    ''' <summary>Switch to the preview page a couple of seconds after generating. The delay
    ''' is the whole point: generating used to jump straight to the preview page, where the
    ''' Upload button occupies the exact pixel the Generate button did, so an impatient
    ''' second click silently uploaded - and overwrote - a file on the printer.</summary>
    Private Sub NavigateToPreviewSoon()
        Dim timer As New System.Windows.Threading.DispatcherTimer With {
            .Interval = TimeSpan.FromSeconds(2)
        }
        AddHandler timer.Tick,
            Sub(sender As Object, args As EventArgs)
                timer.Stop()
                _navigationService.Navigate(GetType(PreviewPage))
            End Sub
        timer.Start()
    End Sub


    ''' <summary>Every drawable, including the children of a group, so the font-coverage
    ''' check also reaches text nested inside a 组合.</summary>
    Private Iterator Function EnumerateAllDrawables(items As IEnumerable(Of IDrawable)) As IEnumerable(Of IDrawable)
        For Each drawable In items
            Dim grouped = TryCast(drawable, NestedDrawableGroup)
            If grouped Is Nothing Then
                Yield drawable
            Else
                For Each child In EnumerateAllDrawables(grouped.DisplayChildren)
                    Yield child
                Next
            End If
        Next
    End Function

    <RelayCommand>
    Private Async Function GenerateGCode() As Task
        Try
            ' Cutting is parked while the blade holder is still being designed. The mode
            ' shares Z and speed with drawing, so generating in Cut would drive a blade at
            ' pen depth - refuse here as well as hiding the button, in case a saved project
            ' or a stale config still selects Cut.
            If Configuration.SelectedToolMode = ProcessorConfiguration.ToolMode.Cut AndAlso
               (UIConfiguration Is Nothing OrElse Not UIConfiguration.EnableCutting) Then
                _snackbarService.GenerateError("切割功能暂未开放",
                                               "刀头尚未完成设计，当前只能生成绘制刀路。请切换到「绘制」模式后重新生成。", 10)
                Return
            End If

            ' A zero speed makes every duration infinite, and a very negative pass delta
            ' would drive Z straight through the bed. Refuse both with a readable message
            ' instead of producing broken G-code (or crashing on the time estimate).
            If Configuration.WorkSpeed <= 0 Then
                _snackbarService.GenerateError("无法生成 G-code", "工作速度必须大于 0，请到工具配置面板修改后再生成")
                Return
            End If

            If Configuration.TravelSpeed <= 0 Then
                _snackbarService.GenerateError("无法生成 G-code", "移动速度必须大于 0，请到工具配置面板修改后再生成")
                Return
            End If

            If Configuration.ZSpeed <= 0 Then
                _snackbarService.GenerateError("无法生成 G-code", "Z 轴速度必须大于 0，请到工具配置面板修改后再生成")
                Return
            End If

            ' A1 的 Z=0 就是床面，负的落笔高度意味着刀尖/笔尖要穿过床板。
            ' 默认值已经是 5 mm，这里挡的是用户手改或旧项目带过来的负数。
            If Configuration.WorkZ < 0 Then
                _snackbarService.GenerateError("无法生成 G-code", $"工作 Z 高度是 {Configuration.WorkZ} mm，笔尖会落到床面以下。请到工具配置面板改成安全的正值")
                Return
            End If

            If Configuration.PassHeightDelta < -10 Then
                _snackbarService.GenerateError("无法生成 G-code", $"每次走刀 Z 偏移为 {Configuration.PassHeightDelta} mm，可能切穿工作台。请改为 -10 mm 以上的值")
                Return
            End If

            ' Cut and Draw share their speeds and Z values, and the mode buttons sit next to
            ' each other. Generating in Cut mode drives the blade down at the pen's Z height,
            ' so ask once before producing that toolpath.
            If Configuration.SelectedToolMode = ProcessorConfiguration.ToolMode.Cut Then
                Dim cutBox As New Wpf.Ui.Controls.MessageBox With {
                    .Title = "正在生成切割刀路",
                    .Content = "当前是切割模式，工作 Z 高度 " & Configuration.WorkZ & " mm 会让刀具下压到材料里。" &
                               "请确认已装上切割刀、取下笔，且 Z 高度适合当前材料。继续生成吗？",
                    .IsPrimaryButtonEnabled = True,
                    .PrimaryButtonText = "取消",
                    .IsSecondaryButtonEnabled = True,
                    .SecondaryButtonText = "继续生成",
                    .CloseButtonText = "取消",
                    .Owner = Application.Current.MainWindow,
                    .WindowStartupLocation = WindowStartupLocation.CenterOwner
                }
                If Await cutBox.ShowDialogAsync() <> Wpf.Ui.Controls.MessageBoxResult.Secondary Then Return
            End If

            ' A character with no stroke data makes its whole text box fall back to outline
            ' glyphs. Name the offending characters, otherwise the user sees their line art
            ' turn into double lines and assumes the font is broken.
            Dim outlineOnly As New List(Of String)
            Dim seenCodes As New HashSet(Of Integer)
            For Each drawable In EnumerateAllDrawables(Document.Drawables)
                Dim tb = TryCast(drawable.DrawableElement, System.Windows.Controls.TextBox)
                If tb Is Nothing OrElse String.IsNullOrEmpty(tb.Text) Then Continue For
                If Not HersheyTextRenderer.IsSingleLineFont(tb.FontFamily) Then Continue For

                For Each ch In HersheyTextRenderer.FindMissingGlyphs(tb.Text)
                    If seenCodes.Add(AscW(ch)) Then outlineOnly.Add(ch.ToString())
                Next
            Next

            If outlineOnly.Count > 0 Then
                Dim shown = String.Join(" ", outlineOnly.Take(6))
                Dim suffix = If(outlineOnly.Count > 6, $" 等 {outlineOnly.Count} 个", "")
                _snackbarService.GenerateCaution(
                    "这些字会画成双线轮廓",
                    $"内置单线字体没有「{shown}」{suffix}的骨架数据。含这些字的文本框会整段回退成空心轮廓，字不会丢，但会变成双线。")
            End If

            _generatingGCode = True
            GenerateGCodeCommand.NotifyCanExecuteChanged()

            Configuration.WorkAreaHeight = Printer.BedHeight
            Configuration.WorkAreaWidth = Printer.BedWidth
            Configuration.ToolOffsetX = Printer.ToolOffsetX
            Configuration.ToolOffsetY = Printer.ToolOffsetY
            Configuration.ToolDownGCode = Printer.ToolDownGCode
            Configuration.ToolUpGCode = Printer.ToolUpGCode

            Configuration.SoftwareVersion = SettingsHandler.Version

            Dim generator As IGenerator = New PolyCutGenerator(Configuration, Printer, Document.Drawables.
                                                               Where(Function(d) Not d.IsHidden).
                                                               ToList(), PolyCanvas.ActiveInstance)

            Dim retcode = Await generator.GenerateGcodeAsync

            If retcode.StatusCode = 1 Then
                _snackbarService.GenerateError("Error".LT(), retcode.Message, 5)
                Return
            End If

            GeneratedGCode = generator.GetGCode
            Dim compiledGCodeString = BuildStringFromGCodes(GeneratedGCode)

            GCode = compiledGCodeString
            GCodeGeometry = New GCodeGeometry(GeneratedGCode)
            _lastGeneratedAt = DateTime.Now

            ' Stay on the drawing page for a moment before switching to the preview page.
            ' Generating used to navigate immediately, which put the Upload button exactly
            ' where the Generate button had been - a second click uploaded the file. Holding
            ' the current page for two seconds removes the trap instead of trying to detect
            ' it after the fact.
            _snackbarService.GenerateSuccess("已生成 G-code", "稍候会自动切换到预览页", 3)
            NavigateToPreviewSoon()
        Finally

            _generatingGCode = False
            GenerateGCodeCommand.NotifyCanExecuteChanged()
        End Try

    End Function

    Private Function CanGenerateGCode() As Boolean
        Return Not _generatingGCode
    End Function

    ''' <summary>
    ''' One-click flow for the A1: generate the G-code and push it straight to the printer.
    ''' </summary>
    <RelayCommand>
    Private Async Function GenerateAndUpload() As Task
        Await GenerateGCode()

        If GeneratedGCode Is Nothing OrElse GeneratedGCode.Count = 0 Then Return
        If String.IsNullOrWhiteSpace(GCode) Then Return

        Dim service = Application.GetService(Of BambuA1Service)()
        If service Is Nothing Then
            _snackbarService.GenerateError("发送到 A1 失败".LT(), "Bambu Lab A1 服务不可用".LT())
            Return
        End If

        If Printer Is Nothing Then
            _snackbarService.GenerateError("发送到 A1 失败".LT(), "未配置打印机".LT())
            Return
        End If

        If Not service.IsConnected Then
            Await service.ConnectAsync(Printer.HostAddress, Printer.AccessCode, Printer.SerialNumber)
        End If

        Dim fileName As String = If(String.IsNullOrWhiteSpace(PolyCutDocumentName), "PlotWorks", PolyCutDocumentName)
        Dim success As Boolean = Await service.UploadGCodeAsync(GCode, fileName, False)

        If success Then
            _snackbarService.GenerateSuccess("已发送到 A1".LT(), "已上传 {0} 到 {1}，请在打印机屏幕上手动启动".LTF(fileName, Printer.HostAddress), 8)
        Else
            Dim detail As String = If(String.IsNullOrWhiteSpace(service.LastError),
                                      "请检查主机地址和访问码后重试",
                                      service.LastError)
            _snackbarService.GenerateError("发送到 A1 失败".LT(), detail)
        End If
    End Function

    ''' <summary>Raises the pen to the configured travel height on the printer.</summary>
    <RelayCommand>
    Private Async Function PenUp() As Task
        Await MovePenToAsync(Configuration.TravelZ, "抬笔")
    End Function

    ''' <summary>Lowers the pen to the configured work height on the printer.</summary>
    <RelayCommand>
    Private Async Function PenDown() As Task
        Await MovePenToAsync(Configuration.WorkZ, "落笔")
    End Function

    Private Async Function MovePenToAsync(z As Double, label As String) As Task
        Dim service = Application.GetService(Of BambuA1Service)()
        If service Is Nothing OrElse Not service.IsConnected Then
            _snackbarService.GenerateError(label & " failed", "请先连接打印机")
            Return
        End If

        ' 抬笔/落笔都会走到这里。A1 的床面就是 Z=0，负值一定是参数错了，
        ' 发出去不是撞床就是被固件拒绝后坐标继续累加（jog 是开环的）。
        If z < 0 Then
            _snackbarService.GenerateError(label & " 已取消",
                                           $"目标高度是 {z:F2} mm，床面以下是无效位置。请到工具配置面板把 Z 高度改成正值")
            Return
        End If

        Dim ci As System.Globalization.CultureInfo = System.Globalization.CultureInfo.InvariantCulture
        Await service.SendGCodeAsync(String.Format(ci, "G90{0}G1 Z{1:F3} F600", vbLf, z))
        _snackbarService.GenerateSuccess(label, String.Format(ci, "Z = {0:F2} mm", z))
    End Function

    ''' <summary>Adjusts the work height in small steps from the canvas toolbar.</summary>
    <RelayCommand>
    Private Sub NudgeWorkZ(deltaText As String)
        Dim delta As Double
        If Not Double.TryParse(deltaText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, delta) Then Return
        Configuration.WorkZ = Math.Round(Configuration.WorkZ + delta, 2)
    End Sub

    ''' <summary>Step used by the Monitor jog dial (0.1 / 1 / 10 mm).</summary>
    <ObservableProperty> Private _JogStep As Double = 1

    ''' <summary>File name used by the Preview page upload button (blank = project name).</summary>
    <ObservableProperty> Private _UploadFileName As String = ""

    ''' <summary>Tracked nozzle position in machine space (bottom-left origin, Y up).</summary>
    <ObservableProperty> Private _ToolX As Double = 0
    <ObservableProperty> Private _ToolY As Double = 0

    ''' <summary>Fits the drawing canvas back to the window (View menu / Ctrl+0).</summary>
    <RelayCommand>
    Private Sub ResetView()
        RaiseEvent ResetViewRequested()
    End Sub

    ''' <summary>Physical reference points captured while jogging, in machine/G-code space.</summary>
    Public ReadOnly Property ReferencePoints As New ObservableCollection(Of ReferencePoint)()

    ''' <summary>Explicit coordinates used to correct the tracked tool position.</summary>
    <ObservableProperty> Private _CalibrationX As Double = 0
    <ObservableProperty> Private _CalibrationY As Double = 0

    ''' <summary>Overwrites the tracked tool position with the coordinates the operator measured.</summary>
    <RelayCommand>
    Private Sub ApplyPositionCalibration()
        If Printer Is Nothing Then Return

        ToolX = Math.Max(0, Math.Min(Printer.BedWidth, CalibrationX))
        ToolY = Math.Max(0, Math.Min(Printer.BedHeight, CalibrationY))

        Dim ci As System.Globalization.CultureInfo = System.Globalization.CultureInfo.InvariantCulture
        _snackbarService.GenerateSuccess("位置已校准",
                                         String.Format(ci, "Tool X {0:F1}  Y {1:F1}", ToolX, ToolY))
    End Sub

    ''' <summary>Jogs the toolhead by one step in the requested direction.</summary>
    <RelayCommand>
    Private Async Function Jog(direction As String) As Task
        Dim service = Application.GetService(Of BambuA1Service)()
        If service Is Nothing OrElse Not service.IsConnected Then
            _snackbarService.GenerateError("移动失败", "请先连接打印机")
            Return
        End If

        Dim stepValue As Double = Math.Max(JogStep, 0.01)
        Dim dx As Double = 0
        Dim dy As Double = 0

        Select Case direction
            Case "Up"
                dy = stepValue
            Case "Down"
                dy = -stepValue
            Case "Left"
                dx = -stepValue
            Case "Right"
                dx = stepValue
            Case Else
                Return
        End Select

        Dim feed As Double = Math.Max(Configuration.TravelSpeed, 1) * 60
        Dim ci As System.Globalization.CultureInfo = System.Globalization.CultureInfo.InvariantCulture
        Dim snippet As String = String.Format(ci, "G91{0}G1 X{1:F3} Y{2:F3} F{3:F0}{0}G90", vbLf, dx, dy, feed)

        Await service.SendGCodeAsync(snippet)

        ToolX = Math.Max(0, Math.Min(Printer.BedWidth, ToolX + dx))
        ToolY = Math.Max(0, Math.Min(Printer.BedHeight, ToolY + dy))
    End Function

    ''' <summary>Homes the printer from the preview action tab.</summary>
    <RelayCommand>
    Private Async Function HomePrinter() As Task
        Dim service = Application.GetService(Of BambuA1Service)()
        If service Is Nothing OrElse Not service.IsConnected Then
            _snackbarService.GenerateError("回零失败", "请先连接打印机")
            Return
        End If
        Await service.HomeAsync()

        'The A1 parks the nozzle at the bed centre after G28.
        ToolX = Printer.BedWidth / 2
        ToolY = Printer.BedHeight / 2
    End Function

    ''' <summary>Records the current tool position as a design reference point.</summary>
    <RelayCommand>
    Private Sub CaptureReferencePoint()
        Dim modeOffsetX As Double = If(Configuration.SelectedToolMode = ProcessorConfiguration.ToolMode.Cut, Configuration.CutOffsetX, Configuration.DrawOffsetX)
        Dim modeOffsetY As Double = If(Configuration.SelectedToolMode = ProcessorConfiguration.ToolMode.Cut, Configuration.CutOffsetY, Configuration.DrawOffsetY)

        Dim offsetX As Double = Configuration.ToolOffsetX + modeOffsetX
        Dim offsetY As Double = Configuration.ToolOffsetY + modeOffsetY

        Dim refX As Double = ToolX - offsetX
        Dim refY As Double = ToolY - offsetY

        'Stored in canvas space (Y down) so the designer can place it without conversion.
        Dim canvasY As Double = Printer.BedHeight - refY

        ReferencePoints.Add(New ReferencePoint(refX, canvasY))
        RenumberReferencePoints()

        Dim ci As System.Globalization.CultureInfo = System.Globalization.CultureInfo.InvariantCulture
        _snackbarService.GenerateSuccess("参考点已记录",
                                         String.Format(ci, "X {0:F1}  Y {1:F1}", refX, refY))
    End Sub

    Private Sub RenumberReferencePoints()
        For i = 0 To ReferencePoints.Count - 1
            ReferencePoints(i).Number = i + 1
        Next
    End Sub

    Public Sub RemoveReferencePoint(point As ReferencePoint)
        If point Is Nothing Then Return
        If Not ReferencePoints.Remove(point) Then Return
        RenumberReferencePoints()
    End Sub

    ''' <summary>Moves the toolhead to one of the four corners of the generated toolpath.</summary>
    <RelayCommand>
    Private Async Function MoveToCorner(corner As String) As Task
        Dim service = Application.GetService(Of BambuA1Service)()
        If service Is Nothing OrElse Not service.IsConnected Then
            _snackbarService.GenerateError("移动失败", "请先连接打印机")
            Return
        End If
        If String.IsNullOrEmpty(corner) Then Return

        Dim contentBounds As Rect? = GetGeneratedContentBounds()
        Dim bounds As Rect

        If contentBounds.HasValue Then
            bounds = contentBounds.Value
        Else
            Dim working As Rect = Printer.WorkingRect
            If working.Width <= 0 OrElse working.Height <= 0 Then
                _snackbarService.GenerateError("移动失败", "No generated toolpath and the working area is empty")
                Return
            End If
            bounds = working
        End If

        Dim x As Double
        Dim y As Double

        Select Case corner
            Case "FrontLeft"
                x = bounds.Left
                y = bounds.Top
            Case "FrontRight"
                x = bounds.Right
                y = bounds.Top
            Case "RearLeft"
                x = bounds.Left
                y = bounds.Bottom
            Case "RearRight"
                x = bounds.Right
                y = bounds.Bottom
            Case Else
                Return
        End Select

        Dim travelZ As Double = Math.Max(Configuration.TravelZ, 0)
        Dim feedRate As Double = Math.Max(Configuration.TravelSpeed, 1) * 60

        Await service.MoveToAsync(x, y, travelZ, feedRate)
    End Function

    ''' <summary>Uploads the current G-code using the file name shown in Printer Control.</summary>
    <RelayCommand>
    Private Async Function UploadGCode() As Task
        If String.IsNullOrWhiteSpace(GCode) Then
            _snackbarService.GenerateError("上传失败", "请先生成 G-code")
            Return
        End If

        ' The Generate button and the Upload button sit in the same corner of the window,
        ' and generating navigates to the preview page. Rapid clicks on Generate would
        ' therefore fall through onto Upload and silently overwrite a file on the printer.
        If _uploading Then
            _snackbarService.GenerateError("上传进行中", "上一次上传尚未完成，请稍候")
            Return
        End If

        If (DateTime.Now - _lastGeneratedAt).TotalSeconds < 2.5 Then
            _snackbarService.GenerateError("上传已忽略", "刚刚生成了 G-code，请确认预览后再点击上传")
            Return
        End If

        Dim defaultName As String = If(String.IsNullOrWhiteSpace(PolyCutDocumentName), "PlotWorks", PolyCutDocumentName)
        Dim fileName As String = If(String.IsNullOrWhiteSpace(UploadFileName), defaultName, UploadFileName.Trim())
        If Not fileName.EndsWith(".gcode", StringComparison.OrdinalIgnoreCase) Then fileName &= ".gcode"

        Dim service = Application.GetService(Of BambuA1Service)()
        If service Is Nothing Then
            _snackbarService.GenerateError("上传失败", "Bambu Lab A1 服务不可用")
            Return
        End If

        _uploading = True
        Try
            If Not service.IsConnected Then
                Await service.ConnectAsync(Printer.HostAddress, Printer.AccessCode, Printer.SerialNumber)
            End If

            Dim ok As Boolean = Await service.UploadGCodeAsync(GCode, fileName, False)

            If ok Then
                _snackbarService.GenerateSuccess("已发送到 A1", "已上传 " & fileName & " 到 " & Printer.HostAddress & "，请在打印机屏幕上手动启动", 8)
            Else
                Dim detail As String = If(String.IsNullOrWhiteSpace(service.LastError),
                                          "请检查主机地址和访问码后重试",
                                          service.LastError)
                _snackbarService.GenerateError("上传失败", detail)
            End If
        Finally
            _uploading = False
        End Try
    End Function

    Private Function BuildStringFromGCodes(GeneratedGCode As List(Of GCode)) As String
        Dim stringBuilder As New Text.StringBuilder()
        For Each gc In GeneratedGCode
            stringBuilder.AppendLine(gc.ToString())
            If gc?.Comment?.Equals("Custom Start GCode") Then
                stringBuilder.Append(StripExtruderNoops(Printer.StartGCode.Trim))
            ElseIf gc?.Comment?.Equals("Custom End GCode") Then
                stringBuilder.Append(Printer.EndGCode.Trim)
            End If
        Next
        Return stringBuilder.ToString()
    End Function

    'A1 does not use the extruder; drop no-op extruder moves left over from old printer profiles.
    Private Shared Function StripExtruderNoops(gcode As String) As String
        If String.IsNullOrWhiteSpace(gcode) Then Return gcode
        Dim kept As New List(Of String)()
        For Each line In gcode.Split(New String() {vbCrLf, vbLf}, StringSplitOptions.None)
            If String.IsNullOrWhiteSpace(line) Then Continue For
            Dim trimmed = line.Trim()
            If trimmed.Equals("G0 E0", StringComparison.OrdinalIgnoreCase) OrElse
               trimmed.Equals("G1 E0", StringComparison.OrdinalIgnoreCase) Then Continue For
            kept.Add(line)
        Next
        Return String.Join(Environment.NewLine, kept)
    End Function

    ''' <summary>
    ''' Bounding box of the generated toolpath in machine coordinates (after all tool offsets).
    ''' Returns Nothing when no G-code has been generated yet.
    ''' </summary>
    Public Function GetGeneratedContentBounds() As Rect?
        If GeneratedGCode Is Nothing OrElse GeneratedGCode.Count = 0 Then Return Nothing

        Dim minX As Double = Double.MaxValue
        Dim minY As Double = Double.MaxValue
        Dim maxX As Double = Double.MinValue
        Dim maxY As Double = Double.MinValue
        Dim found As Boolean = False

        For Each gc In GeneratedGCode
            If gc Is Nothing OrElse Not gc.X.HasValue OrElse Not gc.Y.HasValue Then Continue For

            Dim x As Double = gc.X.Value
            Dim y As Double = gc.Y.Value
            If Double.IsNaN(x) OrElse Double.IsNaN(y) OrElse Double.IsInfinity(x) OrElse Double.IsInfinity(y) Then Continue For

            minX = Math.Min(minX, x)
            minY = Math.Min(minY, y)
            maxX = Math.Max(maxX, x)
            maxY = Math.Max(maxY, y)
            found = True
        Next

        If Not found Then Return Nothing
        Return New Rect(minX, minY, maxX - minX, maxY - minY)
    End Function

    <RelayCommand>
    Sub ExportSVG()
        Dim outDoc As New Svg.SvgDocument With {
            .Width = New SvgUnit(Svg.SvgUnitType.Millimeter, Printer.BedWidth),
            .Height = New SvgUnit(Svg.SvgUnitType.Millimeter, Printer.BedHeight),
            .ViewBox = New Svg.SvgViewBox(0, 0, Printer.BedWidth, Printer.BedHeight)
        }

        For Each drawableL In Document.Drawables
            If TypeOf (drawableL) Is DrawableGroup Then Continue For
            Debug.WriteLine(drawableL.Name)
            Dim finalElement = drawableL?.GetTransformedSVGElement
            outDoc.Children.Add(finalElement)
        Next

        Dim svgText = SVGImportService.SVGDocumentToString(outDoc)
        Dim saveDialog As New Microsoft.Win32.SaveFileDialog With {
            .Filter = "SVG File (*.svg)|*.svg",
            .DefaultExt = ".svg",
            .FileName = "PlotWorks_Export.svg"
        }
        If saveDialog.ShowDialog() = True Then
            IO.File.WriteAllText(saveDialog.FileName, svgText)
            _snackbarService.GenerateSuccess("SVG Exported".LT(), Path.GetFileName(saveDialog.FileName))
        End If
    End Sub


    ' -----------------
    ' Boolean / Geometry operations
    ' -----------------
    Private Sub BooleanOperation(combineMode As GeometryCombineMode)
        Dim selectedItems = SelectionManager.SelectedItems.ToList()
        If selectedItems.Count < 2 Then
            _snackbarService.GenerateError("Error".LT(), "Select at least 2 shapes to combine".LT(), 3)
            Return
        End If

        Dim validation = ValidateShapesForBoolean(selectedItems)
        If Not validation.isValid Then
            _snackbarService.GenerateError("Error".LT(), validation.errorMessage, 4)
            Return
        End If

        _suspendTransformMessageHandling = True
        Dim action As New BooleanOperationAction(Document, selectedItems, combineMode, SelectionManager)
        If action.Execute() Then
            _undoRedoService.Push(action)
        Else
            _snackbarService.GenerateError("Error".LT(), GetBooleanErrorMessage(combineMode), 4)
        End If
        _suspendTransformMessageHandling = False
    End Sub

    Private Function GetBooleanErrorMessage(mode As GeometryCombineMode) As String
        Select Case mode
            Case GeometryCombineMode.Union
                Return "Union operation resulted in an empty geometry. This should not happen.".LT()
            Case GeometryCombineMode.Intersect
                Return "No intersection found. The selected shapes do not overlap.".LT()
            Case GeometryCombineMode.Exclude
                Return "Subtraction resulted in an empty geometry. The shapes may not overlap or the result is fully subtracted.".LT()
            Case GeometryCombineMode.Xor
                Return "XOR operation resulted in an empty geometry. The shapes may be identical.".LT()
            Case Else
                Return "Boolean operation failed.".LT()
        End Select
    End Function


    ' -----------------
    ' Grouping / Ungrouping
    ' -----------------

    <RelayCommand>
    Private Sub GroupSelected()
        ' Grouping has been reported to take the whole process down, which also loses the
        ' unsaved document. Keep the failure local to the command instead of the app.
        Try
            Dim selectedItems = SelectionManager.SelectedItems.ToList()
            If selectedItems.Count < 2 Then
                _snackbarService.GenerateError("Error".LT(), "Select at least 2 shapes to group".LT(), 3)
                Return
            End If

            ExecuteGroupingAction(New GroupAction(Document, selectedItems, SelectionManager))
        Catch ex As Exception
            _snackbarService.GenerateError("无法组合所选元素", ex.Message, 8)
        End Try
    End Sub

    <RelayCommand>
    Private Sub UngroupSelected()
        Try
            Dim selectedItems = SelectionManager.SelectedItems.ToList()
            If Not selectedItems.OfType(Of NestedDrawableGroup)().Any(Function(g) Document.Drawables.Contains(g)) Then
                _snackbarService.GenerateError("Error".LT(), "No groups selected to ungroup".LT(), 3)
                Return
            End If

            ExecuteGroupingAction(New UngroupAction(Document, selectedItems, SelectionManager))
        Catch ex As Exception
            _snackbarService.GenerateError("无法取消组合", ex.Message, 8)
        End Try
    End Sub

    ' -----------------
    ' Aligning a multi-selection
    ' -----------------

    ''' <summary>Move every selected item by the same delta, as one undoable step.</summary>
    Private Sub MoveSelectionBy(dx As Double, dy As Double)
        If Math.Abs(dx) < 0.0001 AndAlso Math.Abs(dy) < 0.0001 Then Return

        Dim items = SelectionManager?.SelectedItems
        If items Is Nothing OrElse items.Count = 0 Then Return

        Dim changes As New List(Of (IDrawable, TransformState, TransformState))
        For Each d In items
            Dim wrapper = TryCast(d.DrawableElement?.Parent, ContentControl)
            If wrapper Is Nothing Then Continue For

            Dim before = TransformAction.CaptureState(wrapper)
            TransformAction.ApplyMove(wrapper, dx, dy)
            Dim after = TransformAction.CaptureState(wrapper)
            changes.Add((d, before, after))
        Next

        If changes.Count > 0 Then _undoRedoService.Push(New TransformAction(changes))
    End Sub

    Private Function SelectedWrappers() As List(Of ContentControl)
        Dim result As New List(Of ContentControl)
        Dim items = SelectionManager?.SelectedItems
        If items Is Nothing Then Return result

        For Each d In items
            Dim wrapper = TryCast(d.DrawableElement?.Parent, ContentControl)
            If wrapper IsNot Nothing Then result.Add(wrapper)
        Next
        Return result
    End Function

    ''' <summary>Line up the selection's left edges (the leftmost item does not move).</summary>
    <RelayCommand>
    Private Sub AlignSelectionLeft()
        Dim wrappers = SelectedWrappers()
        If wrappers.Count < 2 Then Return

        Dim target = wrappers.Min(Function(w) Canvas.GetLeft(w))
        If Double.IsNaN(target) Then Return

        For Each w In wrappers
            Dim dx = target - Canvas.GetLeft(w)
            If Math.Abs(dx) > 0.0001 Then MoveSelectionBySingle(w, dx, 0)
        Next
    End Sub

    ''' <summary>Line up the selection's top edges.</summary>
    <RelayCommand>
    Private Sub AlignSelectionTop()
        Dim wrappers = SelectedWrappers()
        If wrappers.Count < 2 Then Return

        Dim target = wrappers.Min(Function(w) Canvas.GetTop(w))
        If Double.IsNaN(target) Then Return

        For Each w In wrappers
            Dim dy = target - Canvas.GetTop(w)
            If Math.Abs(dy) > 0.0001 Then MoveSelectionBySingle(w, 0, dy)
        Next
    End Sub

    Private Sub MoveSelectionBySingle(wrapper As ContentControl, dx As Double, dy As Double)
        If wrapper Is Nothing Then Return

        Dim owner = SelectionManager.SelectedItems.
            FirstOrDefault(Function(d) Object.ReferenceEquals(TryCast(d.DrawableElement?.Parent, ContentControl), wrapper))
        If owner Is Nothing Then Return

        Dim before = TransformAction.CaptureState(wrapper)
        TransformAction.ApplyMove(wrapper, dx, dy)
        Dim after = TransformAction.CaptureState(wrapper)
        _undoRedoService.Push(New TransformAction(New List(Of (IDrawable, TransformState, TransformState)) From {(owner, before, after)}))
    End Sub

    Private Sub ExecuteGroupingAction(action As IUndoableAction)
        _suspendTransformMessageHandling = True
        Try
            If action.Execute() Then _undoRedoService.Push(action)
        Finally
            _suspendTransformMessageHandling = False
        End Try
    End Sub

    <NotifyPropertyChangedFor(NameOf(CurrentProjectName))>
    <ObservableProperty> Private _currentProjectPath As String = Nothing

    Public ReadOnly Property CurrentProjectName As String
        Get
            Return Path.GetFileNameWithoutExtension(_currentProjectPath)
        End Get
    End Property

    <RelayCommand>
    Private Sub SaveProject()
        If String.IsNullOrEmpty(CurrentProjectPath) Then
            SaveProjectAs()
        Else
            If _projectService.SaveProject(CurrentProjectPath, Document.Drawables, Document.ImportRoots, Configuration, ReferencePoints) Then
                AddRecentItem(CurrentProjectPath)
                _snackbarService.GenerateSuccess("Project Saved".LT(), IO.Path.GetFileName(CurrentProjectPath))
            Else
                _snackbarService.GenerateError("Error".LT(), "Failed to save project".LT(), 3)
            End If
        End If
    End Sub

    <RelayCommand>
    Private Sub SaveProjectAs()
        Dim saveDialog As New Microsoft.Win32.SaveFileDialog With {
            .Filter = "PlotWorks Project (*.polycut)|*.polycut|All Files (*.*)|*.*",
            .DefaultExt = ".polycut"
        }

        If saveDialog.ShowDialog() = True Then
            CurrentProjectPath = saveDialog.FileName
            If _projectService.SaveProject(CurrentProjectPath, Document.Drawables, Document.ImportRoots, Configuration, ReferencePoints) Then
                AddRecentItem(CurrentProjectPath)
                _snackbarService.GenerateSuccess("Project Saved".LT(), IO.Path.GetFileName(CurrentProjectPath))
            Else
                _snackbarService.GenerateError("Error".LT(), "Failed to save project".LT(), 3)
            End If
        End If
    End Sub

    ''' <summary>Save the current project without a command, asking for a path when the
    ''' project has never been saved. Used by the "save before exit" prompt.
    ''' Returns False when the user cancels the file dialog or the write fails.</summary>
    Public Function TrySaveProjectBeforeExit() As Boolean
        Try
            If String.IsNullOrWhiteSpace(CurrentProjectPath) Then
                Dim saveDialog As New Microsoft.Win32.SaveFileDialog With {
                    .Filter = "PlotWorks Project (*.polycut)|*.polycut|All Files (*.*)|*.*",
                    .DefaultExt = ".polycut",
                    .FileName = "PlotWorks项目"
                }
                If saveDialog.ShowDialog() <> True Then Return False
                CurrentProjectPath = saveDialog.FileName
            End If

            Dim ok = _projectService.SaveProject(CurrentProjectPath, Document.Drawables, Document.ImportRoots, Configuration, ReferencePoints)
            If ok Then AddRecentItem(CurrentProjectPath)
            Return ok
        Catch
            Return False
        End Try
    End Function

    <RelayCommand>
    Private Async Function LoadProject() As Task
        Await OpenProject(Nothing)
    End Function

    ''' <summary>Load a project file without the picker. Used by crash recovery.</summary>
    Public Async Function RestoreFromAsync(filePath As String) As Task
        Await OpenProject(filePath)
    End Function

    Private Async Function OpenProject(filePath As String) As Task
        ' Warn if unsaved changes
        If Document.Drawables.Count > 1 Then

            Dim nx As New Wpf.Ui.Controls.MessageBox With {
                .Title = "Load Project".LT(),
                .Content = "Loading a project will clear the current workspace. Continue?".LT(),
                .IsPrimaryButtonEnabled = True,
                .PrimaryButtonText = "保留当前内容".LT(),
                .IsSecondaryButtonEnabled = True,
                .SecondaryButtonText = "清空并打开".LT(),
                .CloseButtonText = "取消".LT(),
                .Owner = Application.Current.MainWindow,
                .WindowStartupLocation = WindowStartupLocation.CenterOwner}


            Dim res = Await nx.ShowDialogAsync()

            If res <> Wpf.Ui.Controls.MessageBoxResult.Secondary Then Return
        End If

        If filePath Is Nothing Then
            Dim openDialog As New Microsoft.Win32.OpenFileDialog With {
                .Filter = "PlotWorks Project (*.polycut)|*.polycut|All Files (*.*)|*.*",
                .CheckFileExists = True
            }
            If openDialog.ShowDialog() <> True Then Return
            filePath = openDialog.FileName
        End If

        Dim action As New LoadProjectAction(Document, _projectService, filePath, SelectionManager)
        If action.Execute() Then
            If action.LoadedConfiguration IsNot Nothing Then Configuration = action.LoadedConfiguration

            ' Files older than the point-based font size store the size number as
            ' millimetres. Loading them with the new rule makes text 0.35x, so say so.
            If String.IsNullOrEmpty(action.LoadedFontSizeUnit) Then
                _snackbarService.GenerateCaution(
                    "字号单位已变更",
                    "这个项目是旧版本保存的：当时的字号数字直接等于毫米。现在字号按 pt 计算，同样的数字会小到 35%。" &
                    "要恢复原来的大小，把字号乘以 2.83（例如 32 → 90）。",
                    15)
            End If

            ReferencePoints.Clear()
            If action.LoadedReferencePoints IsNot Nothing Then
                For Each rp In action.LoadedReferencePoints
                    ReferencePoints.Add(New ReferencePoint(rp.X, rp.Y))
                Next
            End If
            RenumberReferencePoints()

            _undoRedoService.Clear()
            CurrentProjectPath = filePath

            ' Drop any file name typed for the previous job, then re-announce the derived
            ' name so the upload field shows this project's name instead of the last one.
            UploadFileName = ""
            OnPropertyChanged(NameOf(PolyCutDocumentName))

            AddRecentItem(filePath)
            _snackbarService.GenerateSuccess("Project Loaded".LT(), IO.Path.GetFileName(CurrentProjectPath))
            Document.NotifyChanged()
        Else
            _snackbarService.GenerateError("Error".LT(), "Failed to load project".LT(), 3)
        End If
    End Function

    <RelayCommand>
    Private Async Sub NewProject()
        If Document.Drawables.Count > 1 Then
            Dim nx As New Wpf.Ui.Controls.MessageBox With {
                .Title = "New Project".LT(),
                .Content = "Creating a new project will clear the current workspace. Continue?".LT(),
                .IsPrimaryButtonEnabled = True,
                .PrimaryButtonText = "保留当前内容".LT(),
                .IsSecondaryButtonEnabled = True,
                .SecondaryButtonText = "清空并新建".LT(),
                .CloseButtonText = "取消".LT(),
                .Owner = Application.Current.MainWindow,
                .WindowStartupLocation = WindowStartupLocation.CenterOwner}

            Dim res = Await nx.ShowDialogAsync()
            If res <> Wpf.Ui.Controls.MessageBoxResult.Secondary Then Return

        End If

        ' Clear everything
        _suspendTransformMessageHandling = True
        WeedingBoxGenerator.ClearSources()

        ' Clear Document.DrawingGroup children
        If Document.DrawingGroup IsNot Nothing Then
            Document.DrawingGroup.GroupChildren.Clear()
        End If

        ' Clear imported groups
        Document.ImportRoots.Clear()

        ReferencePoints.Clear()

        ' Remove all drawables
        Dim drawablesToRemove = Document.Drawables.ToList()
        For Each drawable In drawablesToRemove
            Document.RemoveDrawableFromCollection(drawable)
        Next

        _suspendTransformMessageHandling = False
        CurrentProjectPath = Nothing
        UploadFileName = ""
        OnPropertyChanged(NameOf(PolyCutDocumentName))

        ' Reinitialize drawing group
        Document.DrawingGroup = New DrawableGroup(Document.DrawingGroupName)
        Document.Drawables.Add(Document.DrawingGroup)
        Document.ImportRoots.Add(Document.DrawingGroup)

        ' The undo history still belongs to the project that was just discarded. Keeping it
        ' let Ctrl+Z/Ctrl+Y resurrect deleted elements (and even whole previous projects).
        _undoRedoService.Clear()

        Document.NotifyChanged()
        _snackbarService.GenerateSuccess("New Project".LT(), "Workspace cleared".LT())
    End Sub


End Class
