$password = Read-Host "Enter PFX password" -AsSecureString

$cert = New-SelfSignedCertificate `
    -Type Custom `
    -Subject "CN=HyPlayer Team" `
    -FriendlyName "HyPlayer Team" `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -KeyAlgorithm RSA `
    -KeyLength 4096 `
    -HashAlgorithm SHA256 `
    -KeyExportPolicy Exportable `
    -KeyUsage DigitalSignature `
    -TextExtension @(
        "2.5.29.37={text}1.3.6.1.5.5.7.3.3"
    ) `
    -NotBefore (Get-Date) `
    -NotAfter (Get-Date).AddYears(3)

Export-PfxCertificate `
    -Cert $cert `
    -FilePath ".\HyPlayer_TemporaryKey.pfx" `
    -Password $password

Remove-Item $cert.PSPath