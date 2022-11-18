using System;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;

using System.Security.Cryptography;
using System.Text;
using Azure.Security.KeyVault.Secrets;
using System.IO;

namespace ClientFunctions
{
    public class ReadDeploymentStatus
    {

        //Decrypt an AES payload
        static string DecryptStringFromBytes_Aes(byte[] cipherText, byte[] Key, byte[] IV)
        {
            // Check arguments.
            if (cipherText == null || cipherText.Length <= 0) throw new ArgumentNullException("cipherText");
            if (Key == null || Key.Length <= 0) throw new ArgumentNullException("Key");
            if (IV == null || IV.Length <= 0) throw new ArgumentNullException("IV");

            // Declare the string used to hold
            // the decrypted text.
            string plaintext = null;

            // Create an Aes object
            // with the specified key and IV.
            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = Key;
                aesAlg.IV = IV;

                // Create a decryptor to perform the stream transform.
                ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);

                // Create the streams used for decryption.
                using (MemoryStream msDecrypt = new MemoryStream(cipherText))
                {
                    using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                    {
                        using (StreamReader srDecrypt = new StreamReader(csDecrypt))
                        {

                            // Read the decrypted bytes from the decrypting stream
                            // and place them in a string.
                            plaintext = srDecrypt.ReadToEnd();
                        }
                    }
                }
            }

            return plaintext;
        }

        // A function for the client to try reading from the providers service bus queue
        [FunctionName("ReadDeploymentStatus")]
        public void Run([ServiceBusTrigger("%COMMAND_QUEUE_NAME%",
            Connection = "COMMAND_QUEUE_CONNECTION_STRING")]byte[] command, ILogger log)
        {

            var responseQueue = Environment.GetEnvironmentVariable("RESPONSE_QUEUE_NAME");
            var responseQueueConnectionString = Environment.GetEnvironmentVariable("REPONSE_QUEUE_CONNECTION_STRING");
            var keyVaultUrl = Environment.GetEnvironmentVariable("AZURE_KEYVAULT_CONNECTION_STRING");

            // Create system assigned managed identity to access the KV with AzureDefaultCredential
            var clientKV = new SecretClient(new Uri(keyVaultUrl), new DefaultAzureCredential());

            // Retrieve secrets using the secret client
            KeyVaultSecret CommandQSasString = clientKV.GetSecret("CommandQSasString");
            KeyVaultSecret ReponseQueuePK = clientKV.GetSecret("ReponseQueuePK");
            KeyVaultSecret AESKey = clientKV.GetSecret("AESPK");
            KeyVaultSecret IV = clientKV.GetSecret("IV");

            // Convert the values into an AES desired format
            string decryptedCommandMessage;
            byte[] aeskey = Encoding.ASCII.GetBytes(AESKey.Value);
            byte[] iv = Encoding.ASCII.GetBytes(IV.Value);

            // Code to decrypt the service bus payload
            using (Aes myAes = Aes.Create())
            {
                decryptedCommandMessage = DecryptStringFromBytes_Aes(command, aeskey, iv);
            }

            // Update Secrets in the key vault
            var secretNewValue1 = new KeyVaultSecret("CommandQSasString", decryptedCommandMessage);
            clientKV.SetSecret(secretNewValue1);

            // Send an ACK message back to the response queuue
            var client = new ServiceBusClient(ReponseQueuePK.Value);
            log.LogInformation($"ReadDeploymentStatus triggered with command: {command}");

            var sender = client.CreateSender(responseQueue);
            var message = new ServiceBusMessage("key update successful");
            sender.SendMessageAsync(message).GetAwaiter().GetResult();

        }
    }
}
