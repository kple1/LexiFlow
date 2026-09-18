param([Parameter(Mandatory)][string]$Destination)
$ErrorActionPreference = 'Stop'
# A dedicated recovery key, unrelated to application or SSH credentials.
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$backupAcl = [System.Security.AccessControl.DirectorySecurity]::new()
$backupAcl.SetAccessRuleProtection($true, $false)
$backupSid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User
$backupAcl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new($backupSid, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
$backupAcl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new([System.Security.Principal.SecurityIdentifier]::new('S-1-5-18'), 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
Set-Acl -LiteralPath $Destination -AclObject $backupAcl
$backupPrivatePath = Join-Path $Destination 'recovery-key.pkcs8.dpapi'
$backupPublicPath = Join-Path $Destination 'recovery-cert.pem'
if ((Test-Path -LiteralPath $backupPrivatePath) -or (Test-Path -LiteralPath $backupPublicPath)) { throw 'Refusing to replace a recovery key.' }
$backupRsa = [System.Security.Cryptography.RSA]::Create(3072)
try {
    $backupRequest = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=LexiFlow Offline Backup Recovery', $backupRsa, [System.Security.Cryptography.HashAlgorithmName]::SHA256, [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $backupCertificate = $backupRequest.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-5), [DateTimeOffset]::UtcNow.AddYears(10))
    $backupPrivate = $backupRsa.ExportPkcs8PrivateKey()
    $backupEncrypted = [System.Security.Cryptography.ProtectedData]::Protect($backupPrivate, $null, [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
    $backupRoundtrip = [System.Security.Cryptography.ProtectedData]::Unprotect($backupEncrypted, $null, [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
    if (-not [System.Security.Cryptography.CryptographicOperations]::FixedTimeEquals($backupPrivate, $backupRoundtrip)) { throw 'Recovery key protection verification failed.' }
    [System.IO.File]::WriteAllBytes($backupPrivatePath, $backupEncrypted)
    [System.IO.File]::WriteAllText($backupPublicPath, $backupCertificate.ExportCertificatePem(), [System.Text.UTF8Encoding]::new($false))
    [Array]::Clear($backupPrivate)
    [Array]::Clear($backupRoundtrip)
    Write-Output "Recovery key protected by Windows DPAPI: $backupPrivatePath"
    Write-Output "Public encryption certificate: $backupPublicPath"
    Write-Output 'Keep a portable encrypted recovery-key export off this PC before relying on disaster recovery.'
} finally {
    $backupRsa.Dispose()
}
