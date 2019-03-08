Imports System.Globalization

<System.ComponentModel.RunInstaller(True)> Partial Class ProjectInstaller
    Inherits System.Configuration.Install.Installer

    'Installer 重写 Dispose，以清理组件列表。
    <System.Diagnostics.DebuggerNonUserCode()>
    Protected Overrides Sub Dispose(ByVal disposing As Boolean)
        Try
            If disposing AndAlso components IsNot Nothing Then
                components.Dispose()
            End If
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

    '组件设计器所必需的
    Private components As System.ComponentModel.IContainer

    '注意: 以下过程是组件设计器所必需的
    '可使用组件设计器修改它。
    '不要使用代码编辑器修改它。
    <System.Diagnostics.DebuggerStepThrough()>
    Private Sub InitializeComponent()
        Me.SlardarDDNSServiceInstaller = New System.ServiceProcess.ServiceInstaller()
        Me.SlardarDDNSServiceProcessInstaller = New System.ServiceProcess.ServiceProcessInstaller()
        '
        'SlardarDDNSServiceInstaller
        '
        Me.SlardarDDNSServiceInstaller.Description = "Slardar DDNS Service Program."
        Me.SlardarDDNSServiceInstaller.DisplayName = "Slardar DDNS Service"
        Me.SlardarDDNSServiceInstaller.ServiceName = "SDDNS"
        Me.SlardarDDNSServiceInstaller.StartType = System.ServiceProcess.ServiceStartMode.Automatic
        '
        'SlardarDDNSServiceProcessInstaller
        '
        Me.SlardarDDNSServiceProcessInstaller.Account = System.ServiceProcess.ServiceAccount.LocalSystem
        Me.SlardarDDNSServiceProcessInstaller.Installers.AddRange(New System.Configuration.Install.Installer() {Me.SlardarDDNSServiceInstaller})
        Me.SlardarDDNSServiceProcessInstaller.Password = Nothing
        Me.SlardarDDNSServiceProcessInstaller.Username = Nothing
        '
        'ProjectInstaller
        '
        Me.Installers.AddRange(New System.Configuration.Install.Installer() {Me.SlardarDDNSServiceProcessInstaller})

    End Sub
    Friend WithEvents SlardarDDNSServiceInstaller As ServiceProcess.ServiceInstaller
    Friend WithEvents SlardarDDNSServiceProcessInstaller As ServiceProcess.ServiceProcessInstaller
End Class
