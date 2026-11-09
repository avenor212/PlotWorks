Imports System.Globalization
Imports System.Text.RegularExpressions
Imports CommunityToolkit.Mvvm.ComponentModel

Imports PolyCut.Core

Partial Public Class Printer : Inherits ObservableObject : Implements ISaveable

    <ImplementsProperty(GetType(ISaveable), NameOf(ISaveable.Version))>
    <ObservableProperty> Private _Version As Single = 0.1

    <ImplementsProperty(GetType(ISaveable), NameOf(ISaveable.Name))>
    <ObservableProperty> Private _Name As String = "Bambu Lab A1"

    <NotifyPropertyChangedFor(NameOf(BedRect))>
    <ObservableProperty> Private _BedWidth As Decimal = 256

    <NotifyPropertyChangedFor(NameOf(BedRect))>
    <ObservableProperty> Private _BedHeight As Decimal = 256

    Private _WorkingOffsetX As Decimal = 0
    Private _WorkingOffsetY As Decimal = 0
    Private _WorkingWidth As Decimal = 256
    Private _WorkingHeight As Decimal = 256

    Public Property WorkingOffsetX As Decimal
        Get
            Return _WorkingOffsetX
        End Get
        Set(value As Decimal)
            SetProperty(_WorkingOffsetX, value, NameOf(WorkingOffsetX))
            OnPropertyChanged(NameOf(WorkingRect))
        End Set
    End Property

    Public Property WorkingOffsetY As Decimal
        Get
            Return _WorkingOffsetY
        End Get
        Set(value As Decimal)
            SetProperty(_WorkingOffsetY, value, NameOf(WorkingOffsetY))
            OnPropertyChanged(NameOf(WorkingRect))
        End Set
    End Property

    Public Property WorkingWidth As Decimal
        Get
            Return _WorkingWidth
        End Get
        Set(value As Decimal)
            SetProperty(_WorkingWidth, value, NameOf(WorkingWidth))
            OnPropertyChanged(NameOf(WorkingRect))
        End Set
    End Property

    Public Property WorkingHeight As Decimal
        Get
            Return _WorkingHeight
        End Get
        Set(value As Decimal)
            SetProperty(_WorkingHeight, value, NameOf(WorkingHeight))
            OnPropertyChanged(NameOf(WorkingRect))
        End Set
    End Property

    Public Sub ClampWorkingArea()
        WorkingOffsetX = Math.Min(Math.Max(WorkingOffsetX, 0), BedWidth)
        WorkingOffsetY = Math.Min(Math.Max(WorkingOffsetY, 0), BedHeight)
        WorkingWidth = Math.Min(Math.Max(WorkingWidth, 0), BedWidth - WorkingOffsetX)
        WorkingHeight = Math.Min(Math.Max(WorkingHeight, 0), BedHeight - WorkingOffsetY)
    End Sub

    Public ReadOnly Property BedRect As Rect
        Get
            Return New Rect(0, 0, BedWidth, BedHeight)
        End Get
    End Property

    Public ReadOnly Property WorkingRect As Rect
        Get
            Return New Rect(WorkingOffsetX, WorkingOffsetY, WorkingWidth, WorkingHeight)
        End Get
    End Property

    <ObservableProperty> Private _StartGCode As String =
        "; ==== 关掉加热，确认单位 ====" & Environment.NewLine &
        "M104 S0" & Environment.NewLine &
        "M140 S0" & Environment.NewLine &
        "G21" & Environment.NewLine &
        "" & Environment.NewLine &
        "; ==== 开始提示音：机器马上要动了 ====" & Environment.NewLine &
        "; Copied verbatim from the A1's own start sound block in 打印机起始G-code.txt," & Environment.NewLine &
        "; minus its trailing M18 (which cuts the motors right before homing)." & Environment.NewLine &
        "M17" & Environment.NewLine &
        "M400 S1" & Environment.NewLine &
        "M1006 S1" & Environment.NewLine &
        "M1006 A0 B10 L100 C37 D10 M60 E37 F10 N60" & Environment.NewLine &
        "M1006 A0 B10 L100 C41 D10 M60 E41 F10 N60" & Environment.NewLine &
        "M1006 A0 B10 L100 C44 D10 M60 E44 F10 N60" & Environment.NewLine &
        "M1006 A0 B10 L100 C0 D10 M60 E0 F10 N60" & Environment.NewLine &
        "M1006 A43 B10 L100 C46 D10 M70 E39 F10 N80" & Environment.NewLine &
        "M1006 A0 B10 L100 C0 D10 M60 E0 F10 N80" & Environment.NewLine &
        "M1006 A0 B10 L100 C43 D10 M60 E39 F10 N80" & Environment.NewLine &
        "M1006 A0 B10 L100 C0 D10 M60 E0 F10 N80" & Environment.NewLine &
        "M1006 A0 B10 L100 C41 D10 M80 E41 F10 N80" & Environment.NewLine &
        "M1006 A0 B10 L100 C44 D10 M80 E44 F10 N80" & Environment.NewLine &
        "M1006 A0 B10 L100 C49 D10 M80 E49 F10 N80" & Environment.NewLine &
        "M1006 A0 B10 L100 C0 D10 M80 E0 F10 N80" & Environment.NewLine &
        "M1006 A44 B10 L100 C48 D10 M60 E39 F10 N80" & Environment.NewLine &
        "M1006 A0 B10 L100 C0 D10 M60 E0 F10 N80" & Environment.NewLine &
        "M1006 A0 B10 L100 C44 D10 M80 E39 F10 N80" & Environment.NewLine &
        "M1006 A0 B10 L100 C0 D10 M60 E0 F10 N80" & Environment.NewLine &
        "M1006 A43 B10 L100 C46 D10 M60 E39 F10 N80" & Environment.NewLine &
        "M1006 W" & Environment.NewLine &
        "" & Environment.NewLine &
        "; ==== 回零 ====" & Environment.NewLine &
        "G28"

    ''' <summary>
    ''' Park the tool and hand the sheet back. Relative Z lift first (so the pen never
    ''' drags across the work), then an absolute move to the corner nearest the operator,
    ''' then the printer's own finish chime and motors off so the sheet can be pulled out.
    '''
    ''' Y convention on this machine (confirmed against the control panel): the bed comes
    ''' TOWARDS the operator as Y grows - pressing "left rear" brings the plate closer. So
    ''' the pick-up corner is large Y, not Y=5. The old Y=5 drove the bed to the far end,
    ''' which is the opposite of "hand the sheet back".
    ''' </summary>
    <ObservableProperty> Private _EndGCode As String =
        "; ==== 结束：抬笔、退到取纸位置 ====" & Environment.NewLine &
        "G91" & Environment.NewLine &
        "G1 Z10 F600" & Environment.NewLine &
        "G90" & Environment.NewLine &
        "; 251 = bed depth 256 - 5; large Y brings the plate towards the operator" & Environment.NewLine &
        "G1 X10 Y251 F3000" & Environment.NewLine &
        "M400" & Environment.NewLine &
        "" & Environment.NewLine &
        "; ==== 结束提示音 ====" & Environment.NewLine &
        "M1006 S1" & Environment.NewLine &
        "M1006 A0 B20 L100 C37 D20 M40 E42 F20 N60" & Environment.NewLine &
        "M1006 A0 B10 L100 C44 D10 M60 E44 F10 N60" & Environment.NewLine &
        "M1006 A0 B10 L100 C46 D10 M80 E46 F10 N80" & Environment.NewLine &
        "M1006 A44 B20 L100 C39 D20 M60 E48 F20 N60" & Environment.NewLine &
        "M1006 A0 B10 L100 C44 D10 M60 E44 F10 N60" & Environment.NewLine &
        "M1006 A0 B10 L100 C0 D10 M60 E0 F10 N60" & Environment.NewLine &
        "M1006 A0 B10 L100 C39 D10 M60 E39 F10 N60" & Environment.NewLine &
        "M1006 A0 B10 L100 C0 D10 M60 E0 F10 N60" & Environment.NewLine &
        "M1006 A0 B10 L100 C44 D10 M60 E44 F10 N60" & Environment.NewLine &
        "M1006 A0 B10 L100 C0 D10 M60 E0 F10 N60" & Environment.NewLine &
        "M1006 A0 B10 L100 C39 D10 M60 E39 F10 N60" & Environment.NewLine &
        "M1006 A0 B10 L100 C0 D10 M60 E0 F10 N60" & Environment.NewLine &
        "M1006 A0 B10 L100 C48 D10 M60 E44 F10 N80" & Environment.NewLine &
        "M1006 A0 B10 L100 C0 D10 M60 E0 F10 N80" & Environment.NewLine &
        "M1006 A44 B20 L100 C49 D20 M80 E41 F20 N80" & Environment.NewLine &
        "M1006 A0 B20 L100 C0 D20 M60 E0 F20 N80" & Environment.NewLine &
        "M1006 A0 B20 L100 C37 D20 M30 E37 F20 N60" & Environment.NewLine &
        "M1006 W" & Environment.NewLine &
        "" & Environment.NewLine &
        "; ==== 关闭 X/Y 电机，可手动取纸（Z 保持锁定） ====" & Environment.NewLine &
        "M18 X Y"

    <ObservableProperty> Private _ToolDownGCode As String = ""
    <ObservableProperty> Private _ToolUpGCode As String = ""
    <ObservableProperty> Private _PreviewStartGCode As String = $"M104 S0{Environment.NewLine}M140 S0{Environment.NewLine}G21{Environment.NewLine}G28"
    <ObservableProperty> Private _PreviewEndGCode As String = $""

    <ObservableProperty> Private _ToolOffsetX As Decimal = 0
    <ObservableProperty> Private _ToolOffsetY As Decimal = 0

    ' Bambu Lab A1 local-network connection settings
    <ObservableProperty> Private _HostAddress As String = ""
    <ObservableProperty> Private _AccessCode As String = ""
    <ObservableProperty> Private _SerialNumber As String = ""

    Public Function Clone() As Printer
        Dim p As New Printer With {
            .Version = Me.Version,
            .Name = Me.Name,
            .BedWidth = Me.BedWidth,
            .BedHeight = Me.BedHeight,
            .WorkingOffsetX = Me.WorkingOffsetX,
            .WorkingOffsetY = Me.WorkingOffsetY,
            .WorkingWidth = Me.WorkingWidth,
            .WorkingHeight = Me.WorkingHeight,
            .StartGCode = Me.StartGCode,
            .EndGCode = Me.EndGCode,
            .ToolDownGCode = Me.ToolDownGCode,
            .ToolUpGCode = Me.ToolUpGCode,
            .PreviewStartGCode = Me.PreviewStartGCode,
            .PreviewEndGCode = Me.PreviewEndGCode,
            .ToolOffsetX = Me.ToolOffsetX,
            .ToolOffsetY = Me.ToolOffsetY,
            .HostAddress = Me.HostAddress,
            .AccessCode = Me.AccessCode,
            .SerialNumber = Me.SerialNumber
        }
        Return p
    End Function

    Public Sub CopyFrom(other As Printer)
        If other Is Nothing Then Return

        Me.Version = other.Version
        Me.Name = other.Name
        Me.BedWidth = other.BedWidth
        Me.BedHeight = other.BedHeight
        Me.WorkingOffsetX = other.WorkingOffsetX
        Me.WorkingOffsetY = other.WorkingOffsetY
        Me.WorkingWidth = other.WorkingWidth
        Me.WorkingHeight = other.WorkingHeight
        Me.StartGCode = other.StartGCode
        Me.EndGCode = other.EndGCode
        Me.ToolDownGCode = other.ToolDownGCode
        Me.ToolUpGCode = other.ToolUpGCode
        Me.PreviewStartGCode = other.PreviewStartGCode
        Me.PreviewEndGCode = other.PreviewEndGCode
        Me.ToolOffsetX = other.ToolOffsetX
        Me.ToolOffsetY = other.ToolOffsetY
        Me.HostAddress = other.HostAddress
        Me.AccessCode = other.AccessCode
        Me.SerialNumber = other.SerialNumber
    End Sub

End Class

