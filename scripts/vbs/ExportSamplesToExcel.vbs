' ============================================================================
' LIMS Portfolio - VB Script middleware
' ExportSamplesToExcel.vbs
' ----------------------------------------------------------------------------
' Exports the sample register (vw_SampleOverview) to a formatted Excel
' workbook using Excel COM automation - the classic "management wants the
' data in Excel" LIMS request. Scheduled weekly or run on demand.
'
' Usage :  cscript //nologo ExportSamplesToExcel.vbs [outputPath.xlsx]
' Note  :  requires Microsoft Excel installed on the machine.
' ============================================================================

Option Explicit

Const CONNECTION_STRING = "Provider=SQLOLEDB;Data Source=localhost;Initial Catalog=LimsDb;Integrated Security=SSPI;"

Dim defaultPath
defaultPath = "C:\Lims\Exports\Samples_" & Year(Now) & Pad(Month(Now), 2) & Pad(Day(Now), 2) & ".xlsx"

If WScript.Arguments.Count > 0 Then defaultPath = WScript.Arguments(0)

ExportToExcel defaultPath

' ---- Main export ------------------------------------------------------------
Sub ExportToExcel(outputPath)
    Dim records
    records = FetchSamples()
    If IsEmpty(records) Then
        WScript.Echo "No data returned - aborting."
        Exit Sub
    End If

    Dim excel, workbook, sheet
    On Error Resume Next
    Set excel = CreateObject("Excel.Application")
    If Err.Number <> 0 Then
        WScript.Echo "ERROR: Excel is not installed or cannot be started."
        Exit Sub
    End If
    On Error GoTo 0

    excel.Visible = False
    excel.DisplayAlerts = False
    Set workbook = excel.Workbooks.Add
    Set sheet = workbook.Worksheets(1)
    sheet.Name = "Sample Register"

    ' ---- Header row ----
    Dim headers, i
    headers = Array("Sample Code", "Client", "Matrix", "Status", _
                    "Collected", "Tests", "Completed", "Pending", "Failed", "Progress %")
    For i = 0 To UBound(headers)
        sheet.Cells(1, i + 1).Value = headers(i)
    Next
    With sheet.Range(sheet.Cells(1, 1), sheet.Cells(1, UBound(headers) + 1))
        .Font.Bold = True
        .Interior.Color = RGB(31, 78, 121)      ' LIMS corporate blue
        .Font.Color = RGB(255, 255, 255)
    End With

    ' ---- Data rows ----
    Dim row, r
    row = 2
    For r = 0 To UBound(records, 2)
        sheet.Cells(row, 1).Value  = records(0, r)
        sheet.Cells(row, 2).Value  = records(1, r)
        sheet.Cells(row, 3).Value  = records(2, r)
        sheet.Cells(row, 4).Value  = records(3, r)
        sheet.Cells(row, 5).Value  = records(4, r)
        sheet.Cells(row, 6).Value  = records(5, r)
        sheet.Cells(row, 7).Value  = records(6, r)
        sheet.Cells(row, 8).Value  = records(7, r)
        sheet.Cells(row, 9).Value  = records(8, r)
        sheet.Cells(row, 10).Value = records(9, r)

        ' Highlight out-of-spec results in red
        If records(8, r) > 0 Then
            sheet.Range(sheet.Cells(row, 9), sheet.Cells(row, 9)).Font.Color = RGB(200, 0, 0)
        End If
        row = row + 1
    Next

    ' ---- Formatting ----
    sheet.Columns.AutoFit
    sheet.Range("A1").Select
    sheet.Rows(1).AutoFilter

    ' ---- Save ----
    EnsureFolder Left(outputPath, InStrRev(outputPath, "\") - 1)
    If fsoFileExists(outputPath) Then DeleteFile outputPath
    workbook.SaveAs outputPath, 51        ' 51 = xlOpenXMLWorkbook (.xlsx)
    workbook.Close False
    excel.Quit

    Set sheet = Nothing : Set workbook = Nothing : Set excel = Nothing

    WScript.Echo "Export completed: " & outputPath & " (" & (row - 2) & " samples)"
End Sub

' ---- Fetch data through the view (2D array) ---------------------------------
Function FetchSamples()
    Dim conn, rs
    Set conn = CreateObject("ADODB.Connection")
    conn.Open CONNECTION_STRING

    Set rs = conn.Execute("SELECT SampleCode, ClientName, Matrix, Status, " & _
                          "CONVERT(varchar(16), CollectedAt, 120) AS CollectedAt, " & _
                          "TotalTests, CompletedTests, PendingTests, FailedResults, ProgressPercent " & _
                          "FROM dbo.vw_SampleOverview ORDER BY Priority, CollectedAt DESC")

    If rs.EOF Then
        FetchSamples = Empty
    Else
        Dim data, r
        data = rs.GetRows()               ' fast 2D array fetch
        FetchSamples = data
    End If

    rs.Close
    conn.Close
    Set rs = Nothing : Set conn = Nothing
End Function

' ---- Helpers ----------------------------------------------------------------
Function fsoFileExists(path)
    Dim fso : Set fso = CreateObject("Scripting.FileSystemObject")
    fsoFileExists = fso.FileExists(path)
End Function

Sub DeleteFile(path)
    Dim fso : Set fso = CreateObject("Scripting.FileSystemObject")
    If fso.FileExists(path) Then fso.DeleteFile path, True
End Sub

Sub EnsureFolder(path)
    Dim fso : Set fso = CreateObject("Scripting.FileSystemObject")
    If Not fso.FolderExists(path) Then fso.CreateFolder path
End Sub

Function Pad(value, length)
    Pad = Right(String(length, "0") & CStr(value), length)
End Function