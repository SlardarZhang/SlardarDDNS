Imports System.Security.Cryptography.X509Certificates
Imports System.Security.Cryptography
Imports System.Net
Imports System.IO
Imports System.Runtime.Serialization.Formatters.Binary
Imports System.Net.Sockets
Imports System.Text.RegularExpressions
Imports System.Security.Authentication
Imports System.Net.Security
Imports System.Threading
Imports DotRas
Imports Microsoft.Win32
Imports System.Net.NetworkInformation

Public Class DDNS
    Private isServer As Boolean
    Private cert As X509Certificate2
    'Private MySqlConnection As MySqlConnection
    Private RemoteIP As IPAddress
    Private Port As Integer
    Private DomainList As String()
    Private Listener As TcpListener
    Private Running As Boolean = True
    Private ServerListener As TcpListener
    Private ServerLoopThread As Thread = Nothing
    Private Serverlog As EventLog
    Private LastUpdate As Date
    Private AES256 As MyAES256
    Private ClientThreadPool As ArrayList

    'Public Sub Test()
    '    OnStart(New String() {"Test"})
    'End Sub
    Public Sub Once()
        OnStart(New String() {"Once"})
    End Sub
    Protected Overrides Sub OnStart(ByVal args() As String)
        Dim OnceMode As Boolean = False
        If args.Length = 1 Then
            If args(0) = "Once" Then
                OnceMode = True
            End If
        End If
        If Not OnceMode Then
            Dim PingTester As New Ping
            While (PingTester IsNot Nothing)
                Try
                    If PingTester.Send("8.8.8.8").Status = IPStatus.Success Then
                        PingTester = Nothing
                    End If
                Catch ex As Exception
                End Try
            End While
        End If
        Try

            If Not OnceMode Then
                If Not EventLog.SourceExists("Slardar DDNS") Then
                    EventLog.CreateEventSource("Slardar DDNS", "Slardar DDNS")
                End If
                Serverlog = New EventLog()
                Serverlog.Source = "Slardar DDNS"

                LastUpdate = New DateTime(0)
            End If

            Dim MyKey As String = GetMachineID.GetMachineID
            AES256 = New MyAES256(MyKey, "Slardar DDNS SalT")
            isServer = LoadConfig("isServer")
            LoadCert(MyKey)

            If Not cert.HasPrivateKey Then
                Throw New Exception("Load Private Key failed")
            End If

            Port = Integer.Parse(CType(LoadConfig("Port"), String))

            Prepare(True)
            If OnceMode Then
                SyncDDNS()
                Console.WriteLine("Success!")
            Else
                Serverlog.WriteEntry("Slardar DDNS service started successfully.", EventLogEntryType.Information)
            End If
        Catch ex As Exception
            If OnceMode Then
            Console.WriteLine("Error:" + ex.Message)
        Else
            Serverlog.WriteEntry("Slardar DDNS service start failed." + vbCrLf + ex.Message, EventLogEntryType.Error)
        End If
        Environment.Exit(1)
        End Try
    End Sub

    Protected Overrides Sub OnStop()
        Running = False
        If ServerLoopThread IsNot Nothing Then
            Try
                ServerLoopThread.Interrupt()
                ServerLoopThread.Join(1)
                ServerLoopThread.Abort()
                ServerLoopThread = Nothing
            Catch
            End Try
        End If
        Serverlog.WriteEntry("Slardar DDNS service stopped successfully.", EventLogEntryType.Information)


    End Sub

    Private Sub Prepare(ByVal PublicIP As Boolean)
        If isServer Then
            Dim LocalAddress As IPAddress = Nothing

            For Each IPA As IPAddress In Array.FindAll(Dns.GetHostEntry(String.Empty).AddressList, Function(add) add.AddressFamily = AddressFamily.InterNetwork)
                If (isPublicIP(IPA.ToString()) Or (Not PublicIP)) And IPA.AddressFamily = AddressFamily.InterNetwork Then
                    LocalAddress = IPA
                    Exit For
                End If
            Next
            If LocalAddress Is Nothing Then
                Throw New Exception("No IP Address can be use!")
            Else
                ServerListener = New TcpListener(LocalAddress, Port)
                Running = True
                ServerLoopThread = New Thread(AddressOf HandleClient)
                ServerLoopThread.IsBackground = True
                ServerLoopThread.Start()
            End If
        Else
            Dim ServerAddStr As String = LoadConfig("ServerIP")
            If ServerAddStr Is Nothing Then
                Throw New Exception("Server Address is not configured.")
            End If
            If Not IPAddress.TryParse(ServerAddStr, RemoteIP) Then
                Dim ServerAddArray As IPAddress() = Dns.GetHostAddresses(ServerAddStr)
                For Each ServerAddress As IPAddress In ServerAddArray
                    If ServerAddress.AddressFamily = AddressFamily.InterNetwork And isPublicIP(ServerAddress.ToString) Then
                        RemoteIP = ServerAddress
                        Exit For
                    End If
                Next
                If RemoteIP Is Nothing Then
                    Throw New Exception("Unknow server address")
                End If
            End If
            If LoadConfig("DomainList") IsNot Nothing Then
                Try
                    Dim ms As New MemoryStream(CType(LoadConfig("DomainList"), Byte()))
                    Dim bf As New BinaryFormatter
                    Dim RegDomainList = bf.Deserialize(ms)
                    Dim RegDomainArray As New List(Of String)
                    For Each domain As String In RegDomainList
                        If domain IsNot Nothing And Not RegDomainArray.Contains(domain) Then
                            RegDomainArray.Add(domain)
                        End If
                    Next
                    DomainList = RegDomainArray.ToArray
                    ms.Close()
                Catch ex As Exception
                End Try
            End If

            Dim watcher As RasConnectionWatcher = New RasConnectionWatcher
            AddHandler watcher.Connected, Sub(sender As Object, EventArgs As RasConnectionEventArgs)
                                              Serverlog.WriteEntry("PPPoE reconnected, Syncing DDNS data.", EventLogEntryType.Information)
                                              Dim Wating As Integer = 0
                                              While isConnected() And Wating < 10
                                                  Thread.Sleep(500)
                                              End While
                                              If SyncDDNS() = False Then
                                                  LastUpdate = Now - New TimeSpan(0, 29, 0)
                                              Else
                                                  LastUpdate = Now
                                              End If
                                          End Sub
            watcher.EnableRaisingEvents = True

            ServerLoopThread = New Thread(Sub()
                                              While (Running)
                                                  If (Now - LastUpdate) >= New TimeSpan(0, 30, 0) Then
                                                      If SyncDDNS() = False Then
                                                          LastUpdate = Now - New TimeSpan(0, 29, 0)
                                                      Else
                                                          LastUpdate = Now
                                                      End If
                                                  End If
                                              End While
                                          End Sub)
            ServerLoopThread.IsBackground = True
            ServerLoopThread.Start()

        End If
    End Sub

    Private Sub HandleClient()
        ClientThreadPool = New ArrayList
        ServerListener.Start()
        While (Running)
            Try
                Dim Client As TcpClient = ServerListener.AcceptTcpClient
                Dim ProcessThread As Thread = New Thread(New ThreadStart(Sub()
                                                                             ProcessClient(Client)
                                                                             Try
                                                                                 ClientThreadPool.Remove(ProcessThread)
                                                                             Catch ex As Exception
                                                                             End Try

                                                                         End Sub))

                ClientThreadPool.Add(ProcessThread)
                ProcessThread.Start()

            Catch ex As Exception
                Serverlog.WriteEntry("Handle Client Error:" + vbCrLf + ex.Message, EventLogEntryType.Error)
            End Try
        End While
        For Each thread As Thread In ClientThreadPool
            Try
                thread.Interrupt()
                thread.Join(1)
                thread.Abort()
            Catch ex As Exception
            End Try
        Next
    End Sub

    Private Sub ProcessClient(client As TcpClient)
        Try
            Dim sslStream As New SslStream(client.GetStream, False, New RemoteCertificateValidationCallback(Function(sender As Object, certificate As X509Certificate, chain As X509Chain, sslPolicyErrors As SslPolicyErrors) As Boolean
                                                                                                                If sslPolicyErrors <> SslPolicyErrors.None Then
                                                                                                                    For Each status As X509ChainStatus In chain.ChainStatus
                                                                                                                        If (status.Status = X509ChainStatusFlags.RevocationStatusUnknown) Or (status.Status = X509ChainStatusFlags.OfflineRevocation) Then
                                                                                                                            Continue For
                                                                                                                        Else
                                                                                                                            Serverlog.WriteEntry(status.StatusInformation + vbCrLf + status.Status.ToString, EventLogEntryType.Error)
                                                                                                                            Return False
                                                                                                                        End If
                                                                                                                    Next
                                                                                                                    Return True
                                                                                                                Else
                                                                                                                    Return True
                                                                                                                End If
                                                                                                            End Function))
            sslStream.ReadTimeout = 10000
            sslStream.WriteTimeout = 10000
            sslStream.AuthenticateAsServer(cert, True, SslProtocols.Tls12, True)
            Dim SR As New StreamReader(sslStream)
            Dim SW As New StreamWriter(sslStream)
            SW.AutoFlush = True
            If sslStream.IsAuthenticated Then
                Dim RemoteDomainList As List(Of String) = GetDNSNames(New X509Certificate2(sslStream.RemoteCertificate.GetRawCertData))
                Dim LocalDomainList As List(Of String) = GetDNSNames(cert)

                Dim Domains As String()
                Dim Size As String = SR.ReadLine
                ReDim Domains(Integer.Parse(Size) - 1)
                For I As Integer = 0 To Domains.Length - 1
                    Domains(I) = SR.ReadLine
                Next

                For Each domain As String In Domains

                    If ContainsDomain(RemoteDomainList, domain) Then
                        If ContainsDomain(LocalDomainList, domain) Then
                            Dim DomainArray As String() = GetDomainArray(LocalDomainList, domain)
                            UpdateDNS(DomainArray, CType(client.Client.RemoteEndPoint, IPEndPoint).Address)
                        Else
                            SW.WriteLine("Update domain " + domain + " failed, Subject Alternative Name field is not includ in Server Certification.")
                            Exit Try
                        End If
                    Else
                        SW.WriteLine("Update domain " + domain + " failed, Subject Alternative Name field is not includ in Client Certification.")
                        Exit Try
                    End If
                Next

                SW.WriteLine("OK")
            Else
                SW.WriteLine("Certification authenticated failed.")
                Throw New Exception("Certification authenticated failed.")
            End If

            Try
                sslStream.Close()
            Catch
            End Try
        Catch ex As Exception
            Serverlog.WriteEntry(ex.Message, EventLogEntryType.Error)
        End Try
        Try
            client.Close()
        Catch
        End Try
    End Sub

    Private Function SyncDDNS() As Boolean
        Try
            Dim tcpClient As New TcpClient(RemoteIP.ToString, Port)
            Dim sslStream As SslStream = New SslStream(tcpClient.GetStream,
                                                       False,
                    New RemoteCertificateValidationCallback(Function(sender As Object, certificate As X509Certificate2, chain As X509Chain, sslPolicyErrors As SslPolicyErrors) As Boolean
                                                                If sslPolicyErrors = SslPolicyErrors.RemoteCertificateNameMismatch And chain.ChainStatus.Length = 0 Then
                                                                    If certificate.Extensions("2.5.29.14").Format(False) = cert.Extensions("2.5.29.35").Format(False).Replace("KeyID=", "") Then
                                                                        Return True
                                                                    Else
                                                                        Return False
                                                                    End If
                                                                ElseIf sslPolicyErrors <> SslPolicyErrors.None Then
                                                                    For Each status As X509ChainStatus In chain.ChainStatus
                                                                        If (status.Status = X509ChainStatusFlags.RevocationStatusUnknown) Or (status.Status = X509ChainStatusFlags.OfflineRevocation) Then
                                                                            Continue For
                                                                        Else
                                                                            Serverlog.WriteEntry(status.StatusInformation + vbCrLf + status.Status.ToString, EventLogEntryType.Error)
                                                                            Return False
                                                                        End If
                                                                    Next
                                                                    Return True
                                                                Else
                                                                    Return True
                                                                End If
                                                            End Function),
                    New LocalCertificateSelectionCallback(Function(sender As Object, targetHost As String, localCertificates As X509CertificateCollection, remoteCertificate As X509Certificate, acceptableIssuers As String())
                                                              Return cert
                                                          End Function))

            sslStream.ReadTimeout = 10000
            sslStream.WriteTimeout = 10000

            sslStream.AuthenticateAsClient("slardar.net", New X509Certificate2Collection(cert), SslProtocols.Tls12, True)
            If sslStream.IsMutuallyAuthenticated Then
                Dim SR As New StreamReader(sslStream)
                Dim SW As New StreamWriter(sslStream)
                SW.AutoFlush = True
                If DomainList IsNot Nothing And DomainList.Length > 0 Then
                    SW.WriteLine(DomainList.Length.ToString)
                    For Each DomainName As String In DomainList
                        If DomainName IsNot Nothing Then
                            Try
                                SW.WriteLine(DomainName)
                            Catch
                            End Try
                        End If
                    Next
                End If

                Dim ReturnStr As String = SR.ReadLine
                If ReturnStr = "OK" Then
                    Serverlog.WriteEntry("UPDATE Domain:" + vbCrLf + "  " + String.Join(",", DomainList) + vbCrLf + "Successful.", EventLogEntryType.SuccessAudit)
                Else
                    Serverlog.WriteEntry("UPDATE Domain:" + vbCrLf + "  " + String.Join(",", DomainList) + vbCrLf + "Error." + vbCrLf + "Reason:" + ReturnStr, EventLogEntryType.FailureAudit)
                End If

            Else
                Throw New Exception("Authenticate Failed")
            End If
            Return True
        Catch ex As Exception
            Serverlog.WriteEntry(ex.Message, EventLogEntryType.Error)
            Return False
        End Try
    End Function

    Private Function isPublicIP(IPString As String) As Boolean
        Return Regex.IsMatch(IPString, "^([0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])(?<!172\.(16|17|18|19|20|21|22|23|24|25|26|27|28|29|30|31))(?<!127)(?<!^10)(?<!^0)\.([0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])(?<!192\.168)(?<!172\.(16|17|18|19|20|21|22|23|24|25|26|27|28|29|30|31))\.([0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])\.([0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])(?<!\.255$)$")
    End Function

    Private Function GetDNSNames(ByVal cert As X509Certificate2) As List(Of String)
        Dim ext As X509Extension = cert.Extensions("2.5.29.17")
        Dim DNSNames As New List(Of String)
        If ext IsNot Nothing Then
            For Each nvp As String In ext.Format(True).Split(New String() {Environment.NewLine}, StringSplitOptions.RemoveEmptyEntries)
                DNSNames.Add(nvp.Replace("DNS Name=", ""))
            Next
        End If

        Return DNSNames
    End Function

    Private Sub UpdateDNS(DomainArray As String(), IPAddress As IPAddress)
        Dim ZoneName As String
        Dim NodeName As String
        If DomainArray.Length = 1 Then
            ZoneName = DomainArray(0)
            NodeName = "."
        ElseIf DomainArray.Length = 2 Then
            ZoneName = DomainArray(0)
            NodeName = DomainArray(1)
        Else
            Serverlog.WriteEntry("UPDATE: " + String.Join(".", DomainArray) + " failed.", EventLogEntryType.FailureAudit)
            Exit Sub
        End If
        'Dim DNSUpdateThread As New Thread(New ThreadStart(Sub()
        Try
            Dim Args As String = ". /RecordDelete " + ZoneName + " " + NodeName + " A /f"
            Dim SettingProcess As New Process
            Dim ProcessInfo As New ProcessStartInfo("dnscmd", Args)
            Dim cmdReturn As String = ""
            ProcessInfo.CreateNoWindow = True
            ProcessInfo.WindowStyle = ProcessWindowStyle.Hidden
            ProcessInfo.UseShellExecute = False
            ProcessInfo.RedirectStandardOutput = True
            ProcessInfo.RedirectStandardError = True
            SettingProcess.StartInfo = ProcessInfo
            SettingProcess.Start()
            SettingProcess.WaitForExit(1000)
            cmdReturn = SettingProcess.StandardOutput.ReadToEnd
            SettingProcess.Dispose()

            Args = ". /RecordAdd " + ZoneName + " " + NodeName + " 300 A " + IPAddress.ToString
            SettingProcess = New Process
            ProcessInfo = New ProcessStartInfo("dnscmd", Args)
            ProcessInfo.UseShellExecute = False
            ProcessInfo.RedirectStandardOutput = True
            ProcessInfo.RedirectStandardError = True
            ProcessInfo.CreateNoWindow = True
            ProcessInfo.WindowStyle = ProcessWindowStyle.Hidden
            SettingProcess.StartInfo = ProcessInfo
            SettingProcess.Start()
            SettingProcess.WaitForExit(1000)
            cmdReturn = cmdReturn + vbCrLf + SettingProcess.StandardOutput.ReadToEnd
            SettingProcess.Dispose()



            If NodeName = "." Then
                Serverlog.WriteEntry("UPDATE: " + ZoneName + "." + " to " + IPAddress.ToString + vbCrLf + "Command Return:" + vbCrLf + cmdReturn, EventLogEntryType.SuccessAudit)
            Else
                Serverlog.WriteEntry("UPDATE: " + NodeName + "." + ZoneName + " to " + IPAddress.ToString + vbCrLf + "Command Return:" + vbCrLf + cmdReturn, EventLogEntryType.SuccessAudit)
            End If
            'ClientThreadPool.Remove(DNSUpdateThread)
        Catch ex As Exception
            Serverlog.WriteEntry("UPDATE: " + String.Join(".", DomainArray) + " to " + IPAddress.ToString + vbCrLf + "Error:" + vbCrLf + ex.Message, EventLogEntryType.FailureAudit)
        End Try

    End Sub

    Private Function LoadConfig(ByVal Key As String) As Object
        Dim Reg As RegistryKey
        If Environment.Is64BitOperatingSystem Then
            Reg = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey("SOFTWARE\Slardar\DDNS", False)
        Else
            Reg = Registry.LocalMachine.OpenSubKey("SOFTWARE\Slardar\DDNS", False)
        End If
        Dim ReturnObj As Object
        Try
            If Reg Is Nothing Then
                Registry.LocalMachine.CreateSubKey("SOFTWARE\Slardar\DDNS")
                ReturnObj = Nothing
            Else
                If Reg.GetValue(Key) Is Nothing Then
                    ReturnObj = Nothing
                Else
                    ReturnObj = Reg.GetValue(Key)
                End If
            End If
        Catch ex As Exception
            ReturnObj = Nothing
        End Try
        If Reg IsNot Nothing Then Reg.Close()
        Return ReturnObj
    End Function

    Private Sub LoadCert(MyKey As String)
        If LoadConfig("Cert") Is Nothing Then
            Throw New Exception("Cert is not set")
        End If
        cert = New X509Certificate2(Convert.FromBase64String(AES256.Decrypt(LoadConfig("Cert"))))
        Dim Params As CspParameters = New CspParameters()
        Params.KeyContainerName = "KeyContainer"
        Dim PrivateKey As RSACryptoServiceProvider = New RSACryptoServiceProvider(Params)
        PrivateKey.FromXmlString(AES256.Decrypt(LoadConfig("Key")))
        cert.PrivateKey = PrivateKey
    End Sub

    Private Function ContainsDomain(DomainList As List(Of String), CheckingDomain As String) As Boolean
        For Each Domain As String In DomainList
            If Domain.IndexOf("*") = -1 Then
                If (Domain = CheckingDomain) Then
                    Return True
                End If
            Else
                Dim MCollection As MatchCollection = Regex.Matches(CheckingDomain, Domain.Replace(".", "\.").Replace("*", "."))
                If MCollection.Count = 1 Then
                    If MCollection.Item(0).Index = (CheckingDomain.Length - Domain.Length) Then
                        Return True
                    End If
                End If
            End If
        Next
        Return False
    End Function

    Private Function GetDomainArray(DomainList As List(Of String), CheckingDomain As String) As String()
        For Each Domain As String In DomainList
            If Domain.IndexOf("*") = -1 Then
                If (Domain = CheckingDomain) Then
                    Return New String() {Domain}
                End If
            Else
                Dim MCollection As MatchCollection = Regex.Matches(CheckingDomain, Domain.Replace(".", "\.").Replace("*", "."))
                If MCollection.Count = 1 Then
                    If MCollection.Item(0).Index = (CheckingDomain.Length - Domain.Length) Then
                        Dim MainDomain, SubDomain As String
                        MainDomain = CheckingDomain.Substring(CheckingDomain.Length - Domain.Length + 2)
                        SubDomain = CheckingDomain.Substring(0, CheckingDomain.Length - Domain.Length + 1)
                        Return New String() {MainDomain, SubDomain}
                    End If
                End If
            End If
        Next
        Return Nothing
    End Function

    Private Function isConnected() As Boolean
        Try
            Dim client As New TcpClient()
            client.ReceiveTimeout = 100
            client.SendTimeout = 100
            client.Connect(RemoteIP.ToString, Port)
            Dim WS As New StreamWriter(client.GetStream)
            WS.Write(0)
            WS.Flush()
            WS.Close()
            client.Close()
            Return True
        Catch
            Return False
        End Try
    End Function
End Class
