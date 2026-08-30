' ============================================================================
' LIMS Portfolio - VB Script middleware
' ImportInstrumentResults.vbs
' ----------------------------------------------------------------------------
' Imports instrument CSV result files into the LIMS database via ADO.
' This is the classic LIMS integration pattern: lab instruments (or their
' vendor software) drop CSV files on a share; a scheduled VB Script (Task
' Scheduler) picks them up and pushes the results into SQL Server.
'
' Usage :  cscript //nologo ImportInstrumentResults.vbs
' Config:  edit the CONSTANTS section below (or use environment variables).
' ============================================================================

Option Explicit

' ---- Configuration ----------------------------------------------------------
Const CONNECTION_STRING = "Provider=SQLOLEDB;Data Source=localhost;Initial Catalog=LimsDb;Integrated Security=SSPI;"
Const INCOMING_FOLDER   = "C:\Lims\InstrumentData\incoming"
Const ARCHIVE_FOLDER    = "C:\Lims\InstrumentData\archive"
Const ERROR_FOLDER      = "C:\Lims\InstrumentData\error"

' ---- Globals ----------------------------------------------------------------
Dim fso, conn, shell
Set fso   = CreateObject("Scripting.FileSystemObject")
Set shell = CreateObject("WScript.Shell")

' ---- Main -------------------------------------------------------------------
Main

Sub Main()
    EnsureFolder INCOMING_FOLDER
    EnsureFolder ARCHIVE_FOLDER
    EnsureFolder ERROR_FOLDER

    Set conn = CreateObject("ADODB.Connection")
    conn.Open CONNECTION_STRING

    Dim folder, file, okCount, failCount
    Set folder = fso.GetFolder(INCOMING_FOLDER)
    okCount = 0 : failCount = 0

    For Each file In folder.Files
        If LCase(fso.GetExtensionName(file.Name)) = "csv" Then
            If ImportFile(file) Then
                okCount = okCount + 1
                MoveFile file, ARCHIVE_FOLDER
            Else
                failCount = failCount + 1
                MoveFile file, ERROR_FOLDER
            End If
        End If
    Next

    LogAudit "VBS_SCRIPT", "IMPORT_RUN", Null, (failCount = 0), _
             "Files OK=" & okCount & ", Failed=" & failCount

    conn.Close
    Set conn = Nothing
    WScript.Echo "Import finished. OK=" & okCount & ", Failed=" & failCount
End Sub

' ---- Import one CSV file ----------------------------------------------------
' Expected line: SampleCode,TestCode,InstrumentCode,ResultValue[,MeasuredAt]
Function ImportFile(file)
    Dim ts, lineNo, line, cols, allOk
    allOk = True
    lineNo = 0

    On Error Resume Next
    Set ts = file.OpenAsTextStream(1, -2)   ' 1=ForReading, -2=system default
    If Err.Number <> 0 Then
        WScript.Echo "ERROR: cannot open " & file.Name & " - " & Err.Description
        ImportFile = False
        Exit Function
    End If
    On Error GoTo 0

    Do While Not ts.AtEndOfStream
        line = Trim(ts.ReadLine)
        lineNo = lineNo + 1

        If line <> "" And Left(line, 1) <> "#" Then
            cols = Split(line, ",")
            If UBound(cols) < 3 Then
                WScript.Echo "WARN: line " & lineNo & " malformed in " & file.Name
                LogAudit "VBS_SCRIPT", "LINE_REJECTED", file.Name, False, _
                         "Line " & lineNo & ": expected 4 columns"
                allOk = False
            ElseIf Not SubmitResult(Trim(cols(0)), UCase(Trim(cols(1))), _
                                    UCase(Trim(cols(2))), Trim(cols(3)), file.Name) Then
                allOk = False
            End If
        End If
    Loop

    ts.Close
    ImportFile = allOk
End Function

' ---- Push one result through the usp_SubmitResult stored procedure ----------
Function SubmitResult(sampleCode, testCode, instrumentCode, valueText, fileName)
    On Error Resume Next

    Dim cmd
    Set cmd = CreateObject("ADODB.Command")
    cmd.ActiveConnection = conn
    cmd.CommandType = 4                  ' adCmdStoredProc
    cmd.CommandText = "usp_SubmitResult"

    cmd.Parameters.Append cmd.CreateParameter("@SampleCode", 200, 1, 30, sampleCode)      ' adVarChar, adParamInput
    cmd.Parameters.Append cmd.CreateParameter("@TestCode", 200, 1, 20, testCode)
    cmd.Parameters.Append cmd.CreateParameter("@ResultValue", 14, 1, , CDbl(valueText))   ' adDecimal/adNumeric
    cmd.Parameters.Append cmd.CreateParameter("@InstrumentCode", 200, 1, 30, instrumentCode)
    cmd.Parameters.Append cmd.CreateParameter("@Comment", 200, 1, 500, "Imported from " & fileName)
    cmd.Parameters.Append cmd.CreateParameter("@Source", 200, 1, 50, "VBS_SCRIPT")

    Dim rs
    Set rs = cmd.Execute

    If Err.Number <> 0 Then
        WScript.Echo "ERROR: " & sampleCode & "/" & testCode & " - " & Err.Description
        LogAudit "VBS_SCRIPT", "SUBMIT_RESULT", sampleCode, False, Err.Description
        Err.Clear
        SubmitResult = False
    Else
        Dim passed : passed = "?"
        If Not rs.EOF Then passed = IIf(rs.Fields("Passed").Value, "PASS", "OUT-OF-SPEC")
        WScript.Echo "OK: " & sampleCode & "/" & testCode & " = " & valueText & " (" & passed & ")"
        SubmitResult = True
    End If

    On Error GoTo 0
End Function

' ---- Helpers ----------------------------------------------------------------
Sub LogAudit(source, action, entityRef, isSuccess, message)
    On Error Resume Next
    Dim cmd
    Set cmd = CreateObject("ADODB.Command")
    cmd.ActiveConnection = conn
    cmd.CommandType = 4
    cmd.CommandText = "usp_LogAudit"
    cmd.Parameters.Append cmd.CreateParameter("@Source", 200, 1, 50, source)
    cmd.Parameters.Append cmd.CreateParameter("@Action", 200, 1, 100, action)
    cmd.Parameters.Append cmd.CreateParameter("@EntityRef", 200, 1, 50, entityRef)
    cmd.Parameters.Append cmd.CreateParameter("@IsSuccess", 11, 1, , isSuccess)   ' adBoolean
    cmd.Parameters.Append cmd.CreateParameter("@Message", 200, 1, 1000, message)
    cmd.Execute
    Err.Clear
    On Error GoTo 0
End Sub

Sub MoveFile(file, targetFolder)
    Dim target
    target = targetFolder & "\" & Year(Now) & Pad(Month(Now), 2) & Pad(Day(Now), 2) & _
             "_" & Pad(Hour(Now), 2) & Pad(Minute(Now), 2) & "_" & file.Name
    On Error Resume Next
    fso.MoveFile file.Path, target
    Err.Clear
    On Error GoTo 0
End Sub

Sub EnsureFolder(path)
    If Not fso.FolderExists(path) Then fso.CreateFolder path
End Sub

Function Pad(value, length)
    Pad = Right(String(length, "0") & CStr(value), length)
End Function

Function IIf(cond, trueVal, falseVal)
    If cond Then IIf = trueVal Else IIf = falseVal
End Function