<#
This script rotates the primary into the secondary and regenerates the primary per the MS Docs.

Excerpt from MS Docs..
Regenerating Keys
It's recommended that you periodically regenerate the keys used in the Shared Access Authorization Policy. 
The primary and secondary key slots exist so that you can rotate keys gradually. If your application generally 
uses the primary key, you can copy the primary key into the secondary key slot, and only then regenerate the primary key. 
The new primary key value can then be configured into the client applications, which have continued access using the old primary 
key in the secondary slot. Once all clients are updated, you can regenerate the secondary key to finally retire the old primary key.
https://docs.microsoft.com/en-us/azure/service-bus-messaging/service-bus-sas
I would recommend saving the old key in the key vault  prior to executing a rotation and set up a primary/secondary key scheme like we use in the service bus.  The rotation is not a atomic transaction wherein it will be rolled
back in a failure so you want to be able to restore the older key in a failure.
#>

Function Invoke-RotateServiceBusKeys
{
param($ResourceGroup, $SBNameSpace, $QueueName, $AuthRuleName)
$primary = Get-AzServiceBusKey -ResourceGroupName $ResourceGroup -Namespace $SBNameSpace -Queue $QueueName -Name $AuthRuleName | Select-Object -Property PrimaryKey

#store the older Primary Key Somewhere prior to rotating
New-AzServiceBusKey -ResourceGroupName $ResourceGroup -Namespace $SBNameSpace -Queue $QueueName -Name $AuthRuleName -RegenerateKey SecondaryKey -KeyValue $primary.PrimaryKey
New-AzServiceBusKey -ResourceGroupName $ResourceGroup -Namespace $SBNameSpace -Queue $QueueName -Name $AuthRuleName -RegenerateKey PrimaryKey

}

Function Invoke-GenerateAESKeys256
{
    param($KeyVaultName, $RotateAESPK, $RoateAESSK))
    # Generate a random AES Encryption Key.
    $AESKeyPK = New-Object Byte[] 32
    $AESKey2K = New-Object Byte[] 32
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($AESKeyPK)
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($AESKey2K)
    
    #Convert to Base64 string
    $AesPKB64 = [Convert]::ToBase64String($AESKeyPK)
    $AesP2B64 = [Convert]::ToBase64String($AESKey2K)

    #Convert to Secure String
    $SecurePK = ConvertTo-SecureString $AesPKB64 -AsPlainText -Force
    $Secure2K = ConvertTo-SecureString $AesP2B64 -AsPlainText -Force

    $secretpk = Set-AzKeyVaultSecret -VaultName $KeyVaultName -Name $RotateAESPK -SecretValue $SecurePK
    $secretsk = Set-AzKeyVaultSecret -VaultName $KeyVaultName -Name $RotateAESSK -SecretValue $Secure2K

}

Function Invoke-RotateAESKeys256
{
    param($KeyVaultName, $RotateAESPK, $RotateAESSK)

    # Retrieve the Primary Key
    $secret = Get-AzKeyVaultSecret -VaultName $KeyVaultName -Name $RotateAESPK -AsPlainText

    # Convert to Secure String
    $SecurePK = ConvertTo-SecureString $secret -AsPlainText -Force

    # Set Secondary to Primary
    $secret = Set-AzKeyVaultSecret -VaultName $KeyVaultName -Name $RotateAESSK -SecretValue $SecurePK

    # Generate new PK
    $AESKeyPK = New-Object Byte[] 32
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($AESKeyPK)

    #Convert to Base64 string
    $AesPKB64 = [Convert]::ToBase64String($AESKeyPK)

    #Convert to Secure String
    $NewSecurePK = ConvertTo-SecureString $AesPKB64 -AsPlainText -Force
    $secret = Set-AzKeyVaultSecret -VaultName $KeyVaultName -Name $RotateAESPK -SecretValue $NewSecurePK
}

# Uncomment this line to generate AES Keys for Secrets rotation
# Invoke-GenerateAESKeys256 -KeyVaultName "sasKeyROtationKV"

# Uncomment to Rotate the AES Keys
# Invoke-RotateAESKeys256 "sasKeyRotationKV"

# Uncomment this line to rotate the Command Queue Keys
# Invoke-RotateServiceBusKeys -ResourceGroup 'KeyRotationRG' -SBNameSpace 'KeyRotationNS' -QueueName 'commandqueue' -AuthRuleName 'customerCommandQueueListenSaS'
  
# Uncomment to Rotate the Response Queue Keys
#Invoke-RotateServiceBusKeys -ResourceGroup 'KeyRotationRG' -SBNameSpace 'KeyRotationNS' -QueueName 'responsequeue' -AuthRuleName 'Responsequeue'
