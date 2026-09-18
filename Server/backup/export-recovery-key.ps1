param(
    [Parameter(Mandatory)][string]$KeyDirectory,
    [Parameter(Mandatory)][string]$OutputKey
)
# Run interactively in PowerShell 7. Never put a recovery password in command arguments/chat.
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $OutputKey) { throw 'Refusing to overwrite an existing key.' }
$backupPassword = Read-Host 'Choose a long, unique recovery passphrase (store separately)' -AsSecureString
$backupConfirm = Read-Host 'Repeat passphrase' -AsSecureString
$backupPass = [System.Net.NetworkCredential]::new('', $backupPassword).Password
$backupRepeat = [System.Net.NetworkCredential]::new('', $backupConfirm).Password
if ($backupPass.Length -lt 20 -or $backupPass -cne $backupRepeat) { throw 'Passphrases must match and contain at least 20 characters.' }
$backupProtected = [System.IO.File]::ReadAllBytes((Join-Path $KeyDirectory 'recovery-key.pkcs8.dpapi'))
$backupPrivate = [System.Security.Cryptography.ProtectedData]::Unprotect($backupProtected, $null, [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
$backupRsa = [System.Security.Cryptography.RSA]::Create()
$backupVerify = [System.Security.Cryptography.RSA]::Create()
try {
    $backupConsumed = 0
    $backupRsa.ImportPkcs8PrivateKey($backupPrivate, [ref]$backupConsumed)
    $backupPbe = [System.Security.Cryptography.PbeParameters]::new([System.Security.Cryptography.PbeEncryptionAlgorithm]::Aes256Cbc, [System.Security.Cryptography.HashAlgorithmName]::SHA256, 600000)
    $backupPem = $backupRsa.ExportEncryptedPkcs8PrivateKeyPem($backupPass, $backupPbe)
    $backupVerify.ImportFromEncryptedPem($backupPem, $backupPass)
    if ($backupVerify.ExportSubjectPublicKeyInfoPem() -cne $backupRsa.ExportSubjectPublicKeyInfoPem()) { throw 'Key export verification failed.' }
    $backupBytes = [System.Text.UTF8Encoding]::new($false).GetBytes($backupPem)
    $backupStream = [System.IO.File]::Open($OutputKey, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    try { $backupStream.Write($backupBytes); $backupStream.Flush($true) } finally { $backupStream.Dispose() }
    Write-Output 'Verified encrypted key exported. Copy it and recovery-cert.pem off this PC; keep the passphrase separately. Do not replace the original key.'
} finally {
    [Array]::Clear($backupPrivate)
    $backupRsa.Dispose()
    $backupVerify.Dispose()
    $backupPass = $null
    $backupRepeat = $null
    $backupPassword.Dispose()
    $backupConfirm.Dispose()
}
