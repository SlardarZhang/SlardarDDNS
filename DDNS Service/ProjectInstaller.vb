Imports System.Configuration.Install
Imports Microsoft.Win32

Public Class ProjectInstaller

    Public Sub New()
        MyBase.New()

        '组件设计器需要此调用。
        InitializeComponent()
        If LoadConfig("Language") IsNot Nothing Then
            Try
                My.Application.ChangeUICulture(LoadConfig("Language"))
            Catch
                My.Application.ChangeUICulture("en")
            End Try
        Else
            My.Application.ChangeUICulture("en")
        End If

        If My.Application.UICulture.Name = "zh-CHS" Then
            SlardarDDNSServiceInstaller.DisplayName = "Slardar DDNS服务"
            SlardarDDNSServiceInstaller.Description = "Slardar DDNS 服务程序。"
        Else
            SlardarDDNSServiceInstaller.DisplayName = "Slardar DDNS Service"
            SlardarDDNSServiceInstaller.Description = "Slardar DDNS service program."
        End If

        '调用 InitializeComponent 后添加初始化代码

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
End Class
