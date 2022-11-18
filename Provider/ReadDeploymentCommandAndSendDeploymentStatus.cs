using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using SecretRotationPOC.ServiceBus;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
namespace CustomerFunctions;

public class ReadDeploymentCommandAndSendDeploymentStatus
{

    //Function to encrypt the new SAS keys to be put on the Service Bus
    static byte[] EncryptStringToBytes_Aes(string plainText, byte[] Key, byte[] IV)
    {
        // Check arguments.
        if (plainText == null || plainText.Length <= 0){
            throw new ArgumentNullException("plainText");
        }   
        if (Key == null || Key.Length <= 0){
            throw new ArgumentNullException("Key");
        }
        if (IV == null || IV.Length <= 0){
            throw new ArgumentNullException("IV");
        }
        byte[] encrypted;

        // Create an Aes object
        // with the specified key and IV.
        using (Aes aesAlg = Aes.Create())
        {
            aesAlg.Key = Key;
            aesAlg.IV = IV;

            // Create an encryptor to perform the stream transform.
            ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);

            // Create the streams used for encryption.
            using (MemoryStream msEncrypt = new MemoryStream())
            {
                using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                {
                    using (StreamWriter swEncrypt = new StreamWriter(csEncrypt))
                    {
                        //Write all data to the stream.
                        swEncrypt.Write(plainText);
                    }
                    encrypted = msEncrypt.ToArray();
                }
            }
        }

        // Return the encrypted bytes from the memory stream.
        return encrypted;
    }

    // This provider-side service bus trigger connects to the provider Service Bus with a managed identity to read the contents.
    [FunctionName("ReadDeploymentCommandAndSendDeploymentStatus")]

    public static void Run([ServiceBusTrigger("%INITIATED_DEPLOYMENT_QUEUE_NAME%",
        Connection = "ServiceBusConnection")]string command, ILogger log)
    {
        log.LogInformation($"ReadDeploymentCommandAndSendDeploymentStatus triggered with command: {command}");

        // Add date time stamps for testing purposes
        ServiceBusService sbService = new(log);
        DateTime now = DateTime.Now;
        TimeSpan span = now.AddYears(1) - now;

        // Retrieve varibles from the local settings
        string commandQueue = Environment.GetEnvironmentVariable("COMMAND_QUEUE_NAME");
        string responseQName = Environment.GetEnvironmentVariable("RESPONSE_QUEUE_NAME");
        string cmdSaPolicyName = Environment.GetEnvironmentVariable("COMMAND_QUEUE_AUTH_POLICY_NAME");
        string respSaPolicyName = Environment.GetEnvironmentVariable("REPONSE_QUEUE_AUTH_POLICY_NAME");
        string vaultUrl = Environment.GetEnvironmentVariable("AZURE_KEYVAULT_CONNECTION_STRING");
        
 
        // Create system assigned managed identity to access the KV with AzureDefaultCredential
        var clientKV = new SecretClient(new Uri(vaultUrl), new DefaultAzureCredential());
        KeyVaultSecret RootManageSharedAccessKeyConnectionString = clientKV.GetSecret("RootManageSharedAccessKeyConnectionString");
        KeyVaultSecret AESKey = clientKV.GetSecret("AESPK");
        KeyVaultSecret IV = clientKV.GetSecret("IV");
       
        //grab the sas key using ServiceBus.cs 
        var cmdConnectionStr = sbService.GetSASTokenConnectionString(RootManageSharedAccessKeyConnectionString.Value, commandQueue, cmdSaPolicyName, span);
        var credential = new DefaultAzureCredential();


        // ServiceBusConnection resolves to ServiceBusConnection__fullyQualifiedNamespace
        // which is referenced within the local.settings.json file to use the managed identity
        string serviceBusNamespace = Environment.GetEnvironmentVariable("ServiceBusConnection__fullyQualifiedNamespace");
        var sbClient = new ServiceBusClient(serviceBusNamespace, credential);

        byte[] encryptedCommandMessage;
        byte[] aeskey = Encoding.ASCII.GetBytes(AESKey.Value);
        byte[] iv = Encoding.ASCII.GetBytes(IV.Value);
        using (Aes myAes = Aes.Create())
        {
            encryptedCommandMessage = EncryptStringToBytes_Aes(cmdConnectionStr.Result, aeskey, iv);
        }
        
        var sender = sbClient.CreateSender(commandQueue);
        var message = new ServiceBusMessage(new BinaryData(encryptedCommandMessage));
        sender.SendMessageAsync(message).GetAwaiter().GetResult();

        
    }
}