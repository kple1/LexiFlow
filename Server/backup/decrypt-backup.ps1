param(
    [Parameter(Mandatory)][string]$EncryptedBackup,
    [Parameter(Mandatory)][string]$KeyDirectory,
    [Parameter(Mandatory)][string]$OutputArchive,
    [string]$OpenSsl = 'C:/Program Files/Git/usr/bin/openssl.exe'
)
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $OutputArchive) { throw 'Refusing to overwrite an existing archive.' }
$backupProtected = [System.IO.File]::ReadAllBytes((Join-Path $KeyDirectory 'recovery-key.pkcs8.dpapi'))
$backupPrivate = [System.Security.Cryptography.ProtectedData]::Unprotect($backupProtected, $null, [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
$backupRsa = [System.Security.Cryptography.RSA]::Create()
try {
    $backupConsumed = 0
    $backupRsa.ImportPkcs8PrivateKey($backupPrivate, [ref]$backupConsumed)
    $backupTemporaryKey = Join-Path ([System.IO.Path]::GetFullPath($KeyDirectory)) ('decrypt-' + [Guid]::NewGuid().ToString('N') + '.pem')
    $backupPassphrase = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    $backupPbe = [System.Security.Cryptography.PbeParameters]::new([System.Security.Cryptography.PbeEncryptionAlgorithm]::Aes256Cbc, [System.Security.Cryptography.HashAlgorithmName]::SHA256, 200000)
    [System.IO.File]::WriteAllText($backupTemporaryKey, $backupRsa.ExportEncryptedPkcs8PrivateKeyPem($backupPassphrase, $backupPbe), [System.Text.UTF8Encoding]::new($false))
    $backupProcessInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $backupProcessInfo.FileName = $OpenSsl
    $backupProcessInfo.UseShellExecute = $false
    $backupProcessInfo.RedirectStandardInput = $true
    $backupProcessInfo.StandardInputEncoding = [System.Text.UTF8Encoding]::new($false)
    $backupProcessInfo.RedirectStandardError = $true
    $backupProcessInfo.CreateNoWindow = $true
    foreach ($argument in @('cms', '-decrypt', '-binary', '-inform', 'DER', '-in', $EncryptedBackup, '-recip', (Join-Path $KeyDirectory 'recovery-cert.pem'), '-inkey', $backupTemporaryKey, '-passin', 'stdin', '-out', $OutputArchive)) {
        $backupProcessInfo.ArgumentList.Add($argument)
    }
    $backupProcess = [System.Diagnostics.Process]::Start($backupProcessInfo)
    $backupProcess.StandardInput.Write($backupPassphrase + "`n")
    $backupProcess.StandardInput.Close()
    $backupProcess.WaitForExit()
    if ($backupProcess.ExitCode -ne 0) {
        $backupDiagnostic = $backupProcess.StandardError.ReadToEnd()
        throw ('Backup decryption failed (do not use partial output). OpenSSL: ' + $backupDiagnostic)
    }
    Write-Output 'Backup decrypted with the off-host recovery key; keep the output private.'
} finally {
    [Array]::Clear($backupPrivate)
    $backupRsa.Dispose()
    if ($backupProcess) { $backupProcess.Dispose() }
    if ($backupTemporaryKey -and (Test-Path -LiteralPath $backupTemporaryKey)) {
        Remove-Item -LiteralPath $backupTemporaryKey
    }
}
